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

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:[^\\/\r\n:*?""<>|\s]+[\\/])*([^\\/\r\n:*?""<>|\s]*)",
        RegexOptions.Compiled);

    private static readonly Regex UnixPath = new(
        @"(?<![\w.<])/(?:[^/\s]+/)+([^/\s]*)", RegexOptions.Compiled);

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
