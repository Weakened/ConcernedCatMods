using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.WorkArea;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Interop;

/// <summary>#296: Cartographer's published <c>concernedcat.workarea/1</c>, one
/// BCL delegate in the plugin's <c>ConcernedCatCapabilities</c>.
///
/// <b>Nothing here can be made to do anything.</b> The contract lists and
/// describes the areas the player drew in the current world; there is no op
/// that assigns, starts, edits or deletes. A consumer (Foreman, when its lane
/// adopts this) treats an area as the player's intent and validates every
/// order and target itself, on the host.
///
/// The protocol is <see cref="WorkAreaResponder"/>, game-free and shared. This
/// file finds the runtime's store through a function, never a cached
/// reference, so a rebuilt runtime is never answered about from a dead one.
/// </summary>
internal sealed class WorkAreaCapabilityPublisher
{
    private static readonly TimeSpan FaultLogCooldown = TimeSpan.FromSeconds(30);

    private readonly Func<(long WorldUid, IReadOnlyList<WorkArea> Areas)?> _source;
    private readonly string _providerVersion;
    private readonly ManualLogSource _log;
    private DateTime _lastFaultLogged = DateTime.MinValue;
    private bool _shuttingDown;

    internal WorkAreaCapabilityPublisher(
        Func<(long WorldUid, IReadOnlyList<WorkArea> Areas)?> source, string providerVersion, ManualLogSource log)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _providerVersion = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Endpoint = Handle;
        _log.LogInfo(
            "Work-area capability " + WorkAreaContract.Id + "/" + WorkAreaContract.Major +
            " published for other Concerned Cat mods. It only lists the areas you drew; no other mod can change them or start work through it.");
    }

    internal static string Key => CapabilityMap.KeyFor(WorkAreaContract.Id, WorkAreaContract.Major);

    internal Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> Endpoint { get; }

    internal void Shutdown() => _shuttingDown = true;

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> request)
    {
        try
        {
            return WorkAreaResponder.Answer(request, _providerVersion, Snapshot);
        }
        catch (Exception exception)
        {
            LogFault(exception);
            return WorkAreaContract.Reply(
                WorkAreaContract.Statuses.ProviderError, _providerVersion, exception.GetType().Name);
        }
    }

    private PublishedWorkAreas? Snapshot()
    {
        if (_shuttingDown)
        {
            return null;
        }

        (long WorldUid, IReadOnlyList<WorkArea> Areas)? current = _source();
        if (current == null)
        {
            return null;
        }

        return ToPublished(current.Value.WorldUid, current.Value.Areas);
    }

    /// <summary>A copy, so a consumer holding the reply never sees a later
    /// edit through it.</summary>
    internal static PublishedWorkAreas ToPublished(long worldUid, IReadOnlyList<WorkArea> areas)
    {
        var published = new List<PublishedWorkArea>(areas.Count);
        foreach (WorkArea area in areas)
        {
            published.Add(new PublishedWorkArea(area.Id, area.Name, area.Purpose, area.Boundary));
        }

        return new PublishedWorkAreas(worldUid.ToString(CultureInfo.InvariantCulture), published);
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
                "Work-area capability caught an error answering another mod (" +
                SafeLogText.Brief(exception) + "); it answered ProviderError and nothing was changed.");
        }
        catch (Exception)
        {
            // Logging must never turn an answer into a throw.
        }
    }
}
