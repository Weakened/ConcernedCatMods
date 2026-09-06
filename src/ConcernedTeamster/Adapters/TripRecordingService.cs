using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Owns trip persistence at runtime (CT-016): feeds the pure
/// recorder from the pump's pulled-cart snapshots, and on every trip
/// finalization does a read-merge-prune-write cycle against this world's
/// sidecar — atomic write, versioned header, world-UID check, backup
/// before a refused file would ever be replaced. Files live under
/// <c>BepInEx/config/ConcernedCatMods/ConcernedTeamster/</c> with a
/// "teamster" infix, so they can never collide with Cartographer's
/// sidecars (different folder AND different names). No Valheim save file
/// is ever touched.</summary>
internal sealed class TripRecordingService
{
    private const int MaxRecoveryEventsRetained = 50;

    private readonly TripRecorder _recorder;
    private readonly TripRecorderOptions _options;
    private readonly ManualLogSource _log;
    private readonly string _pluginVersion;
    private readonly List<RecoveryEvent> _recoveryEvents = new();
    private bool _ioFailureLogged;
    private bool _refusalLogged;

    public TripRecordingService(TripRecorderOptions options, ManualLogSource log, string pluginVersion)
    {
        _options = options;
        _recorder = new TripRecorder(options);
        _log = log;
        _pluginVersion = pluginVersion;
    }

    /// <summary>Sidecar recovery events (backup/quarantine/migration) this
    /// session, oldest first, for the Support Bundle panel (CT-039).
    /// Bounded — a session that somehow saw more than this has bigger
    /// problems than a long list would help with.</summary>
    public IReadOnlyList<RecoveryEvent> RecoveryEvents => _recoveryEvents;

    public static string SidecarDirectory =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedTeamster");

    public static string SidecarPathFor(long worldUid) =>
        Path.Combine(SidecarDirectory, "teamster_trips_" +
            worldUid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt");

    public void FeedPulled(CartTelemetry telemetry)
    {
        _recorder.FeedPulled(telemetry);
        PersistFinishedTrips();
    }

    public void NotifyNotPulled(double nowSeconds)
    {
        _recorder.NotifyNotPulled(nowSeconds);
        PersistFinishedTrips();
    }

    /// <summary>World exit / shutdown: finalize the open trip and flush.
    /// Uses the world UID captured while the world was still up when the
    /// live query already fails.</summary>
    public void FlushAndReset(long lastKnownWorldUid)
    {
        IReadOnlyList<Trip> drained = _recorder.DrainOnReset();
        if (drained.Count > 0)
        {
            Persist(drained, lastKnownWorldUid);
        }
    }

    private void PersistFinishedTrips()
    {
        IReadOnlyList<Trip> finished = _recorder.DrainFinishedTrips();
        if (finished.Count == 0)
        {
            return;
        }

        if (!WorldContextAdapter.TryGetWorldUid(out long worldUid))
        {
            WarnIoOnce("world UID unavailable; finished trip dropped rather than misfiled");
            return;
        }

        Persist(finished, worldUid);
    }

    private void Persist(IReadOnlyList<Trip> newTrips, long worldUid)
    {
        if (worldUid == 0L)
        {
            WarnIoOnce("world UID unavailable at flush; finished trip dropped rather than misfiled");
            return;
        }

        string path = SidecarPathFor(worldUid);
        string? existingText = SidecarFileStore.TryRead(path, out string? readError);
        if (readError is not null)
        {
            WarnIoOnce("sidecar read failed: " + readError);
            return;
        }

        TripSidecar.ParseResult existing = TripSidecar.Parse(existingText, worldUid);

        // CT-039 / DEF-teamster-v0.4-001: what to do this cycle is decided
        // by a pure function (TripPersistPlan.Decide), not this method's
        // own control flow — see that type for why. This method's only
        // job is to execute the plan exactly: back up first when the plan
        // says to, abort without writing if that backup fails, otherwise
        // proceed. Nothing here decides whether a backup is warranted.
        TripPersistPlan.Plan plan = TripPersistPlan.Decide(existing);
        if (plan.BackupReason == "refused" && !_refusalLogged)
        {
            _refusalLogged = true;
            _log.LogWarning("Trip sidecar at " + path + ": " + plan.LogWarning);
        }
        else if (plan.BackupReason == "malformed" && !_ioFailureLogged)
        {
            _ioFailureLogged = true;
            _log.LogWarning(plan.LogWarning);
        }

        if (plan.LogInfo is not null)
        {
            _log.LogInfo(plan.LogInfo);
        }

        if (plan.BackupReason is not null)
        {
            if (!SidecarFileStore.TryBackup(path, plan.BackupReason, out string? backupError))
            {
                WarnIoOnce("sidecar backup failed: " + backupError + "; trips held in memory");
                return;
            }

            RecordRecoveryEvent(plan.BackupReason, path, plan.LogWarning ?? plan.LogInfo!);
        }

        string composed = TripSidecar.MergeAndCompose(
            existing, newTrips, _options.MaxTripsRetained, worldUid, _pluginVersion);
        if (!SidecarFileStore.TryWriteAtomic(path, composed, out string? writeError))
        {
            WarnIoOnce("sidecar write failed: " + writeError + "; previous file left intact");
        }
    }

    /// <summary>Loads this world's persisted trips and segment scores for
    /// the history/bottleneck UI (CT-018/CT-019). Empty on any failure;
    /// refused files stay untouched.</summary>
    public (System.Collections.Generic.IReadOnlyList<Trip> Trips,
        Domain.RoadQuality.RoadQualityIndex Segments,
        long WorldUid) LoadWorldData()
    {
        var emptySegments = new Domain.RoadQuality.RoadQualityIndex();
        if (!WorldContextAdapter.TryGetWorldUid(out long worldUid))
        {
            return (System.Array.Empty<Trip>(), emptySegments, 0L);
        }

        string? text = SidecarFileStore.TryRead(SidecarPathFor(worldUid), out _);
        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, worldUid);
        if (parsed.Refused)
        {
            return (System.Array.Empty<Trip>(), emptySegments, worldUid);
        }

        Domain.RoadQuality.RoadQualityIndex segments = parsed.NeedsMigration
            ? Domain.RoadQuality.RoadQualityIndex.ComputeFromTrips(parsed.Trips)
            : parsed.Segments;
        return (parsed.Trips, segments, worldUid);
    }

    /// <summary>Deletes one trip's raw record (its data only — the
    /// cumulative road-quality segments are documented history and stay).
    /// Atomic rewrite; ids renumber densely afterwards.</summary>
    public bool DeleteTrip(int tripId)
    {
        if (!WorldContextAdapter.TryGetWorldUid(out long worldUid))
        {
            return false;
        }

        string path = SidecarPathFor(worldUid);
        string? text = SidecarFileStore.TryRead(path, out string? readError);
        if (readError is not null || text is null)
        {
            return false;
        }

        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, worldUid);
        if (parsed.Refused)
        {
            return false;
        }

        var remaining = new List<Trip>();
        bool removed = false;
        foreach (Trip trip in parsed.Trips)
        {
            if (trip.Id == tripId && !removed)
            {
                removed = true;
                continue;
            }

            remaining.Add(trip);
        }

        if (!removed)
        {
            return false;
        }

        IReadOnlyList<Trip> renumbered = TripSidecar.Prune(remaining, int.MaxValue);
        string composed = TripSidecar.Compose(renumbered, worldUid, _pluginVersion, parsed.Segments);
        if (!SidecarFileStore.TryWriteAtomic(path, composed, out string? writeError))
        {
            WarnIoOnce("sidecar write failed during delete: " + writeError);
            return false;
        }

        return true;
    }

    private void WarnIoOnce(string message)
    {
        if (_ioFailureLogged)
        {
            return;
        }

        _ioFailureLogged = true;
        _log.LogWarning("Trip recording: " + message + ".");
    }

    private void RecordRecoveryEvent(string reason, string path, string message)
    {
        if (_recoveryEvents.Count >= MaxRecoveryEventsRetained)
        {
            return;
        }

        _recoveryEvents.Add(new RecoveryEvent(reason, Path.GetFileName(path), message));
    }
}
