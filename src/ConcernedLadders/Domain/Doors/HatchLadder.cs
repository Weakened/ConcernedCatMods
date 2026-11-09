using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>Which part of a trapdoor a collider belongs to, for the ladder's landing search.</summary>
internal enum HatchPart
{
    /// <summary>Not part of a trapdoor at all.</summary>
    None,

    /// <summary>One of the four fixed frame bands: a floor like any other.</summary>
    Frame,

    /// <summary>The hinged lid, which moves.</summary>
    Lid,

    /// <summary>A trapdoor whose hinge could not be found, so the lid cannot be told from the frame.</summary>
    Unknown,
}

/// <summary>The ladder side of the floor hatch (CL-004 #485, slice 4). Game-free: the placement adapter
/// (<c>Pieces/LadderSnapAlignment</c>), the climb runtime (<c>Runtime/LadderRuntime</c>) and its landing search
/// (<c>Runtime/LadderClearance</c>) ask these rules, and the lid runtime (<c>Runtime/TrapdoorLid</c>) answers them with
/// <see cref="HatchMotion.IsPassage"/> and <see cref="HatchMotion.IsLanding"/> from its own progress and the native door
/// state. Nothing here exists in a world where <c>Trapdoors/Enabled</c> is off: no trapdoor is registered, so no
/// adapter ever finds one, and every ladder path stays exactly as it was.
///
/// <para>The three rules:</para>
/// <list type="bullet">
/// <item><b>Anchor.</b> Auto and Top aimed at a trapdoor hang the ladder in the hatch's own anchor
/// (<see cref="HatchGeometry.LadderOrigin"/>, facing the hinge), whatever the hammer's rotation: a turned ladder would
/// not leave the climber in the opening (<see cref="HatchGeometry.ClimberMargins"/> is measured square). Bottom and the
/// side modes keep the floor-edge behaviour, to which a trapdoor is one more 2 m floor tile, and so does Auto when
/// there is no room below the hatch (as Auto then stands on a floor edge).</item>
/// <item><b>Passage.</b> A climber's route (a mount, an exit) that would cross a trapdoor's opening is committed only
/// while that lid is a passage: fully open and not asked to close. A lid asked to close holds against a climber it
/// would touch (slice 3), so a route already under way finishes, but no new one starts through it.</item>
/// <item><b>Landing.</b> The lid is a floor the landing search may stand a climber on only while it is closed and
/// staying closed. A moving, held or open lid is never a landing, even where its tilted face would pass the search's
/// slope test; a ray that meets it finds no landing at that point at all. The frame is always a floor.</item>
/// </list></summary>
internal static class HatchLadder
{
    /// <summary>True for the Cycle Snap modes that hang a ladder in a hatch's anchor: Auto and Top.</summary>
    internal static bool UsesAnchor(LadderSnapMode mode) => mode == LadderSnapMode.Auto || mode == LadderSnapMode.Top;

    /// <summary>The point the anchored ladder attaches at, hatch-local: the middle of the front frame band's inner face,
    /// at the snap plane. The placement reach is measured to it.</summary>
    internal static ClimbPoint AnchorJoint => new ClimbPoint(0f, HatchGeometry.SnapPlaneY, -HatchGeometry.HalfAperture);

    /// <summary>The anchored ladder, hatch-local, in the edge snap's own terms: origin
    /// <see cref="HatchGeometry.LadderOrigin"/>, climbing side (+z of the ladder) toward the hinge, against the front
    /// band's inner face whose normal points into the opening, hanging (its top on the snap plane).</summary>
    internal static LadderEdgePlacement Anchor =>
        new LadderEdgePlacement(HatchGeometry.LadderOrigin, 0f, 1f, 0f, 1f, AnchorJoint, 0f, true);

    /// <summary>Where, hatch-local, the anchor's room-below test looks: the middle of the ladder's footprint, just under
    /// the frame. Auto hangs in the anchor only when nothing solid lies below this for most of a section's height.</summary>
    internal static ClimbPoint RoomProbe =>
        new ClimbPoint(0f, HatchGeometry.FrameBottomY - RoomProbeBelowFrame,
            HatchGeometry.LadderOrigin.Z + (LadderFootprint.Section.Back + LadderFootprint.Section.Front) * 0.5f);

    /// <summary>The room test starts this far under the frame, so it never meets the hatch's own colliders.</summary>
    internal const float RoomProbeBelowFrame = 0.05f;

    /// <summary>How far down the room test looks from <see cref="RoomProbe"/>: most of a section, as the floor-edge
    /// test does (<c>SectionHeight - 0.1</c> from just above the edge).</summary>
    internal const float RoomProbeDepth = LadderEdgeSnap.SectionHeight - 0.2f;

    /// <summary>Does a climber's capsule at hatch-local (<paramref name="x"/>, <paramref name="z"/>) of radius
    /// <paramref name="radius"/>, occupying heights <paramref name="low"/> to <paramref name="high"/> relative to the
    /// hatch's origin over its route, cross this hatch's opening? The opening is widened by the radius on every side
    /// (a square, not a rounded rectangle, so a capsule near a corner counts), and the hatch is as deep as its frame,
    /// less <see cref="ContactSlack"/> at each face: a capsule standing on the frame's top, or whose head just meets its
    /// underside, touches the hatch and does not cross it. Any non-finite input crosses: a route that cannot be measured
    /// is held to the passage rule.</summary>
    internal static bool Crosses(float x, float z, float radius, float low, float high)
    {
        if (!Finite(x) || !Finite(z) || !Finite(radius) || !Finite(low) || !Finite(high) || radius < 0f) { return true; }
        float reach = HatchGeometry.HalfAperture + radius;
        return Math.Abs(x) < reach && Math.Abs(z) < reach &&
               Math.Min(low, high) < HatchGeometry.TopY - ContactSlack && Math.Max(low, high) > HatchGeometry.FrameBottomY + ContactSlack;
    }

    /// <summary>The heights one straight leg of a route occupies, from its lower end to the capsule's top
    /// (<paramref name="capsuleHeight"/>, any float margin included) at its higher end. Each leg is tested over its own
    /// span, never the whole route's: a settle onto a floor occupies only that floor's height, so a landing on a closed
    /// lid, or beside a hatch, is not mistaken for a crossing because the route started lower down.</summary>
    internal static void LegSpan(float fromY, float toY, float capsuleHeight, out float low, out float high)
    {
        low = Math.Min(fromY, toY);
        high = Math.Max(fromY, toY) + capsuleHeight;
    }

    /// <summary>How far a capsule may reach into the hatch's depth at its top or bottom face and still only touch it,
    /// metres: the ladder clearance's own skin, by which a landing floats above the surface it found.</summary>
    internal const float ContactSlack = 0.02f;

    /// <summary>May a route be committed past this hatch? Always when it does not cross the opening; otherwise only
    /// while the lid is a passage (<see cref="HatchMotion.IsPassage"/>).</summary>
    internal static bool AllowsRoute(bool crosses, bool lidIsPassage) => !crosses || lidIsPassage;

    /// <summary>May the landing search stand a climber on a surface of this part? Anything that is not a trapdoor, and
    /// a trapdoor's frame, always; its lid only while it is a landing (<see cref="HatchMotion.IsLanding"/>); a trapdoor
    /// whose lid cannot be told from its frame, never.</summary>
    internal static bool IsLandingSurface(HatchPart part, bool lidIsLanding) => part switch
    {
        HatchPart.None => true,
        HatchPart.Frame => true,
        HatchPart.Lid => lidIsLanding,
        _ => false,
    };

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
