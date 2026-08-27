using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

internal sealed class RoadPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;

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
                _log.LogWarning($"Skipped {result.MalformedRows} malformed road-atlas row(s) in {path}.");
            }

            return new RoadAtlas(result.Strokes);
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load road atlas from {path}: {exception}");
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
            _rateLimited.Error("atlas-save", $"Could not save road atlas to {path}: {exception}");
            TryDelete(temporaryPath);
            return false;
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
