using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>An axis-aligned box in hatch-local metres.</summary>
internal readonly struct HatchBox
{
    internal HatchBox(ClimbPoint min, ClimbPoint max)
    {
        Min = min;
        Max = max;
    }

    internal ClimbPoint Min { get; }

    internal ClimbPoint Max { get; }

    internal ClimbPoint Centre => new ClimbPoint((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f, (Min.Z + Max.Z) * 0.5f);

    internal ClimbPoint Size => new ClimbPoint(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);
}

/// <summary>The lid at one hinge angle: an oriented box turned about the hatch's local x axis.</summary>
internal readonly struct LidPose
{
    internal LidPose(float angleDegrees, ClimbPoint centre, ClimbPoint size)
    {
        AngleDegrees = angleDegrees;
        Centre = centre;
        Size = size;
    }

    /// <summary>Rotation about local +x that lifts the front edge: 0 closed, <see cref="HatchGeometry.OpenAngleDegrees"/> open.
    /// The model turns +z toward -y as the angle grows, which is the direction of Unity's own positive x rotation, so
    /// a runtime box built from this pose uses <c>Quaternion.Euler(AngleDegrees, 0, 0)</c> about the hinge.</summary>
    internal float AngleDegrees { get; }

    internal ClimbPoint Centre { get; }

    /// <summary>The lid's unrotated extents (x width, y thickness, z length).</summary>
    internal ClimbPoint Size { get; }
}

/// <summary>The 2 m x 2 m floor hatch (CL-004 #485): one hinged lid in a fixed frame, flush with a native wood floor.
/// Hatch-local axes are the piece's: x right, y up, z forward. The hinge runs along x at the rear (+z) edge, so the
/// lid lifts its front edge and stands upright over the rear of the opening; a ladder hangs from the front edge,
/// facing the hinge. Every number here is a design default measured against the installed game's wood_floor
/// (Valheim 1.0.21), not an in-game observation; the placed-piece rows are OWNER GO-AROUND PENDING.</summary>
internal static class HatchGeometry
{
    /// <summary>Outside footprint, metres (the native 2 m floor grid).</summary>
    internal const float Module = 2f;

    internal const float HalfModule = Module / 2f;

    /// <summary>The native wood_floor's snap plane: its four corner snap points sit at y = 0.</summary>
    internal const float SnapPlaneY = 0f;

    /// <summary>The wood_floor collider's top: child at y 0.037, scale y 0.1, box centre -0.0503, size 1.2975.</summary>
    internal const float TopY = 0.037f + 0.1f * (-0.0503f + 1.2975f / 2f);

    /// <summary>The wood_floor collider's bottom; the frame is exactly as deep as a wood floor.</summary>
    internal const float FrameBottomY = 0.037f + 0.1f * (-0.0503f - 1.2975f / 2f);

    /// <summary>Width of each of the four frame bands around the opening.</summary>
    internal const float FrameWidth = 0.2f;

    /// <summary>Half the opening inside the frame: the aperture is 1.6 m x 1.6 m.</summary>
    internal const float HalfAperture = HalfModule - FrameWidth;

    /// <summary>The visual gap between the lid's edge and the frame on every side.</summary>
    internal const float LidGap = 0.01f;

    internal const float HalfLid = HalfAperture - LidGap;

    internal const float LidLength = 2f * HalfLid;

    internal const float LidThickness = 0.08f;

    /// <summary>The hinge axis: along x through the lid's top rear edge. With the axis on the top surface the lid
    /// never swings into the frame (every lid point stays at or in front of the hinge), so the frame needs no rebate.</summary>
    internal const float HingeY = TopY;

    internal const float HingeZ = HalfLid;

    /// <summary>Fully open: the lid stands upright behind the opening, its front face <see cref="LidThickness"/> in
    /// front of the hinge.</summary>
    internal const float OpenAngleDegrees = 90f;

    /// <summary>The clear opening once fully open: the lid's front face, upright, bounds it at the rear.</summary>
    internal const float ClearRearZ = HingeZ - LidThickness;

    internal const float ClearFrontZ = -HalfAperture;

    /// <summary>The four corner snap points, at the same places and with the same native token as wood_floor's, so the
    /// hatch takes a floor tile's place on the grid beside wood and (as a wood floor would) stone flooring.</summary>
    internal static readonly IReadOnlyList<ClimbPoint> CornerSnaps = new[]
    {
        new ClimbPoint(HalfModule, SnapPlaneY, -HalfModule),
        new ClimbPoint(-HalfModule, SnapPlaneY, -HalfModule),
        new ClimbPoint(HalfModule, SnapPlaneY, HalfModule),
        new ClimbPoint(-HalfModule, SnapPlaneY, HalfModule),
    };

    internal const string CornerSnapToken = "$hud_snappoint_corner";

    /// <summary>The four fixed frame bands. Front and rear run the full module width; the sides fill between them.</summary>
    internal static readonly IReadOnlyList<HatchBox> Frame = new[]
    {
        new HatchBox(new ClimbPoint(-HalfModule, FrameBottomY, -HalfModule), new ClimbPoint(HalfModule, TopY, -HalfAperture)),
        new HatchBox(new ClimbPoint(-HalfModule, FrameBottomY, HalfAperture), new ClimbPoint(HalfModule, TopY, HalfModule)),
        new HatchBox(new ClimbPoint(-HalfModule, FrameBottomY, -HalfAperture), new ClimbPoint(-HalfAperture, TopY, HalfAperture)),
        new HatchBox(new ClimbPoint(HalfAperture, FrameBottomY, -HalfAperture), new ClimbPoint(HalfModule, TopY, HalfAperture)),
    };

    /// <summary>The lid at a hinge angle, clamped to [0, open]. NaN is the closed lid. Allocation-free.</summary>
    internal static LidPose Lid(float angleDegrees)
    {
        float angle = Clamp(angleDegrees);
        double radians = angle * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        // The lid's centre relative to the hinge before rotation: half a thickness down, half a length forward (-z).
        const float dy = -LidThickness / 2f, dz = -HalfLid;
        float y = dy * cos - dz * sin;
        float z = dy * sin + dz * cos;
        return new LidPose(angle, new ClimbPoint(0f, HingeY + y, HingeZ + z), new ClimbPoint(LidLength, LidThickness, LidLength));
    }

    /// <summary>One of the lid's eight corners at a hinge angle, without allocating: bit 0 of <paramref name="index"/>
    /// picks the side (x), bit 1 the bottom face (y), bit 2 the front edge (z). A box's extremes are its corners, so
    /// bounds and clearance tests over these are exact.</summary>
    internal static ClimbPoint LidCorner(float angleDegrees, int index)
    {
        float angle = Clamp(angleDegrees);
        double radians = angle * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        float x = (index & 1) == 0 ? -HalfLid : HalfLid;
        float dy = (index & 2) == 0 ? 0f : -LidThickness;
        float dz = (index & 4) == 0 ? 0f : -LidLength;
        return new ClimbPoint(x, HingeY + dy * cos - dz * sin, HingeZ + dy * sin + dz * cos);
    }

    /// <summary>All eight corners. Allocates: for tests and diagnostics only, never per frame.</summary>
    internal static ClimbPoint[] LidCorners(float angleDegrees)
    {
        var corners = new ClimbPoint[8];
        for (int index = 0; index < 8; ++index) { corners[index] = LidCorner(angleDegrees, index); }
        return corners;
    }

    /// <summary>The ladder that serves the hatch: hung inside the opening from its front edge, top level with the
    /// snap plane (as <see cref="LadderEdgeSnap"/> hangs a ladder from a floor), rails' back face
    /// <see cref="LadderEdgeSnap.Gap"/> from the frame's inner face, climbing side facing the hinge (+z).
    /// Returns the ladder section's origin; its footprint is <see cref="LadderFootprint.Section"/>.</summary>
    internal static ClimbPoint LadderOrigin =>
        new ClimbPoint(0f, SnapPlaneY - LadderEdgeSnap.SectionHeight, -HalfAperture + LadderEdgeSnap.Gap - LadderEdgeSnap.RailBack);

    /// <summary>The centre of a climber's capsule in hatch z on a ladder face, from the runtime's own lane rule.</summary>
    internal static float ClimberCentreZ(LadderFace face, float radius)
    {
        LadderFootprint section = LadderFootprint.Section;
        return LadderOrigin.Z + ClimbPolicy.LaneDepth(face, radius, section.Back, section.Front);
    }

    /// <summary>The margins between a front-lane climber's capsule and the clear opening once the lid is open,
    /// metres: to the front frame band, to the open lid's face, and to each side band. False (all margins negative
    /// infinity) for an unusable radius. Only the front lane (facing the hinge) passes through the hatch: the rear
    /// lane lies under the front frame band and the floor beyond it, so a climber on the back of the ladder meets
    /// the floor, as on any ladder hung from a floor edge.</summary>
    internal static bool ClimberMargins(float radius, out float front, out float rear, out float side)
    {
        front = rear = side = float.NegativeInfinity;
        if (!Finite(radius) || radius < 0.05f || radius > 1f) { return false; }
        float centreZ = ClimberCentreZ(LadderFace.Front, radius);
        front = (centreZ - radius) - ClearFrontZ;
        rear = ClearRearZ - (centreZ + radius);
        side = HalfAperture - radius;
        return true;
    }

    /// <summary>The smallest of <see cref="ClimberMargins"/>. Negative means the capsule would not fit.</summary>
    internal static float ClimberMargin(float radius) =>
        ClimberMargins(radius, out float front, out float rear, out float side)
            ? Math.Min(front, Math.Min(rear, side))
            : float.NegativeInfinity;

    /// <summary>NaN is the closed lid; an infinite angle clamps to its end of the swing.</summary>
    private static float Clamp(float angleDegrees) =>
        float.IsNaN(angleDegrees) ? 0f : Math.Max(0f, Math.Min(OpenAngleDegrees, angleDegrees));

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
