using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.WorkOrders;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Interop;

/// <summary>#296: reads Concerned Foreman's <c>concernedcat.workorders/1</c>
/// so a work area on the map can show who works there, the progress and why
/// the order stopped.
///
/// Foreman is found by its GUID string and its capability property, once per
/// world session; there is no compile-time reference and only BCL types cross.
/// The contract only answers: nothing here can start, pause or cancel an order.
/// It is asked at most once a second, and only while the large map is open.
/// A missing Foreman, a fault or an answer about another world is "nothing to
/// show", never "no order".</summary>
internal sealed class WorkOrderProviderDiscovery
{
    private const float PollSeconds = 1f;

    private readonly Action<string> _log;
    private bool _probed;
    private Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? _endpoint;
    private float _askedAt = float.NegativeInfinity;
    private string _signature = string.Empty;

    public WorkOrderProviderDiscovery(Action<string> log)
    {
        _log = log ?? (_ => { });
    }

    /// <summary>The latest answer, or null when there is none to show.</summary>
    public WorkOrderLookup? Latest { get; private set; }

    /// <summary>Bumps whenever what the map shows changes.</summary>
    public long Stamp { get; private set; }

    /// <summary>Asks again if a second has passed. True when the answer
    /// changed what the map shows.</summary>
    public bool Poll(float now, long worldUid)
    {
        if (now - _askedAt < PollSeconds && now >= _askedAt)
        {
            return false;
        }

        _askedAt = now;
        EnsureProbed();
        WorkOrderLookup? latest = _endpoint == null
            ? null
            : WorkOrderClient.List(
                _endpoint,
                worldUid == 0L ? string.Empty : worldUid.ToString(CultureInfo.InvariantCulture),
                Plugin.PluginVersion);
        Latest = latest != null && latest.IsOk ? latest : null;

        string signature = WorkAreaOrders.Signature(Latest) + "#" + Describe(Latest);
        if (string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            return false;
        }

        _signature = signature;
        Stamp++;
        return true;
    }

    /// <summary>The world went away: look again next session, show nothing
    /// until then.</summary>
    public void Forget()
    {
        _probed = false;
        _endpoint = null;
        _askedAt = float.NegativeInfinity;
        Latest = null;
        _signature = string.Empty;
        Stamp++;
    }

    private static string Describe(WorkOrderLookup? orders)
    {
        if (orders == null)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        foreach (PublishedWorkOrder order in orders.Orders)
        {
            text.Append(order.StateLine).Append('|').Append(order.ReasonLine).Append('|')
                .Append(string.Join("|", order.ProgressLines)).Append('#');
        }

        return text.ToString();
    }

    private void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        string note;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(WorkOrderContract.ProviderGuid, out PluginInfo info) || info == null)
            {
                note = "Concerned Foreman is not installed";
            }
            else if (info.Instance == null)
            {
                note = "Concerned Foreman is installed but not running";
            }
            else
            {
                PropertyInfo? property = info.Instance.GetType().GetProperty(
                    CapabilityMap.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                object? map = property != null && property.CanRead ? property.GetValue(info.Instance, null) : null;
                note = CapabilityMap.TryGetEndpoint(
                    map as IReadOnlyDictionary<string, object>, WorkOrderContract.Id, WorkOrderContract.Major, out _endpoint)
                    ? "Concerned Foreman publishes work orders"
                    : "this Concerned Foreman version does not publish work orders";
            }
        }
        catch (Exception exception)
        {
            _endpoint = null;
            note = "looking for Concerned Foreman's work orders failed (" + exception.GetType().Name + ")";
        }

        try
        {
            _log("Work areas: " + note + ".");
        }
        catch (Exception)
        {
            // A broken log sink must not undo a discovery.
        }
    }
}
