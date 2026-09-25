using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedTeamster.Domain.Support;

/// <summary>Scrubs every line of a support bundle before it is composed
/// (CT-039). Recent log lines are the riskiest input here — they are
/// written for a developer reading BepInEx's own log, not for a bundle a
/// player might hand to someone else, and can embed a full path (this
/// mod's own warning lines do exactly that) or a raw world UID. Pattern-
/// scrubbing every line uniformly, regardless of source, means a future
/// caller does not have to remember to sanitize by hand — this is the
/// same defense-in-depth Concerned Cartographer's own crash-report
/// sanitizer applies, reimplemented independently here since the two
/// products share no compile-time reference.</summary>
public static class SupportBundleSanitizer
{
    public const int MaxLineLength = 2000;

    // Order matters, same reasoning as the sibling product's sanitizer:
    // URLs before paths (URLs contain slashes), coordinates before the
    // long-digit mask (so the marker stays readable), paths before the
    // generic digit mask. Unlike the sibling product's sanitizer, the
    // matched path's terminal segment is NOT retained in the replacement
    // (no "/$1"): Cartographer's composer only ever builds strings from
    // known-safe fixed components (never a raw log line), so keeping a
    // path's file name there is provably safe; this sanitizer's whole
    // purpose is scrubbing arbitrary free-text log lines it cannot make
    // that assumption about, and a future log line whose leaf filename
    // itself carried identifying text would otherwise leak it verbatim.
    // Sidecar/bundle file names are already surfaced separately and more
    // safely (bare file name, not a path, so this regex never matches it
    // at all, with only its digits masked by the pass below).
    private static readonly Regex Urls = new(
        @"\b(?:https?|wss?|ftp)://\S+", RegexOptions.Compiled);

    private static readonly Regex CoordinatePairs = new(
        @"\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)?\s*\)",
        RegexOptions.Compiled);

    // A path segment's own characters. The space runs below put whitespace
    // back in the two places it belongs (#410).
    private const string WindowsSegmentChar = @"[^\\/\r\n:*?""<>|\s]";
    private const string UnixSegmentChar = @"[^/\s]";

    // Mod-manager profile paths contain spaces - `...\Thunderstore Mod
    // Manager\DataFolder\...`, `/Library/Application Support/...`,
    // `.../profiles/My Test/...` - and forbidding whitespace outright stopped
    // the match at the first one, so `<path>` replaced the head and everything
    // from `Mod` onwards travelled verbatim: the profile name, the folder
    // layout, and whatever the player's own folders are called (#410). The user
    // name is before that point and always went, which is why this was a leak
    // rather than a breach - and why SupportBundleTests passed over it.
    //
    // This sanitizer's input is the reason it matters more here than in the
    // sibling product. LogTailRecorder hands it raw BepInEx log lines, and the
    // class comment above already records that this mod's own warning lines
    // embed full paths; a support bundle is then a file whose whole purpose is
    // to be handed to somebody else.
    //
    // A space is admitted inside a segment only when the token after it does
    // not start with a lower-case letter. `\p{Ll}` rather than `[a-z]`, because
    // the sibling product's first attempt at this rule was ASCII-only and so
    // silently excluded every non-Latin folder name - `Mein Ärger`, `Мои Моды` -
    // which is exactly the non-English-locale player it was meant to protect,
    // and excluded punctuation-led mod folders (`!Mods`, `Rock & Roll`) too.
    // Folders are named that way; English and German sentences are not.
    private const string NotLowerCase = @"[^\p{Ll}";

    private const string WindowsRunStart = NotLowerCase + @"\\/\r\n:*?""<>|\s.]";

    private const string UnixRunStart = NotLowerCase + @"/<>\s.]";

    // Inside the directory chain the run needs no further guard: the segment it
    // extends must still end at a separator, so a run that has wandered into
    // prose simply fails to match. Dots and commas are safe here.
    private const string WindowsChainRun =
        "(?: " + WindowsRunStart + WindowsSegmentChar + @"*)*";

    // `<` and `>` are excluded from the Unix run because Sanitize replaces in
    // sequence: by the time UnixPath runs, this scrubber's own `<path>` markers
    // are already in the text, and a run that may hold them crosses one.
    private const string UnixRunChar = @"[^/<>\s]";

    private const string UnixChainRun = "(?: " + UnixRunStart + UnixRunChar + @"*)*";

    // The FINAL component has no separator to anchor it, and it is where an
    // unguarded run does damage. The guards are the sibling product's, and
    // each closes a failure an independent review demonstrated there:
    //
    // 1. NoFileExtension - a path ending in a FILE name takes no run at all.
    //    Its purpose differs here, and that is worth being exact about: there
    //    is no kept terminal segment to protect (see the note above), so what
    //    it protects is the SENTENCE after the path. `wrote ...\b.cfg OK` lost
    //    the `OK`; `plugin.dll 0.9.0` lost the version; `b.cfg Cannot Be Read`
    //    and the German-locale `b.cfg Zugriffsverweigerung` lost the reason,
    //    .NET localizing its messages and German capitalizing nouns. A path
    //    ending in a folder is the only shape where a run buys any privacy.
    // 2. No dot, comma or semicolon in a run token, so a run cannot reach
    //    across `, retrying` or into `World.db` - which SaveFileNames below
    //    still has to see.
    // 3. PathEnd - the run is taken only where the path visibly ends: end of
    //    text, end of LINE, a quote, a comma or a semicolon. `:` is
    //    deliberately NOT an end marker. A segment may not hold a colon, so a
    //    run that reached the next path's drive letter stopped at it and
    //    consumed the `D` of `D:\...`, leaving that whole second path
    //    unscrubbed - strictly worse than the pattern being replaced.
    private const string WindowsFinalRunChar = @"[^\\/\r\n:*?""<>|\s.,;]";

    private const string UnixFinalRunChar = @"[^/<>\r\n\s.,;]";

    private const string WindowsFinalRun =
        "(?: " + WindowsRunStart + WindowsFinalRunChar + @"*)*";

    private const string UnixFinalRun = "(?: " + UnixRunStart + UnixFinalRunChar + @"*)*";

    private const string NoFileExtension = @"(?<!\.[A-Za-z0-9]{1,8})";

    private const string PathEnd = @"(?=$|[\r\n'""`,;])";

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"[\\/])*"
            + WindowsSegmentChar + "*(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    // Also refuses to start at a `/` that directly follows `>`: that is this
    // scrubber's own marker, never a separator in the original text.
    private static readonly Regex UnixPath = new(
        @"(?<![\w.<>])/(?:" + UnixSegmentChar + "+" + UnixChainRun + "/)+"
            + UnixSegmentChar + "*(?:" + NoFileExtension + UnixFinalRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex UsersFragment = new(
        @"\bUsers[\\/][^\\/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SaveFileNames = new(
        @"[^\\/\s""']+\.(db|fwl|fch)(\.old|\.bak)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Ipv4 = new(
        @"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b", RegexOptions.Compiled);

    private static readonly Regex HexBlob = new(
        @"\b[A-Fa-f0-9]{32,}\b", RegexOptions.Compiled);

    private static readonly Regex TokenBlob = new(
        @"[A-Za-z0-9+/=_\-]{40,}", RegexOptions.Compiled);

    // Catches both a raw world UID (ZNet.GetWorldUID(), a long integer)
    // and the Steam64 portion of a cart id ("<userId>:<id>") — Teamster
    // never reads or logs a human-readable world name at all, only this
    // numeric identifier, so masking every 7+ digit run closes that
    // surface completely rather than chasing each call site that logs one.
    private static readonly Regex LongDigits = new(
        @"\d{7,}", RegexOptions.Compiled);

    public static string Sanitize(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return "";
        }

        string result = line!;
        result = Urls.Replace(result, "<url>");
        result = CoordinatePairs.Replace(result, "(<pos>)");
        result = WindowsPath.Replace(result, "<path>");
        result = UnixPath.Replace(result, "<path>");
        result = UsersFragment.Replace(result, "Users/<user>");
        result = SaveFileNames.Replace(result, "<save>.$1");
        result = Ipv4.Replace(result, "<ip>");
        result = HexBlob.Replace(result, "<hex>");
        result = TokenBlob.Replace(result, "<token>");
        result = LongDigits.Replace(result, "<n>");

        if (result.Length > MaxLineLength)
        {
            result = result.Substring(0, MaxLineLength) + "…[truncated]";
        }

        return result;
    }
}
