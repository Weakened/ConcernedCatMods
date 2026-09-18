using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Interop;

/// <summary>Teamster's published <c>concernedcat.haul/1</c> (DECISIONS.md D7,
/// #317): the value of the plugin's <c>ConcernedCatCapabilities</c> property,
/// one BCL delegate other Concerned Cat mods find by GUID and call from their
/// own update. Only mscorlib types cross; nothing is registered with the game
/// and nothing runs unless another mod calls.
///
/// Gunnar is reached through a source function, never cached: his runtime
/// (#313) binds and unbinds with world loads. The provider behind the delegate
/// catches every exception itself; this adapter only adds one throttled log
/// line when that happens, so a provider bug is visible without flooding the
/// log from a consumer that polls.</summary>
internal sealed class HaulCapabilityPublisher
{
    private static readonly TimeSpan FaultLogCooldown = TimeSpan.FromSeconds(30);

    private readonly HaulProvider _provider;
    private readonly ManualLogSource _log;
    private int _loggedFaults;
    private DateTime _lastFaultLogged = DateTime.MinValue;

    internal HaulCapabilityPublisher(Func<IHaulService?> haulService, string providerVersion, ManualLogSource log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _provider = new HaulProvider(haulService, providerVersion);

        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint = Handle;
        Capabilities = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major)] = endpoint,
        });

        _log.LogInfo(
            "Haul capability " + HaulContract.Id + "/" + HaulContract.Major + " published for other Concerned Cat mods. " +
            "Gunnar hauls for them only while Workers/GunnarHaulingEnabled is on and a cart is assigned to him.");
    }

    internal IReadOnlyDictionary<string, object> Capabilities { get; }

    /// <summary>The plugin is going away: every later call answers Unavailable.
    /// </summary>
    internal void Shutdown() => _provider.BeginShutdown();

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> request)
    {
        IReadOnlyDictionary<string, string> reply = _provider.Handle(request);
        if (_provider.FaultCount != _loggedFaults)
        {
            _loggedFaults = _provider.FaultCount;
            try
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastFaultLogged >= FaultLogCooldown)
                {
                    _lastFaultLogged = now;
                    _log.LogWarning(
                        "Haul capability caught an error answering another mod (" + _provider.LastFault +
                        "); it answered ProviderError and nothing was changed.");
                }
            }
            catch (Exception)
            {
                // Logging is best-effort; the reply is already safe.
            }
        }

        return reply;
    }
}
