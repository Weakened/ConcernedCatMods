using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Backup, restore, export, and the sanitized support report for
/// one world's sidecar family. Backups/exports are plain folder copies of
/// the mod's own files (never world saves); restore copies them back and
/// takes a safety backup of the current state first. The support report is
/// sanitized by construction: versions, settings, row counts, and file
/// sizes — never coordinates, names, notes, or world identifiers beyond
/// the numeric UID.</summary>
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

        _log.LogInfo($"Atlas backup: {copied} file(s) to {target}");
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

        _log.LogInfo($"Atlas restore: {restored} file(s) from {backupPath}");
        return $"Restored {restored} file(s) from {Path.GetFileName(backupPath)} " +
            "(a pre-restore safety backup was taken). Log out and back in to load the restored atlas.";
    }

    /// <summary>The sanitized support report: safe to paste in a bug
    /// report.</summary>
    public string WriteSupportReport(long worldUid, string pluginVersion, string effectiveConfig)
    {
        string path = Path.Combine(DataDirectory, "support-report.txt");
        var lines = new List<string>
        {
            "# Concerned Cartographer support report (sanitized: no positions, names, or notes)",
            $"generated-utc: {DateTime.UtcNow:o}",
            $"plugin-version: {pluginVersion}",
            $"world-uid: {worldUid}",
            $"config: {effectiveConfig}",
        };

        foreach (string suffix in SidecarSuffixes)
        {
            string file = Path.Combine(DataDirectory, worldUid.ToString(CultureInfo.InvariantCulture) + suffix);
            if (!File.Exists(file))
            {
                lines.Add($"{suffix}: absent");
                continue;
            }

            long size = new FileInfo(file).Length;
            string counts;
            try
            {
                string[] content = File.ReadAllLines(file);
                counts = suffix switch
                {
                    ".roads.tsv" => Describe(RoadAtlasCodec.Parse(content)),
                    ".pins.tsv" => Describe(PinCodec.Parse(content)),
                    _ => Describe(RouteCodec.Parse(content)),
                };
            }
            catch (Exception exception)
            {
                counts = "unreadable: " + exception.GetType().Name;
            }

            lines.Add($"{suffix}: {size} bytes, {counts}");
        }

        lines.Add($"backups: {ListBackups(worldUid).Count}");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Describe(RoadAtlasCodec.ParseResult result)
    {
        int points = 0;
        foreach (RoadStroke stroke in result.Strokes)
        {
            points += stroke.Points.Count;
        }

        return $"{result.Strokes.Count} strokes, {points} points, {result.MalformedRows} malformed";
    }

    private static string Describe(PinCodec.ParseResult result)
    {
        return $"{result.Pins.Count} pins, {result.MalformedRows} malformed";
    }

    private static string Describe(RouteCodec.ParseResult result)
    {
        return $"{result.Routes.Count} routes, {result.MalformedRows} malformed";
    }
}
