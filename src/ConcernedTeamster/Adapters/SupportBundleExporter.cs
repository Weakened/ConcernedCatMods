using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using TheConcernedCat.ConcernedTeamster.Domain.Compatibility;
using TheConcernedCat.ConcernedTeamster.Domain.Support;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Gathers the real data a support bundle needs (config values,
/// compatibility results, sidecar file summaries, recovery events, recent
/// log lines) and hands it to the pure <see cref="SupportBundleComposer"/>
/// (CT-039). Only this class touches the file system for the bundle
/// feature — <c>SupportBundleComposer</c> and
/// <c>SupportBundleSanitizer</c> stay pure and are fully covered by the
/// Domain-only test project.</summary>
internal static class SupportBundleExporter
{
    private const string SidecarFilePrefix = "teamster_trips_";
    private const string SidecarFileSuffix = ".txt";

    public static string BundleDirectory =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedTeamster", "SupportBundles");

    /// <summary>Composes and atomically writes a new bundle file, returning
    /// its path on success. Every <c>SidecarFileStore</c> operation reports
    /// failure as a false result with a message rather than throwing; the
    /// one exception is <see cref="GatherSidecarSummaries"/>'s directory
    /// enumeration, which could in principle throw on a filesystem race
    /// (a sidecar deleted or its permissions changed between listing and
    /// reading) — CT-042 security review found this doc comment previously
    /// overstated "never throws" here. That narrow case is still fail-safe
    /// in practice: <c>SupportBundlePanel.HandleExportClicked</c> wraps this
    /// call in its own try/catch, so an unexpected exception here disables
    /// only the Support panel for the session, never the whole plugin.</summary>
    public static (bool Success, string? Path, string? Error) Export(
        string pluginVersion,
        TeamsterSettings settings,
        TripRecordingService? trips,
        LogTailRecorder? logTail,
        DateTime generatedUtc)
    {
        IReadOnlyList<string> compatibilityLines = ComposeCompatibilityLines();
        List<SupportBundleComposer.SidecarSummary> sidecars = GatherSidecarSummaries(out int backupFileCount);
        IReadOnlyList<RecoveryEvent> recoveryEvents =
            trips?.RecoveryEvents ?? Array.Empty<RecoveryEvent>();
        IReadOnlyList<string> recentLog = logTail?.Snapshot() ?? Array.Empty<string>();

        List<string> lines = SupportBundleComposer.Compose(
            generatedUtc,
            pluginVersion,
            ComposeEffectiveConfigSummary(settings),
            compatibilityLines,
            sidecars,
            backupFileCount,
            recoveryEvents,
            recentLog);

        string fileName = "support-bundle-" +
            generatedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
        string path = Path.Combine(BundleDirectory, fileName);
        bool wrote = SidecarFileStore.TryWriteAtomic(path, string.Join("\n", lines), out string? error);
        return (wrote, wrote ? path : null, error);
    }

    private static string ComposeEffectiveConfigSummary(TeamsterSettings settings)
    {
        return "Enabled=" + settings.Enabled.Value +
            ", DebugLogging=" + settings.DebugLogging.Value +
            ", Brake.Enabled=" + settings.BrakeEnabled.Value +
            ", Trips.Enabled=" + settings.TripsEnabled.Value +
            ", UiScale=" + settings.UiScale.Value.ToString("0.##", CultureInfo.InvariantCulture) +
            ", Profile=" + settings.ActiveProfile.Value +
            ", ConfigSchemaVersion=" + settings.SchemaVersion.Value;
    }

    private static IReadOnlyList<string> ComposeCompatibilityLines()
    {
        IReadOnlyList<ModDetectionResult>? results = CompatibilityAdapter.Results;
        if (results is null)
        {
            return new[] { "(not yet probed)" };
        }

        IReadOnlyList<string> detected = CompatibilityStatusPresenter.ComposeDetectedLines(results);
        return detected.Count == 0
            ? new[] { CompatibilityStatusPresenter.ComposeNoneDetectedLine() }
            : detected;
    }

    /// <summary>Enumerates every trip sidecar file found (not just the
    /// current world's), parsing each against the world UID encoded in its
    /// own file name. A file that fails to parse as a UID, or that cannot
    /// be read, is silently skipped rather than reported as an error —
    /// the bundle's job is a best-effort summary, not another validator.</summary>
    private static List<SupportBundleComposer.SidecarSummary> GatherSidecarSummaries(out int backupFileCount)
    {
        var summaries = new List<SupportBundleComposer.SidecarSummary>();
        backupFileCount = 0;
        string directory = TripRecordingService.SidecarDirectory;
        if (!Directory.Exists(directory))
        {
            return summaries;
        }

        backupFileCount = Directory.GetFiles(directory, SidecarFilePrefix + "*" + SidecarFileSuffix + ".bak-*").Length;

        foreach (string path in Directory.GetFiles(directory, SidecarFilePrefix + "*" + SidecarFileSuffix))
        {
            string fileName = Path.GetFileName(path);
            string digits = fileName.Substring(
                SidecarFilePrefix.Length, fileName.Length - SidecarFilePrefix.Length - SidecarFileSuffix.Length);
            if (!long.TryParse(digits, out long worldUid))
            {
                continue;
            }

            string? text = SidecarFileStore.TryRead(path, out string? readError);
            if (readError is not null || text is null)
            {
                continue;
            }

            TripSidecar.ParseResult parsed = TripSidecar.Parse(text, worldUid);
            long kilobytes = (new FileInfo(path).Length + 1023) / 1024;
            summaries.Add(new SupportBundleComposer.SidecarSummary(
                fileName, kilobytes, parsed.Trips.Count, parsed.Errors.Count));
        }

        return summaries;
    }
}
