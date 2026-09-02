using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>One survey rule: a prefab-name pattern (exact, or prefix with a
/// trailing '*'), the pin suggestion it produces, its bounds, and whether
/// it is currently enabled (RC11 blocker 10: rules are edited from the
/// Survey UI; a disabled rule stays in the file but matches nothing).</summary>
internal sealed class SurveyRule
{
    public SurveyRule(string pattern, string iconId, string category, float duplicateRadiusMeters, float expiryMinutes, bool enabled = true)
    {
        Pattern = pattern;
        IconId = iconId;
        Category = category;
        DuplicateRadiusMeters = duplicateRadiusMeters;
        ExpiryMinutes = expiryMinutes;
        Enabled = enabled;
    }

    public string Pattern { get; }
    public string IconId { get; }
    public string Category { get; }
    public float DuplicateRadiusMeters { get; }
    public float ExpiryMinutes { get; }
    public bool Enabled { get; set; }
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

    /// <summary>Removes the rule at a UI index. Returns it for the
    /// confirmation message, or null when out of range.</summary>
    public SurveyRule? RemoveRuleAt(int index)
    {
        if (index < 0 || index >= _rules.Count)
        {
            return null;
        }

        SurveyRule removed = _rules[index];
        _rules.RemoveAt(index);
        return removed;
    }

    /// <summary>Enables/disables the rule at a UI index.</summary>
    public SurveyRule? SetRuleEnabled(int index, bool enabled)
    {
        if (index < 0 || index >= _rules.Count)
        {
            return null;
        }

        _rules[index].Enabled = enabled;
        return _rules[index];
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
            if (!rule.Enabled || !PatternMatches(rule.Pattern, name))
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
        yield return "# pattern<TAB>icon<TAB>category<TAB>duplicate-radius-m<TAB>expiry-minutes ('pattern*' = prefix, '!pattern' = never pin; an optional 6th field 'off' disables a rule)";
        foreach (string blocked in _blacklist)
        {
            yield return "!" + blocked;
        }

        foreach (SurveyRule rule in _rules)
        {
            // Enabled rules keep the 5-field RC10 shape (so untouched
            // starter files still normalize to the defaults); only a
            // disabled rule carries the 6th "off" field.
            string row = string.Join(
                "\t",
                rule.Pattern,
                rule.IconId,
                rule.Category,
                rule.DuplicateRadiusMeters.ToString("R", CultureInfo.InvariantCulture),
                rule.ExpiryMinutes.ToString("R", CultureInfo.InvariantCulture));
            yield return rule.Enabled ? row : row + "\toff";
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
            if (parts.Length < 5 || parts.Length > 6 ||
                parts[0].Trim().Length == 0 ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
                radius < 0f || radius > 500f ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float expiry) ||
                expiry < 0f || expiry > 24f * 60f)
            {
                malformedRows++;
                continue;
            }

            bool enabled = true;
            if (parts.Length == 6)
            {
                string state = parts[5].Trim().ToLowerInvariant();
                if (state == "off")
                {
                    enabled = false;
                }
                else if (state != "on" && state.Length != 0)
                {
                    malformedRows++;
                    continue;
                }
            }

            set.AddRule(new SurveyRule(Clean(parts[0]), parts[1].Trim(), parts[2].Trim(), radius, expiry, enabled));
        }

        return set;
    }

    /// <summary>The starter rule file written when none exists (broadened
    /// in RC10, feedback 10): a bounded, immediately useful set — common
    /// gatherables and wild seeds, ore deposits, dungeon entrances, lore
    /// runestones and boss vegvisirs — every one duplicate-radius and
    /// expiry bounded so a walk through a forest yields a handful of
    /// reviewable observations, never a flood. Players edit the file
    /// freely; it is the shareable import/export format.</summary>
    public static SurveyRuleSet Default()
    {
        var set = new SurveyRuleSet();

        // Gatherables (30 m duplicate radius, 2 h expiry).
        set.AddRule(new SurveyRule("raspberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("blueberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("cloudberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_mushroom*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_thistle*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_dandelion*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_flint*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_seedcarrot*", "cc:resource", "Resources", 40f, 120f));
        set.AddRule(new SurveyRule("pickable_seedturnip*", "cc:resource", "Resources", 40f, 120f));
        set.AddRule(new SurveyRule("pickable_seedonion*", "cc:resource", "Resources", 40f, 120f));
        set.AddRule(new SurveyRule("gucksack*", "cc:resource", "Resources", 40f, 120f));
        set.AddRule(new SurveyRule("beehive*", "cc:resource", "Resources", 60f, 240f));

        // Ore and metal deposits (40 m duplicate radius, 4 h expiry).
        set.AddRule(new SurveyRule("rock4_copper*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("minerock_tin*", "cc:mine", "Resources", 30f, 240f));
        set.AddRule(new SurveyRule("silvervein*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("minerock_obsidian*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("mudpile*", "cc:mine", "Resources", 40f, 240f));

        // Dungeon entrances and points of interest (8 h expiry).
        set.AddRule(new SurveyRule("crypt*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("sunkencrypt*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("trollcave*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("mountaincave*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("runestone*", "cc:objective", "Points of interest", 80f, 480f));
        set.AddRule(new SurveyRule("vegvisir*", "cc:objective", "Points of interest", 80f, 480f));

        set.AddBlacklist("piece_*");
        set.AddBlacklist("vfx_*");
        set.AddBlacklist("sfx_*");
        set.AddBlacklist("fx_*");
        return set;
    }

    /// <summary>The exact starter set shipped by RC8/RC9, used to recognize
    /// an untouched RC8-era starter file and upgrade it in place to the
    /// broadened RC10 set. A file the player edited never matches and is
    /// never touched.</summary>
    public static SurveyRuleSet Rc8StarterSet()
    {
        var set = new SurveyRuleSet();
        set.AddRule(new SurveyRule("raspberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("blueberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("cloudberrybush*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_mushroom*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("pickable_thistle*", "cc:resource", "Resources", 30f, 120f));
        set.AddRule(new SurveyRule("rock4_copper*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("minerock_tin*", "cc:mine", "Resources", 30f, 240f));
        set.AddRule(new SurveyRule("silvervein*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("minerock_obsidian*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("mudpile*", "cc:mine", "Resources", 40f, 240f));
        set.AddRule(new SurveyRule("crypt*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("sunkencrypt*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("trollcave*", "cc:dungeon", "Dungeons", 80f, 480f));
        set.AddRule(new SurveyRule("vegvisir*", "cc:objective", "Points of interest", 80f, 480f));
        set.AddBlacklist("piece_*");
        set.AddBlacklist("vfx_*");
        set.AddBlacklist("sfx_*");
        set.AddBlacklist("fx_*");
        return set;
    }

    /// <summary>The exact starter set shipped by v1.0 RCs before RC8, used
    /// to recognize an untouched starter file and upgrade it in place. A
    /// file the player edited never matches and is never touched.</summary>
    public static SurveyRuleSet LegacyStarterSet()
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
