using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>The profile's stable author identity: a GUID generated once and
/// kept in the config folder. Used for audit labels and the
/// non-owner-delete policy; it is labeling, not authentication (see
/// HUMAN_ATTENTION.md).</summary>
internal static class AuthorIdentity
{
    private static string? _cached;

    public static string Get(ManualLogSource log)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        string path = Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "author-id.txt");
        try
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (Guid.TryParseExact(existing, "N", out _))
                {
                    _cached = existing;
                    return existing;
                }
            }

            string created = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, created);
            _cached = created;
            return created;
        }
        catch (Exception exception)
        {
            log.LogWarning($"Could not persist an author identity; audit labels stay empty this session: {SafeLogText.Brief(exception)}");
            _cached = "";
            return "";
        }
    }
}
