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
        public IconDefinition(string id, string displayName, string defaultCategory, int vanillaType, string keywords)
        {
            Id = id;
            DisplayName = displayName;
            DefaultCategory = defaultCategory;
            VanillaType = vanillaType;
            Keywords = keywords;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string DefaultCategory { get; }

        /// <summary>Ordinal of the vanilla Minimap.PinType used to render
        /// this icon.</summary>
        public int VanillaType { get; }

        public string Keywords { get; }
    }

    // Append-only. Never reorder, rename, or reuse an Id.
    private static readonly IconDefinition[] Definitions =
    {
        new("vanilla:fire", "Fire", "Camp", 0, "fire camp bonfire spawn"),
        new("vanilla:house", "House", "Base", 1, "house base home village building"),
        new("vanilla:hammer", "Hammer", "Work", 2, "hammer crafting work mine quarry"),
        new("vanilla:dot", "Dot", "General", 3, "dot ball marker generic default"),
        new("vanilla:portal", "Portal", "Travel", 6, "portal travel teleport gate"),
        new("cc:road", "Road", "Infrastructure", 3, "road path street junction crossing"),
        new("cc:harbor", "Harbor", "Travel", 3, "harbor port dock ship boat"),
        new("cc:resource", "Resource", "Resources", 2, "resource ore wood berries deposit"),
        new("cc:danger", "Danger", "Danger", 0, "danger enemy spawner warning"),
        new("cc:farm", "Farm", "Base", 1, "farm crops cultivated animals"),
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
