using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Backup, restore, export, and the sanitized support report for
/// one world's sidecar family. Backups/exports are plain folder copies of
/// the mod's own files (never world saves); restore copies them back and
/// takes a safety backup of the current state first. The support report is
/// sanitized by construction (privacy audit, CC-098): versions, settings,
/// row counts, and file sizes — never coordinates, names, notes, world
/// identifiers (including the numeric world UID), or file paths. The
/// world UID is used here only to LOCATE files on disk; it is never
/// passed to the report composer and never logged.
///
/// <b>Names on disk are composed by <see cref="Storage.AtlasBackupNaming"/>,
/// never by interpolation, and the sidecar family is
/// <c>CartographerWorldSidecars</c>, never a local copy (#367).</b> Both were
/// silent losses: a backup folder named under the player's own culture is one
/// the invariant glob in <see cref="ListBackups"/> cannot find, and a local list
/// of three of the five sidecars meant two of a player's files were in no backup
/// and no report while both looked complete.</summary>
internal sealed class AtlasBackupTools
{
    private readonly ManualLogSource _log;

    public AtlasBackupTools(ManualLogSource log)
    {
        _log = log;
    }

    private static string DataDirectory => CartographerPaths.Root;

    private static string BackupRoot => CartographerPaths.Backups;

    /// <summary>Every per-world sidecar, from the one list (#367).
    ///
    /// This used to be its own array of three of the five, so
    /// <c>.survey-rejected.tsv</c> (a player's rejected-observation memory, the
    /// thing that stops the survey re-offering what he already said no to) and
    /// <c>.terrain-intent.tsv</c> (the exclusion mask) were in no backup, could
    /// not be restored, kept stale journals across a restore, and were missing
    /// from the support report while it looked complete. A second copy of a list
    /// is a "which one is right" problem, and
    /// <c>CartographerPaths</c> already named the probe's evidence lists as the
    /// authority. This is now a reference to it, not a copy of it.</summary>
    private static readonly string[] SidecarSuffixes = CartographerWorldSidecars.Suffixes;

    public string Backup(long worldUid, string label = "backup")
    {
        // Composed by AtlasBackupNaming, invariantly: ListBackups globs for the
        // invariant uid, and a folder name formatted under the player's own
        // culture is a backup the lister cannot find (#367).
        string target = Path.Combine(
            BackupRoot, AtlasBackupNaming.FolderName(worldUid, DateTime.UtcNow, label));
        Directory.CreateDirectory(target);
        int copied = 0;
        foreach (string suffix in SidecarSuffixes)
        {
            string source = Path.Combine(DataDirectory, AtlasBackupNaming.SidecarName(worldUid, suffix));
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
            BackupRoot, AtlasBackupNaming.SearchPattern(worldUid)))
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
                DataDirectory, AtlasBackupNaming.JournalName(worldUid, suffix));
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }
        }

        _log.LogInfo($"Atlas restore: {restored} file(s) restored from the chosen backup.");
        return $"Restored {restored} file(s) from {Path.GetFileName(backupPath)} " +
            "(a pre-restore safety backup was taken). Log out and back in to load the restored atlas.";
    }

    /// <summary>What this report used to be called. A mod manager's
    /// configuration editor decides what to offer by extension, and
    /// <c>.txt</c> is on its list, so the report was listed among the files a
    /// player may edit — the same defect as <c>author-id.txt</c> and the last
    /// one left of #304. It is superseded, not adopted: the contents are
    /// regenerated from scratch every time, so there is nothing in the old file
    /// to carry across.</summary>
    private const string PriorReportName = "support-report.txt";

    /// <summary>The sanitized support report: safe to paste in a bug
    /// report. This wrapper only locates files; every content line comes
    /// from the pure, unit-tested <see cref="SupportReportComposer"/>,
    /// whose signature cannot receive the world UID or any path.
    ///
    /// <b><c>.log</c>, deliberately, and not <c>.dat</c>.</b> A configuration
    /// editor does not open <c>.log</c>, so the report stops being offered as a
    /// setting; a person does, which matters because this is the one file we
    /// ask people to find and send us. It stays in the product's directory
    /// under <c>BepInEx/config</c>, which is the folder a profile export copies
    /// wholesale — see <see cref="CartographerPaths"/>.</summary>
    public string WriteSupportReport(long worldUid, string pluginVersion, string effectiveConfig)
    {
        string path = CartographerPaths.InRoot("support-report.log");
        var sidecars = new List<(string Suffix, string Status)>();
        foreach (string suffix in SidecarSuffixes)
        {
            string file = Path.Combine(DataDirectory, AtlasBackupNaming.SidecarName(worldUid, suffix));
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

        // AtlasTextFile, not a bare write: the product's data directory may not
        // exist yet on the profile that is asking for help (#367).
        AtlasTextFile.WriteLines(path, SupportReportComposer.Compose(
            DateTime.UtcNow, pluginVersion, effectiveConfig, sidecars, ListBackups(worldUid).Count));
        RemoveSupersededReport(path);
        return path;
    }

    /// <summary>Removes the older build's <c>.txt</c> report, once the new one
    /// is actually on disk.
    ///
    /// <b>Only after, and only if.</b> The new report is written first and its
    /// presence is checked before anything is deleted, so a failed write never
    /// costs somebody the report they were about to send us. The old one holds
    /// nothing the new one does not — every line is regenerated — so there is
    /// no adoption to do, only the tidy that stops a player having two reports
    /// and sending the stale one.</summary>
    private void RemoveSupersededReport(string writtenPath)
    {
        try
        {
            if (!File.Exists(writtenPath))
            {
                return;
            }

            string prior = CartographerPaths.InRoot(PriorReportName);
            if (File.Exists(prior))
            {
                File.Delete(prior);
            }
        }
        catch (Exception exception)
        {
            // A leftover report is untidy, never harmful, and never worth
            // failing the command a player ran to ask us for help.
            _log.LogWarning(
                "The older support report could not be removed, so there are two of them; the " +
                $"newer one is the .log: {SafeLogText.Brief(exception)}");
        }
    }
}
