using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Domain.Support;

/// <summary>Pure composer for the sanitized support bundle (CT-039). Every
/// emitted line passes through <see cref="SupportBundleSanitizer"/>
/// regardless of source — recent log lines especially, since they are
/// written for a developer reading BepInEx's own log and can embed a full
/// path (this mod's own warning lines do exactly that) or a raw world
/// UID. Callers pass in already-gathered data (file names and counts, not
/// content) — this class does no IO of its own, matching Concerned
/// Cartographer's own support-report composer's shape.</summary>
public static class SupportBundleComposer
{
    public const string Header =
        "# Concerned Teamster support bundle " +
        "(sanitized: no world identifiers beyond a masked number, no player names, no full paths)";

    public sealed class SidecarSummary
    {
        public SidecarSummary(string fileName, long kilobyteSize, int tripCount, int malformedRowCount)
        {
            FileName = fileName;
            KilobyteSize = kilobyteSize;
            TripCount = tripCount;
            MalformedRowCount = malformedRowCount;
        }

        /// <summary>File name only (for example
        /// "teamster_trips_1234567890.txt") — never a full path. The
        /// embedded world UID is still masked by the sanitizer below.</summary>
        public string FileName { get; }

        public long KilobyteSize { get; }

        public int TripCount { get; }

        public int MalformedRowCount { get; }
    }

    public static List<string> Compose(
        DateTime generatedUtc,
        string pluginVersion,
        string effectiveConfigSummary,
        IReadOnlyList<string> compatibilityLines,
        IReadOnlyList<SidecarSummary> sidecars,
        int backupFileCount,
        IReadOnlyList<RecoveryEvent> recoveryEvents,
        IReadOnlyList<string> recentLogLines)
    {
        var lines = new List<string>
        {
            Header,
            Scrub("generated-utc: " + generatedUtc.ToString("yyyy-MM-dd HH:mm:ss") + "Z"),
            Scrub("plugin-version: " + pluginVersion),
            Scrub("config: " + effectiveConfigSummary),
            "",
            "## Compatibility",
        };

        if (compatibilityLines.Count == 0)
        {
            lines.Add(Scrub("(not yet probed)"));
        }
        else
        {
            foreach (string line in compatibilityLines)
            {
                lines.Add(Scrub(line));
            }
        }

        lines.Add("");
        lines.Add("## Sidecars (" + backupFileCount + " backup file(s) across all reasons)");
        if (sidecars.Count == 0)
        {
            lines.Add(Scrub("(none found)"));
        }
        else
        {
            foreach (SidecarSummary sidecar in sidecars)
            {
                lines.Add(Scrub(
                    sidecar.FileName + ": " + sidecar.KilobyteSize + " KB, " +
                    sidecar.TripCount + " trip(s), " + sidecar.MalformedRowCount + " malformed row(s)"));
            }
        }

        lines.Add("");
        lines.Add("## Recovery events this session");
        if (recoveryEvents.Count == 0)
        {
            lines.Add(Scrub("(none)"));
        }
        else
        {
            foreach (RecoveryEvent recoveryEvent in recoveryEvents)
            {
                lines.Add(Scrub(
                    recoveryEvent.SidecarFileName + " [" + recoveryEvent.Reason + "]: " +
                    recoveryEvent.Message));
            }
        }

        lines.Add("");
        lines.Add("## Recent log lines (this mod only, newest last)");
        if (recentLogLines.Count == 0)
        {
            lines.Add(Scrub("(none captured)"));
        }
        else
        {
            foreach (string logLine in recentLogLines)
            {
                lines.Add(Scrub(logLine));
            }
        }

        return lines;
    }

    private static string Scrub(string line) => SupportBundleSanitizer.Sanitize(line);
}
