using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.WorkOrders;

namespace TheConcernedCat.ConcernedForeman.Runtime.Interop;

/// <summary>#296: publishes <c>concernedcat.workorders/1</c> so Concerned
/// Cartographer can show, on the work area the player drew, who works there,
/// how far the order has got and why it stopped.
///
/// The endpoint is a BCL delegate in the plugin's
/// <see cref="CapabilityMap.PropertyName"/> map. It only answers: the contract
/// has no op that starts, pauses, edits or cancels an order. Every request is
/// answered from the snapshot the caller supplies, which is built from the
/// same facts and view as Foreman's own collection panel. A fault answers
/// ProviderError and is logged once per session.</summary>
internal sealed class WorkOrderCapabilityPublisher
{
    private readonly Func<PublishedWorkOrders?> _snapshot;
    private readonly string _providerVersion;
    private readonly Action<string> _warn;
    private bool _faultLogged;

    public WorkOrderCapabilityPublisher(Func<PublishedWorkOrders?> snapshot, string providerVersion, Action<string> warn)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _providerVersion = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;
        _warn = warn ?? (_ => { });
        Endpoint = Answer;
    }

    public static string Key => CapabilityMap.KeyFor(WorkOrderContract.Id, WorkOrderContract.Major);

    public Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> Endpoint { get; }

    private IReadOnlyDictionary<string, string> Answer(IReadOnlyDictionary<string, string> request)
    {
        try
        {
            return WorkOrderResponder.Answer(request, _providerVersion, _snapshot);
        }
        catch (Exception exception)
        {
            if (!_faultLogged)
            {
                _faultLogged = true;
                try
                {
                    _warn("Work-order capability answered with an error: " + exception.GetType().Name);
                }
                catch (Exception)
                {
                    // A broken log sink must not escape the endpoint.
                }
            }

            return WorkOrderContract.Reply(WorkOrderContract.Statuses.ProviderError, _providerVersion, exception.GetType().Name);
        }
    }

    /// <summary>The loaded world's uid as the contract carries it, or empty
    /// when no world is loaded.</summary>
    internal static string CurrentWorldUid()
    {
        try
        {
            ZNet net = ZNet.instance;
            long world = net != null ? net.GetWorldUID() : 0L;
            return world == 0L ? string.Empty : world.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
