using System;
using System.IO;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>The profile's stable author identity: a GUID generated once and
/// kept in the config folder. Used for audit labels and the
/// non-owner-delete policy; it is labeling, not authentication (see
/// HUMAN_ATTENTION.md).
///
/// It lives in the <c>state</c> subfolder rather than beside the sidecars,
/// because a mod manager's configuration editor listed the old
/// <c>author-id.txt</c> among the files a player may edit (#304) and this one
/// is generated and read by the mod alone. A file an older build left in the
/// product directory is adopted once — read, checked, copied, read back, and
/// only then removed.</summary>
internal static class AuthorIdentity
{
    private const string FileName = "author-id" + MarkerFile.Extension;
    private const string LegacyFileName = "author-id.txt";

    private static string? _cached;

    /// <summary>Where the onboarding tip's marker lives. It is named here only
    /// because both markers are adopted in one place.</summary>
    internal static class OnboardingMarker
    {
        public const string FileName = "onboarding-shown" + MarkerFile.Extension;
        public const string LegacyFileName = "onboarding-shown.txt";

        public static string Path => MarkerPath(FileName);
    }

    /// <summary>Both markers this product keeps, adopted together at startup.
    ///
    /// One place, because adopting them at different lifecycle points is how
    /// the first attempt left <c>onboarding-shown.txt</c> behind on any profile
    /// that was launched but never entered a world — half of #304 unfixed, on
    /// exactly the machines whose owner was only checking the update.</summary>
    public static void AdoptMarkers(ManualLogSource log)
    {
        Adopt(FileName, LegacyFileName, IsIdentity, log);
        Adopt(OnboardingMarker.FileName, OnboardingMarker.LegacyFileName, _ => true, log);
    }

    public static string Get(ManualLogSource log)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            string path = Adopt(FileName, LegacyFileName, IsIdentity, log);
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (IsIdentity(existing))
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

    private static bool IsIdentity(string? contents) =>
        contents is not null && Guid.TryParseExact(contents.Trim(), "N", out _);

    private static string MarkerPath(string name) =>
        CartographerPaths.InState(name);

    private static string Adopt(
        string name, string legacyName, Func<string, bool> isUsable, ManualLogSource log) =>
        MarkerFile.Adopt(
            CartographerPaths.Root, name, legacyName, isUsable,
            why => log.LogWarning(
                $"Moving this mod's own bookkeeping out of your settings folder did not finish: {why}."));
}
