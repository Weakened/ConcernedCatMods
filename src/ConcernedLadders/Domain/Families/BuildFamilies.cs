using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Doors;

namespace TheConcernedCat.ConcernedLadders.Domain.Families;

/// <summary>The building-material families railings and ladders come in (#556, #558; owner 2026-11-10: "we can also
/// extend the ladders and railings in a similar fashion ... beautifully integrate with vanilla assets"). Wood, Copper
/// and Iron are the ladders' own three; the rest are the vanilla families the doors already follow (#555), each built
/// from the game's own pole of that material.</summary>
internal enum BuildFamily
{
    Wood = 0,
    Copper = 1,
    Iron = 2,
    Darkwood = 3,
    Ashwood = 4,
    Flametal = 5,
    Dvergr = 6,
    Log = 7,
}

/// <summary>How a family's railings are decorated (#556, owner references): the plain wooden kind, copper's slightly
/// decorated kind, or iron's detailed kind.</summary>
internal enum RailingStyle
{
    Plain = 0,
    Decorated = 1,
    Detailed = 2,
}

/// <summary>One family: the vanilla piece it is cloned from (its health class, support, damage and repair; for the vanilla
/// families also its recipe, station and health, read from it at registration so they stay the game's), the material it
/// borrows, where on that material's texture it samples (#548's rule: every one of these textures but the plank sheet and
/// copper's plain colour is an atlas), its tint, and its railings' style.</summary>
internal sealed class BuildFamilyInfo
{
    internal BuildFamilyInfo(BuildFamily family, string donor, string materialPrefab, string material, UvRegion region,
        (float R, float G, float B) tint, RailingStyle style, float? health, string? station, RailingCostLine[]? costs)
    {
        Family = family;
        Donor = donor;
        MaterialPrefab = materialPrefab;
        Material = material;
        Region = region;
        Tint = tint;
        Style = style;
        Health = health;
        Station = station;
        Costs = costs;
    }

    internal BuildFamily Family { get; }

    internal string Name => Family.ToString();

    internal string Word => Family.ToString().ToLower(CultureInfo.InvariantCulture);

    internal string Donor { get; }

    internal string MaterialPrefab { get; }

    internal string Material { get; }

    internal UvRegion Region { get; }

    internal (float R, float G, float B) Tint { get; }

    internal RailingStyle Style { get; }

    /// <summary>Null: the donor's own health.</summary>
    internal float? Health { get; }

    /// <summary>Null: the donor's own crafting station.</summary>
    internal string? Station { get; }

    /// <summary>Null: the donor's own recipe, so the piece costs what the game's pole of that material costs.</summary>
    internal IReadOnlyList<RailingCostLine>? Costs { get; }

    /// <summary>True for the vanilla families, whose health, station and recipe are the donor pole's.</summary>
    internal bool FollowsDonor => Health == null;
}

/// <summary>One recipe line of a fixed (non-donor) cost.</summary>
internal readonly struct RailingCostLine
{
    internal RailingCostLine(string item, int amount)
    {
        Item = item;
        Amount = amount;
    }

    internal string Item { get; }

    internal int Amount { get; }
}

/// <summary>The eight families (#556, #558). The regions were read from each donor pole's own largest UV island in the
/// installed game (Valheim 1.0.21) by <c>cl-ladders-devtools/doors/pole_islands.py</c>; the copper material carries no
/// colour texture (a colour and a normal map only) and the plank sheet is seamless, so both tile. Game-free so the table is
/// tested.</summary>
internal static class BuildFamilies
{
    internal static readonly IReadOnlyList<BuildFamilyInfo> All = new[]
    {
        new BuildFamilyInfo(BuildFamily.Wood, "woodwall", "woodwall", "woodwall", UvRegion.Tiling, (1f, 1f, 1f), RailingStyle.Plain, 200f, "piece_workbench", null),
        new BuildFamilyInfo(BuildFamily.Copper, "woodiron_pole", "Copper", "copper", UvRegion.Tiling, (1f, 0.84f, 0.7f), RailingStyle.Decorated, 400f, "forge", null),
        // Ironbeam_d holds wood and iron: the pole's iron band island, not the whole sheet.
        new BuildFamilyInfo(BuildFamily.Iron, "woodiron_pole", "woodiron_pole", "Ironbeam_mat", UvRegion.Rect(0.8348f, 0.0552f, 0.9095f, 0.4913f), (0.8f, 0.84f, 0.9f), RailingStyle.Detailed, 800f, "forge", null),
        new BuildFamilyInfo(BuildFamily.Darkwood, "darkwood_pole", "darkwood_pole", "DarkWood_mat", UvRegion.Rect(0.6312f, 0.2212f, 0.7565f, 0.5655f), (1f, 1f, 1f), RailingStyle.Plain, null, null, null),
        new BuildFamilyInfo(BuildFamily.Ashwood, "ashwood_pole_2m", "ashwood_pole_2m", "AshWood_mat", UvRegion.Rect(0.4772f, 0.0435f, 0.9451f, 0.9795f), (1f, 1f, 1f), RailingStyle.Plain, null, null, null),
        new BuildFamilyInfo(BuildFamily.Flametal, "Piece_flametal_beam", "Piece_flametal_beam", "flametalbeam_mat", UvRegion.Rect(0.4119f, 0.4155f, 0.4900f, 0.8291f), (1f, 1f, 1f), RailingStyle.Detailed, null, null, null),
        new BuildFamilyInfo(BuildFamily.Dvergr, "piece_dvergr_pole", "piece_dvergr_pole", "DvergrTownPieces_Mat", UvRegion.Rect(0.0250f, 0.0978f, 0.4593f, 0.8087f), (1f, 1f, 1f), RailingStyle.Plain, null, null, null),
        new BuildFamilyInfo(BuildFamily.Log, "wood_pole_log", "wood_pole_log", "logwall", UvRegion.Rect(0.0701f, 0.0109f, 0.4945f, 0.9266f), (1f, 1f, 1f), RailingStyle.Plain, null, null, null),
    };

    internal static BuildFamilyInfo Of(BuildFamily family)
    {
        foreach (BuildFamilyInfo info in All)
        {
            if (info.Family == family) return info;
        }

        throw new ArgumentOutOfRangeException(nameof(family));
    }
}
