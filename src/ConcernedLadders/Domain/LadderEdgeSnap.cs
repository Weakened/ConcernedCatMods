using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Which end of the ladder a snap attaches (the native Cycle Snap modes this product keeps).</summary>
internal enum LadderSnapMode
{
    Auto,
    Bottom,
    Top,
}

/// <summary>Where a ladder goes when it is snapped to a floor or a wall (owner decision 2026-10-26: "Auto, Top and
/// Bottom snap it to the middle of large floors and walls, the way walls snap to floor components").
///
/// Floors and walls only carry corner snap points, and a wall snaps to a floor because its two bottom corners land
/// on two floor corners exactly one edge apart. A ladder is narrower than that edge, so its place is the middle of the
/// edge, worked out from the piece's own corner points. Everything here is in the target piece's local space (x
/// right, y up, z forward), so the adapter transforms in and out.
///
/// <list type="bullet">
/// <item>Floor (corners spread on both horizontal axes): the edge nearest the aim. The ladder hangs below it (Auto and
/// Top: you climb up onto the floor) or stands on it going up (Bottom), centred on the edge, flush against it, its
/// climbing side facing away from the floor.</item>
/// <item>Wall (corners on one horizontal axis and two heights): the face toward the player. The ladder stands on the
/// wall's bottom edge going up (Auto and Bottom) or hangs from its top edge (Top), centred on the wall, flush against
/// the face, climbing side toward the player.</item>
/// </list></summary>
internal static class LadderEdgeSnap
{
    /// <summary>The ladder's height, and the distance from its origin back to the rails' back face.</summary>
    internal const float SectionHeight = 2f;
    internal const float RailBack = 0.08f;

    /// <summary>The visual gap left between the rails and the surface they rest against.</summary>
    internal const float Gap = 0.01f;

    /// <summary>Shortest corner-to-corner span that counts as a floor or wall side (excludes stone thickness).</summary>
    internal const float MinimumEdge = 1.5f;

    /// <summary>Lowest corner height difference that makes a piece a wall rather than a floor slab.</summary>
    internal const float MinimumWallHeight = 0.9f;

    /// <summary>The ladder origin (bottom-centre) and the horizontal direction its climbing side faces, both in the
    /// target piece's local space, and the joint (the edge midpoint the ladder attaches to). False when the corner
    /// points describe neither a floor nor a wall.</summary>
    internal static bool TryPlace(IReadOnlyList<ClimbPoint> corners, ClimbPoint aim, ClimbPoint viewer, LadderSnapMode mode,
        out ClimbPoint origin, out float facingX, out float facingZ, out ClimbPoint joint)
    {
        origin = default;
        joint = default;
        facingX = 0f;
        facingZ = 1f;
        if (corners == null || corners.Count < 4 || !aim.IsFinite || !viewer.IsFinite) { return false; }
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (ClimbPoint p in corners)
        {
            if (!p.IsFinite) { return false; }
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }

        float spanX = maxX - minX, spanZ = maxZ - minZ;
        float centreX = (minX + maxX) * 0.5f, centreZ = (minZ + maxZ) * 0.5f;
        if (spanX >= MinimumEdge && spanZ >= MinimumEdge)
        {
            // Floor: the four sides of its top level; the side nearest the aim.
            float[] distance = { Math.Abs(aim.X - minX), Math.Abs(aim.X - maxX), Math.Abs(aim.Z - minZ), Math.Abs(aim.Z - maxZ) };
            int edge = 0;
            for (int index = 1; index < 4; index++) { if (distance[index] < distance[edge]) { edge = index; } }
            float midX = edge == 0 ? minX : edge == 1 ? maxX : centreX;
            float midZ = edge == 2 ? minZ : edge == 3 ? maxZ : centreZ;
            facingX = edge == 0 ? -1f : edge == 1 ? 1f : 0f;
            facingZ = edge == 2 ? -1f : edge == 3 ? 1f : 0f;
            joint = new ClimbPoint(midX, maxY, midZ);
            float back = Gap - RailBack; // The floor's side face is the edge line itself.
            float baseY = mode == LadderSnapMode.Bottom ? maxY : maxY - SectionHeight;
            origin = new ClimbPoint(midX + facingX * back, baseY, midZ + facingZ * back);
            return true;
        }

        bool alongX = spanX >= MinimumEdge && spanX >= spanZ;
        bool alongZ = !alongX && spanZ >= MinimumEdge;
        if ((!alongX && !alongZ) || maxY - minY < MinimumWallHeight) { return false; }

        // Wall: the face toward the viewer. The face lies at the corners' outer thickness on that side, or at the aimed
        // surface when the corners sit on the wall's centre plane (a wood wall).
        float side = alongX ? Math.Sign(viewer.Z - centreZ) : Math.Sign(viewer.X - centreX);
        if (side == 0f) { side = 1f; }
        float cornerFace = alongX ? (side > 0f ? maxZ : minZ) : (side > 0f ? maxX : minX);
        float aimed = alongX ? aim.Z : aim.X;
        float face = side > 0f ? Math.Max(cornerFace, aimed) : Math.Min(cornerFace, aimed);
        float plane = face + side * (Gap - RailBack);
        float edgeY = mode == LadderSnapMode.Top ? maxY : minY;
        float originY = mode == LadderSnapMode.Top ? maxY - SectionHeight : minY;
        if (alongX)
        {
            facingX = 0f; facingZ = side;
            joint = new ClimbPoint(centreX, edgeY, face);
            origin = new ClimbPoint(centreX, originY, plane);
        }
        else
        {
            facingX = side; facingZ = 0f;
            joint = new ClimbPoint(face, edgeY, centreZ);
            origin = new ClimbPoint(plane, originY, centreZ);
        }

        return true;
    }
}
