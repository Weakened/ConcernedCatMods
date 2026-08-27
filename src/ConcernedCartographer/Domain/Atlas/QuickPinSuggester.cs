using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Turns a targeted world object's names into a pin suggestion:
/// cleaned display name plus icon/category from a keyword table. Pure and
/// deterministic; the adapter supplies the hover/prefab names.</summary>
internal static class QuickPinSuggester
{
    public readonly struct Suggestion
    {
        public Suggestion(string name, string iconId, string category)
        {
            Name = name;
            IconId = iconId;
            Category = category;
        }

        public string Name { get; }
        public string IconId { get; }
        public string Category { get; }
    }

    private static readonly (string Keyword, string IconId, string Category)[] Rules =
    {
        ("portal", "vanilla:portal", "Travel"),
        ("dock", "cc:harbor", "Travel"),
        ("ship", "cc:harbor", "Travel"),
        ("karve", "cc:harbor", "Travel"),
        ("longship", "cc:harbor", "Travel"),
        ("raft", "cc:harbor", "Travel"),
        ("rock", "cc:resource", "Resources"),
        ("ore", "cc:resource", "Resources"),
        ("deposit", "cc:resource", "Resources"),
        ("mine", "cc:resource", "Resources"),
        ("copper", "cc:resource", "Resources"),
        ("tin", "cc:resource", "Resources"),
        ("silver", "cc:resource", "Resources"),
        ("berr", "cc:resource", "Resources"),
        ("mushroom", "cc:resource", "Resources"),
        ("carrot", "cc:farm", "Base"),
        ("turnip", "cc:farm", "Base"),
        ("beehive", "cc:farm", "Base"),
        ("crypt", "cc:danger", "Danger"),
        ("cave", "cc:danger", "Danger"),
        ("dungeon", "cc:danger", "Danger"),
        ("spawner", "cc:danger", "Danger"),
        ("nest", "cc:danger", "Danger"),
        ("bonfire", "vanilla:fire", "Camp"),
        ("fire", "vanilla:fire", "Camp"),
        ("house", "vanilla:house", "Base"),
        ("hut", "vanilla:house", "Base"),
        ("tower", "vanilla:house", "Base"),
        ("ruin", "vanilla:house", "Explore"),
        ("rune", "vanilla:dot", "Explore"),
        ("stone", "vanilla:dot", "Explore"),
        ("workbench", "vanilla:hammer", "Work"),
        ("forge", "vanilla:hammer", "Work"),
        ("smelter", "vanilla:hammer", "Work"),
        ("kiln", "vanilla:hammer", "Work"),
    };

    public static Suggestion Suggest(string? hoverName, string? prefabName)
    {
        string display = CleanName(hoverName, prefabName);
        string haystack = ((hoverName ?? "") + " " + (prefabName ?? "")).ToLowerInvariant();

        foreach ((string keyword, string iconId, string category) in Rules)
        {
            if (haystack.Contains(keyword))
            {
                return new Suggestion(display, iconId, category);
            }
        }

        return new Suggestion(display, IconRegistry.DefaultIconId, "Explore");
    }

    /// <summary>Prefers the localized hover name; falls back to a cleaned
    /// prefab name ("TreasureChest_meadows(Clone)" → "Treasurechest
    /// meadows").</summary>
    public static string CleanName(string? hoverName, string? prefabName)
    {
        string hover = StripMarkup(hoverName ?? "").Trim();
        if (hover.Length > 0 && !hover.StartsWith("$", StringComparison.Ordinal))
        {
            return hover;
        }

        string prefab = (prefabName ?? "").Replace("(Clone)", "").Replace('_', ' ').Trim();
        if (prefab.Length == 0)
        {
            return "Marked spot";
        }

        return char.ToUpperInvariant(prefab[0]) + prefab.Substring(1).ToLowerInvariant();
    }

    private static string StripMarkup(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        bool inTag = false;
        foreach (char character in text)
        {
            if (character == '<')
            {
                inTag = true;
            }
            else if (character == '>')
            {
                inTag = false;
            }
            else if (!inTag && character != '\n' && character != '\r')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
