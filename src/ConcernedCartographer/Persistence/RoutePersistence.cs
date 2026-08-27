using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Snapshot + journal persistence for routes, mirroring pins with
/// one difference: the journal queue coalesces to the LATEST state per
/// route, so freehand drawing (one mutation per point) appends each route
/// once per flush instead of once per point.</summary>
internal sealed class RoutePersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;
    private readonly Dictionary<Guid, AtlasRoute> _pendingJournal = new();
    private long _journalWorldUid;

    public RoutePersistence(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public RouteStore Load(long worldUid)
    {
        _pendingJournal.Clear();
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

            bool replayed = false;
            if (File.Exists(journalPath))
            {
                lines.AddRange(File.ReadAllLines(journalPath));
                replayed = true;
            }

            RouteCodec.ParseResult result = RouteCodec.Parse(lines);
            if (result.MalformedRows > 0)
            {
                _log.LogWarning($"Skipped {result.MalformedRows} malformed route row(s) for world {worldUid}.");
            }

            var store = new RouteStore(result.Routes);
            if (replayed)
            {
                _log.LogInfo($"Recovered route journal for world {worldUid}: {result.Routes.Count} route(s) after replay.");
                Save(worldUid, store, force: true);
            }

            return store;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load routes for world {worldUid}: {exception}");
            return new RouteStore();
        }
    }

    public void QueueJournal(AtlasRoute route)
    {
        _pendingJournal[route.Id.Value] = route;
    }

    public void FlushJournal()
    {
        if (_pendingJournal.Count == 0)
        {
            return;
        }

        string journalPath = GetJournalPath(_journalWorldUid);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
            var lines = new List<string>();
            foreach (AtlasRoute route in _pendingJournal.Values)
            {
                lines.AddRange(RouteCodec.SerializeRoute(route));
            }

            File.AppendAllLines(journalPath, lines);
            _pendingJournal.Clear();
        }
        catch (Exception exception)
        {
            _rateLimited.Error("route-journal", $"Could not append the route journal: {exception}");
        }
    }

    public bool Save(long worldUid, RouteStore store, bool force = false)
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
                foreach (string line in RouteCodec.Serialize(store.All))
                {
                    writer.WriteLine(line);
                }
            }

            File.Copy(temporaryPath, snapshotPath, overwrite: true);
            File.Delete(temporaryPath);
            _pendingJournal.Clear();
            TryDelete(GetJournalPath(worldUid));
            store.MarkClean();
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Error("route-save", $"Could not save routes for world {worldUid}: {exception}");
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
            worldUid.ToString(CultureInfo.InvariantCulture) + ".routes-atlas.tsv");
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
            // Best effort.
        }
    }
}
