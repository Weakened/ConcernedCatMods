using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC11 blockers 11/14: one mechanical prefab-name → human-name
/// policy for every surface that ever shows an object name (survey rows,
/// survey pins, quick pins, map labels). "RaspberryBush(Clone)" must read
/// "Raspberry Bush", never "Raspberrybush" and never engine garbage.
///
/// Mechanics: strip Unity clone/counter suffixes, split on underscores,
/// spaces, lower→Upper case boundaries and letter↔digit boundaries, drop
/// pure-digit tokens and the leading "pickable" noise token (when other
/// tokens remain), expand known all-lowercase compound prefab words
/// (silvervein → Silver Vein), and Title-case each word. Localized hover
/// names always outrank this (see QuickPinSuggester); this is the
/// fallback that must never be garbage.</summary>
internal static class NameHumanizer
{
    /// <summary>Known all-lowercase compound prefab words that case
    /// splitting cannot separate. Keys and values are single tokens
    /// (already cleaned, lowercase).</summary>
    private static readonly Dictionary<string, string> CompoundWords = new(StringComparer.Ordinal)
    {
        ["silvervein"] = "Silver Vein",
        ["mudpile"] = "Mud Pile",
        ["gucksack"] = "Guck Sack",
        ["trollcave"] = "Troll Cave",
        ["sunkencrypt"] = "Sunken Crypt",
        ["mountaincave"] = "Mountain Cave",
        ["minerock"] = "Mine Rock",
        ["raspberrybush"] = "Raspberry Bush",
        ["blueberrybush"] = "Blueberry Bush",
        ["cloudberrybush"] = "Cloudberry Bush",
        ["beehive"] = "Beehive",
        ["fircone"] = "Fir Cone",
        ["pinecone"] = "Pine Cone",
    };

    public static string Humanize(string? rawName)
    {
        string cleaned = QuickPinSuggester.CleanObjectName(rawName);
        if (cleaned.Length == 0)
        {
            return "";
        }

        List<string> tokens = SplitTokens(cleaned);

        // Drop noise: pure digits, and a leading "pickable"/"piece"
        // marker — but never down to an empty name.
        for (int index = tokens.Count - 1; index >= 0; index--)
        {
            if (tokens.Count > 1 && IsPureDigits(tokens[index]))
            {
                tokens.RemoveAt(index);
            }
        }

        while (tokens.Count > 1 &&
            (string.Equals(tokens[0], "pickable", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(tokens[0], "piece", StringComparison.OrdinalIgnoreCase)))
        {
            tokens.RemoveAt(0);
        }

        var builder = new System.Text.StringBuilder();
        foreach (string token in tokens)
        {
            string expanded = CompoundWords.TryGetValue(token.ToLowerInvariant(), out string known)
                ? known
                : TitleCase(token);
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(expanded);
        }

        return builder.ToString();
    }

    private static List<string> SplitTokens(string cleaned)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        char previous = '\0';
        foreach (char character in cleaned)
        {
            bool boundary =
                character == ' ' ||
                (char.IsUpper(character) && char.IsLower(previous)) ||
                (char.IsDigit(character) != char.IsDigit(previous) && previous != '\0' && character != ' ');
            if (boundary && current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            if (character != ' ')
            {
                current.Append(character);
            }

            previous = character;
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static bool IsPureDigits(string token)
    {
        foreach (char character in token)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    private static string TitleCase(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        return char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant();
    }
}
