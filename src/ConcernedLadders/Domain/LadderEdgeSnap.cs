using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>The native Cycle Snap modes a ladder offers (owner revision 2026-10-26: Auto, Bottom, Top, Left side,
/// Right side).</summary>
internal enum LadderSnapMode
{
    Auto,
    Bottom,
    Top,

    /// <summary>The ladder's visible left rail (as seen by a player facing the climbing side) lines up with the
    /// target's left corner.</summary>
    LeftSide,

    /// <summary>The ladder's visible right rail lines up with the target's right corner.</summary>
    RightSide,
}

/// <summary>The ladder prefab's snap points, in section-local metres, and how a selected point names a mode.
///
/// <para>Bottom-centre and top-centre are the end-to-end stack contract the game's own snapping pairs. The two side
/// points only carry a mode for Cycle Snap: they sit far below the section (100 m and 200 m), so the game's snap
/// search (0.5 m pairing radius, pieces within 10 m of the ghost) can never pair them with a floor or wall corner or
/// with another ladder's centre points, and never a ghost's left point with a placed ladder's right point (that would
/// need a 100 m vertical offset). The only pairing left is a side point with the same side point of a ladder within
/// 0.5 m, which moves the ghost exactly onto that ladder, the same result the game already finds through the centre
/// points. Corner points at real positions cross-matched other ladders and floors and shifted sections sideways and in
/// depth (reproduced in game 2026-10-26); these cannot. The placement adapter owns where a side-mode ghost goes: the
/// game puts the ghost so the selected point lies on the aimed point, 100 m above it, and the adapter first moves the
/// ghost back so its origin lies on the aimed point.</para></summary>
internal static class LadderSnapPoints
{
    internal static readonly ClimbPoint Bottom = new ClimbPoint(0f, 0f, 0f);
    internal static readonly ClimbPoint Top = new ClimbPoint(0f, 2f, 0f);
    internal static readonly ClimbPoint LeftSide = new ClimbPoint(0f, -100f, 0f);
    internal static readonly ClimbPoint RightSide = new ClimbPoint(0f, -200f, 0f);

    /// <summary>How close an authored point must be to name a mode (float noise only).</summary>
    internal const float Match = 0.01f;

    internal static bool IsSide(LadderSnapMode mode) => mode == LadderSnapMode.LeftSide || mode == LadderSnapMode.RightSide;

    /// <summary>The mode a selected snap point's authored local position names. False for any other point.</summary>
    internal static bool TryModeOf(ClimbPoint local, out LadderSnapMode mode)
    {
        mode = LadderSnapMode.Auto;
        if (!local.IsFinite) { return false; }
        if (Near(local, Bottom)) { mode = LadderSnapMode.Bottom; return true; }
        if (Near(local, Top)) { mode = LadderSnapMode.Top; return true; }
        if (Near(local, LeftSide)) { mode = LadderSnapMode.LeftSide; return true; }
        if (Near(local, RightSide)) { mode = LadderSnapMode.RightSide; return true; }
        return false;
    }

    private static bool Near(ClimbPoint a, ClimbPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz <= Match * Match;
    }
}

/// <summary>A ladder section's horizontal footprint in its own local space (x right, z toward the climbing side):
/// the rails' outer edges at ±<see cref="HalfWidth"/>, their back at <see cref="Back"/> and front at
/// <see cref="Front"/>. All three products' sections share the collider box (0.8 m wide, z 0.08 to 0.24); the metal
/// rails fill it exactly and the wood rails sit 1 cm inside it.</summary>
internal readonly struct LadderFootprint
{
    internal LadderFootprint(float halfWidth, float back, float front)
    {
        HalfWidth = halfWidth;
        Back = back;
        Front = front;
    }

    internal static LadderFootprint Section => new LadderFootprint(0.4f, LadderEdgeSnap.RailBack, 0.24f);

    internal float HalfWidth { get; }
    internal float Back { get; }
    internal float Front { get; }

    internal bool IsValid => LadderRules.Finite(HalfWidth) && LadderRules.Finite(Back) && LadderRules.Finite(Front) &&
                             HalfWidth > 0.05f && HalfWidth < 1f && Front > Back && Front - Back < 1f;
}

/// <summary>Everything the edge snap needs, in the target piece's local space (x right, y up, z forward).</summary>
internal struct LadderEdgeRequest
{
    /// <summary>The target piece's own snap points (its corners).</summary>
    internal IReadOnlyList<ClimbPoint> Corners;

    /// <summary>The point the placement ray hit on the target.</summary>
    internal ClimbPoint Aim;

    /// <summary>The camera, which picks the face of a wall.</summary>
    internal ClimbPoint Viewer;

    internal LadderSnapMode Mode;

    internal LadderFootprint Footprint;

    /// <summary>On a floor, stand on the edge instead of hanging below it (Auto and the side modes when there is no
    /// room below; Bottom always stands).</summary>
    internal bool StandOnFloorEdge;

    /// <summary>The player's build rotation: the ghost's forward direction before the snap moved it. Absent means the
    /// ladder faces straight out of the surface.</summary>
    internal bool HasPlayerFacing;
    internal float PlayerFacingX;
    internal float PlayerFacingZ;

    /// <summary>The aimed collider's bounds: the real body of the piece, whose faces may lie outside its corner points
    /// (a wood wall's corners sit on its centre plane, its face 0.15 m out).</summary>
    internal bool HasBody;
    internal ClimbPoint BodyMin;
    internal ClimbPoint BodyMax;
}

/// <summary>Where the edge snap puts a ladder, in the target piece's local space.</summary>
internal readonly struct LadderEdgePlacement
{
    internal LadderEdgePlacement(ClimbPoint origin, float facingX, float facingZ, float normalX, float normalZ, ClimbPoint joint,
        float turnDegrees, bool onFloor)
    {
        Origin = origin;
        FacingX = facingX;
        FacingZ = facingZ;
        NormalX = normalX;
        NormalZ = normalZ;
        Joint = joint;
        TurnDegrees = turnDegrees;
        OnFloor = onFloor;
    }

    /// <summary>The ladder's origin (bottom-centre).</summary>
    internal ClimbPoint Origin { get; }

    /// <summary>The ladder's climbing side (its local +Z).</summary>
    internal float FacingX { get; }
    internal float FacingZ { get; }

    /// <summary>The surface's outward direction (the floor edge's or wall face's normal).</summary>
    internal float NormalX { get; }
    internal float NormalZ { get; }

    /// <summary>The point on the target the ladder attaches at: the edge midpoint, or the left or right corner, on
    /// the face, at the edge's height. The placement reach is measured to it.</summary>
    internal ClimbPoint Joint { get; }

    /// <summary>The ladder's yaw relative to straight out of the surface, in [-45°, 45°).</summary>
    internal float TurnDegrees { get; }

    internal bool OnFloor { get; }
}

/// <summary>Where a ladder goes when it is snapped to a floor or a wall (owner decisions 2026-10-26).
///
/// <para>Floors and walls only carry corner snap points; a wall snaps to a floor because its two bottom corners land
/// on two floor corners one edge apart. Everything here is in the target piece's local space, so the adapter
/// transforms in and out.</para>
/// <list type="bullet">
/// <item>Floor (corners spread on both horizontal axes): the edge nearest the aim. The ladder hangs below it (Auto,
/// Top, the side modes: you climb up onto the floor) or stands on it going up (Bottom, or
/// <see cref="LadderEdgeRequest.StandOnFloorEdge"/>).</item>
/// <item>Wall (corners on one horizontal axis and two heights): the face toward the player. The ladder stands on the
/// wall's bottom edge (Auto, Bottom, the side modes) or hangs from its top edge (Top).</item>
/// </list>
///
/// <para>Along the edge: Auto, Top and Bottom centre the ladder on it; Left side puts the ladder's visible left rail's
/// outer edge on the target's left corner, Right side its right rail's on the right corner. Left and right are as
/// seen by a player standing on the climbing side facing the surface, which is how the preview is looked at.</para>
///
/// <para>Depth: the deepest point of the ladder's footprint rests <see cref="Gap"/> off the surface's real face: the
/// outermost of the corner points, the aimed point and the aimed collider's bounds on that side (each at most
/// <see cref="MaximumBodyOverhang"/> beyond the corners), so a ladder never sinks into a wall whose corners lie on its
/// centre plane.</para>
///
/// <para>Rotation (owner revision 2026-10-26): the player's build rotation is kept, turned by whole quarter turns only,
/// so the climbing side faces as nearly straight out of the surface as that rotation allows: within ±45°, never into
/// the surface. With the hammer square to the building (the usual case) the ladder faces straight out; each native
/// rotation step (22.5°) then turns it 22.5° about the selected anchor (the edge midpoint, or the left or right
/// corner), and the footprint is pushed out so its deepest corner, not its centre, rests against the face.</para></summary>
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

    /// <summary>The farthest the aimed body or point may stand proud of the corner points and still count as the face.</summary>
    internal const float MaximumBodyOverhang = 0.6f;

    /// <summary>A build rotation this close to square with the surface counts as square (float noise only).</summary>
    internal const float SquareDegrees = 0.5f;

    /// <summary>The original Auto/Bottom/Top centring with the ladder facing straight out (kept for callers and tests
    /// that predate the side modes and rotation).</summary>
    internal static bool TryPlace(IReadOnlyList<ClimbPoint> corners, ClimbPoint aim, ClimbPoint viewer, LadderSnapMode mode,
        out ClimbPoint origin, out float facingX, out float facingZ, out ClimbPoint joint)
    {
        var request = new LadderEdgeRequest { Corners = corners, Aim = aim, Viewer = viewer, Mode = mode, Footprint = LadderFootprint.Section };
        bool placed = TryPlace(request, out LadderEdgePlacement placement);
        origin = placement.Origin;
        facingX = placed ? placement.FacingX : 0f;
        facingZ = placed ? placement.FacingZ : 1f;
        joint = placement.Joint;
        return placed;
    }

    /// <summary>The ladder's placement on a floor or wall, or false when the corner points describe neither, or any
    /// input is not finite.</summary>
    internal static bool TryPlace(in LadderEdgeRequest request, out LadderEdgePlacement placement)
    {
        placement = default;
        IReadOnlyList<ClimbPoint> corners = request.Corners;
        if (corners == null || corners.Count < 4 || !request.Aim.IsFinite || !request.Viewer.IsFinite || !request.Footprint.IsValid) { return false; }
        if (request.HasBody && (!request.BodyMin.IsFinite || !request.BodyMax.IsFinite)) { return false; }
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
        float nx, nz, edgeY, originY;
        bool floor = spanX >= MinimumEdge && spanZ >= MinimumEdge;
        if (floor)
        {
            // Floor: the four sides of its top level; the side nearest the aim.
            ClimbPoint aim = request.Aim;
            float[] distance = { Math.Abs(aim.X - minX), Math.Abs(aim.X - maxX), Math.Abs(aim.Z - minZ), Math.Abs(aim.Z - maxZ) };
            int edge = 0;
            for (int index = 1; index < 4; index++) { if (distance[index] < distance[edge]) { edge = index; } }
            nx = edge == 0 ? -1f : edge == 1 ? 1f : 0f;
            nz = edge == 2 ? -1f : edge == 3 ? 1f : 0f;
            edgeY = maxY;
            bool stand = request.Mode == LadderSnapMode.Bottom || request.StandOnFloorEdge;
            originY = stand ? maxY : maxY - SectionHeight;
        }
        else
        {
            bool alongX = spanX >= MinimumEdge && spanX >= spanZ;
            bool alongZ = !alongX && spanZ >= MinimumEdge;
            if ((!alongX && !alongZ) || maxY - minY < MinimumWallHeight) { return false; }

            // Wall: the face toward the viewer.
            float side = alongX ? Math.Sign(request.Viewer.Z - centreZ) : Math.Sign(request.Viewer.X - centreX);
            if (side == 0f) { side = 1f; }
            nx = alongX ? 0f : side;
            nz = alongX ? side : 0f;
            bool top = request.Mode == LadderSnapMode.Top;
            edgeY = top ? maxY : minY;
            originY = top ? maxY - SectionHeight : minY;
        }

        // The surface's outward normal n and the edge axis e: a player standing outside looking at the face (along
        // -n) has e on their right. Corner coordinates along both.
        float ex = -nz, ez = nx;
        float cornerFace = float.MinValue, leftEnd = float.MaxValue, rightEnd = float.MinValue;
        foreach (ClimbPoint p in corners)
        {
            cornerFace = Math.Max(cornerFace, p.X * nx + p.Z * nz);
            float along = p.X * ex + p.Z * ez;
            leftEnd = Math.Min(leftEnd, along);
            rightEnd = Math.Max(rightEnd, along);
        }

        float face = cornerFace;
        float limit = cornerFace + MaximumBodyOverhang;
        float aimed = request.Aim.X * nx + request.Aim.Z * nz;
        if (aimed <= limit) { face = Math.Max(face, aimed); }
        if (request.HasBody)
        {
            // n is a unit axis, so this is the bounds' extent on that side.
            float body = Math.Max(request.BodyMin.X * nx + request.BodyMin.Z * nz, request.BodyMax.X * nx + request.BodyMax.Z * nz);
            if (body <= limit) { face = Math.Max(face, body); }
        }

        float turn = Turn(request, nx, nz, out float fx, out float fz);

        // The footprint's corners relative to the origin, projected on n and e. The ladder's local +X is (fz, -fx).
        LadderFootprint footprint = request.Footprint;
        float rx = fz, rz = -fx;
        float deepest = float.MaxValue, alongMin = float.MaxValue, alongMax = float.MinValue;
        for (int corner = 0; corner < 4; corner++)
        {
            float x = (corner & 1) == 0 ? -footprint.HalfWidth : footprint.HalfWidth;
            float z = (corner & 2) == 0 ? footprint.Back : footprint.Front;
            float ox = x * rx + z * fx, oz = x * rz + z * fz;
            deepest = Math.Min(deepest, ox * nx + oz * nz);
            float along = ox * ex + oz * ez;
            alongMin = Math.Min(alongMin, along);
            alongMax = Math.Max(alongMax, along);
        }

        float jointAlong, originAlong;
        switch (request.Mode)
        {
            case LadderSnapMode.LeftSide:
                jointAlong = leftEnd;
                originAlong = leftEnd - alongMin;
                break;
            case LadderSnapMode.RightSide:
                jointAlong = rightEnd;
                originAlong = rightEnd - alongMax;
                break;
            default:
                jointAlong = (leftEnd + rightEnd) * 0.5f;
                originAlong = jointAlong - (alongMin + alongMax) * 0.5f;
                break;
        }

        float originOut = face + Gap - deepest;
        var origin = new ClimbPoint(nx * originOut + ex * originAlong, originY, nz * originOut + ez * originAlong);
        var joint = new ClimbPoint(nx * face + ex * jointAlong, edgeY, nz * face + ez * jointAlong);
        if (!origin.IsFinite || !joint.IsFinite) { return false; }
        placement = new LadderEdgePlacement(origin, fx, fz, nx, nz, joint, turn, floor);
        return true;
    }

    /// <summary>The ladder's facing: the player's build rotation turned by whole quarter turns to lie within ±45° of
    /// the surface normal (-45° inclusive, 45° exclusive, so a tie is deterministic). Returns the remaining turn in
    /// degrees; no rotation, or one within <see cref="SquareDegrees"/> of square, faces exactly along n.</summary>
    internal static float Turn(in LadderEdgeRequest request, float nx, float nz, out float fx, out float fz)
    {
        fx = nx;
        fz = nz;
        if (!request.HasPlayerFacing) { return 0f; }
        float px = request.PlayerFacingX, pz = request.PlayerFacingZ;
        if (!LadderRules.Finite(px) || !LadderRules.Finite(pz) || px * px + pz * pz < 1e-6f) { return 0f; }
        double relative = (Math.Atan2(px, pz) - Math.Atan2(nx, nz)) * 180.0 / Math.PI;
        double turn = relative - 90.0 * Math.Floor((relative + 45.0) / 90.0);
        if (Math.Abs(turn) < SquareDegrees || Math.Abs(turn - 45.0) < 1e-3) { turn = Math.Abs(turn) < SquareDegrees ? 0.0 : -45.0; }
        if (turn == 0.0) { return 0f; }
        double yaw = Math.Atan2(nx, nz) + turn * Math.PI / 180.0;
        fx = (float)Math.Sin(yaw);
        fz = (float)Math.Cos(yaw);
        return (float)turn;
    }
}
