using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>The ladder side of the sloped cellar door (CL-005 #486, slice 4), as <see cref="HatchLadder"/> is the
/// trapdoor's. Game-free: the placement adapter (<c>Pieces/LadderSnapAlignment</c>), the climb runtime
/// (<c>Runtime/LadderRuntime</c>) and its landing search (<c>Runtime/LadderClearance</c>) ask these rules, and the leaf
/// runtime (<c>Runtime/CellarLeaves</c>) answers them with <see cref="HatchMotion.IsPassage"/> and
/// <see cref="HatchMotion.IsLanding"/> from the pair's one progress and the native door state. Nothing here exists in a
/// world where <c>CellarDoors/Enabled</c> is off: no cellar door is registered, so no adapter ever finds one, and every
/// ladder path stays exactly as it was.
///
/// <para>The rules (each a lead default pending owner confirmation, <c>CELLAR_DOORS.md</c> section 8):</para>
/// <list type="bullet">
/// <item><b>Never a floor.</b> A cellar door's cover rises 0.30 to 1.40 m above the floor it stands in, so the floor-edge
/// snap (which reads its four corner snaps as a 2 m floor tile) would hang or stand a ladder at the outside of a frame
/// that is not there. Aimed at a cellar door, Auto (when there is room below) and Top hang the ladder in the cover's
/// own anchor (<see cref="Anchor"/>); every other mode, and Auto without room below, is refused, never placed on an
/// edge.</item>
/// <item><b>Passage.</b> A route that would cross the opening is committed only while the pair is a passage: both
/// leaves fully open and not asked to close. The cover's depth is the frame's, from its bottom up to the slope's top
/// over the capsule (<see cref="Crosses"/>), so a capsule standing on the closed leaves does not cross it.</item>
/// <item><b>Landing.</b> A leaf is a floor the landing search may stand a climber on only while the pair is closed and
/// staying closed (<see cref="HatchLadder.IsLandingSurface"/>, reused). A moving, held or open leaf is never a
/// landing, even its upright top edge, whose face passes the search's slope test; a ray that meets one finds no landing
/// there at all. The fixed frame is an ordinary piece.</item>
/// <item><b>Over the front panel.</b> The anchored ladder's top is the snap plane, 0.40 to 0.47 m below the front
/// panel's top. A top exit from the opening, or a descending entry into it, crosses the panel. Its standard lifts (6 cm
/// and a 0.3 m step over the higher end) leave the feet below the panel's top, and the clearance query's capsule
/// floats 5 cm over the feet with a 2 cm skin, so the 0.3 m lift from the floor would pass the query while the real
/// body's bottom met the panel's inner edge about 5.5 cm deep. So a route with one end in a cover's opening and the
/// other outside it at the floor's level (<see cref="LeavesOverFrame"/>) tries only lifts that carry the feet at least
/// to <see cref="OverPanelY"/>, <see cref="PanelClearance"/> over the panel's inner top edge (<see cref="TryPanelLift"/>,
/// <see cref="Lifts"/>). A descending entry's hands then reach over the panel to that height, not to the run's top.
/// Every leg is still swept with the capsule and still needs the passage.</item>
/// </list></summary>
internal static class CellarLadder
{
    /// <summary>True for the Cycle Snap modes that hang a ladder in a cellar door's anchor: Auto and Top, as on a
    /// trapdoor.</summary>
    internal static bool UsesAnchor(LadderSnapMode mode) => HatchLadder.UsesAnchor(mode);

    /// <summary>The point the anchored ladder attaches at, piece-local: the middle of the front panel's inner face, at
    /// the snap plane. The placement reach is measured to it.</summary>
    internal static ClimbPoint AnchorJoint => new ClimbPoint(0f, CellarGeometry.SnapPlaneY, CellarGeometry.ApertureFrontZ);

    /// <summary>The anchored ladder, piece-local, in the edge snap's own terms: origin
    /// <see cref="CellarGeometry.PlannedLadderOrigin"/>, climbing side (+z of the ladder) toward the rear header, against
    /// the front panel's inner face whose normal points into the opening, hanging (its top on the snap plane).</summary>
    internal static LadderEdgePlacement Anchor =>
        new LadderEdgePlacement(CellarGeometry.PlannedLadderOrigin, 0f, 1f, 0f, 1f, AnchorJoint, 0f, true);

    /// <summary>Where, piece-local, the anchor's room-below test looks: the middle of the ladder's footprint, just under
    /// the frame. Auto hangs in the anchor only when nothing solid lies below this for most of a section's height.</summary>
    internal static ClimbPoint RoomProbe =>
        new ClimbPoint(0f, CellarGeometry.FrameBottomY - HatchLadder.RoomProbeBelowFrame,
            CellarGeometry.PlannedLadderOrigin.Z + (LadderFootprint.Section.Back + LadderFootprint.Section.Front) * 0.5f);

    /// <summary>How far down the room test looks from <see cref="RoomProbe"/>: the trapdoor's depth.</summary>
    internal const float RoomProbeDepth = HatchLadder.RoomProbeDepth;

    /// <summary>Does a climber's capsule at piece-local (<paramref name="x"/>, <paramref name="z"/>) of radius
    /// <paramref name="radius"/>, occupying heights <paramref name="low"/> to <paramref name="high"/> relative to the
    /// piece's origin over one leg of its route, cross this cover's opening? The opening (x within the cheeks, z between
    /// the front panel and the rear header) is widened by the radius on every side, a rectangle rather than a rounded
    /// one, so a capsule near a corner counts. The cover is as deep as its frame, from the frame's bottom up to the slope's
    /// top at the capsule's centre (clamped to the opening), less <see cref="HatchLadder.ContactSlack"/> at each face: a
    /// capsule standing on the closed leaves, or on the floor clear of the module, touches or misses the cover and does
    /// not cross it. Any non-finite input crosses: a route that cannot be measured is held to the passage rule.</summary>
    internal static bool Crosses(float x, float z, float radius, float low, float high)
    {
        if (!Finite(x) || !Finite(z) || !Finite(radius) || !Finite(low) || !Finite(high) || radius < 0f) { return true; }
        if (!(Math.Abs(x) < CellarGeometry.HalfAperture + radius &&
              z > CellarGeometry.ApertureFrontZ - radius && z < CellarGeometry.ApertureRearZ + radius))
        {
            return false;
        }

        float top = CellarGeometry.TopAt(Math.Max(CellarGeometry.ApertureFrontZ, Math.Min(CellarGeometry.ApertureRearZ, z)));
        return Math.Min(low, high) < top - HatchLadder.ContactSlack &&
               Math.Max(low, high) > CellarGeometry.FrameBottomY + HatchLadder.ContactSlack;
    }

    /// <summary>How far, metres, the feet of a route over the front panel pass above its inner top edge. The query
    /// capsule already floats a few centimetres over the feet (<c>LadderClearance</c>), so the panel is cleared by more
    /// than this; the sweep decides.</summary>
    internal const float PanelClearance = 0.05f;

    /// <summary>The feet height, piece-local, of the panel lift: the front panel's inner top edge, its highest point, plus
    /// <see cref="PanelClearance"/>.</summary>
    internal static readonly float OverPanelY = CellarGeometry.TopAt(CellarGeometry.ApertureFrontZ) + PanelClearance;

    /// <summary>Is this piece-local point where a route that leaves or enters through the opening starts or ends: inside
    /// the opening in plan, and from one ladder section below the snap plane up to <see cref="OverPanelY"/>? The anchored
    /// ladder's climber near its top, and the lane a descending entry lowers onto, are; a point on the floor around the
    /// piece, on the leaves, or further down a longer shaft is not.</summary>
    internal static bool InOpening(float x, float y, float z) =>
        Finite(x) && Finite(y) && Finite(z) && InOpeningPlan(x, z) &&
        y > CellarGeometry.SnapPlaneY - LadderEdgeSnap.SectionHeight && y < OverPanelY;

    /// <summary>Does a route between these two piece-local points pass over the frame: one end in the opening
    /// (<see cref="InOpening"/>), the other outside the opening in plan and above the frame's bottom (the floor around
    /// the piece, or anything else at that level)? A route to a landing inside the opening, or down in the dig below the
    /// frame, does not, and keeps the standard lifts.</summary>
    internal static bool LeavesOverFrame(float inX, float inY, float inZ, float outX, float outY, float outZ) =>
        InOpening(inX, inY, inZ) && Finite(outX) && Finite(outY) && Finite(outZ) &&
        !InOpeningPlan(outX, outZ) && outY > CellarGeometry.FrameBottomY;

    /// <summary>The panel lift for a route over a cover's frame whose higher end is at <paramref name="baseY"/>, where
    /// that cover's <see cref="OverPanelY"/> is at <paramref name="overPanelY"/> (both in one frame, world or
    /// piece-local): what raises the feet to it. False when no lift is needed (the higher end is already at or over that
    /// height) or when the input is not finite (no cover's frame is crossed).</summary>
    internal static bool TryPanelLift(float baseY, float overPanelY, out float lift)
    {
        lift = overPanelY - baseY;
        if (!Finite(lift) || lift <= 0f)
        {
            lift = 0f;
            return false;
        }

        return true;
    }

    /// <summary>The lifts, in order, that a route over a cover's frame tries when it needs <paramref name="panelLift"/>
    /// (from <see cref="TryPanelLift"/>): the standard route and step lifts, each raised to at least the panel lift,
    /// duplicates dropped, so no lift leaves the feet below <see cref="OverPanelY"/>. Empty when the panel lift is not
    /// finite. Allocates one small array, as the standard path's own lift list does.</summary>
    internal static float[] Lifts(float panelLift)
    {
        if (!Finite(panelLift)) { return Array.Empty<float>(); }
        return panelLift < LadderRules.StepLift
            ? new[] { Math.Max(LadderRules.RouteLift, panelLift), LadderRules.StepLift }
            : new[] { panelLift };
    }

    private static bool InOpeningPlan(float x, float z) =>
        Math.Abs(x) < CellarGeometry.HalfAperture && z > CellarGeometry.ApertureFrontZ && z < CellarGeometry.ApertureRearZ;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
