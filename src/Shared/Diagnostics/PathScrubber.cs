using System.Text.RegularExpressions;

namespace TheConcernedCat.Diagnostics;

/// <summary>Scrubs a line of free text of anything that identifies the machine
/// or the player before it reaches a log, a console reply or a file somebody
/// shares (#411).
///
/// <b>Why this is shared source rather than a third copy.</b> Two products had
/// written this independently, because products never reference each other at
/// compile time. Then #388 fixed a defect in one and #410 had to fix the same
/// defect in the other, separately, having first had to notice it was there.
/// Concerned Foreman had no scrubber at all and was about to need a third. A
/// path pattern contains no Unity, BepInEx or Jötunn type and no product
/// namespace, so it satisfies the rule for <c>src/Shared/</c> exactly, and
/// compiling it into each consumer as source keeps "products never reference
/// each other" intact.
///
/// <b>One thing is parameterised, and it is a real difference rather than a
/// preference.</b> Whether the path's last component survives. A composer that
/// only ever builds strings from known-safe fixed parts can keep a file name for
/// diagnostics; a scrubber whose input is the raw log tail cannot, because a leaf
/// file name can itself be the identifying content. Both callers state which they
/// are and why.
///
/// <b>The space rule, and the two directions it has to be right in.</b> A space
/// is admitted inside a path segment, and the discriminator between "this folder
/// name has more words in it" and "the path ended and a sentence began" is COUNT,
/// not case: at most two space-joined tokens per segment. Two covers every real
/// multi-word folder in these paths — `Thunderstore Mod Manager`, `Documents and
/// Settings`, `Program Files (x86)`, `Application Support`, `Eren cansunar` — and
/// refuses the longer runs prose produces.
///
/// An earlier version tested case, and it was wrong both ways at once. Against
/// privacy: a token beginning lower case refused the run, so a user name with a
/// space in it kept the surname, the folder layout and the profile name. Against
/// diagnostics: a capitalised run had no length limit, so
/// `wrote C:\a\b OK See BepInEx/LogOutput.log` collapsed and took the pointer to
/// the log file with it. Counting tokens fixes both and needs no case class.
///
/// <b>Stated limits</b>, because they are real and a caller's own documentation
/// has to name them:
///
/// 1. A path ending at a FOLDER followed by at most two capitalised words loses
///    those words. A diagnostics loss, and the direction this errs on purpose.
/// 2. <b>A segment of four or more words is not covered, and when that segment is
///    the account folder this is a USER-NAME leak rather than a diagnostics
///    loss.</b> `C:\Users\Eren Can Sunar Jr\AppData\…` keeps `Can Sunar Jr` and
///    everything after it, including the profile name. A three-word name is
///    covered; a four-word one is not. Raising the cap would buy that back and
///    spend it on prose, which is the trade the cap exists to make, so it is
///    named here rather than quietly widened.
/// 3. An extension of more than eight characters, or one ending in a
///    non-alphanumeric, is not recognised as an extension.
/// 4. UNC paths are scrubbed from their unambiguous double-backslash root.
///    Relative Windows paths are scrubbed when they have at least two
///    separators; their first segment cannot contain a space, because allowing
///    a free-text run there would consume the prose before a path.
/// 5. A single quote is not excluded from a segment, so with
///    <c>keepFileName: false</c> a quoted path swallows its closing quote and the
///    sentence's full stop: <c>…path 'C:\a\x.tsv'.</c> becomes
///    <c>…path '&lt;path&gt;</c>. Cosmetic, and pre-existing in both products.
/// </summary>
/// <b>Internal, like every other shared-source type here.</b> This is compiled
/// into each consumer rather than referenced, so each assembly gets its own copy
/// and none of them publishes it. That is not a style choice: ConcernedNPC is a
/// LIBRARY whose public surface is pinned by `PublicSurfaceTests` and costs a
/// version bump to change, and a `public` type here would have silently become
/// part of that package's API the moment the library compiled this folder. The
/// test caught it; `internal` is the fix, and it matches `src/Shared/Workers`
/// and `src/Shared/Interop`.
internal static class PathScrubber
{
    /// <summary>The longest line any caller keeps. Past this it is truncated
    /// rather than sent, because an unbounded line in a report is its own
    /// problem.</summary>
    public const int DefaultMaxLength = 2000;

    private const string WindowsSegmentChar = @"[^\\/\r\n:*?""<>|\s\uE000-\uE003]";
    private const string UnixSegmentChar = @"[^/\s]";
    private const string UnixRunChar = @"[^/<>\s]";
    private const string WindowsFinalRunChar = @"[^\\/\r\n:*?""<>|\s.,;\uE000-\uE003]";
    private const string UnixFinalRunChar = @"[^/<>\r\n\s.,;]";

    private const string TokenCap = "{0,2}";

    private const string WindowsChainRun = "(?: " + WindowsSegmentChar + "+)" + TokenCap;
    private const string UnixChainRun = "(?: " + UnixRunChar + "+)" + TokenCap;
    private const string WindowsFinalRun = "(?: " + WindowsFinalRunChar + "+)" + TokenCap;
    private const string UnixFinalRun = "(?: " + UnixFinalRunChar + "+)" + TokenCap;

    // A path ending in a FILE name takes no run at all, which is what keeps the
    // sentence after it. `:` is deliberately not an end marker: a run that
    // reached the next path's drive letter consumed the `D` of `D:\…` and left
    // that whole second path unscrubbed.
    private const string NoFileExtension = @"(?<!\.[A-Za-z0-9]{1,8})";
    private const string PathEnd = @"(?=$|[\r\n'""`,;])";

    // Order matters: URLs before paths (URLs contain slashes), coordinates
    // before the long-digit mask so the marker stays readable, paths before the
    // IP and digit masks.
    private static readonly Regex Urls = new(
        @"\b(?:https?|wss?|ftp)://\S+", RegexOptions.Compiled);

    private static readonly Regex CoordinatePairs = new(
        @"\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)?\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"[\\/])*("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    // A UNC path has no drive-letter anchor. Require a server plus share;
    // the share itself is the kept final component at a bare UNC root. A
    // leading double backslash is unambiguous and needs no prose guard.
    private static readonly Regex WindowsUncPath = new(
        @"\\\\(?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"\\){1,}("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    // A relative Windows path has no root anchor. The first segment is the
    // token immediately next to its first backslash (no space-run there), then
    // at least one more directory segment is required. That chooses `plugins`
    // rather than the prose before `plugins\\ConcernedCatMods\\...`, while the
    // start guard keeps regex text such as `\d+\.\d+` out because every
    // apparent segment there starts after `\`.
    private static readonly Regex WindowsRelativePath = new(
        @"(?<![\\/\w.<>])" + WindowsSegmentChar + @"+\\(?:"
            + WindowsSegmentChar + "+" + WindowsChainRun + @"\\){1,}("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension
            + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex FileThenProseBeforeRelative = new(
        @"\.[A-Za-z0-9]{1,8}(?: " + WindowsSegmentChar + @"+)+ $",
        RegexOptions.Compiled);

    // Refuses to start at a `/` that directly follows `>`: that is this
    // scrubber's own marker, never a separator in the original text.
    private static readonly Regex UnixPath = new(
        @"(?<![\w.<>])/(?:" + UnixSegmentChar + "+" + UnixChainRun + "/)+("
            + UnixSegmentChar + "*)(?:" + NoFileExtension + UnixFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex UsersFragment = new(
        @"\bUsers[\\/][^\\/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Valheim save-file names ARE world and character names (MyWorld.db,
    // Eren.fch), so path scrubbing alone is not enough for them even when it
    // keeps a file name on purpose.
    private static readonly Regex SaveFileNames = new(
        @"[^\\/\s""']+\.(db|fwl|fch)(\.old|\.bak)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Ipv4 = new(
        @"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b", RegexOptions.Compiled);

    private static readonly Regex HexBlob = new(
        @"\b[A-Fa-f0-9]{32,}\b", RegexOptions.Compiled);

    private static readonly Regex TokenBlob = new(
        @"[A-Za-z0-9+/=_\-]{40,}", RegexOptions.Compiled);

    private static readonly Regex LongDigits = new(@"\d{7,}", RegexOptions.Compiled);

    // Relative paths run first so a rooted-path match cannot absorb prose
    // and a later relative path into one span. Private markers protect actual
    // rooted spans from that pass. A separate continuation marker preserves the
    // documented four-word-segment limit when its residual tail looks relative.
    // Markers are per-input string tokens, not fixed characters. The private-use
    // prefix grows until it is absent from the input, so raw user text cannot
    // impersonate scrubber state or be removed as if it were internal data.
    private static void CreateMarkerTokens(
        string text,
        out string cappedContinuation,
        out string rootedPathStart,
        out string rootedPathEnd)
    {
        string prefix = "\uE000";
        while (text.IndexOf(prefix, System.StringComparison.Ordinal) >= 0)
        {
            prefix += "\uE000";
        }

        cappedContinuation = prefix + "\uE001";
        rootedPathStart = prefix + "\uE002";
        rootedPathEnd = prefix + "\uE003";
    }

    private static bool IsWindowsSegmentCharacter(char value) =>
        !char.IsWhiteSpace(value)
        && "\\/\r\n:*?\"<>|".IndexOf(value) < 0
        && (value < '\uE000' || value > '\uE003');

    private static bool HasCappedContinuation(string text, int index)
    {
        int tokens = 0;
        while (index < text.Length && text[index] == ' ')
        {
            index++;
            int tokenStart = index;
            while (index < text.Length && IsWindowsSegmentCharacter(text[index]))
            {
                index++;
            }

            if (index == tokenStart)
            {
                return false;
            }

            tokens++;
            if (index < text.Length && text[index] == '\\')
            {
                return tokens >= 3;
            }
        }

        return false;
    }

    private static string MarkRootedWindowsPaths(
        Regex pattern,
        string text,
        string cappedContinuation,
        string rootedPathStart,
        string rootedPathEnd) =>
        pattern.Replace(text, match =>
        {
            foreach (Match relative in WindowsRelativePath.Matches(match.Value))
            {
                if (relative.Index > 0
                    && FileThenProseBeforeRelative.IsMatch(
                        match.Value.Substring(0, relative.Index)))
                {
                    return rootedPathStart
                        + match.Value.Substring(0, relative.Index)
                        + rootedPathEnd
                        + match.Value.Substring(relative.Index);
                }
            }

            return rootedPathStart
                + match.Value
                + rootedPathEnd
                + (HasCappedContinuation(text, match.Index + match.Length)
                    ? cappedContinuation
                    : "");
        });

    private static bool IsCappedContinuationGap(
        string text,
        int marker,
        int markerLength,
        int matchIndex)
    {
        for (int index = marker + markerLength; index < matchIndex; index++)
        {
            char value = text[index];
            if (value != ' ' && !IsWindowsSegmentCharacter(value))
            {
                return false;
            }
        }

        return matchIndex > marker + markerLength;
    }

    private static string ReplaceRelativeWindowsPaths(
        string text,
        string replacement,
        string cappedContinuation,
        string rootedPathStart,
        string rootedPathEnd)
    {
        int usedMarker = -1;
        string scrubbed = WindowsRelativePath.Replace(text, match =>
        {
            int lineStart = match.Index;
            while (lineStart > 0
                && text[lineStart - 1] != '\r'
                && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            int rootedStart = match.Index > lineStart
                ? text.LastIndexOf(
                    rootedPathStart,
                    match.Index - 1,
                    match.Index - lineStart,
                    System.StringComparison.Ordinal)
                : -1;
            int rootedEnd = match.Index > lineStart
                ? text.LastIndexOf(
                    rootedPathEnd,
                    match.Index - 1,
                    match.Index - lineStart,
                    System.StringComparison.Ordinal)
                : -1;

            if (rootedStart > rootedEnd)
            {
                return match.Value;
            }

            int marker = match.Index > lineStart
                ? text.LastIndexOf(
                    cappedContinuation,
                    match.Index - 1,
                    match.Index - lineStart,
                    System.StringComparison.Ordinal)
                : -1;

            if (marker >= lineStart
                && marker != usedMarker
                && IsCappedContinuationGap(
                    text,
                    marker,
                    cappedContinuation.Length,
                    match.Index))
            {
                usedMarker = marker;
                return match.Value;
            }

            return match.Result(replacement);
        });

        return scrubbed
            .Replace(cappedContinuation, "")
            .Replace(rootedPathStart, "")
            .Replace(rootedPathEnd, "");
    }

    /// <summary>Scrubs one line.</summary>
    /// <param name="text">Anything. Null and empty answer with the empty
    /// string, so no caller needs a null check of its own.</param>
    /// <param name="keepFileName">Whether a matched path's last component
    /// survives. <c>true</c> for a caller that composes its own strings from
    /// known-safe parts and wants the file name for diagnostics; <c>false</c>
    /// for a caller handling arbitrary text, where a leaf file name can itself
    /// be the identifying content.</param>
    /// <param name="maxLength">The line is truncated past this.</param>
    public static string Scrub(string? text, bool keepFileName, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        string windows = keepFileName ? "<path>/$1" : "<path>";
        string unix = windows;

        string result = text!;
        result = Urls.Replace(result, "<url>");
        result = CoordinatePairs.Replace(result, "(<pos>)");
        CreateMarkerTokens(
            result,
            out string cappedContinuation,
            out string rootedPathStart,
            out string rootedPathEnd);
        result = MarkRootedWindowsPaths(
            WindowsPath,
            result,
            cappedContinuation,
            rootedPathStart,
            rootedPathEnd);
        result = MarkRootedWindowsPaths(
            WindowsUncPath,
            result,
            cappedContinuation,
            rootedPathStart,
            rootedPathEnd);
        result = ReplaceRelativeWindowsPaths(
            result,
            windows,
            cappedContinuation,
            rootedPathStart,
            rootedPathEnd);
        result = WindowsPath.Replace(result, windows);
        result = WindowsUncPath.Replace(result, windows);
        result = UnixPath.Replace(result, unix);
        result = UsersFragment.Replace(result, "Users/<user>");
        result = SaveFileNames.Replace(result, "<save>.$1");
        result = Ipv4.Replace(result, "<ip>");
        result = HexBlob.Replace(result, "<hex>");
        result = TokenBlob.Replace(result, "<token>");
        result = LongDigits.Replace(result, "<n>");

        if (result.Length > maxLength)
        {
            result = result.Substring(0, maxLength) + "…[truncated]";
        }

        return result;
    }
}
