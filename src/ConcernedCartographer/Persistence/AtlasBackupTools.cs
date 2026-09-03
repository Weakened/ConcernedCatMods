using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Backup, restore, export, and the sanitized support report for
/// one world's sidecar family. Backups/exports are plain folder copies of
/// the mod's own files (never world saves); restore copies them back and
/// takes a safety backup of the current state first. The support report is
/// sanitized by construction (privacy audit, CC-098): versions, settings,
/// row counts, and file sizes — never coordinates, names, notes, world
/// identifiers (including the numeric world UID), or file paths. The
/// world UID is used here only to LOCATE files on disk; it is never
/// passed to the report composer and never logged.</summary>
internal sealed class AtlasBackupTools
{
    private readonly ManualLogSource _log;

    public AtlasBackupTools(ManualLogSource log)
    {
        _log = log;
    }

    private static string DataDirectory =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer");

    private static string BackupRoot => Path.Combine(DataDirectory, "backups");

    private static readonly string[] SidecarSuffixes =
    {
        ".roads.tsv",
        ".pins.tsv",
        ".routes-atlas.tsv",
    };

    public string Backup(long worldUid, string label = "backup")
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string target = Path.Combine(BackupRoot, $"{worldUid}-{stamp}-{label}");
        Directory.CreateDirectory(target);
        int copied = 0;
        foreach (string suffix in SidecarSuffixes)
        {
            string source = Path.Combine(DataDirectory, worldUid.ToString(CultureInfo.InvariantCulture) + suffix);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(target, Path.GetFileName(source)), overwrite: true);
                copied++;
            }
        }

        // Privacy: the folder name carries the world UID, so the log line
        // reports the count only; the console return shows the location.
        _log.LogInfo($"Atlas backup: {copied} file(s) copied into a new backup folder.");
        return target;
    }

    public List<string> ListBackups(long worldUid)
    {
        var backups = new List<string>();
        if (!Directory.Exists(BackupRoot))
        {
            return backups;
        }

        foreach (string directory in Directory.GetDirectories(
            BackupRoot, worldUid.ToString(CultureInfo.InvariantCulture) + "-*"))
        {
            backups.Add(directory);
        }

        backups.Sort(StringComparer.OrdinalIgnoreCase);
        backups.Reverse();
        return backups;
    }

    /// <summary>Copies a backup's files over the live sidecars, after
    /// snapshotting the current state as a safety backup. Takes effect on
    /// the next world load.</summary>
    public string Restore(long worldUid, string backupPath)
    {
        if (!Directory.Exists(backupPath))
        {
            return "That backup no longer exists.";
        }

        Backup(worldUid, "pre-restore");
        int restored = 0;
        foreach (string file in Directory.GetFiles(backupPath))
        {
            string destination = Path.Combine(DataDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
            restored++;
        }

        // Stale journals would replay over the restored snapshots.
        foreach (string suffix in SidecarSuffixes)
        {
            string journal = Path.Combine(
                DataDirectory, worldUid.ToString(CultureInfo.InvariantCulture) + suffix + ".journal");
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }
        }

        _log.LogInfo($"Atlas restore: {restored} file(s) restored from the chosen backup.");
        return $"Restored {restored} file(s) from {Path.GetFileName(backupPath)} " +
            "(a pre-restore safety backup was taken). Log out and back in to load the restored atlas.";
    }

    /// <summary>The sanitized support report: safe to paste in a bug
    /// report. This wrapper only locates files; every content line comes
    /// from the pure, unit-tested <see cref="SupportReportComposer"/>,
    /// whose signature cannot receive the world UID or any path.</summary>
    public string WriteSupportReport(long worldUid, string pluginVersion, string effectiveConfig)
    {
        string path = Path.Combine(DataDirectory, "support-report.txt");
        var sidecars = new List<(string Suffix, string Status)>();
        foreach (string suffix in SidecarSuffixes)
        {
            string file = Path.Combine(DataDirectory, worldUid.ToString(CultureInfo.InvariantCulture) + suffix);
            if (!File.Exists(file))
            {
                sidecars.Add((suffix, SupportReportComposer.AbsentStatus));
                continue;
            }

            try
            {
                sidecars.Add((suffix, SupportReportComposer.DescribeSidecar(
                    suffix, File.ReadAllLines(file), new FileInfo(file).Length)));
            }
            catch (Exception exception)
            {
                sidecars.Add((suffix, SupportReportComposer.UnreadableStatus(exception)));
            }
        }

        File.WriteAllLines(path, SupportReportComposer.Compose(
            DateTime.UtcNow, pluginVersion, effectiveConfig, sidecars, ListBackups(worldUid).Count));
        return path;
    }
}
