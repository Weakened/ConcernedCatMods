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
    private readonly List<Trip> _pendingRetryTrips = new();
    private long _pendingRetryWorldUid;
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
        if (drained.Count > 0 || _pendingRetryTrips.Count > 0)
        {
            Persist(drained, lastKnownWorldUid);
        }
    }

    private void PersistFinishedTrips()
    {
        IReadOnlyList<Trip> finished = _recorder.DrainFinishedTrips();
        if (finished.Count == 0 && _pendingRetryTrips.Count == 0)
        {
            return;
        }

        if (!WorldContextAdapter.TryGetWorldUid(out long worldUid))
        {
            // Deliberately not retried: a lost world context (unlike a
            // transient disk failure below) has no sensible retry target —
            // if a world UID becomes available again it could be a
            // different world, and misattributing a pending trip to it
            // would be worse than the honest drop already logged here.
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

        // CT-039 review finding: every failure branch below used to say a
        // variant of "trips held in memory" while actually just returning
        // — TripRecorder.DrainFinishedTrips/DrainOnReset already cleared
        // its own queue before newTrips reached here, so nothing was
        // actually held anywhere and a transient I/O failure silently lost
        // real trip data. combined is genuinely retried on the next cycle
        // now (bounded by the sidecar's own retention cap — it can never
        // usefully hold more than the file would keep anyway).
        List<Trip> combined = CombineWithPendingRetry(newTrips, worldUid);

        string path = SidecarPathFor(worldUid);
        string? existingText = SidecarFileStore.TryRead(path, out string? readError);
        if (readError is not null)
        {
            WarnIoOnce("sidecar read failed: " + readError + "; will retry with the next trip");
            RetainForRetry(combined, worldUid);
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
                WarnIoOnce("sidecar backup failed: " + backupError + "; will retry with the next trip");
                RetainForRetry(combined, worldUid);
                return;
            }

            RecordRecoveryEvent(plan.BackupReason, path, plan.LogWarning ?? plan.LogInfo!);
        }

        string composed = TripSidecar.MergeAndCompose(
            existing, combined, _options.MaxTripsRetained, worldUid, _pluginVersion);
        if (!SidecarFileStore.TryWriteAtomic(path, composed, out string? writeError))
        {
            WarnIoOnce(
                "sidecar write failed: " + writeError + "; previous file left intact; will retry with the next trip");
            RetainForRetry(combined, worldUid);
        }
    }

    /// <summary>Prepends any trips a prior cycle failed to persist ahead of
    /// this cycle's newly-finished ones and clears the pending queue — the
    /// combination itself is pure (<see cref="TripPersistPlan.CombineForRetry"/>);
    /// callers must call <see cref="RetainForRetry"/> again themselves if
    /// this attempt also fails.
    /// <para>CT-039 review finding: a pending retry queue has no per-trip
    /// world tag, but at any moment it only ever holds trips from the one
    /// world that was live when <see cref="RetainForRetry"/> last ran — so
    /// if the world changes before the retry succeeds (this instance is a
    /// session-long singleton that survives a player exiting one world and
    /// loading another), merging them into a DIFFERENT world's sidecar
    /// would misattribute them, exactly the outcome the world-UID-
    /// unavailable branch above already refuses to risk. Discarded with an
    /// honest log line and a recorded recovery event instead.</para></summary>
    private List<Trip> CombineWithPendingRetry(IReadOnlyList<Trip> newTrips, long worldUid)
    {
        if (TripPersistPlan.ShouldDiscardPendingRetry(_pendingRetryTrips.Count, _pendingRetryWorldUid, worldUid))
        {
            int discarded = _pendingRetryTrips.Count;
            _pendingRetryTrips.Clear();
            WarnIoOnce(
                discarded + " trip(s) pending retry from a previous world were discarded; " +
                "the world changed before they could be saved");
            RecordRecoveryEvent(
                "world-changed",
                SidecarPathFor(_pendingRetryWorldUid),
                discarded + " trip(s) pending retry after an earlier save failure were discarded " +
                "because the world changed before they could be saved.");
        }

        List<Trip> combined = TripPersistPlan.CombineForRetry(_pendingRetryTrips, newTrips);
        _pendingRetryTrips.Clear();
        return combined;
    }

    private void RetainForRetry(List<Trip> trips, long worldUid)
    {
        _pendingRetryWorldUid = worldUid;
        _pendingRetryTrips.AddRange(TripPersistPlan.BoundForRetry(trips, _options.MaxTripsRetained));
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
