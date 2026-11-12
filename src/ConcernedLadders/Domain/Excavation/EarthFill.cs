using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The earth fill setting (#577). On by default, but only within the Iron Shovel's own switch, which is off by
/// default: with excavation off there is no shovel and nothing is filled.</summary>
internal static class EarthFillSettings
{
    internal const string Section = "Excavation";

    internal const string Key = "EarthFill";

    internal const bool EnabledByDefault = true;

    internal const string Description =
        "UNFINISHED (#577), effective only with Excavation/Enabled: each hole the Iron Shovel digs gets straight earth sides. " +
        "The sloping 1 m border around the hole is filled with earth blocks up to the ground beside it, so a wall built on " +
        "the floor's edge stands against flat earth. The hammer removes a block (no cost, no refund). Not yet tested in game. " +
        "Turning Excavation off again, or uninstalling, makes the game delete every earth block the next time a world loads.";
}

/// <summary>One 1 m lattice cell, named by its smaller corner vertex.</summary>
internal readonly struct FillCell : IEquatable<FillCell>
{
    internal FillCell(int x, int z)
    {
        X = x;
        Z = z;
    }

    internal int X { get; }

    internal int Z { get; }

    public bool Equals(FillCell other) => X == other.X && Z == other.Z;

    public override bool Equals(object? obj) => obj is FillCell other && Equals(other);

    public override int GetHashCode() => X * 397 ^ Z;

    public override string ToString() => "(" + X.ToString(CultureInfo.InvariantCulture) + ", " + Z.ToString(CultureInfo.InvariantCulture) + ")";
}

/// <summary>One earth block: a cell, filled from <see cref="Bottom"/> (the hole's floor) up to <see cref="Top"/> (the ground
/// beside it), in world metres.</summary>
internal readonly struct FillBlock
{
    internal FillBlock(FillCell cell, float bottom, float top)
    {
        Cell = cell;
        Bottom = bottom;
        Top = top;
    }

    internal FillCell Cell { get; }

    internal float Bottom { get; }

    internal float Top { get; }

    internal float Height => Top - Bottom;

    /// <summary>Whether <paramref name="other"/> is this block, as found again in the world (its height may have been read
    /// back through a float scale).</summary>
    internal bool Same(in FillBlock other) =>
        Cell.Equals(other.Cell) && Math.Abs(Bottom - other.Bottom) <= EarthFill.MatchTolerance && Math.Abs(Top - other.Top) <= EarthFill.MatchTolerance;
}

/// <summary>Straight earth sides for a dug hole (#577, owner 2026-11-11: "Is it possible ... surrounded sloped area are
/// filled with ground material?", then "I want to implement the earth fill idea"). A hole's side cannot be vertical: the
/// ground slopes across the 1 m cell between the floor's edge and the untouched ground (EXCAVATION.md section 3). Each
/// such cell around the hole gets one earth block standing on the floor's level and reaching the ground beside it, so the
/// hole's side is a vertical earth face and the ground around it is flat to the edge.
///
/// <para>Game-free so every rule is tested: which cells get a block, how tall, which existing blocks a new hole takes out,
/// and which an undo puts back. The game side places and removes the blocks.</para></summary>
internal static class EarthFill
{
    /// <summary>The earth block's prefab: a networked piece, never in a build menu, placed only by a dig.</summary>
    internal const string Prefab = "tcc_earth_fill";

    internal const string NameToken = "cl_earth_fill";

    internal const string EnglishName = "Earth";

    /// <summary>A cell whose ground is less than this above the floor needs no block: there is no slope worth filling (the
    /// neighbouring cell is another hole of the same depth, say). Lead default.</summary>
    internal const float MinimumHeight = 0.25f;

    /// <summary>How closely a block found in the world must match a remembered one to be the same block.</summary>
    internal const float MatchTolerance = 0.05f;

    /// <summary>Whether <paramref name="cell"/> lies under the floor of <paramref name="footprint"/>: its four corners are
    /// all floor vertices. A block there stands inside the new hole and is taken out by it.</summary>
    internal static bool Inside(DigFootprint footprint, FillCell cell) =>
        cell.X >= footprint.MinX && cell.X < footprint.MaxX && cell.Z >= footprint.MinZ && cell.Z < footprint.MaxZ;

    /// <summary>Whether a dig of <paramref name="footprint"/> to a floor at <paramref name="bottom"/> takes out
    /// <paramref name="found"/>, an earth block already standing there: when the new floor covers its cell, or when its cell
    /// is in the new ring but the block stands on a higher floor (the same spot dug deeper), which would leave the slope
    /// showing under it. A taken-out ring cell is filled again, full height; any other block found marks its cell as
    /// filled already.</summary>
    internal static bool TakesOut(DigFootprint footprint, float bottom, in FillBlock found) =>
        Inside(footprint, found.Cell) || found.Bottom > bottom + MatchTolerance;

    /// <summary>The cells of the 1 m ring around the floor: every cell with a floor vertex at one or more of its corners but
    /// not all four. These are the sloping cells.</summary>
    internal static IEnumerable<FillCell> Ring(DigFootprint footprint)
    {
        for (int x = footprint.MinX - 1; x <= footprint.MaxX; x++)
        {
            for (int z = footprint.MinZ - 1; z <= footprint.MaxZ; z++)
            {
                var cell = new FillCell(x, z);
                if (!Inside(footprint, cell)) yield return cell;
            }
        }
    }

    /// <summary>The blocks a dig of <paramref name="footprint"/> to a floor at <paramref name="bottom"/> places: one per ring
    /// cell not already <paramref name="occupied"/> by a block, reaching the highest of the cell's corners that are not floor
    /// vertices (read with <paramref name="ground"/>, the heights before the dig, which the dig leaves alone there), when that
    /// is at least <see cref="MinimumHeight"/> above the floor. A cell any of whose outer corners cannot be read gets no
    /// block.</summary>
    internal static void Plan(DigFootprint footprint, float bottom, Func<int, int, float?> ground, ICollection<FillCell> occupied,
        List<FillBlock> into)
    {
        into.Clear();
        if (!LadderRules.Finite(bottom)) return;
        foreach (FillCell cell in Ring(footprint))
        {
            if (occupied.Contains(cell)) continue;
            float top = float.NegativeInfinity;
            bool readable = true;
            for (int dx = 0; dx <= 1 && readable; dx++)
            {
                for (int dz = 0; dz <= 1; dz++)
                {
                    int x = cell.X + dx, z = cell.Z + dz;
                    if (OnFloor(footprint, x, z)) continue;
                    float? height = ground(x, z);
                    if (height == null || !LadderRules.Finite(height.Value)) { readable = false; break; }
                    top = Math.Max(top, height.Value);
                }
            }

            if (readable && top - bottom >= MinimumHeight) into.Add(new FillBlock(cell, bottom, top));
        }
    }

    /// <summary>The cell a block stands on, from its position: a block's origin is its cell's centre at floor level.</summary>
    internal static FillCell CellAt(float x, float z) =>
        new FillCell((int)Math.Floor(x / ExcavationGeometry.VertexSpacing), (int)Math.Floor(z / ExcavationGeometry.VertexSpacing));

    /// <summary>A block's origin in world metres: its cell's centre, at the floor.</summary>
    internal static (float X, float Y, float Z) Origin(in FillBlock block) =>
        ((block.Cell.X + 0.5f) * ExcavationGeometry.VertexSpacing, block.Bottom, (block.Cell.Z + 0.5f) * ExcavationGeometry.VertexSpacing);

    private static bool OnFloor(DigFootprint footprint, int x, int z) =>
        x >= footprint.MinX && x <= footprint.MaxX && z >= footprint.MinZ && z <= footprint.MaxZ;
}
