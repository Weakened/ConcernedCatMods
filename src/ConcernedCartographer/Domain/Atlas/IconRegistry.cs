using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Curated, stable icon identities. IDs are namespaced strings and
/// the table is append-only by contract: reordering or adding entries never
/// changes an existing pin's identity, and unknown IDs are preserved
/// verbatim while rendering as the fallback. The core stores only the
/// vanilla pin-type ordinal; game-side code converts it to the enum.</summary>
internal static class IconRegistry
{
    public const string DefaultIconId = "vanilla:dot";

    /// <summary>Vanilla PinType ordinal used when an icon ID is unknown
    /// (the plain dot).</summary>
    public const int FallbackVanillaType = 3;

    public sealed class IconDefinition
    {
        public IconDefinition(string id, string displayName, string defaultCategory, int vanillaType, string keywords, string spriteKey = "")
        {
            Id = id;
            DisplayName = displayName;
            DefaultCategory = defaultCategory;
            VanillaType = vanillaType;
            Keywords = keywords;
            SpriteKey = spriteKey;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string DefaultCategory { get; }

        /// <summary>Ordinal of the vanilla Minimap.PinType this icon SAVES
        /// as (and renders as wherever the CC sprite is unavailable). The
        /// saved vanilla pin keeps this type, so disable/uninstall — or an
        /// older mod version reading the atlas — degrades to a sensible
        /// vanilla icon instead of losing the pin.</summary>
        public int VanillaType { get; }

        public string Keywords { get; }

        /// <summary>Embedded CC sprite name for icons with a distinct
        /// visual of their own (RC8: every cc:* icon), or "" for icons that
        /// render the vanilla sprite. The sprite is a session rendering
        /// override only — nothing about it is written into saves.</summary>
        public string SpriteKey { get; }

        public bool HasCustomSprite => SpriteKey.Length > 0;
    }

    // Append-only. Never reorder, rename, or reuse an Id.
    private static readonly IconDefinition[] Definitions =
    {
        new("vanilla:fire", "Fire", "Camp", 0, "fire camp bonfire spawn"),
        new("vanilla:house", "House", "Base", 1, "house base home village building"),
        new("vanilla:hammer", "Hammer", "Work", 2, "hammer crafting work mine quarry"),
        new("vanilla:dot", "Dot", "General", 3, "dot ball marker generic default"),
        new("vanilla:portal", "Portal", "Travel", 6, "portal travel teleport gate"),
        new("cc:road", "Road / Junction", "Infrastructure", 3, "road path street junction crossing signpost", "cc-road"),
        new("cc:harbor", "Harbor / Anchor", "Travel", 3, "harbor port dock ship boat anchor", "cc-harbor"),
        new("cc:resource", "Resource", "Resources", 2, "resource ore wood berries deposit gather", "cc-resource"),
        new("cc:danger", "Danger", "Danger", 0, "danger enemy spawner warning skull", "cc-danger"),
        new("cc:farm", "Farm", "Base", 1, "farm crops cultivated animals sprout", "cc-farm"),
        // RC8 additions — real distinct CC visuals with stable identities.
        new("cc:mine", "Mine", "Resources", 2, "mine pickaxe quarry ore tunnel", "cc-mine"),
        new("cc:fishing", "Fishing", "Resources", 3, "fishing fish spot water catch", "cc-fishing"),
        new("cc:camp", "Camp", "Camp", 0, "camp tent shelter rest outpost", "cc-camp"),
        new("cc:travel", "Travel", "Travel", 6, "travel route direction arrow waypoint", "cc-travel"),
        new("cc:trader", "Trader / Shop", "Points of interest", 1, "trader shop merchant haldor coins", "cc-trader"),
        new("cc:dungeon", "Dungeon / Cave", "Dungeons", 0, "dungeon cave crypt entrance burial", "cc-dungeon"),
        new("cc:objective", "Objective / Star", "Points of interest", 3, "objective star goal quest important", "cc-objective"),
    };

    private static readonly Dictionary<string, IconDefinition> ById = BuildIndex();

    public static IReadOnlyList<IconDefinition> All => Definitions;

    public static bool TryResolve(string? iconId, out IconDefinition definition)
    {
        if (!string.IsNullOrEmpty(iconId) && ById.TryGetValue(iconId!, out definition!))
        {
            return true;
        }

        definition = ById[DefaultIconId];
        return false;
    }

    /// <summary>The vanilla pin-type ordinal for an icon ID, falling back to
    /// the dot for unknown IDs without touching the stored identity.</summary>
    public static int ResolveVanillaType(string? iconId)
    {
        return TryResolve(iconId, out IconDefinition definition)
            ? definition.VanillaType
            : FallbackVanillaType;
    }

    /// <summary>The registry ID matching a vanilla pin-type ordinal, used
    /// when adopting vanilla pins. Unknown ordinals map to the default.</summary>
    public static string FromVanillaType(int vanillaType)
    {
        foreach (IconDefinition definition in Definitions)
        {
            if (definition.VanillaType == vanillaType && definition.Id.StartsWith("vanilla:", StringComparison.Ordinal))
            {
                return definition.Id;
            }
        }

        return DefaultIconId;
    }

    public static List<IconDefinition> Search(string? query)
    {
        var results = new List<IconDefinition>();
        string needle = (query ?? "").Trim().ToLowerInvariant();
        foreach (IconDefinition definition in Definitions)
        {
            if (needle.Length == 0 ||
                definition.Id.ToLowerInvariant().Contains(needle) ||
                definition.DisplayName.ToLowerInvariant().Contains(needle) ||
                definition.Keywords.Contains(needle))
            {
                results.Add(definition);
            }
        }

        return results;
    }

    private static Dictionary<string, IconDefinition> BuildIndex()
    {
        var index = new Dictionary<string, IconDefinition>(StringComparer.Ordinal);
        foreach (IconDefinition definition in Definitions)
        {
            index.Add(definition.Id, definition);
        }

        return index;
    }
}
