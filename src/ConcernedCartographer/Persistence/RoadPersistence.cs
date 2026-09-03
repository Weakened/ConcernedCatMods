using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

internal sealed class RoadPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;

    // Sidecars still in the pre-source v1 format get one backup copy before
    // the first v2 save rewrites them, because a downgraded v0.1 mod cannot
    // read v2 rows and would discard the file as fully malformed.
    private readonly System.Collections.Generic.HashSet<string> _legacyPathsAwaitingBackup = new();

    public RoadPersistence(ManualLogSource log)
    {
        _log = log;

        // Autosave retries every few seconds forever on a broken disk; one
        // error per minute is plenty to diagnose without flooding the log.
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public RoadAtlas Load(long worldUid)
    {
        string path = GetPath(worldUid);
        if (!File.Exists(path))
        {
            return new RoadAtlas();
        }

        try
        {
            RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(File.ReadLines(path));
            if (result.MalformedRows > 0)
            {
                _log.LogWarning($"Skipped {result.MalformedRows} malformed road-atlas row(s) in this world's sidecar.");
            }

            if (result.LegacyRows > 0)
            {
                _legacyPathsAwaitingBackup.Add(path);
                _log.LogInfo(
                    $"This world's road atlas uses the v1 format ({result.LegacyRows} row(s)); " +
                    "the original will be kept as .v1.bak when it is first rewritten in v2.");
            }

            var atlas = new RoadAtlas(result.Strokes);

            // RC8 road-source-authority migration: strokes recorded by the
            // retired passive sources (traversal walking, chunk recovery —
            // including every pre-source v1 row) are cleaned out once, with
            // the original file kept beside the sidecar. Explicit
            // Pathen/Paved construction strokes survive untouched.
            RoadAtlas.MigrationResult authority = atlas.RemoveNonConstructionStrokes();
            if (authority.RemovedStrokes > 0)
            {
                TakeAuthorityMigrationBackup(path);
                _log.LogInfo(
                    $"Road source authority (v1): removed {authority.RemovedStrokes} passive stroke(s) " +
                    $"({authority.RemovedPoints} point(s)) recorded by traversal/chunk recovery; " +
                    $"{atlas.Strokes.Count} explicit construction stroke(s) remain. " +
                    "The pre-migration file was kept as .pre-authority.bak.");
            }

            RoadAtlas.MaintenanceResult maintenance = atlas.PerformMaintenance();
            if (maintenance.MergedStrokes > 0 || maintenance.RemovedPoints > 0)
            {
                _log.LogInfo(
                    $"Road atlas maintenance: merged {maintenance.MergedStrokes} stroke fragment(s), " +
                    $"simplified away {maintenance.RemovedPoints} point(s); {atlas.Strokes.Count} stroke(s), " +
                    $"{atlas.PointCount} point(s) remain.");
            }

            return atlas;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load road atlas from disk: {SafeLogText.Describe(exception)}");
            return new RoadAtlas();
        }
    }

    public bool Save(long worldUid, RoadAtlas atlas)
    {
        string path = GetPath(worldUid);
        string temporaryPath = path + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (_legacyPathsAwaitingBackup.Contains(path) && File.Exists(path))
            {
                string backupPath = path + ".v1.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(path, backupPath);
                    _log.LogInfo("Backed up the v1 road atlas beside its sidecar (.v1.bak) before the first v2 save.");
                }

                _legacyPathsAwaitingBackup.Remove(path);
            }

            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                foreach (string line in RoadAtlasCodec.Serialize(atlas.Strokes))
                {
                    writer.WriteLine(line);
                }
            }

            File.Copy(temporaryPath, path, overwrite: true);
            File.Delete(temporaryPath);
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Error("atlas-save", $"Could not save road atlas to disk: {SafeLogText.Describe(exception)}");
            TryDelete(temporaryPath);
            return false;
        }
    }

    /// <summary>One-time safety copy before the road-source-authority
    /// migration rewrites a sidecar: the first migration for a world keeps
    /// the original as .pre-authority.bak and never overwrites an existing
    /// backup, so the pre-RC8 atlas stays recoverable by hand.</summary>
    private void TakeAuthorityMigrationBackup(string path)
    {
        try
        {
            string backupPath = path + ".pre-authority.bak";
            if (File.Exists(path) && !File.Exists(backupPath))
            {
                File.Copy(path, backupPath);
                _log.LogInfo("Backed up the pre-migration road atlas beside its sidecar (.pre-authority.bak).");
            }
        }
        catch (Exception exception)
        {
            _rateLimited.Error("authority-backup", $"Could not back up the road atlas before the authority migration: {SafeLogText.Describe(exception)}");
        }
    }

    private readonly System.Collections.Generic.HashSet<long> _reconcileBackupsTaken = new();

    /// <summary>Journals destructive reconciliation: before the first
    /// coverage removal in a session touches a world's atlas, the last saved
    /// sidecar is copied to .pre-reconcile.bak (overwritten next session).
    /// Manual recovery = delete the sidecar, rename the backup.</summary>
    public void BackupBeforeReconciliation(long worldUid)
    {
        if (!_reconcileBackupsTaken.Add(worldUid))
        {
            return;
        }

        try
        {
            string path = GetPath(worldUid);
            if (File.Exists(path))
            {
                File.Copy(path, path + ".pre-reconcile.bak", overwrite: true);
                _log.LogInfo("Backed up the road atlas beside its sidecar (.pre-reconcile.bak) before this session's first reconciliation.");
            }
        }
        catch (Exception exception)
        {
            _rateLimited.Error("reconcile-backup", $"Could not back up the road atlas before reconciliation: {SafeLogText.Describe(exception)}");
        }
    }

    private static string GetPath(long worldUid)
    {
        return Path.Combine(
            Paths.ConfigPath,
            "ConcernedCatMods",
            "ConcernedCartographer",
            worldUid.ToString(CultureInfo.InvariantCulture) + ".roads.tsv");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; the original write error has already been logged.
        }
    }
}
