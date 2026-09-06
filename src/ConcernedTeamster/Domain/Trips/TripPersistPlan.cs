using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>What a sidecar persist cycle must do, decided as a pure
/// function of the parsed existing file (CT-039 / DEF-teamster-v0.4-001).
/// Previously "a refused or migrating file is backed up before the
/// rewrite" lived only in <c>TripRecordingService.Persist</c>'s hand-written
/// if/if control flow — an Adapters-layer, BepInEx-bound class the test
/// project cannot reach, so nothing failed if that ordering ever
/// regressed. Extracting the decision here makes the ordering guarantee a
/// property of this method's return value instead: the caller's only job
/// is to execute <see cref="Plan.BackupReason"/> (if not null) and abort
/// without writing if that backup fails, which is now a few trivially
/// correct lines rather than the place the guarantee actually lived.</summary>
public static class TripPersistPlan
{
    public sealed class Plan
    {
        public Plan(string? backupReason, string? logWarning, string? logInfo)
        {
            BackupReason = backupReason;
            LogWarning = logWarning;
            LogInfo = logInfo;
        }

        /// <summary>Non-null when the caller MUST back up the existing file
        /// (with this exact reason) before writing anything, and must not
        /// write at all if that backup fails.</summary>
        public string? BackupReason { get; }

        /// <summary>A one-time warning to log for this cycle, or null.</summary>
        public string? LogWarning { get; }

        /// <summary>A one-time info line to log for this cycle, or null.</summary>
        public string? LogInfo { get; }
    }

    public static Plan Decide(TripSidecar.ParseResult existing)
    {
        if (existing.Refused)
        {
            return new Plan(
                backupReason: "refused",
                logWarning: "Trip sidecar was refused (" + string.Join("; ", existing.Errors) +
                    "); backing it up and starting fresh.",
                logInfo: null);
        }

        if (existing.Errors.Count > 0)
        {
            // CT-039: previously this case took NO backup even though its
            // malformed rows are discarded for good the moment the next
            // write recomposes the file from only the valid trips — a
            // real, silent, unrecoverable-after-the-fact loss of whatever
            // the malformed lines contained. Now backed up like the other
            // two cases, so the original is always recoverable.
            //
            // Checked before NeedsMigration below: a corrupted v1 file can
            // have both Errors.Count > 0 and NeedsMigration true at once,
            // in which case this branch's "malformed" label wins over
            // "migrate-v1" for the log line and recorded RecoveryEvent —
            // cosmetic only. Either label still triggers exactly the same
            // backup, and TripSidecar.MergeAndCompose checks
            // existing.NeedsMigration directly (not this plan's label), so
            // the segment-recompute migration itself still runs correctly
            // regardless of which reason string won here.
            return new Plan(
                backupReason: "malformed",
                logWarning: "Trip sidecar had " + existing.Errors.Count +
                    " malformed line(s); valid trips were kept and the original was backed up.",
                logInfo: null);
        }

        if (existing.NeedsMigration)
        {
            return new Plan(
                backupReason: "migrate-v1",
                logWarning: null,
                logInfo: "Trip sidecar migrated from format v1: segment scores recomputed from " +
                    existing.Trips.Count + " stored trip(s); original backed up.");
        }

        return new Plan(backupReason: null, logWarning: null, logInfo: null);
    }

    /// <summary>Whether a pending retry queue must be discarded rather
    /// than combined with this cycle's new trips, because the world
    /// changed since it was retained (CT-039 review finding: a session
    /// survives a player exiting one world and loading another, so a
    /// naive combine could merge trips into a DIFFERENT world's sidecar
    /// and road-quality history — the same misattribution the "world UID
    /// unavailable" path already refuses to risk, via a different
    /// trigger). At any moment a pending queue holds trips from exactly
    /// one world (whichever was live when it was last retained), so a
    /// single UID comparison is sufficient — no per-trip tagging needed.</summary>
    public static bool ShouldDiscardPendingRetry(int pendingCount, long pendingWorldUid, long currentWorldUid)
    {
        return pendingCount > 0 && pendingWorldUid != currentWorldUid;
    }

    /// <summary>Prepends a prior cycle's not-yet-persisted trips ahead of
    /// this cycle's newly-finished ones (CT-039 review finding: a failed
    /// persist attempt used to just drop <c>newTrips</c> — the recorder
    /// had already cleared its own queue before handing them over — so a
    /// transient I/O failure silently lost real trip data despite the
    /// caller's log line claiming otherwise). Pure list combination,
    /// extracted here — like <see cref="Decide"/> above — so it is
    /// directly testable; the caller (Adapters-layer, untestable) only
    /// decides when to call it and what to do with a failed attempt's
    /// result.</summary>
    public static List<Trip> CombineForRetry(IReadOnlyList<Trip> pending, IReadOnlyList<Trip> newTrips)
    {
        if (pending.Count == 0)
        {
            return new List<Trip>(newTrips);
        }

        var combined = new List<Trip>(pending.Count + newTrips.Count);
        combined.AddRange(pending);
        combined.AddRange(newTrips);
        return combined;
    }

    /// <summary>Bounds a retry queue to at most <paramref name="maxRetained"/>
    /// entries, dropping the OLDEST first — a pending queue can never
    /// usefully hold more trips than the sidecar itself would keep once
    /// written, and the oldest-first eviction matches
    /// <see cref="TripSidecar.Prune"/>'s own newest-wins convention, so a
    /// persistently broken disk cannot grow this queue unboundedly.</summary>
    public static List<Trip> BoundForRetry(List<Trip> trips, int maxRetained)
    {
        int excess = trips.Count - maxRetained;
        if (excess > 0)
        {
            trips.RemoveRange(0, excess);
        }

        return trips;
    }
}
