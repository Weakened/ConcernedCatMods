using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedLadders.Domain.Railings;

namespace TheConcernedCat.ConcernedLadders.Domain.Basement;

/// <summary>The basement wall setting (#568). Off by default, as every 1.1.0 addition is.</summary>
internal static class BasementSettings
{
    internal const string Section = "BasementWalls";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = false;

    internal const string Description =
        "UNFINISHED (#568): adds stone and wooden basement wall pieces to the hammer's Building tab, 1 or 2 m wide and 1, 2 " +
        "or 4 m tall to match the Iron Shovel's holes, each with a lip at ground level that covers the sloping edge of a " +
        "dug hole, and a corner cap - none of it yet tested in game. Leave this off. Turning it off again, or uninstalling, " +
        "makes the game delete every placed basement wall the next time a world loads, with no refund. In multiplayer every " +
        "player and the host or server must have the same value. Restart after changing it.";
}

/// <summary>The two basement materials (owner, 2026-11-11: "premade walls like stone basement, wooden basement").</summary>
internal enum BasementMaterial
{
    Wood = 0,
    Stone = 1,
}

/// <summary>One box of a basement piece, in the piece's own space: x along the wall, y up from the hole's floor, z outward
/// from the hole (the wall's inner face is z 0, on the floor's edge).</summary>
internal readonly struct BasementPart
{
    internal BasementPart(string name, Point3 centre, Point3 size)
    {
        Name = name;
        Centre = centre;
        Size = size;
    }

    internal string Name { get; }

    internal Point3 Centre { get; }

    internal Point3 Size { get; }
}

/// <summary>One basement piece (#568). A dug hole's sides cannot be vertical: the ground slopes over one 1 m lattice cell
/// from the floor's edge out to the untouched ground (engine limit, EXCAVATION.md section 3). A basement wall makes it read as
/// a straight wall: a panel as tall as the dig, standing on the floor's edge with its inner face on it, and at its top a lip
/// at ground level reaching one cell outward over the slope. Where two walls meet at a corner their lips leave one diagonal
/// cell, which the corner cap covers. Game-free so every number is tested; in game, OWNER GO-AROUND PENDING.</summary>
internal sealed class BasementPiece
{
    /// <summary>The panel's thickness, outward from the floor's edge into the slope.</summary>
    internal const float Thickness = 0.2f;

    /// <summary>The lip's thickness, down from ground level.</summary>
    internal const float LipThickness = 0.12f;

    /// <summary>How far the lip reaches outward: the sloping cell, one lattice spacing.</summary>
    internal const float LipReach = 1f;

    private BasementPiece(BasementMaterial material, int width, int height, bool corner)
    {
        Material = material;
        Width = width;
        Height = height;
        IsCorner = corner;
    }

    internal static BasementPiece Wall(BasementMaterial material, int width, int height) => new BasementPiece(material, width, height, false);

    internal static BasementPiece Corner(BasementMaterial material) => new BasementPiece(material, 1, 0, true);

    internal BasementMaterial Material { get; }

    /// <summary>Metres along the wall (1 or 2).</summary>
    internal int Width { get; }

    /// <summary>Metres from the hole's floor to the ground (1, 2 or 4: the shovel's cube depths); 0 for a corner cap.</summary>
    internal int Height { get; }

    internal bool IsCorner { get; }

    private string MaterialWord => Material.ToString().ToLower(CultureInfo.InvariantCulture);

    internal string Prefab => "tcc_basement_" + MaterialWord + (IsCorner ? "_corner" : "_" + N(Width) + "x" + N(Height));

    internal string Token => "cl_" + Prefab.Substring("tcc_".Length);

    internal string English => (Material == BasementMaterial.Wood ? "Wooden" : "Stone") +
                               (IsCorner ? " Basement Corner Cap" : " Basement Wall " + N(Width) + "x" + N(Height));

    /// <summary>The boxes: a wall's panel and lip, or a corner cap's slab. A corner cap's origin is its inner corner at
    /// ground level, the point it shares with the walls' lips.</summary>
    internal IReadOnlyList<BasementPart> Parts()
    {
        if (IsCorner)
            return new[] { new BasementPart("cap", new Point3(LipReach / 2f, -LipThickness / 2f, LipReach / 2f), new Point3(LipReach, LipThickness, LipReach)) };
        float h = Height, w = Width;
        return new[]
        {
            new BasementPart("panel", new Point3(0f, (h - LipThickness) / 2f, Thickness / 2f), new Point3(w, h - LipThickness, Thickness)),
            new BasementPart("lip", new Point3(0f, h - LipThickness / 2f, LipReach / 2f), new Point3(w, LipThickness, LipReach)),
        };
    }

    /// <summary>The snap points: a wall's bottom and top corners on its inner face (so it snaps to a floor's edge, to the
    /// wall beside it and to the one above), and its lip's outer corners; a corner cap's four top corners.</summary>
    internal IReadOnlyList<Point3> Snaps()
    {
        if (IsCorner)
            return new[] { new Point3(0f, 0f, 0f), new Point3(LipReach, 0f, 0f), new Point3(0f, 0f, LipReach), new Point3(LipReach, 0f, LipReach) };
        float half = Width / 2f;
        return new[]
        {
            new Point3(-half, 0f, 0f), new Point3(half, 0f, 0f),
            new Point3(-half, Height, 0f), new Point3(half, Height, 0f),
            new Point3(-half, Height, LipReach), new Point3(half, Height, LipReach),
        };
    }

    /// <summary>Lead default: one unit of the material per two square metres of panel and lip, at least one.</summary>
    internal int Amount => IsCorner ? 1 : Math.Max(1, (int)Math.Ceiling((Width * (Height - LipThickness) + Width * LipReach) / 2f - 1e-4));

    internal string Item => Material == BasementMaterial.Wood ? "Wood" : "Stone";

    internal string Station => Material == BasementMaterial.Wood ? "piece_workbench" : "piece_stonecutter";

    /// <summary>The vanilla piece cloned for health class, support, damage and repair, and the material borrowed from it.</summary>
    internal string Donor => Material == BasementMaterial.Wood ? "woodwall" : "stone_wall_2x1";

    internal string MaterialPrefab => Donor;

    internal string MaterialName => Material == BasementMaterial.Wood ? "woodwall" : "stone_mat";

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Every basement piece (#568): both materials, 1 and 2 m wide, 1, 2 and 4 m tall, and a corner cap each.</summary>
internal static class BasementWalls
{
    internal const string DescriptionToken = "cl_basement_description";

    internal static readonly int[] Widths = { 1, 2 };

    internal static readonly int[] Heights = { 1, 2, 4 };

    internal static readonly IReadOnlyList<BasementPiece> All = Build();

    internal static Dictionary<string, string> English()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptionToken] = "Lines a dug hole: stands on the floor's edge, its lip covers the sloping ground at the top.",
        };
        foreach (BasementPiece piece in All) english[piece.Token] = piece.English;
        return english;
    }

    private static IReadOnlyList<BasementPiece> Build()
    {
        var all = new List<BasementPiece>();
        foreach (BasementMaterial material in new[] { BasementMaterial.Wood, BasementMaterial.Stone })
        {
            foreach (int height in Heights)
                foreach (int width in Widths) all.Add(BasementPiece.Wall(material, width, height));
            all.Add(BasementPiece.Corner(material));
        }

        return all;
    }
}
