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

    /// <summary>The friendly fallback when nothing human-readable exists
    /// (RC10 feedback 15).</summary>
    public const string FallbackName = "Marked object";

    public static Suggestion Suggest(string? hoverName, string? prefabName)
    {
        return Suggest(hoverName, new[] { prefabName });
    }

    /// <summary>RC10 feedback 15: the adapter passes every identity it can
    /// see — the raw hover object, its ZNetView prefab root, the transform
    /// root — in that order. Technical engine names (Collider (1), trigger,
    /// mesh…) are skipped for the display name but still feed keyword
    /// matching, so a chest's "Collider" child still pins as the chest.</summary>
    public static Suggestion Suggest(string? hoverName, IReadOnlyList<string?> objectNameCandidates)
    {
        string display = CleanName(hoverName, objectNameCandidates);

        var haystackBuilder = new System.Text.StringBuilder((hoverName ?? "").ToLowerInvariant());
        foreach (string? candidate in objectNameCandidates)
        {
            haystackBuilder.Append(' ').Append((candidate ?? "").ToLowerInvariant());
        }

        string haystack = haystackBuilder.ToString();
        foreach ((string keyword, string iconId, string category) in Rules)
        {
            if (haystack.Contains(keyword))
            {
                return new Suggestion(display, iconId, category);
            }
        }

        return new Suggestion(display, IconRegistry.DefaultIconId, "Explore");
    }

    public static string CleanName(string? hoverName, string? prefabName)
    {
        return CleanName(hoverName, new[] { prefabName });
    }

    /// <summary>Prefers the localized hover name's FIRST LINE (interaction
    /// prompts live on later lines); then the first non-technical object
    /// name, cleaned ("TreasureChest_meadows(Clone)" → "Treasurechest
    /// meadows"); then the friendly fallback.</summary>
    public static string CleanName(string? hoverName, IReadOnlyList<string?> objectNameCandidates)
    {
        string hover = StripMarkup(FirstLine(hoverName ?? "")).Trim();
        if (hover.Length > 0 && !hover.StartsWith("$", StringComparison.Ordinal))
        {
            return hover;
        }

        foreach (string? candidate in objectNameCandidates)
        {
            string cleaned = CleanObjectName(candidate);
            if (cleaned.Length > 0 && !IsTechnicalName(cleaned))
            {
                // RC11 blockers 11/14: the shared humanizer — case/underscore
                // splitting, noise-token removal, compound expansion — so a
                // prefab fallback reads "Raspberry Bush", never
                // "Raspberrybush".
                string humanized = NameHumanizer.Humanize(cleaned);
                if (humanized.Length > 0)
                {
                    return humanized;
                }
            }
        }

        return FallbackName;
    }

    /// <summary>True for generic engine/child object names that must never
    /// become a pin name: colliders, triggers, meshes, LOD nodes, attach
    /// and snap points, primitive shapes, bare clones.</summary>
    public static bool IsTechnicalName(string? objectName)
    {
        string name = CleanObjectName(objectName).ToLowerInvariant();
        if (name.Length == 0)
        {
            return true;
        }

        // Trailing digits never make a technical name meaningful
        // ("collider2", "lod1").
        string stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ');
        switch (stem)
        {
            case "collider":
            case "colliders":
            case "collision":
            case "trigger":
            case "mesh":
            case "meshes":
            case "model":
            case "instance":
            case "clone":
            case "root":
            case "gameobject":
            case "new gameobject":
            case "new game object":
            case "cube":
            case "capsule":
            case "cylinder":
            case "sphere":
            case "quad":
            case "plane":
            case "hitbox":
            case "attach":
            case "attachpoint":
            case "attach point":
            case "pivot":
            case "snappoint":
            case "snap point":
            case "visual":
            case "graphics":
            case "gfx":
            case "default":
            case "lod":
                return true;
            default:
                return stem.StartsWith("snappoint", StringComparison.Ordinal) ||
                    stem.StartsWith("attachpoint", StringComparison.Ordinal) ||
                    stem.StartsWith("lod ", StringComparison.Ordinal);
        }
    }

    /// <summary>Strips "(Clone)" suffixes, trailing "(1)"-style counters,
    /// and underscores; collapses whitespace.</summary>
    public static string CleanObjectName(string? objectName)
    {
        string name = (objectName ?? "").Trim();
        bool changed = true;
        while (changed && name.Length > 0)
        {
            changed = false;
            if (name.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
                changed = true;
            }
            else if (name.EndsWith(")", StringComparison.Ordinal))
            {
                int open = name.LastIndexOf('(');
                if (open >= 0 && IsDigitsOrSpaces(name, open + 1, name.Length - 1))
                {
                    name = name.Substring(0, open).TrimEnd();
                    changed = true;
                }
            }
        }

        var builder = new System.Text.StringBuilder(name.Length);
        bool pendingSpace = false;
        foreach (char character in name)
        {
            char mapped = character == '_' ? ' ' : character;
            if (mapped == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(mapped);
        }

        return builder.ToString();
    }

    private static bool IsDigitsOrSpaces(string text, int start, int endExclusive)
    {
        bool sawDigit = false;
        for (int index = start; index < endExclusive; index++)
        {
            char character = text[index];
            if (char.IsDigit(character))
            {
                sawDigit = true;
            }
            else if (character != ' ')
            {
                return false;
            }
        }

        return sawDigit;
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOfAny(new[] { '\n', '\r' });
        return newline < 0 ? text : text.Substring(0, newline);
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
