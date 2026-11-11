using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The terrain lattice the excavation works on (#487; since #551 the player sizes each hole, <see cref="DigFootprint"/>).
/// Valheim's terrain is a height per vertex on a whole-metre lattice; a cell between vertices is two flat triangles. So a
/// flat floor is a rectangle of vertices set to one height, and a wall is the one-cell slope from its outer vertices to the
/// first untouched ring. Nothing can make that slope vertical (EXCAVATION.md section 3).</summary>
internal static class ExcavationGeometry
{
    /// <summary>Spacing of the terrain vertex lattice the planner assumes. The engine adapter reads the live heightmap's
    /// spacing and refuses when it differs, rather than plan on a lattice that is not there.</summary>
    internal const float VertexSpacing = 1f;

    /// <summary>Nearest lattice vertex to a world coordinate, rounding exactly as Heightmap.WorldToVertex does (floor of
    /// value + 0.5), so the planned vertex is the one the engine op will change.</summary>
    internal static int SnapToVertex(float world) => (int)Math.Floor(world / VertexSpacing + 0.5f);
}
