using System;
using System.IO;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Runtime.Companions;
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
/// is generated and read by the mod alone. Files earlier builds left elsewhere
/// are adopted — read, checked, staged, copied, read back, and only then
/// removed.
///
/// <b>A new identity is the last resort, never a consequence of a bad
/// moment.</b> Minting one orphans everything the profile has ever shared: the
/// non-owner-delete policy stops recognising the player's own pins and routes,
/// and no later start can undo it. So this mints only after every prior
/// location has been looked at and none of them held something usable.</summary>
internal static class AuthorIdentity
{
    private const string FileName = "author-id" + MarkerFile.Extension;

    /// <summary>Everywhere this marker has lived, oldest first.
    ///
    /// <c>author-id.txt</c> is the original. <c>author-id.dat</c> in the
    /// product directory is the build between (commit <c>6903a65</c>) that
    /// changed the extension without moving the file — the change #343 shipped
    /// and #351 replaced. A profile that ran it has neither the original name
    /// nor the current path, so leaving that entry out silently mints a new
    /// identity for exactly the machines this migration exists for.</summary>
    private static readonly string[] LegacyFileNames = { "author-id.txt", "author-id" + MarkerFile.Extension };

    private static string? _cached;

    /// <summary>Where the onboarding tip's marker lives. It is named here only
    /// because both markers are adopted in one place.</summary>
    internal static class OnboardingMarker
    {
        public const string FileName = "onboarding-shown" + MarkerFile.Extension;

        public static readonly string[] LegacyFileNames =
            { "onboarding-shown.txt", "onboarding-shown" + MarkerFile.Extension };

        public static string Path => MarkerPath(FileName);
    }

    /// <summary>Both markers this product keeps, adopted together at startup.
    ///
    /// One place, because adopting them at different lifecycle points is how
    /// the first attempt left <c>onboarding-shown.txt</c> behind on any profile
    /// that was launched but never entered a world — half of #304 unfixed, on
    /// exactly the machines whose owner was only checking the update.
    ///
    /// Never throws: it runs before the crash hub attaches, so an exception
    /// here would take the whole plugin down with nothing to report it.
    /// </summary>
    public static void AdoptMarkers(ManualLogSource log)
    {
        try
        {
            Resolve(FileName, LegacyFileNames, IsIdentity, log, out _);
            Resolve(OnboardingMarker.FileName, OnboardingMarker.LegacyFileNames, _ => true, log, out _);
        }
        catch (Exception exception)
        {
            log.LogWarning(
                "Moving this mod's own bookkeeping out of your settings folder could not be attempted: " +
                SafeLogText.Brief(exception));
        }
    }

    public static string Get(ManualLogSource log)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            if (Resolve(FileName, LegacyFileNames, IsIdentity, log, out string path) is { } existing)
            {
                _cached = existing.Trim();
                return _cached;
            }

            string created = Guid.NewGuid().ToString("N");
            if (!MarkerFile.TryWrite(path, created, Warn(log)))
            {
                // Usable for this session, deliberately not cached and not
                // treated as settled: the next start looks again, and if the
                // real identity was merely unreadable for a moment it is still
                // there to be found.
                log.LogWarning(
                    "A new author identity could not be saved, so this session uses a temporary one " +
                    "and nothing is written over what may already be there.");
                return created;
            }

            _cached = created;
            return created;
        }
        catch (Exception exception)
        {
            log.LogWarning($"Could not persist an author identity; audit labels stay empty this session: {SafeLogText.Brief(exception)}");

            // Deliberately not cached. An empty identity turns off the sync
            // self-echo filter, which is how a profile starts re-ingesting its
            // own broadcast shares; if the failure was transient, the next
            // caller should get the real answer rather than a permanent blank.
            return "";
        }
    }

    private static bool IsIdentity(string? contents) =>
        contents is not null && Guid.TryParseExact(contents.Trim(), "N", out _);

    private static string MarkerPath(string name) =>
        Path.Combine(CartographerLegacyProbe.DataDirectory, MarkerFile.FolderName, name);

    private static string? Resolve(
        string name,
        string[] legacyNames,
        Func<string, bool> isUsable,
        ManualLogSource log,
        out string path)
    {
        MarkerFile.TryResolve(
            CartographerLegacyProbe.DataDirectory, name, legacyNames, isUsable, Warn(log),
            out path, out string? contents);
        return contents;
    }

    private static Action<string> Warn(ManualLogSource log) =>
        why => log.LogWarning(
            $"Moving this mod's own bookkeeping out of your settings folder did not finish: {why}.");
}
