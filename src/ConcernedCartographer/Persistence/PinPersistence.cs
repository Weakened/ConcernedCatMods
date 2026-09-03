using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Snapshot-plus-journal persistence for the pin store. Mutations
/// append full rows to the journal (buffered, flushed on the autosave tick);
/// snapshots are written atomically on world switch/quit and absorb the
/// journal. Recovery after an interrupted write is simply parsing snapshot
/// then journal lines — the codec resolves per identity by highest
/// revision, so a truncated trailing journal line loses at most that one
/// row and never any valid entity.</summary>
internal sealed class PinPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;
    private readonly List<string> _pendingJournalRows = new();
    private long _journalWorldUid;

    public PinPersistence(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public PinStore Load(long worldUid)
    {
        _pendingJournalRows.Clear();
        _journalWorldUid = worldUid;

        string snapshotPath = GetSnapshotPath(worldUid);
        string journalPath = GetJournalPath(worldUid);

        try
        {
            var lines = new List<string>();
            if (File.Exists(snapshotPath))
            {
                lines.AddRange(File.ReadAllLines(snapshotPath));
            }

            bool replayedJournal = false;
            if (File.Exists(journalPath))
            {
                lines.AddRange(File.ReadAllLines(journalPath));
                replayedJournal = true;
            }

            PinCodec.ParseResult result = PinCodec.Parse(lines);
            if (result.MalformedRows > 0)
            {
                _log.LogWarning($"Skipped {result.MalformedRows} malformed pin row(s) for this world.");
            }

            var store = new PinStore(result.Pins);
            if (replayedJournal)
            {
                _log.LogInfo(
                    $"Recovered pin journal for this world: {result.Pins.Count} pin(s) after replay " +
                    $"({result.SupersededRows} superseded row(s)); compacting into a fresh snapshot.");
                Save(worldUid, store, force: true);
            }

            return store;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load pins for this world: {SafeLogText.Describe(exception)}");
            return new PinStore();
        }
    }

    /// <summary>Queues one changed pin for the next journal flush.</summary>
    public void QueueJournal(AtlasPin pin)
    {
        _pendingJournalRows.Add(PinCodec.SerializeRow(pin));
    }

    /// <summary>Appends queued rows to the journal file. Cheap enough for
    /// the autosave cadence; a crash before the next snapshot loses nothing
    /// that was flushed.</summary>
    public void FlushJournal()
    {
        if (_pendingJournalRows.Count == 0)
        {
            return;
        }

        string journalPath = GetJournalPath(_journalWorldUid);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
            File.AppendAllLines(journalPath, _pendingJournalRows);
            _pendingJournalRows.Clear();
        }
        catch (Exception exception)
        {
            _rateLimited.Error("pin-journal", $"Could not append the pin journal for this world: {SafeLogText.Describe(exception)}");
        }
    }

    /// <summary>Atomic snapshot; on success the journal is absorbed and
    /// truncated.</summary>
    public bool Save(long worldUid, PinStore store, bool force = false)
    {
        if (!force && !store.IsDirty)
        {
            return false;
        }

        string snapshotPath = GetSnapshotPath(worldUid);
        string temporaryPath = snapshotPath + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                foreach (string line in PinCodec.Serialize(store.All))
                {
                    writer.WriteLine(line);
                }
            }

            File.Copy(temporaryPath, snapshotPath, overwrite: true);
            File.Delete(temporaryPath);

            _pendingJournalRows.Clear();
            TryDelete(GetJournalPath(worldUid));
            store.MarkClean();
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Error("pin-save", $"Could not save pins for this world: {SafeLogText.Describe(exception)}");
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static string GetSnapshotPath(long worldUid)
    {
        return Path.Combine(
            Paths.ConfigPath,
            "ConcernedCatMods",
            "ConcernedCartographer",
            worldUid.ToString(CultureInfo.InvariantCulture) + ".pins.tsv");
    }

    private static string GetJournalPath(long worldUid)
    {
        return GetSnapshotPath(worldUid) + ".journal";
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
            // Best effort; the original error has been logged.
        }
    }
}
