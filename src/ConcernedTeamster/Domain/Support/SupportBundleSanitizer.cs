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

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:[\\/](?:[^\\/\r\n:*?""<>|\s]+[\\/])*[^\\/\r\n:*?""<>|\s]*",
        RegexOptions.Compiled);

    private static readonly Regex UnixPath = new(
        @"(?<![\w.<])/(?:[^/\s]+/)+[^/\s]*", RegexOptions.Compiled);

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
