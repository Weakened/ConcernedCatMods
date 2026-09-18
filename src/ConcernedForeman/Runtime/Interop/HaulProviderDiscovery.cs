using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection.Cooperation;

namespace TheConcernedCat.ConcernedForeman.Runtime.Interop;

/// <summary>Finds Concerned Teamster's <c>concernedcat.haul/1</c> (D7, #317): the
/// only code in Foreman that asks BepInEx about another product.
///
/// Probed once per world session, when a world comes up - after every plugin's
/// Awake, so load order cannot make Teamster look absent - with exactly one
/// read of the provider's public <see cref="CapabilityMap.PropertyName"/>
/// property. The pure <see cref="HaulProviderGate"/> decides, and its one line
/// is logged. The endpoint is kept for the session and re-read after the world
/// unloads. There is no compile-time reference to Teamster anywhere: the GUID
/// and the property name are strings, and only BCL types cross.</summary>
internal sealed class HaulProviderDiscovery : IHaulEndpointSource
{
    private readonly Action<string> _log;
    private bool _probed;

    internal HaulProviderDiscovery(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public HaulDiscovery Discovery { get; private set; } = HaulDiscovery.NotProbed;

    internal void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        Discovery = HaulProviderGate.Evaluate(Lookup);
        try
        {
            _log(Discovery.LogLine);
        }
        catch (Exception)
        {
            // A broken log sink must not undo a discovery.
        }
    }

    /// <summary>The world went away: the next world session reads again.</summary>
    internal void Forget()
    {
        _probed = false;
        Discovery = HaulDiscovery.NotProbed;
    }

    private static HaulProviderLookup Lookup()
    {
        if (!Chainloader.PluginInfos.TryGetValue(HaulContract.ProviderGuid, out PluginInfo info) || info == null)
        {
            return HaulProviderLookup.NotFound();
        }

        System.Version? version = info.Metadata?.Version;
        object? instance = info.Instance;
        if (instance == null)
        {
            return HaulProviderLookup.Detected(version, null, "the plugin instance is not available");
        }

        PropertyInfo? property = instance.GetType().GetProperty(
            CapabilityMap.PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanRead)
        {
            return HaulProviderLookup.Detected(version, null, CapabilityMap.PropertyName + " is not published");
        }

        try
        {
            return HaulProviderLookup.Detected(version, property.GetValue(instance, null));
        }
        catch (Exception exception)
        {
            return HaulProviderLookup.Detected(
                version, null, "reading " + CapabilityMap.PropertyName + " threw " + exception.GetType().Name);
        }
    }
}
