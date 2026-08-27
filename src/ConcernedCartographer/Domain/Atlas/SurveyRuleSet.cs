using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>One survey rule: a prefab-name pattern (exact, or prefix with a
/// trailing '*'), the pin suggestion it produces, and its bounds.</summary>
internal sealed class SurveyRule
{
    public SurveyRule(string pattern, string iconId, string category, float duplicateRadiusMeters, float expiryMinutes)
    {
        Pattern = pattern;
        IconId = iconId;
        Category = category;
        DuplicateRadiusMeters = duplicateRadiusMeters;
        ExpiryMinutes = expiryMinutes;
    }

    public string Pattern { get; }
    public string IconId { get; }
    public string Category { get; }
    public float DuplicateRadiusMeters { get; }
    public float ExpiryMinutes { get; }
}

/// <summary>The shareable survey configuration: ordered rules plus
/// blacklist patterns ('!pattern' rows). The serialized form contains only
/// patterns and suggestions — no machine paths, no secrets — so the file
/// itself is the import/export format. Matching precedence: blacklist
/// first, then exact matches, then the longest matching prefix.</summary>
internal sealed class SurveyRuleSet
{
    public const string Header = "# ConcernedCartographer survey rules v1";

    private readonly List<SurveyRule> _rules = new();
    private readonly List<string> _blacklist = new();

    public IReadOnlyList<SurveyRule> Rules => _rules;
    public IReadOnlyList<string> Blacklist => _blacklist;

    public void AddRule(SurveyRule rule)
    {
        _rules.Add(rule);
    }

    public void AddBlacklist(string pattern)
    {
        _blacklist.Add(pattern);
    }

    public bool TryMatch(string? prefabName, out SurveyRule match)
    {
        match = null!;
        if (string.IsNullOrEmpty(prefabName))
        {
            return false;
        }

        string name = Clean(prefabName!);
        foreach (string blocked in _blacklist)
        {
            if (PatternMatches(blocked, name))
            {
                return false;
            }
        }

        SurveyRule? best = null;
        int bestSpecificity = -1;
        foreach (SurveyRule rule in _rules)
        {
            if (!PatternMatches(rule.Pattern, name))
            {
                continue;
            }

            // Exact matches outrank prefixes; longer prefixes outrank shorter.
            int specificity = rule.Pattern.EndsWith("*", StringComparison.Ordinal)
                ? rule.Pattern.Length - 1
                : int.MaxValue;
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                best = rule;
            }
        }

        if (best is null)
        {
            return false;
        }

        match = best;
        return true;
    }

    public IEnumerable<string> Serialize()
    {
        yield return Header;
        yield return "# pattern<TAB>icon<TAB>category<TAB>duplicate-radius-m<TAB>expiry-minutes ('pattern*' = prefix, '!pattern' = never pin)";
        foreach (string blocked in _blacklist)
        {
            yield return "!" + blocked;
        }

        foreach (SurveyRule rule in _rules)
        {
            yield return string.Join(
                "\t",
                rule.Pattern,
                rule.IconId,
                rule.Category,
                rule.DuplicateRadiusMeters.ToString("R", CultureInfo.InvariantCulture),
                rule.ExpiryMinutes.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public static SurveyRuleSet Parse(IEnumerable<string> lines, out int malformedRows)
    {
        var set = new SurveyRuleSet();
        malformedRows = 0;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("!", StringComparison.Ordinal))
            {
                string blocked = Clean(line.Substring(1));
                if (blocked.Length == 0)
                {
                    malformedRows++;
                }
                else
                {
                    set.AddBlacklist(blocked);
                }

                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 5 ||
                parts[0].Trim().Length == 0 ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
                radius < 0f || radius > 500f ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float expiry) ||
                expiry < 0f || expiry > 24f * 60f)
            {
                malformedRows++;
                continue;
            }

            set.AddRule(new SurveyRule(Clean(parts[0]), parts[1].Trim(), parts[2].Trim(), radius, expiry));
        }

        return set;
    }

    /// <summary>The starter rule file written when none exists: safe,
    /// conservative examples the player can edit.</summary>
    public static SurveyRuleSet Default()
    {
        var set = new SurveyRuleSet();
        set.AddRule(new SurveyRule("rock4_copper*", "cc:resource", "Resources", 40f, 60f));
        set.AddRule(new SurveyRule("rock3_silver*", "cc:resource", "Resources", 40f, 60f));
        set.AddRule(new SurveyRule("mudpile*", "cc:resource", "Resources", 40f, 60f));
        set.AddRule(new SurveyRule("crypt*", "cc:danger", "Danger", 80f, 120f));
        set.AddBlacklist("piece_*");
        return set;
    }

    public static string Clean(string prefabName)
    {
        return prefabName.Replace("(Clone)", "").Trim().ToLowerInvariant();
    }

    private static bool PatternMatches(string pattern, string cleanedName)
    {
        string cleanedPattern = pattern.Trim().ToLowerInvariant();
        if (cleanedPattern.EndsWith("*", StringComparison.Ordinal))
        {
            return cleanedName.StartsWith(cleanedPattern.Substring(0, cleanedPattern.Length - 1), StringComparison.Ordinal);
        }

        return cleanedName == cleanedPattern;
    }
}
