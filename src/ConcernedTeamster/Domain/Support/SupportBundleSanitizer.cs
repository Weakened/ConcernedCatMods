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
    // generic digit mask (the kept file name is still masked afterwards).
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
        result = WindowsPath.Replace(result, "<path>/$1");
        result = UnixPath.Replace(result, "<path>/$1");
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
