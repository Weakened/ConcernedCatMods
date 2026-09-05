using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedTeamster.Domain.Localization;

/// <summary>The localization catalog for Concerned Teamster's user-facing
/// strings (CT-032), mirroring the pattern proven in Concerned Cartographer:
/// English defaults, override loading from a translator file, a template
/// generator, and English fallback so a partial translation can never blank
/// the UI. Additions over the Cartographer version: a missing-key is tracked
/// so the caller can log it ONCE (fallback to the key text meanwhile), and
/// overrides whose <c>{n}</c> placeholders do not match the English are
/// rejected at parse time, not left to fail at format time. Pure and
/// game-free; the adapter wires file IO and the game language.</summary>
public static class TeamsterStrings
{
    public const string TemplateHeader =
        "# ConcernedTeamster strings v1 — key<TAB>translation (missing keys fall back to English)";

    // English catalog. Keys are stable contracts (namespaced by surface);
    // values may carry {0},{1}... placeholders resolved by Format.
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // Route picker (CT-022)
        ["routes.pick"] = "Pick a route to profile.",
        ["routes.selected"] = "Selected: {0}",
        ["routes.lostSelection"] = "Selected route is no longer available in Cartographer — pick another.",
        ["routes.none"] = "No routes in this world yet — draw one in Cartographer.",
        ["routes.unreadable"] = "Cartographer routes are not readable right now.",
        ["routes.unreadableCleared"] = "Cartographer routes are not readable right now — selection cleared.",
        ["routes.unnamed"] = "(unnamed route)",
        ["routes.noGeometry"] = "(no usable geometry)",

        // Route report (CT-024)
        ["report.title"] = "Route report: {0}",
        ["report.needProfile"] = "Select a route and let it finish profiling first.",
        ["report.noProblems"] =
            "No problem sections: every sampled grade stays under {0}% and nothing went unsampled.",
        ["report.loadUnavailableNoModel"] = "Load advice unavailable: no calibration data loaded.",
        ["report.loadUnavailableNoGrade"] = "Load advice unavailable: no sampled grade data yet.",
    };

    private static Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _missingReported = new(StringComparer.Ordinal);

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> Defaults => English;

    public static bool HasKey(string key) => English.ContainsKey(key);

    public static void LoadOverrides(Dictionary<string, string> overrides)
    {
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Resolves a key: a valid override wins, else the English
    /// default, else the key text itself. A key with no English default is a
    /// programming error, not a translation gap — <paramref name="known"/>
    /// exposes that so a caller MAY warn once (via
    /// <see cref="ShouldReportMissing"/>); the presenters that resolve keys
    /// today are pure and reference only existing keys, so the one-shot log
    /// is wired as the broader presenter migration lands.</summary>
    public static string Get(string key, out bool known)
    {
        known = English.ContainsKey(key);
        if (_overrides.TryGetValue(key, out string? translated) && !string.IsNullOrEmpty(translated))
        {
            return translated;
        }

        return known ? English[key] : key;
    }

    public static string Get(string key) => Get(key, out _);

    /// <summary>Resolves and formats a key. A broken translation (bad
    /// placeholders) falls back to the English format; a NaN of arguments
    /// never crashes the UI.</summary>
    public static string Format(string key, params object[] arguments)
    {
        string template = Get(key, out _);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            return English.TryGetValue(key, out string? english)
                ? string.Format(CultureInfo.InvariantCulture, english, arguments)
                : key;
        }
    }

    /// <summary>Records that a key had no English default, returning true
    /// only the first time so the caller logs it exactly once.</summary>
    public static bool ShouldReportMissing(string key)
    {
        return _missingReported.Add(key);
    }

    /// <summary>The set of placeholder indices in a string (e.g. {0},{2} →
    /// {0,2}). Used to validate that a translation keeps the same slots.</summary>
    public static SortedSet<int> PlaceholderIndices(string value)
    {
        var indices = new SortedSet<int>();
        foreach (Match match in PlaceholderPattern.Matches(value ?? string.Empty))
        {
            if (int.TryParse(match.Groups[1].Value, out int index))
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    /// <summary>The translator template: header plus every key with its
    /// English text, tab-separated with newlines/tabs escaped.</summary>
    public static IEnumerable<string> TranslatorTemplate()
    {
        yield return TemplateHeader;
        foreach (KeyValuePair<string, string> entry in English)
        {
            yield return entry.Key + "\t" + Escape(entry.Value);
        }
    }

    /// <summary>Parses a translation file. A row is skipped (and counted)
    /// when it is malformed, names an unknown key, or its placeholders do not
    /// match the English default — a placeholder mismatch would format wrong
    /// or throw, so it never enters the override set.</summary>
    public static Dictionary<string, string> ParseOverrides(IEnumerable<string> lines, out int skippedRows)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        skippedRows = 0;
        foreach (string rawLine in lines ?? Array.Empty<string>())
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                skippedRows++;
                continue;
            }

            string key = line.Substring(0, tab);
            if (!English.TryGetValue(key, out string? english))
            {
                skippedRows++;
                continue;
            }

            string value = Unescape(line.Substring(tab + 1));
            if (!PlaceholderIndices(value).SetEquals(PlaceholderIndices(english)))
            {
                skippedRows++;
                continue;
            }

            overrides[key] = value;
        }

        return overrides;
    }

    // Percent-encoding (mirroring the invertible scheme proven in
    // Concerned Cartographer's AtlasText — reimplemented here, not
    // referenced, to keep the products independent). Single-pass and fully
    // round-trippable: unlike sequential \-escapes, a backslash before a
    // 't'/'n'/'r' cannot be mis-decoded because only "%HH" triples decode.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '%': builder.Append("%25"); break;
                case '\t': builder.Append("%09"); break;
                case '\n': builder.Append("%0A"); break;
                case '\r': builder.Append("%0D"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '%' && index + 2 < value.Length &&
                TryParseHex(value[index + 1], value[index + 2], out char decoded))
            {
                builder.Append(decoded);
                index += 2;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool TryParseHex(char high, char low, out char value)
    {
        value = '\0';
        if (!TryHexDigit(high, out int hi) || !TryHexDigit(low, out int lo))
        {
            return false;
        }

        value = (char)((hi << 4) | lo);
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        value = 0;
        return false;
    }
}
