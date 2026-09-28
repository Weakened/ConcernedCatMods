using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Client-side scrubber for every piece of free text that can
/// enter a crash report (#97). Exception messages and stack traces may
/// embed absolute paths (with machine usernames), file names carrying
/// world identifiers, URLs/hosts, IPs, coordinates, Steam-style numeric
/// IDs, or secret-shaped blobs — all are pattern-scrubbed before the
/// event exists, and every field is length-capped. Structural exclusion
/// does the rest: events are built only from allowlisted fields, so data
/// with no recognizable shape (a world or character name in prose) has
/// no field to travel in — the mod's own error messages never embed
/// user-authored content.</summary>
internal static class CrashReportSanitizer
{
    public const int MaxSubsystemLength = 60;
    public const int MaxMessageLength = 2000;
    public const int MaxStackLength = 8000;

    // Order matters and is fixed by Sanitize below: URLs before paths
    // (URLs contain slashes), coordinates before the long-digit mask
    // (so the marker stays readable), paths before IP/digit masks (the
    // kept file name is still masked afterwards).
    private static readonly Regex Urls = new(
        @"\b(?:https?|wss?|ftp)://\S+", RegexOptions.Compiled);

    private static readonly Regex CoordinatePairs = new(
        @"\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)?\s*\)",
        RegexOptions.Compiled);

    // A path segment's own characters: everything a Windows path segment
    // may not contain, plus whitespace, which the space runs below put back
    // in the two places it belongs.
    private const string WindowsSegmentChar = @"[^\\/\r\n:*?""<>|\s\uE000-\uE003]";
    private const string UnixSegmentChar = @"[^/\s]";

    // Mod-manager profile paths contain spaces — `...\Thunderstore Mod
    // Manager\DataFolder\...`, `/Library/Application Support/...`,
    // `.../profiles/My Test/...` — and forbidding whitespace outright
    // stopped the match at the first one, so the head of the path was
    // replaced and everything from `Mod` onwards travelled verbatim: the
    // profile name, the folder layout, and whatever a player's folders are
    // called (#388). The user name sits before that point and was scrubbed,
    // which is why this was a leak rather than a disaster.
    //
    // A space is admitted inside a segment, and the discriminator between
    // "this folder name has more words in it" and "the path ended and a
    // sentence began" is COUNT, not case: at most TWO space-joined tokens
    // per segment.
    //
    // Two is not arbitrary. It covers every real multi-word folder in the
    // paths this scrubber sees — `Thunderstore Mod Manager`, `Documents and
    // Settings`, `Program Files (x86)`, `Application Support`, `My Test`,
    // `Eren cansunar` — and refuses the longer runs prose produces.
    //
    // <b>Why not case (#410's review).</b> This rule tested case first, and
    // an independent review took that apart in two directions at once.
    // Against privacy: a token beginning lower case refused the run, so
    // `C:\Users\Eren cansunar\AppData\…` scrubbed to
    // `<path>/Users cansunar\AppData\…` and handed over a surname, a folder
    // layout and a profile name — `Documents and Settings` and lower-case
    // non-Latin folders (`Meine änderungen`, `Мои моды`) went the same way.
    // Against diagnostics: a capitalised run was admitted without limit, so
    // `wrote C:\a\b OK See BepInEx/LogOutput.log` became `wrote <path>/b`
    // and took the log pointer with it. The stated reason for `\p{Ll}` over
    // `[a-z]` was also simply wrong: a NEGATED ASCII class is BROADER, not
    // narrower — what excluded non-Latin names was the POSITIVE
    // `[A-Z0-9_\-(\[]` of the version before it. Counting tokens fixes both
    // directions and needs no case class, so the question does not arise.
    private const string TokenCap = "{0,2}";

    // `<` and `>` are excluded from every run, on the Unix side as well as
    // the Windows side where a path segment could never hold them anyway.
    // Sanitize replaces in sequence, so by the time UnixPath runs the text
    // already contains this scrubber's own `<path>/` markers — and a run
    // that may hold `<` and `>` will happily cross one, swallow the file
    // name the Windows pass had just kept, and leave `<path><path>/x.cfg`.
    //
    // The same hazard from the other side: a `/` that directly follows `>`
    // is this scrubber's own marker, never a path separator in the original
    // text, so UnixPath must not start there.
    private const string NotAfterAMarker = @"(?<![\w.<>])";

    private const string UnixRunChar = @"[^/<>\s]";

    // Inside the directory chain the run has a second guard for free: the
    // segment it extends must still end at a separator. Dots and commas are
    // therefore safe here — `My Mods V1.2\Valheim\x.cfg` is one path.
    private const string WindowsChainRun =
        "(?: " + WindowsSegmentChar + "+)" + TokenCap;

    private const string UnixChainRun = "(?: " + UnixRunChar + "+)" + TokenCap;

    // The FINAL component has no separator to be anchored by, and that is
    // where an unguarded run did real damage. Three guards, each closing a
    // failure an independent review demonstrated against the first version:
    //
    // 1. NoFileExtension. A path that ends in a FILE name takes no run at
    //    all. `wrote C:\a\b.cfg OK` kept `OK`; a run swallowed it, and so
    //    did it swallow a version (`plugin.dll 0.9.0`), a reason
    //    (`b.cfg Cannot Be Read`, `b.cfg Zugriffsverweigerung` — .NET
    //    localizes its messages and German capitalizes nouns) and the
    //    `.db` marker SaveFileNames needs (`Erens New World.db`). A path
    //    ending in a folder is the only one that can still run on, which
    //    is also the only shape where a run buys any privacy.
    //
    //    What it recognises is a dot plus one to eight ALPHANUMERICS, which
    //    is less than "a file name": `notes.configuration` (thirteen) and
    //    `b.cfg~` (ending in a tilde) are not seen as extensions, so a run
    //    may still follow them. Stated rather than left to be found.
    // 2. No dot, comma or semicolon in a run token, so a run cannot reach
    //    across `, retrying` or into `World.db`.
    // 3. PathEnd. The run is taken only where the path visibly ends: end
    //    of text, end of LINE (exception.ToString() is multi-line the
    //    moment there is a stack trace), a quote, a comma or a semicolon.
    //    `:` is deliberately NOT an end marker: a run that reached the
    //    next path's drive letter stopped at its colon and consumed the
    //    `D` of `D:\...`, which left that whole second path unscrubbed —
    //    strictly worse than the pattern being replaced.
    private const string WindowsFinalRunChar = @"[^\\/\r\n:*?""<>|\s.,;\uE000-\uE003]";

    private const string UnixFinalRunChar = @"[^/<>\r\n\s.,;]";

    private const string WindowsFinalRun =
        "(?: " + WindowsFinalRunChar + "+)" + TokenCap;

    private const string UnixFinalRun = "(?: " + UnixFinalRunChar + "+)" + TokenCap;

    private const string NoFileExtension = @"(?<!\.[A-Za-z0-9]{1,8})";

    private const string PathEnd = @"(?=$|[\r\n'""`,;])";

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"[\\/])*("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex WindowsUncPath = new(
        @"\\\\(?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"\\){1,}("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    // Relative Windows paths are intentionally backslash-only and need at
    // least two directory separators. A strict first segment starts beside
    // the first slash rather than swallowing prose; the boundary keeps regex
    // text such as `\d+\.\d+` untouched.
    private static readonly Regex WindowsRelativePath = new(
        @"(?<![\\/\w.<>])" + WindowsSegmentChar + @"+\\(?:"
            + WindowsSegmentChar + "+" + WindowsChainRun + @"\\){1,}("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension
            + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex FileThenProseBeforeRelative = new(
        @"\.[A-Za-z0-9]{1,8}(?: " + WindowsSegmentChar + @"+)+ $",
        RegexOptions.Compiled);

    private static readonly Regex UnixPath = new(
        NotAfterAMarker + "/(?:" + UnixSegmentChar + "+" + UnixChainRun + "/)+("
            + UnixSegmentChar + "*)(?:" + NoFileExtension + UnixFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex UsersFragment = new(
        @"\bUsers[\\/][^\\/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Ipv4 = new(
        @"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b", RegexOptions.Compiled);

    private static readonly Regex HexBlob = new(
        @"\b[A-Fa-f0-9]{32,}\b", RegexOptions.Compiled);

    private static readonly Regex TokenBlob = new(
        @"[A-Za-z0-9+/=_\-]{40,}", RegexOptions.Compiled);

    private static readonly Regex LongDigits = new(
        @"\d{7,}", RegexOptions.Compiled);

    // Valheim save-file names ARE world/character names (MyWorld.db/.fwl,
    // Eren.fch), so path scrubbing alone (which keeps file names for
    // diagnostics) is not enough for them.
    private static readonly Regex SaveFileNames = new(
        @"[^\\/\s""']+\.(db|fwl|fch)(\.old|\.bak)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Relative paths run first so a rooted-path match cannot absorb prose
    // and a later relative path into one span. Private markers protect actual
    // rooted spans and the documented four-word-segment residual during that pass.
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
        while (text.IndexOf(prefix, StringComparison.Ordinal) >= 0)
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
                    StringComparison.Ordinal)
                : -1;
            int rootedEnd = match.Index > lineStart
                ? text.LastIndexOf(
                    rootedPathEnd,
                    match.Index - 1,
                    match.Index - lineStart,
                    StringComparison.Ordinal)
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
                    StringComparison.Ordinal)
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

    public static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

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
            "<path>/$1",
            cappedContinuation,
            rootedPathStart,
            rootedPathEnd);
        result = WindowsPath.Replace(result, "<path>/$1");
        result = WindowsUncPath.Replace(result, "<path>/$1");
        result = UnixPath.Replace(result, "<path>/$1");
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
