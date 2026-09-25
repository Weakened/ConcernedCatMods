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
    // may not contain, plus whitespace, which SpaceRun below puts back in
    // the one place it belongs.
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
    // A space inside a segment is admitted only when the token after it
    // starts like a folder name rather than like prose: upper case, a
    // digit, `_`, `-`, `(` or `[`. That is a heuristic and it is the whole
    // defence against the opposite failure — swallowing the sentence that
    // follows an unquoted path, which would destroy the diagnostic instead
    // of the privacy. English prose continues in lower case (`... b.txt was
    // not found, see the log/file for details.`), so it is left alone; a
    // folder whose name starts lower case AND contains a space (`steam
    // games`) is the stated limit, and the user name before it is still
    // scrubbed.
    private const string WindowsSpaceRun =
        @"(?: [A-Z0-9_\-(\[]" + WindowsSegmentChar + @"*)*";

    private const string UnixSpaceRun =
        @"(?: [A-Z0-9_\-(\[]" + UnixSegmentChar + @"*)*";

    // Inside the directory chain a space run needs no further guard: the
    // segment it extends must still end at a separator, so a run that has
    // wandered into prose simply fails to match. The FINAL component has no
    // separator to be anchored by, so a space run there is taken only when
    // the path visibly ends — end of text, a quote, or a character no path
    // may contain. That is what keeps `<path>/b.cfg Cannot be read.` intact
    // while `'...\Thunderstore Mod Manager'` is replaced whole.
    private const string PathEnd = @"(?=$|['""`:*?<>|])";

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:" + WindowsSegmentChar + "+" + WindowsSpaceRun + @"[\\/])*("
            + WindowsSegmentChar + "*)(?:" + WindowsSpaceRun + PathEnd + ")?",
        RegexOptions.Compiled);

    private static readonly Regex UnixPath = new(
        @"(?<![\w.<])/(?:" + UnixSegmentChar + "+" + UnixSpaceRun + "/)+("
            + UnixSegmentChar + "*)(?:" + UnixSpaceRun + PathEnd + ")?",
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
