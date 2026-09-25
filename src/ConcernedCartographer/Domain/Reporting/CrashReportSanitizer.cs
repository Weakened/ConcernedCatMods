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
    private const string WindowsSegmentChar = @"[^\\/\r\n:*?""<>|\s]";
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
    // A space is admitted inside a segment only when the token after it
    // starts like a folder name rather than like prose. The signal is
    // negative on purpose: anything BUT a lower-case letter. Folders are
    // `Mod Manager`, `Application Support`, `Program Files (x86)`,
    // `!Mods`, `Мои Моды`; English and German sentences continue in lower
    // case. `\p{Ll}` rather than `[a-z]` because an earlier ASCII-only
    // version of this rule silently excluded every non-Latin folder name,
    // which is exactly the non-English-locale player it was supposed to
    // protect.
    private const string NotLowerCase = @"[^\p{Ll}";

    // `<` and `>` are excluded from every run, on the Unix side as well as
    // the Windows side where a path segment could never hold them anyway.
    // Sanitize replaces in sequence, so by the time UnixPath runs the text
    // already contains this scrubber's own `<path>/` markers — and a run
    // that may hold `<` and `>` will happily cross one, swallow the file
    // name the Windows pass had just kept, and leave `<path><path>/x.cfg`.
    private const string WindowsRunStart =
        NotLowerCase + @"\\/\r\n:*?""<>|\s.]";

    private const string UnixRunStart = NotLowerCase + @"/<>\s.]";

    // The same hazard from the other side: a `/` that directly follows `>`
    // is this scrubber's own marker, never a path separator in the original
    // text, so UnixPath must not start there.
    private const string NotAfterAMarker = @"(?<![\w.<>])";

    private const string UnixRunChar = @"[^/<>\s]";

    // Inside the directory chain the run needs no further guard: the
    // segment it extends must still end at a separator, so a run that has
    // wandered into prose simply fails to match. Dots and commas are
    // therefore safe here — `My Mods V1.2\Valheim\x.cfg` is one path.
    private const string WindowsChainRun =
        "(?: " + WindowsRunStart + WindowsSegmentChar + @"*)*";

    private const string UnixChainRun =
        "(?: " + UnixRunStart + UnixRunChar + @"*)*";

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
    // 2. No dot, comma or semicolon in a run token, so a run cannot reach
    //    across `, retrying` or into `World.db`.
    // 3. PathEnd. The run is taken only where the path visibly ends: end
    //    of text, end of LINE (exception.ToString() is multi-line the
    //    moment there is a stack trace), a quote, a comma or a semicolon.
    //    `:` is deliberately NOT an end marker: a run that reached the
    //    next path's drive letter stopped at its colon and consumed the
    //    `D` of `D:\...`, which left that whole second path unscrubbed —
    //    strictly worse than the pattern being replaced.
    private const string WindowsFinalRunChar = @"[^\\/\r\n:*?""<>|\s.,;]";

    private const string UnixFinalRunChar = @"[^/<>\r\n\s.,;]";

    private const string WindowsFinalRun =
        "(?: " + WindowsRunStart + WindowsFinalRunChar + @"*)*";

    private const string UnixFinalRun =
        "(?: " + UnixRunStart + UnixFinalRunChar + @"*)*";

    private const string NoFileExtension = @"(?<!\.[A-Za-z0-9]{1,8})";

    private const string PathEnd = @"(?=$|[\r\n'""`,;])";

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:" + WindowsSegmentChar + "+" + WindowsChainRun + @"[\\/])*("
            + WindowsSegmentChar + "*)(?:" + NoFileExtension + WindowsFinalRun + PathEnd + ")?",
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

    public static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        string result = text!;
        result = Urls.Replace(result, "<url>");
        result = CoordinatePairs.Replace(result, "(<pos>)");
        result = WindowsPath.Replace(result, "<path>/$1");
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
