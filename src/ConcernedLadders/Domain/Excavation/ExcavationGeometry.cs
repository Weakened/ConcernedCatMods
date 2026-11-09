using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The two Iron Shovel modes (#487). WORKING ASSUMPTION, not a confirmed owner requirement: the
/// requested 2 x 2 m and 4 x 4 m holes are read as 2 x 2 x 2 m and 4 x 4 x 4 m (width x length x depth).
/// See docs/mods/concerned-ladders/EXCAVATION.md section 1.</summary>
internal enum ExcavationMode
{
    Small = 0,
    Large = 1,
}

/// <summary>The geometry contract, in terrain vertices. Valheim's terrain is a height per vertex on a
/// whole-metre lattice; a cell between vertices is two flat triangles. So a flat floor is the square of
/// vertices set to one height, and the wall is the one-cell slope from its outer vertices to the first
/// untouched ring. Nothing here can make that slope vertical (EXCAVATION.md section 3).</summary>
internal readonly struct ExcavationGeometry
{
    /// <summary>Spacing of the terrain vertex lattice the planner assumes. The engine adapter must read the
    /// live heightmap's spacing and refuse when it differs, rather than plan on a lattice that is not there.</summary>
    internal const float VertexSpacing = 1f;

    private ExcavationGeometry(ExcavationMode mode, int halfExtent, float depth)
    {
        Mode = mode;
        HalfExtent = halfExtent;
        Depth = depth;
    }

    internal ExcavationMode Mode { get; }

    /// <summary>Floor vertices run from centre - HalfExtent to centre + HalfExtent on each axis.</summary>
    internal int HalfExtent { get; }

    /// <summary>Floor depth below the datum, in metres.</summary>
    internal float Depth { get; }

    /// <summary>Flat usable floor edge, metres: the distance between the outermost floor vertices.</summary>
    internal float UsableFloorSize => 2 * HalfExtent * VertexSpacing;

    /// <summary>Edge of the whole changed square, metres: the floor plus one sloping cell on every side.</summary>
    internal float BorderSize => (2 * HalfExtent + 2) * VertexSpacing;

    /// <summary>The untouched ring sits one vertex outside the floor; the datum is read from it.</summary>
    internal int RingDistance => HalfExtent + 1;

    /// <summary>Vertices on one side of the sampled square: floor plus ring.</summary>
    internal int SampleSide => 2 * RingDistance + 1;

    /// <summary>Distance from the centre to the farthest point whose surface changes (a border corner). A
    /// ward check on this radius covers every changed cell; the radius vanilla's own terrain-op check uses
    /// (the op radius) does not reach the floor corners of a square op.</summary>
    internal float AccessCheckRadius => (float)(RingDistance * VertexSpacing * Math.Sqrt(2.0));

    /// <summary>Steepest wall on flat ground, degrees from horizontal: depth over one cell.</summary>
    internal float FlatGroundWallAngleDegrees => (float)(Math.Atan2(Depth, VertexSpacing) * 180.0 / Math.PI);

    internal static ExcavationGeometry For(ExcavationMode mode) => mode switch
    {
        ExcavationMode.Small => new ExcavationGeometry(mode, 1, 2f),
        ExcavationMode.Large => new ExcavationGeometry(mode, 2, 4f),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Nearest lattice vertex to a world coordinate, rounding exactly as Heightmap.WorldToVertex
    /// does (floor of value + 0.5), so the planned centre is the vertex the engine op will centre on.</summary>
    internal static int SnapToVertex(float world) => (int)Math.Floor(world / VertexSpacing + 0.5f);
}
