using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>The profile's stable author identity: a GUID generated once and
/// kept in the config folder. Used for audit labels and the
/// non-owner-delete policy; it is labeling, not authentication (see
/// HUMAN_ATTENTION.md).
///
/// The file is <c>author-id.dat</c>, not <c>author-id.txt</c>: a config editor
/// listed the old name among the files a player may edit (#304), and this one
/// is generated and read by the mod alone. A <c>.txt</c> written by an older
/// build is adopted once and removed, so the identity does not change.</summary>
internal static class AuthorIdentity
{
    private const string FileName = "author-id" + MarkerFile.Extension;
    private const string LegacyFileName = "author-id.txt";

    private static string? _cached;

    public static string Directory =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer");

    public static string Get(ManualLogSource log)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        string path = MarkerFile.Adopt(Directory, FileName, LegacyFileName);
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
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
