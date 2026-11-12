using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>A hole (#551; since #574 always one of the ready-made <see cref="DigSizes"/>, placed by <see cref="DigSnap"/>):
/// a rectangle of floor vertices on the 1 m lattice, from one corner to the other, and a depth in whole metres. Floor vertices run from <see cref="MinX"/> to <see cref="MaxX"/> and
/// <see cref="MinZ"/> to <see cref="MaxZ"/> inclusive, so the flat floor is <see cref="Width"/> by
/// <see cref="Length"/> metres; the untouched ring the datum is read from is one vertex outside it.</summary>
internal readonly struct DigFootprint : IEquatable<DigFootprint>
{
    /// <summary>The shortest side of a floor, metres: one lattice cell (#567, owner: a 1x1x1 cube).</summary>
    internal const int MinimumSide = 1;

    /// <summary>The longest side of a floor, metres (lead default): one dig stays a bounded batch of single-vertex
    /// operations, at most 81 vertices.</summary>
    internal const int MaximumSide = 8;

    internal const int MinimumDepth = 1;

    /// <summary>7 m: the engine never takes terrain more than 8 m below its unmodified surface, and the planner refuses a
    /// floor within 1 cm of that clamp, so an 8 m dig on natural ground could never be made. No dig, first or later,
    /// takes ground further than that clamp below its natural surface.</summary>
    internal const int MaximumDepth = 7;

    internal const int DefaultDepth = 2;

    private DigFootprint(int minX, int minZ, int maxX, int maxZ, int depth)
    {
        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
        Depth = depth;
    }

    internal int MinX { get; }

    internal int MinZ { get; }

    internal int MaxX { get; }

    internal int MaxZ { get; }

    /// <summary>Metres below the datum.</summary>
    internal int Depth { get; }

    internal int Width => MaxX - MinX;

    internal int Length => MaxZ - MinZ;

    /// <summary>Floor vertices on the x and z sides.</summary>
    internal int VerticesX => Width + 1;

    internal int VerticesZ => Length + 1;

    internal int FloorVertices => VerticesX * VerticesZ;

    /// <summary>The rectangle from the corner the player set (<paramref name="anchorX"/>, <paramref name="anchorZ"/>)
    /// toward the one they aim at, each side clamped to <see cref="MinimumSide"/>..<see cref="MaximumSide"/> metres
    /// away from the anchor in the aimed direction (toward +x or +z when the aim is on the anchor), and the depth
    /// clamped to <see cref="MinimumDepth"/>..<see cref="MaximumDepth"/>. The anchor is always a corner.</summary>
    internal static DigFootprint FromCorners(int anchorX, int anchorZ, int aimX, int aimZ, int depth)
    {
        Side(anchorX, aimX, out int minX, out int maxX);
        Side(anchorZ, aimZ, out int minZ, out int maxZ);
        return new DigFootprint(minX, minZ, maxX, maxZ, ClampDepth(depth));
    }

    internal DigFootprint WithDepth(int depth) => new DigFootprint(MinX, MinZ, MaxX, MaxZ, ClampDepth(depth));

    internal static int ClampDepth(int depth) => Math.Max(MinimumDepth, Math.Min(MaximumDepth, depth));

    internal bool ContainsFloor(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public bool Equals(DigFootprint other) =>
        MinX == other.MinX && MinZ == other.MinZ && MaxX == other.MaxX && MaxZ == other.MaxZ && Depth == other.Depth;

    public override bool Equals(object? obj) => obj is DigFootprint other && Equals(other);

    public override int GetHashCode() => (((MinX * 397 ^ MinZ) * 397 ^ MaxX) * 397 ^ MaxZ) * 397 ^ Depth;

    private static void Side(int anchor, int aim, out int min, out int max)
    {
        int span = Math.Max(MinimumSide, Math.Min(MaximumSide, Math.Abs(aim - anchor)));
        if (aim < anchor) { min = anchor - span; max = anchor; }
        else { min = anchor; max = anchor + span; }
    }
}
