using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.WorkArea;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>#296 slice 3: Concerned Cartographer's work areas, read through
/// <c>concernedcat.workarea/1</c> when Cartographer happens to be installed.
///
/// The same discovery shape as <c>PresenceProviderDiscovery</c>: Cartographer
/// is found by its GUID string in <c>Chainloader.PluginInfos</c> and its
/// <see cref="CapabilityMap.PropertyName"/> property is read once per world
/// session; only BCL types cross, and there is no compile-time reference to
/// Cartographer anywhere. Every question goes to the provider at the moment it
/// is asked, so a redrawn or deleted area is seen at the next checkpoint.
///
/// The contract has no op that changes anything: Foreman can list and read
/// areas the player drew. It cannot draw, edit or delete one.</summary>
internal sealed class CartographerWorkAreas
{
    private readonly Action<string> _log;
    private bool _probed;
    private Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? _endpoint;
    private string _probeNote = "not looked for yet";

    public CartographerWorkAreas(Action<string> log)
    {
        _log = log ?? (_ => { });
    }

    /// <summary>For status lines: what the last look found.</summary>
    public string ProbeNote
    {
        get
        {
            EnsureProbed();
            return _probeNote;
        }
    }

    /// <summary>The world went away: the next world session looks again.
    /// </summary>
    public void Forget()
    {
        _probed = false;
        _endpoint = null;
        _probeNote = "not looked for yet";
    }

    public WorkAreaLookup List() =>
        WorkAreaClient.List(Endpoint(), CurrentWorldUid(), Plugin.PluginVersion);

    public WorkAreaLookup Describe(string areaId) =>
        WorkAreaClient.Describe(Endpoint(), areaId, CurrentWorldUid(), Plugin.PluginVersion);

    private Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? Endpoint()
    {
        EnsureProbed();
        return _endpoint;
    }

    private void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        _endpoint = null;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(WorkAreaContract.ProviderGuid, out PluginInfo info) || info == null)
            {
                _probeNote = "Concerned Cartographer is not installed";
            }
            else if (info.Instance == null)
            {
                _probeNote = "Concerned Cartographer is installed but not running";
            }
            else
            {
                PropertyInfo? property = info.Instance.GetType().GetProperty(
                    CapabilityMap.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                object? map = property != null && property.CanRead ? property.GetValue(info.Instance, null) : null;
                if (CapabilityMap.TryGetEndpoint(
                        map as IReadOnlyDictionary<string, object>, WorkAreaContract.Id, WorkAreaContract.Major, out var endpoint))
                {
                    _endpoint = endpoint;
                    _probeNote = "Concerned Cartographer " + (info.Metadata?.Version?.ToString() ?? "?") + " publishes work areas";
                }
                else
                {
                    _probeNote = "this Concerned Cartographer version does not publish work areas";
                }
            }
        }
        catch (Exception exception)
        {
            _endpoint = null;
            _probeNote = "looking for Concerned Cartographer's work areas failed (" + exception.GetType().Name + ")";
        }

        try
        {
            _log("Collection: work areas - " + _probeNote + ".");
        }
        catch (Exception)
        {
            // A broken log sink must not undo a discovery.
        }
    }

    private static string CurrentWorldUid()
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
