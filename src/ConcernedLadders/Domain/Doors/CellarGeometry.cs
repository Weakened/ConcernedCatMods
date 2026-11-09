using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>Which of a cellar door's two leaves. Left is the leaf on the piece's -x side, hinged at x = -<see cref="CellarGeometry.HingeU"/>.</summary>
internal enum CellarLeaf
{
    Left,
    Right,
}

/// <summary>A point of a convex frame profile in the piece's (z, y) plane.</summary>
internal readonly struct ProfilePoint
{
    internal ProfilePoint(float z, float y)
    {
        Z = z;
        Y = y;
    }

    internal float Z { get; }

    internal float Y { get; }
}

/// <summary>One fixed frame part of a cellar door: a convex quadrilateral profile in the piece's (z, y) plane,
/// counter-clockwise seen from +x, extruded along x from <see cref="MinX"/> to <see cref="MaxX"/>. The sloped tops of
/// the side cheeks, front panel and rear header are not boxes, so the frame is prisms; the piece adapter builds each as
/// a mesh with a convex mesh collider from exactly these eight corners, so collision and visuals share one shape.</summary>
internal sealed class CellarPrism
{
    internal CellarPrism(string name, float minX, float maxX, ProfilePoint[] profile)
    {
        Name = name;
        MinX = minX;
        MaxX = maxX;
        Profile = profile;
    }

    internal string Name { get; }

    internal float MinX { get; }

    internal float MaxX { get; }

    /// <summary>Four points, counter-clockwise in (z, y) seen from +x.</summary>
    internal IReadOnlyList<ProfilePoint> Profile { get; }

    /// <summary>True when <paramref name="point"/> lies more than <paramref name="skin"/> inside the prism on every
    /// face, so touching a face is not inside. Allocation-free.</summary>
    internal bool ContainsStrictly(ClimbPoint point, float skin)
    {
        if (!(point.X > MinX + skin && point.X < MaxX - skin)) { return false; }
        for (int index = 0; index < Profile.Count; ++index)
        {
            ProfilePoint a = Profile[index];
            ProfilePoint b = Profile[(index + 1) % Profile.Count];
            float edgeZ = b.Z - a.Z, edgeY = b.Y - a.Y;
            float length = (float)Math.Sqrt(edgeZ * edgeZ + edgeY * edgeY);
            if (length <= 0f) { return false; }
            // Counter-clockwise: the inside is to the left of each edge.
            float left = (edgeZ * (point.Y - a.Y) - edgeY * (point.Z - a.Z)) / length;
            if (!(left > skin)) { return false; }
        }

        return true;
    }
}

/// <summary>The sloped cellar door (CL-005 #486): a fixed frame of two side cheeks, a low front panel and a high rear
/// header around an opening, covered by two leaves that meet at a central seam and are hinged at their outer sides.
/// One native door state drives both leaves through one progress (<see cref="CellarPairOccupancy{TLeaves}"/> and
/// <see cref="HatchMotion"/>), so they are always at the same angle, mirror images of each other.
///
/// <para>Piece-local axes are the piece's: x right, y up, z forward. The low end is the front (-z), the high end the
/// rear (+z), which is the side that stands against a wall. The leaves lie in the <b>slope frame</b>: u along x,
/// v up the slope from the front top edge, w along the slope's upward normal; <see cref="ToPiece"/> converts. The base
/// takes a native 2 m wood floor tile's place in the floor grid, with the same four corner snaps and the same bottom,
/// so its base sits in the floor around it; above that floor's top the frame rises to the sloped cover (0.30 m at the
/// front).</para>
///
/// <para>Every number here is a design default measured against the installed game's wood_floor (Valheim 1.0.21),
/// not an in-game observation; every placed-piece row is OWNER GO-AROUND PENDING.</para></summary>
internal static class CellarGeometry
{
    /// <summary>Outside footprint, metres: the native 2 m floor grid.</summary>
    internal const float Module = HatchGeometry.Module;

    internal const float HalfModule = Module / 2f;

    /// <summary>The native wood_floor's snap plane, as the trapdoor's.</summary>
    internal const float SnapPlaneY = HatchGeometry.SnapPlaneY;

    /// <summary>The wood_floor collider's top: the floor around the piece, from which the front and rear heights are
    /// measured.</summary>
    internal const float FloorTopY = HatchGeometry.TopY;

    /// <summary>Every frame part reaches down to the wood_floor collider's bottom, as the trapdoor's frame does.</summary>
    internal const float FrameBottomY = HatchGeometry.FrameBottomY;

    /// <summary>Height of the cover's top above the floor at the front (low) edge.</summary>
    internal const float FrontHeight = 0.3f;

    /// <summary>The slope's rise per metre of run: 1 m over the 2 m module (26.57 degrees).</summary>
    internal const float SlopeTangent = 0.5f;

    /// <summary>The cover's top at the front edge (z = -1).</summary>
    internal const float FrontTopY = FloorTopY + FrontHeight;

    /// <summary>The cover's top at the rear edge (z = +1): the rear header's outer top edge.</summary>
    internal const float RearTopY = FrontTopY + Module * SlopeTangent;

    /// <summary>Width (x) of each side cheek. The cheeks are the frame's sides, from the floor's bottom up to the slope.</summary>
    internal const float CheekWidth = 0.15f;

    /// <summary>Depth (z) of the low front panel.</summary>
    internal const float FrontPanelDepth = 0.15f;

    /// <summary>Depth (z) of the high rear header. Deeper than the front so that a wall standing on the floor-grid edge
    /// behind the piece, centred on it as a native wall on a floor edge is, intrudes only into the header and never
    /// into the leaves' sweep, as long as it is no more than 0.4 m thick (the sweep ends 0.01 m in front of the header).
    /// Native wall depths are not in CL-004's census; a thicker wall holds the leaves.</summary>
    internal const float RearHeaderDepth = 0.2f;

    /// <summary>Half the opening between the cheeks.</summary>
    internal const float HalfAperture = HalfModule - CheekWidth;

    /// <summary>The opening's front and rear faces in z: the front panel's and the rear header's inner faces.</summary>
    internal const float ApertureFrontZ = -HalfModule + FrontPanelDepth;

    internal const float ApertureRearZ = HalfModule - RearHeaderDepth;

    /// <summary>The visual gap between a leaf and the cheek, panel or header beside it.</summary>
    internal const float LeafGap = 0.01f;

    /// <summary>The gap between the two leaves at the central seam.</summary>
    internal const float SeamGap = 0.01f;

    internal const float LeafThickness = 0.06f;

    /// <summary>Each hinge axis runs up the slope through its leaf's top outer edge, at u = +/- this. With the axis on
    /// the top surface the leaf never swings into its cheek (every leaf point stays at or inside the hinge), so the
    /// cheek needs no rebate.</summary>
    internal const float HingeU = HalfAperture - LeafGap;

    /// <summary>A leaf's width across the slope, from its hinge to the seam.</summary>
    internal const float LeafWidth = HingeU - SeamGap / 2f;

    /// <summary>Fully open: each leaf stands at a right angle to the slope, in the vertical plane of its hinge. Equal
    /// to <see cref="HatchGeometry.OpenAngleDegrees"/>, which is what lets the leaves reuse <see cref="HatchMotion"/>
    /// unchanged (a test pins the equality).</summary>
    internal const float OpenAngleDegrees = 90f;

    /// <summary>Half the clear width once open: the open leaves' inner faces, a leaf's thickness inside each hinge.</summary>
    internal const float ClearHalfWidth = HingeU - LeafThickness;

    /// <summary>The four corner snap points and token of a wood_floor, as the trapdoor's: the base takes a floor
    /// tile's place on the grid.</summary>
    internal static IReadOnlyList<ClimbPoint> CornerSnaps => HatchGeometry.CornerSnaps;

    internal const string CornerSnapToken = HatchGeometry.CornerSnapToken;

    /// <summary>The slope angle, degrees.</summary>
    internal static readonly float SlopeDegrees = (float)(Math.Atan(SlopeTangent) * 180.0 / Math.PI);

    private static readonly float SlopeCos = (float)Math.Cos(Math.Atan(SlopeTangent));

    private static readonly float SlopeSin = (float)Math.Sin(Math.Atan(SlopeTangent));

    /// <summary>Where each leaf begins up the slope (v): its front end clears the front panel's inner top edge by
    /// <see cref="LeafGap"/> along z.</summary>
    internal static readonly float LeafStartV = (FrontPanelDepth + LeafGap) / SlopeCos;

    /// <summary>Where each leaf ends up the slope (v): the lowest point of its rear end, its bottom rear edge, stands
    /// <see cref="LeafGap"/> in front of the rear header. A leaf turns about an axis along v, so its rear end stays in
    /// one plane and comes no nearer the header than that edge at any angle.</summary>
    internal static readonly float LeafEndV = (Module - RearHeaderDepth - LeafGap - LeafThickness * SlopeSin) / SlopeCos;

    internal static readonly float LeafLength = LeafEndV - LeafStartV;

    /// <summary>The farthest any leaf point is from its hinge axis: the free bottom edge's distance.</summary>
    internal static readonly float LeafReach = (float)Math.Sqrt(LeafWidth * LeafWidth + LeafThickness * LeafThickness);

    /// <summary>The slope frame's origin in piece space: the front top edge's centre.</summary>
    internal static ClimbPoint SlopeOrigin => new ClimbPoint(0f, FrontTopY, -HalfModule);

    /// <summary>The cover's top surface height at a piece z (the slope plane w = 0).</summary>
    internal static float TopAt(float z) => FrontTopY + (z + HalfModule) * SlopeTangent;

    /// <summary>A slope-frame point in piece space. The slope frame is the piece frame turned by
    /// <c>Quaternion.Euler(-SlopeDegrees, 0, 0)</c> about x at <see cref="SlopeOrigin"/>, with (u, w, v) as its (x, y, z).</summary>
    internal static ClimbPoint ToPiece(float u, float w, float v) =>
        new ClimbPoint(u, FrontTopY + v * SlopeSin + w * SlopeCos, -HalfModule + v * SlopeCos - w * SlopeSin);

    /// <summary>The four fixed frame parts. Their tops lie in the slope plane, flush with the closed leaves.</summary>
    internal static readonly IReadOnlyList<CellarPrism> Frame = new[]
    {
        new CellarPrism("cheek_left", -HalfModule, -HalfAperture, Cheek()),
        new CellarPrism("cheek_right", HalfAperture, HalfModule, Cheek()),
        new CellarPrism("front_panel", -HalfAperture, HalfAperture, new[]
        {
            new ProfilePoint(-HalfModule, FrameBottomY),
            new ProfilePoint(ApertureFrontZ, FrameBottomY),
            new ProfilePoint(ApertureFrontZ, TopAt(ApertureFrontZ)),
            new ProfilePoint(-HalfModule, FrontTopY),
        }),
        new CellarPrism("rear_header", -HalfAperture, HalfAperture, new[]
        {
            new ProfilePoint(ApertureRearZ, FrameBottomY),
            new ProfilePoint(HalfModule, FrameBottomY),
            new ProfilePoint(HalfModule, RearTopY),
            new ProfilePoint(ApertureRearZ, TopAt(ApertureRearZ)),
        }),
    };

    /// <summary>The hinge pivot of a leaf in the slope frame (u, w, v): on its hinge axis, half way along the leaf.
    /// The leaf runtime turns this pivot about its local z (the slope frame's v) by
    /// <c>Quaternion.Euler(0, 0, -angle)</c> for the right leaf and <c>Quaternion.Euler(0, 0, angle)</c> for the left,
    /// which is <see cref="LeafCorner"/>'s direction (a test pins the convention).</summary>
    internal static ClimbPoint HingeInSlope(CellarLeaf leaf) =>
        new ClimbPoint(leaf == CellarLeaf.Right ? HingeU : -HingeU, 0f, (LeafStartV + LeafEndV) / 2f);

    /// <summary>The sign of the leaf runtime's z rotation for an opening angle: -1 for the right leaf, +1 for the left.</summary>
    internal static float HingeRotationSign(CellarLeaf leaf) => leaf == CellarLeaf.Right ? -1f : 1f;

    /// <summary>A leaf's box in its hinge pivot's space, closed: it lies inboard of the hinge (toward the seam) and
    /// below it. Returns the centre; the size is <see cref="LeafSize"/>.</summary>
    internal static ClimbPoint LeafCentreInHinge(CellarLeaf leaf) =>
        new ClimbPoint(leaf == CellarLeaf.Right ? -LeafWidth / 2f : LeafWidth / 2f, -LeafThickness / 2f, 0f);

    /// <summary>A leaf's extents: x across the slope, y thickness, z up the slope.</summary>
    internal static ClimbPoint LeafSize => new ClimbPoint(LeafWidth, LeafThickness, LeafLength);

    /// <summary>One of a leaf's eight corners at an opening angle, in piece space, without allocating: bit 0 of
    /// <paramref name="index"/> picks the end (front or rear up the slope), bit 1 the bottom face, bit 2 the free (seam)
    /// edge. The left leaf is the right leaf mirrored in x at the same angle. A box's extremes are its corners, so
    /// bounds tests over these are exact.</summary>
    internal static ClimbPoint LeafCorner(CellarLeaf leaf, float angleDegrees, int index)
    {
        SlopeCorner(leaf, angleDegrees, index, out float u, out float w, out float v);
        return ToPiece(u, w, v);
    }

    /// <summary>A leaf corner in the slope frame (u, w, v). Allocation-free.</summary>
    internal static void SlopeCorner(CellarLeaf leaf, float angleDegrees, int index, out float u, out float w, out float v)
    {
        double radians = Clamp(angleDegrees) * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        // The right leaf, relative to its hinge: the seam edge is LeafWidth toward -u, the bottom LeafThickness down.
        float du = (index & 4) == 0 ? 0f : -LeafWidth;
        float dw = (index & 2) == 0 ? 0f : -LeafThickness;
        float right = HingeU + du * cos + dw * sin;
        w = -du * sin + dw * cos;
        v = (index & 1) == 0 ? LeafStartV : LeafEndV;
        u = leaf == CellarLeaf.Right ? right : -right;
    }

    /// <summary>All eight corners of a leaf. Allocates: for tests and diagnostics only, never per frame.</summary>
    internal static ClimbPoint[] LeafCorners(CellarLeaf leaf, float angleDegrees)
    {
        var corners = new ClimbPoint[8];
        for (int index = 0; index < 8; ++index) { corners[index] = LeafCorner(leaf, angleDegrees, index); }
        return corners;
    }

    /// <summary>The leaf's complete sweep from closed to open, as a box in the slope frame (u, w, v as x, y, z): the
    /// exact bounds of everything the leaf passes through. Across the slope it reaches from its hinge to
    /// <see cref="LeafReach"/> inside it (its free bottom edge, a few degrees open); along the normal from a thickness
    /// below the slope to a leaf's width above it (upright); along the slope its own length.</summary>
    internal static HatchBox SweepInSlope(CellarLeaf leaf)
    {
        float inner = HingeU - LeafReach;
        return leaf == CellarLeaf.Right
            ? new HatchBox(new ClimbPoint(inner, -LeafThickness, LeafStartV), new ClimbPoint(HingeU, LeafWidth, LeafEndV))
            : new HatchBox(new ClimbPoint(-HingeU, -LeafThickness, LeafStartV), new ClimbPoint(-inner, LeafWidth, LeafEndV));
    }

    /// <summary>The piece-space box that contains a leaf's sweep: the bounds of <see cref="SweepInSlope"/>'s eight
    /// corners. Forward of the module at the top (an open leaf stands square to the slope, so it leans over the low
    /// end), and nowhere behind the rear header or beside the cheeks.</summary>
    internal static HatchBox SweepInPiece(CellarLeaf leaf)
    {
        HatchBox slope = SweepInSlope(leaf);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int index = 0; index < 8; ++index)
        {
            ClimbPoint p = ToPiece(
                (index & 1) == 0 ? slope.Min.X : slope.Max.X,
                (index & 2) == 0 ? slope.Min.Y : slope.Max.Y,
                (index & 4) == 0 ? slope.Min.Z : slope.Max.Z);
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
        }

        return new HatchBox(new ClimbPoint(minX, minY, minZ), new ClimbPoint(maxX, maxY, maxZ));
    }

    /// <summary>The clear passage once both leaves are open, in plan: between the open leaves' inner faces across, and
    /// between the front panel and the rear header along. Nothing of the frame or either open leaf is inside this
    /// column at any height.</summary>
    internal static HatchBox ClearColumn(float bottomY, float topY) =>
        new HatchBox(new ClimbPoint(-ClearHalfWidth, bottomY, ApertureFrontZ), new ClimbPoint(ClearHalfWidth, topY, ApertureRearZ));

    /// <summary>The margins of a standing or climbing capsule of <paramref name="radius"/> in the open passage, metres:
    /// to each open leaf's inner face, and the free travel left along the opening. False (both negative infinity) for
    /// an unusable radius.</summary>
    internal static bool PassageMargins(float radius, out float side, out float travel)
    {
        side = travel = float.NegativeInfinity;
        if (!Finite(radius) || radius < 0.05f || radius > 1f) { return false; }
        side = ClearHalfWidth - radius;
        travel = (ApertureRearZ - ApertureFrontZ) - 2f * radius;
        return true;
    }

    /// <summary>The planned ladder position, for the ladder slice: hung inside the opening from the front panel's
    /// inner face, climbing side facing the rear, its top level with the snap plane, as <see cref="HatchGeometry"/>'s
    /// ladder hangs from the trapdoor's front band. Not wired to placement yet.</summary>
    internal static ClimbPoint PlannedLadderOrigin =>
        new ClimbPoint(0f, SnapPlaneY - LadderEdgeSnap.SectionHeight, ApertureFrontZ + LadderEdgeSnap.Gap - LadderEdgeSnap.RailBack);

    /// <summary>A front-lane climber's margins on the planned ladder once both leaves are open: to the front panel, to
    /// the rear header, and to each open leaf. False (all negative infinity) for an unusable radius.</summary>
    internal static bool ClimberMargins(float radius, out float front, out float rear, out float side)
    {
        front = rear = side = float.NegativeInfinity;
        if (!Finite(radius) || radius < 0.05f || radius > 1f) { return false; }
        LadderFootprint section = LadderFootprint.Section;
        float centreZ = PlannedLadderOrigin.Z + ClimbPolicy.LaneDepth(LadderFace.Front, radius, section.Back, section.Front);
        front = (centreZ - radius) - ApertureFrontZ;
        rear = ApertureRearZ - (centreZ + radius);
        side = ClearHalfWidth - radius;
        return true;
    }

    private static ProfilePoint[] Cheek() => new[]
    {
        new ProfilePoint(-HalfModule, FrameBottomY),
        new ProfilePoint(HalfModule, FrameBottomY),
        new ProfilePoint(HalfModule, RearTopY),
        new ProfilePoint(-HalfModule, FrontTopY),
    };

    /// <summary>NaN is the closed leaf; an infinite angle clamps to its end of the swing.</summary>
    private static float Clamp(float angleDegrees) =>
        float.IsNaN(angleDegrees) ? 0f : Math.Max(0f, Math.Min(OpenAngleDegrees, angleDegrees));

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>The runtime's per-leaf occupancy question (CL-005 #486): would this leaf's pose at this angle overlap
/// anything it must not move into? The leaf runtime (<c>Runtime/CellarLeaves</c>, slice 3) answers it with the
/// trapdoor's bounded, skinned overlap, one per leaf, ignoring the piece's own colliders by identity; tests answer it
/// with a script.</summary>
internal interface ICellarLeafOccupancy
{
    bool Occupied(CellarLeaf leaf, float angleDegrees);
}

/// <summary>The pair rule (CL-005 #486): both leaves follow one progress, so the pair's pose at an angle is occupied
/// when either leaf's is. Fed to <see cref="HatchMotion.Step{TProbe}"/> and <see cref="HatchMotion.Settle{TProbe}"/>
/// unchanged, a leaf that meets something holds <b>both</b> leaves at the last pose clear for both: the leaves can never
/// be at different angles, so one can never be open while the other is shut, on this client or any other. The pair is a
/// passage only when <see cref="HatchMotion.IsPassage"/> (both fully open and staying open) and a landing only when
/// <see cref="HatchMotion.IsLanding"/> (both closed and staying closed). A struct over a constrained type parameter, so
/// nothing is boxed or allocated per frame.</summary>
internal readonly struct CellarPairOccupancy<TLeaves> : IHatchOccupancy
    where TLeaves : ICellarLeafOccupancy
{
    private readonly TLeaves _leaves;

    internal CellarPairOccupancy(TLeaves leaves) => _leaves = leaves;

    public bool Occupied(float angleDegrees) =>
        _leaves.Occupied(CellarLeaf.Left, angleDegrees) || _leaves.Occupied(CellarLeaf.Right, angleDegrees);
}

/// <summary>The cellar door's first pose (CL-005 #486 slice 3, lead default 15): fail-closed against fixed
/// obstructions. <see cref="HatchMotion.Settle{TProbe}"/> alone takes the open end whenever the stored end is
/// occupied. That is right for someone standing in the entrance, whom the closed leaves must not be put into. It is
/// wrong for a fixed wall on the rear edge that is in the closed leaves but not the open ones: the cover would load open
/// while the native state says closed, and could never close past the wall; and a cover stored open (a Use asked it
/// to open, the wall held it) would load open for the same reason. So, in order:
/// <list type="number">
/// <item>closed, whatever is stored, when something is in the closed pose (<paramref name="all"/>) but nothing that
/// could be pushed is (<paramref name="movers"/>: a character, an item, a cart or a ship, or any dynamic body). That
/// something is fixed: a wall, a piece, a kinematic part. The closed leaves are kinematic, so where they overlap it nothing moves, and a walled cover
/// is shut rather than open on any client;</item>
/// <item>otherwise <see cref="HatchMotion.Settle{TProbe}"/> with the full probe, unchanged: the stored end when it is
/// clear (the normal case, every client agrees), and otherwise the trapdoor's tested policy, which keeps the leaves out
/// of whoever is in the closed pose or where the open leaves stand.</item>
/// </list>
/// <see cref="HatchMotion"/> itself is not changed. Game-free and allocation-free; two questions before the
/// trapdoor's own.</summary>
internal static class CellarSettle
{
    internal static float First<TAll, TMovers>(bool open, TAll all, TMovers movers)
        where TAll : IHatchOccupancy
        where TMovers : IHatchOccupancy
    {
        if (all.Occupied(HatchMotion.Angle(0f)) && !movers.Occupied(HatchMotion.Angle(0f))) { return 0f; }
        return HatchMotion.Settle(open, all);
    }
}
