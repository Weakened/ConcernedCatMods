using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Runtime.Companions;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Presence;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Interop;

/// <summary>Cartographer's published <c>concernedcat.presence/1</c> (#317): the
/// value of the plugin's <c>ConcernedCatCapabilities</c> property, one BCL
/// delegate other Concerned Cat mods find by GUID and call from their own
/// update.
///
/// <b>Nothing here can be made to do anything.</b> The contract has two ops and
/// both of them only read. Foreman asks whether Hulgi is here and free so a
/// survey can honestly say it was a joint one; it cannot ask him to come, and
/// there is no op it could use to try. That is what makes <c>SPEC.md</c>
/// GATHER-03's "a busy or absent Hulgi is never cloned or credited" a property
/// of the design rather than a promise about the consumer's manners.
///
/// The protocol itself is <see cref="PresenceResponder"/>, which is game-free
/// and shared, so the two-assembly test exercises the same dispatch and the
/// same reply shape that ship. This file does three things the shared half
/// cannot: it finds the director, it converts a Unity position, and it logs.
///
/// The director is reached through a source function, never cached: it is
/// rebuilt with the runtime, and a captured reference to a dead one would
/// answer about a companion who is no longer there.</summary>
internal sealed class PresenceCapabilityPublisher
{
    private static readonly TimeSpan FaultLogCooldown = TimeSpan.FromSeconds(30);

    private readonly Func<CompanionDirector?> _director;
    private readonly string _providerVersion;
    private readonly ManualLogSource _log;
    private DateTime _lastFaultLogged = DateTime.MinValue;
    private bool _shuttingDown;

    internal PresenceCapabilityPublisher(
        Func<CompanionDirector?> director, string providerVersion, ManualLogSource log)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _providerVersion = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint = Handle;
        Capabilities = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(PresenceContract.Id, PresenceContract.Major)] = endpoint,
        });

        _log.LogInfo(
            "Presence capability " + PresenceContract.Id + "/" + PresenceContract.Major +
            " published for other Concerned Cat mods. It only reports whether a companion is here and free; " +
            "no other mod can move him, show him or change anything about him through it.");
    }

    internal IReadOnlyDictionary<string, object> Capabilities { get; }

    /// <summary>The plugin is going away: every later call answers Unavailable
    /// rather than reaching a half-disposed runtime.</summary>
    internal void Shutdown() => _shuttingDown = true;

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> request)
    {
        try
        {
            CompanionDirector? director = _shuttingDown ? null : _director();
            return PresenceResponder.Answer(
                request, _providerVersion, ready: director != null, facts: id => FactsFor(director!, id));
        }
        catch (Exception exception)
        {
            LogFault(exception);
            return PresenceContract.Reply(
                PresenceContract.Statuses.ProviderError, _providerVersion, exception.GetType().Name);
        }
    }

    /// <summary>One companion ships in this product. Anybody else is answered
    /// with <see cref="PresenceFacts.Nobody"/> rather than refused.</summary>
    private static PresenceFacts FactsFor(CompanionDirector director, string companionId)
    {
        if (!string.Equals(companionId, PresenceContract.Companions.Hulgi, StringComparison.Ordinal))
        {
            return PresenceFacts.Nobody;
        }

        CompanionPresenceFacts facts = director.PresenceFacts();
        if (facts.Position is Vector3 at)
        {
            return new PresenceFacts(
                facts.Known, facts.Present, facts.Visible, facts.Free,
                hasPosition: true, at.x, at.y, at.z);
        }

        return new PresenceFacts(facts.Known, facts.Present, facts.Visible, facts.Free);
    }

    private void LogFault(Exception exception)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastFaultLogged < FaultLogCooldown)
            {
                return;
            }

            _lastFaultLogged = now;
            _log.LogWarning(
                "Presence capability caught an error answering another mod (" +
                SafeLogText.Brief(exception) + "); it answered ProviderError and nothing was changed.");
        }
        catch (Exception)
        {
            // A broken log sink must not turn a handled fault into an unhandled one.
        }
    }
}
