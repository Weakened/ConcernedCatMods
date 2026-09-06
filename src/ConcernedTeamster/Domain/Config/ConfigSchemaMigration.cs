namespace TheConcernedCat.ConcernedTeamster.Domain.Config;

/// <summary>What a config load must do about its stored schema version
/// (CT-039), decided as a pure function so the ladder is directly testable
/// rather than living only in <c>Plugin.Awake</c>'s BepInEx-bound,
/// untestable startup sequence — the same reasoning as
/// <c>TripPersistPlan</c> for the sidecar side.</summary>
public static class ConfigSchemaMigration
{
    public sealed class Plan
    {
        public Plan(bool needsMigration, bool isFromANewerVersion, string? logMessage)
        {
            NeedsMigration = needsMigration;
            IsFromANewerVersion = isFromANewerVersion;
            LogMessage = logMessage;
        }

        /// <summary>True when the caller must advance the stored version to
        /// <see cref="ConfigSchemaVersion.Current"/> after applying whatever
        /// (if any) value changes this specific step requires.</summary>
        public bool NeedsMigration { get; }

        /// <summary>True when the stored version is newer than this build
        /// understands (the mod was downgraded after a newer version wrote
        /// this config) — the caller must change nothing and leave the
        /// stored version exactly as found, never guess a rollback.</summary>
        public bool IsFromANewerVersion { get; }

        /// <summary>A line to log for this outcome, or null when nothing is
        /// worth reporting (already current).</summary>
        public string? LogMessage { get; }
    }

    public static Plan Decide(int storedVersion)
    {
        if (storedVersion > ConfigSchemaVersion.Current)
        {
            return new Plan(
                needsMigration: false,
                isFromANewerVersion: true,
                logMessage: "Config schema version " + storedVersion + " is newer than this build " +
                    "understands (current: " + ConfigSchemaVersion.Current + "); settings are left " +
                    "exactly as found. This usually means the mod was downgraded after a newer " +
                    "version wrote this config file.");
        }

        if (storedVersion == ConfigSchemaVersion.Current)
        {
            return new Plan(needsMigration: false, isFromANewerVersion: false, logMessage: null);
        }

        if (storedVersion == ConfigSchemaVersion.PreVersioning)
        {
            // The one real migration every existing install goes through:
            // introducing the version marker itself. No other setting's
            // value changes, so no backup is warranted here — unlike the
            // sidecar files, nothing in this step can lose data (see
            // RECOVERY.md for why config migration and sidecar migration
            // are treated differently on purpose).
            return new Plan(
                needsMigration: true,
                isFromANewerVersion: false,
                logMessage: "Config schema versioning introduced at version " +
                    ConfigSchemaVersion.Current + "; no existing setting value changed.");
        }

        return new Plan(
            needsMigration: true,
            isFromANewerVersion: false,
            logMessage: "Config schema migrated from version " + storedVersion + " to " +
                ConfigSchemaVersion.Current + ".");
    }
}
