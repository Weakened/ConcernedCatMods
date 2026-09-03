using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Per-world sidecar IO for the terrain-intent exclusion mask
/// (DEF-v1.0-005): <c>&lt;world-uid&gt;.terrain-intent.tsv</c> next to the
/// roads/pins sidecars. Same safety rules as every other sidecar: never a
/// Valheim save file, temp-file write flow, rate-limited failures.</summary>
internal sealed class TerrainIntentPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;

    public TerrainIntentPersistence(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public TerrainIntentMask Load(long worldUid)
    {
        string path = GetPath(worldUid);
        if (!File.Exists(path))
        {
            return new TerrainIntentMask();
        }

        try
        {
            TerrainIntentCodec.ParseResult result = TerrainIntentCodec.Parse(File.ReadLines(path));
            if (result.UnsupportedVersion)
            {
                _log.LogWarning(
                    "This world's terrain-intent sidecar has an unsupported header (written by a newer version?); " +
                    "starting with no exclusions for this session. The file is rewritten in v1 on the next save.");
            }
            else if (result.MalformedRows > 0)
            {
                _log.LogWarning($"Skipped {result.MalformedRows} malformed terrain-intent row(s) in this world's sidecar.");
            }

            return result.Mask;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load terrain intent from disk: {SafeLogText.Describe(exception)}");
            return new TerrainIntentMask();
        }
    }

    public bool Save(long worldUid, TerrainIntentMask mask)
    {
        string path = GetPath(worldUid);
        string temporaryPath = path + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                foreach (string line in TerrainIntentCodec.Serialize(mask))
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
            _rateLimited.Error("terrain-intent-save", $"Could not save terrain intent to disk: {SafeLogText.Describe(exception)}");
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
            worldUid.ToString(CultureInfo.InvariantCulture) + ".terrain-intent.tsv");
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
