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

    /// <summary>Everywhere this marker has lived, <b>newest first</b>.
    ///
    /// <c>author-id.dat</c> in the product directory is the build between (commit
    /// <c>6903a65</c>) that changed the extension without moving the file — the
    /// change #343 shipped and #351 replaced. <c>author-id.txt</c> is the
    /// original. Newest first because where both exist, the later build's is the
    /// value the atlas was most recently keyed on.
    ///
    /// <b>Literals, deliberately.</b> These were written as
    /// <c>"author-id" + MarkerFile.Extension</c>, which meant a future change of
    /// that constant would silently delete a historical location from this list
    /// and mint a new identity for exactly the profiles the migration exists for.
    /// A fact about the past cannot be derived from a value that can change.
    /// </summary>
    private static readonly string[] PriorFileNames = { "author-id.dat", "author-id.txt" };

    private static string? _cached;

    /// <summary>Where the onboarding tip's marker lives. It is named here only
    /// because both markers are adopted in one place.</summary>
    internal static class OnboardingMarker
    {
        public const string FileName = "onboarding-shown" + MarkerFile.Extension;

        public static readonly string[] PriorFileNames =
            { "onboarding-shown.dat", "onboarding-shown.txt" };

        public static string Path => MarkerPath(FileName);

        /// <summary>A marker has to say something. <c>_ =&gt; true</c> accepted a
        /// zero-byte file as a usable value, which is the very "existence is not
        /// adoption" mistake this migration was written to remove — behind a
        /// predicate that could not say no. A torn write then permanently ended
        /// the migration and suppressed the #264 introduction for good.</summary>
        public static bool IsRecorded(string? contents) => !string.IsNullOrWhiteSpace(contents);
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
        // One guard EACH. A shared try meant any failure resolving the author
        // marker skipped the onboarding marker entirely — which is exactly the
        // outcome the paragraph above says adopting them together prevents.
        Adopt(log, () => Resolve(FileName, PriorFileNames, IsIdentity, log, out _));
        Adopt(log, () => Resolve(
            OnboardingMarker.FileName, OnboardingMarker.PriorFileNames,
            OnboardingMarker.IsRecorded, log, out _));
    }

    private static void Adopt(ManualLogSource log, Func<string?> resolve)
    {
        try
        {
            resolve();
        }
        catch (Exception exception)
        {
            log.LogWarning(
                "Moving this mod's own bookkeeping out of your settings folder could not be " +
                "attempted: " + SafeLogText.Brief(exception));
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
            MarkerFile.MarkerSearch search = MarkerFile.Resolve(
                CartographerPaths.Root, FileName, PriorFileNames, IsIdentity, Warn(log),
                out string path, out string? found);

            if (search == MarkerFile.MarkerSearch.Found && found is not null)
            {
                _cached = found.Trim();
                return _cached;
            }

            if (search == MarkerFile.MarkerSearch.PriorFileUnread)
            {
                // A file from an older build is sitting right there and could not
                // be read. Creating an identity now would write a marker that
                // reads as usable on every later start, and nothing would ever
                // look at that file again — one bad moment, orphaned for good.
                //
                // Empty, not cached: audit labels stay blank for this session and
                // the next start tries again. PinStore leaves LastAuthor alone
                // when this is empty, so nothing is mis-attributed meanwhile.
                log.LogWarning(
                    "An author identity from an older build is present but could not be read, so " +
                    "this session adds no audit labels rather than starting a second identity. " +
                    "It will be tried again next time.");
                return "";
            }

            string created = Guid.NewGuid().ToString("N");
            if (!MarkerFile.TryWrite(path, created, Warn(log)))
            {
                // Empty rather than the unsaved value. Returning it stamped a
                // throwaway identity into pin and route records as OwnerAuthor —
                // which is not guarded the way LastAuthor is — and the next
                // session's different GUID then made the player's own deletes be
                // refused as somebody else's for good.
                log.LogWarning(
                    "A new author identity could not be saved, so this session adds no audit " +
                    "labels rather than using one that will not come back.");
                return "";
            }

            _cached = created;
            return created;
        }
        catch (Exception exception)
        {
            log.LogWarning($"Could not persist an author identity; audit labels stay empty this session: {SafeLogText.Brief(exception)}");

            // Deliberately not cached: if the failure was transient, the next
            // caller should get the real answer rather than a permanent blank.
            return "";
        }
    }

    /// <summary>What is known about the onboarding marker, answered once.</summary>
    internal readonly struct OnboardingMarkerState
    {
        public OnboardingMarkerState(bool alreadyShown, string path)
        {
            AlreadyShown = alreadyShown;
            Path = path;
        }

        /// <summary>A marker with something in it was found, here or in a prior
        /// location that has now been adopted.</summary>
        public bool AlreadyShown { get; }

        /// <summary>Where the marker belongs.</summary>
        public string Path { get; }
    }

    /// <summary>Whether the first-run tip has already been shown.
    ///
    /// Goes through <see cref="MarkerFile"/> rather than a bare
    /// <c>File.Exists</c>, so a veteran whose older build's marker has not been
    /// adopted yet is not told the tip is still owed — and so a zero-byte marker
    /// does not count as having been shown.</summary>
    internal static OnboardingMarkerState FindOnboardingMarker(ManualLogSource log)
    {
        MarkerFile.MarkerSearch search = MarkerFile.Resolve(
            CartographerPaths.Root,
            OnboardingMarker.FileName,
            OnboardingMarker.PriorFileNames,
            OnboardingMarker.IsRecorded,
            Warn(log),
            out string path,
            out _);

        return new OnboardingMarkerState(search == MarkerFile.MarkerSearch.Found, path);
    }

    /// <summary>Records that the tip was shown, staged and verified like every
    /// other marker write.</summary>
    internal static bool RecordOnboardingShown(string? path, ManualLogSource log) =>
        !string.IsNullOrEmpty(path) &&
        MarkerFile.TryWrite(path!, DateTime.UtcNow.ToString("o"), Warn(log));

    private static bool IsIdentity(string? contents) =>
        contents is not null && Guid.TryParseExact(contents.Trim(), "N", out _);

    private static string MarkerPath(string name) =>
        CartographerPaths.InState(name);

    private static string? Resolve(
        string name,
        string[] priorNames,
        Func<string, bool> isUsable,
        ManualLogSource log,
        out string path)
    {
        MarkerFile.Resolve(
            CartographerPaths.Root, name, priorNames, isUsable, Warn(log),
            out path, out string? contents);
        return contents;
    }

    private static Action<string> Warn(ManualLogSource log) =>
        why => log.LogWarning(
            $"Moving this mod's own bookkeeping out of your settings folder did not finish: {why}.");
}
