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
}
