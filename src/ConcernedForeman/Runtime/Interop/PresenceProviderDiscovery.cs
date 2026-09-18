using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Presence;
using TheConcernedCat.Settlement.Collection.Cooperation;

namespace TheConcernedCat.ConcernedForeman.Runtime.Interop;

/// <summary>Finds Concerned Cartographer's <c>concernedcat.presence/1</c>
/// (#317), so a survey can honestly say whether Hulgi was in it.
///
/// The same shape as <see cref="HaulProviderDiscovery"/> and for the same
/// reasons: probed once per world session, after every plugin's Awake so load
/// order cannot make Cartographer look absent, with exactly one read of the
/// provider's public <see cref="CapabilityMap.PropertyName"/> property. The
/// pure <see cref="PresenceProviderGate"/> decides and its one line is logged.
/// There is no compile-time reference to Cartographer anywhere: the GUID and
/// the property name are strings, and only BCL types cross.
///
/// What this cannot do is worth saying out loud, because it is the guarantee
/// the feature rests on: the presence contract has no op that changes
/// anything. Foreman can learn that Hulgi is here and free. It cannot call him,
/// move him, show him or start anything, so a survey credited to him is a
/// survey he was actually standing in.</summary>
internal sealed class PresenceProviderDiscovery
{
    private readonly Action<string> _log;
    private bool _probed;

    internal PresenceProviderDiscovery(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public PresenceDiscovery Discovery { get; private set; } = PresenceDiscovery.NotProbed;

    internal void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        Discovery = PresenceProviderGate.Evaluate(Lookup);
        try
        {
            _log(Discovery.LogLine);
        }
        catch (Exception)
        {
            // A broken log sink must not undo a discovery.
        }
    }

    /// <summary>The world went away: the next world session reads again.
    /// </summary>
    internal void Forget()
    {
        _probed = false;
        Discovery = PresenceDiscovery.NotProbed;
    }

    private static PresenceProviderLookup Lookup()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PresenceContract.ProviderGuid, out PluginInfo info) || info == null)
        {
            return PresenceProviderLookup.NotFound();
        }

        System.Version? version = info.Metadata?.Version;
        object? instance = info.Instance;
        if (instance == null)
        {
            return PresenceProviderLookup.Detected(version, null, "the plugin instance is not available");
        }

        PropertyInfo? property = instance.GetType().GetProperty(
            CapabilityMap.PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanRead)
        {
            return PresenceProviderLookup.Detected(version, null, CapabilityMap.PropertyName + " is not published");
        }

        try
        {
            return PresenceProviderLookup.Detected(version, property.GetValue(instance, null));
        }
        catch (Exception exception)
        {
            return PresenceProviderLookup.Detected(
                version, null, "reading " + CapabilityMap.PropertyName + " threw " + exception.GetType().Name);
        }
    }
}
