using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Ladder-to-ladder stacking (owner correction 2026-10-26). The game snaps a placement ghost by moving it
/// so one of its snap points coincides with a nearby piece's snap point, keeping whatever rotation the player last
/// used. For a ladder stacked on a ladder that leaves a twisted section that is not one climbable column. When the
/// game has already brought the ghost's bottom-centre onto another ladder's top-centre (or the ghost's top-centre
/// onto another ladder's bottom-centre), the ghost takes that ladder's rotation and is placed so the two anchors
/// coincide exactly. Every other snap (walls, floors, explicit wall corners) is left to the game.</summary>
internal static class LadderSnap
{
    /// <summary>The game's own snap leaves the anchors exactly coincident; this only absorbs float noise.</summary>
    internal const float Coincident = 0.01f;

    /// <summary>Section height between the bottom-centre and top-centre anchors.</summary>
    internal const float SectionHeight = 2f;

    /// <summary>The aligned ghost origin for a stack, or false when no centre-to-centre stack was snapped.
    /// <paramref name="ghostBottom"/>/<paramref name="ghostTop"/> are the ghost's current world anchors;
    /// <paramref name="partnerBottom"/>/<paramref name="partnerTop"/> the other ladder's. The ghost keeps the
    /// partner's rotation, so its origin lies straight above (or below) the partner's.</summary>
    internal static bool TryAlignStack(ClimbPoint ghostBottom, ClimbPoint ghostTop, ClimbPoint partnerBottom, ClimbPoint partnerTop,
        out ClimbPoint alignedOrigin, out bool above)
    {
        alignedOrigin = default;
        above = false;
        if (Distance(ghostBottom, partnerTop) <= Coincident)
        {
            alignedOrigin = partnerTop; // The ghost's bottom-centre is its origin.
            above = true;
            return true;
        }

        if (Distance(ghostTop, partnerBottom) <= Coincident)
        {
            alignedOrigin = new ClimbPoint(partnerBottom.X, partnerBottom.Y - SectionHeight, partnerBottom.Z);
            return true;
        }

        return false;
    }

    /// <summary>Which end of a column a ghost that overlaps it should extend.</summary>
    internal enum Extend { Nearest, Above, Below }

    /// <summary>The ghost origin that extends a placed column when the placement ray hits that column's rails
    /// (observed in the owner's session: the game finds no snap point within its 0.5 m radius and leaves a valid ghost
    /// overlapping or beside the ladder). <paramref name="extend"/> comes from the selected snap mode: Above stacks on
    /// the column's top, Below hangs from its bottom, Nearest takes the end nearer <paramref name="aimHeight"/>.
    /// <paramref name="runBottom"/>/<paramref name="runTop"/> are the connected column's centreline ends. A column
    /// that stands on something (<paramref name="standsOnSomething"/>: ground or a floor right under its foot) is never
    /// extended below by Auto, which would bury the new section; an explicit Top mode is the player's choice.</summary>
    internal static bool TryExtendColumn(float aimHeight, ClimbPoint runBottom, ClimbPoint runTop, Extend extend, bool standsOnSomething,
        out ClimbPoint alignedOrigin, out bool above)
    {
        alignedOrigin = default;
        above = false;
        if (float.IsNaN(aimHeight) || float.IsInfinity(aimHeight) || runTop.Y - runBottom.Y < SectionHeight - Coincident) { return false; }
        above = extend == Extend.Above || (extend == Extend.Nearest && (standsOnSomething || aimHeight >= (runBottom.Y + runTop.Y) * 0.5f));
        alignedOrigin = above ? runTop : new ClimbPoint(runBottom.X, runBottom.Y - SectionHeight, runBottom.Z);
        return true;
    }

    /// <summary>The game's placement reach (Player.PieceRayTest: eye to aimed point under the build tool's place
    /// distance plus the piece's extra distance), applied to the joint a snapped ladder attaches at, so aiming at
    /// something reachable never builds at an unreachable far end.</summary>
    internal static bool WithinPlacementReach(ClimbPoint eye, ClimbPoint joint, float reach)
    {
        if (!eye.IsFinite || !joint.IsFinite || float.IsNaN(reach) || float.IsInfinity(reach) || reach <= 0f) { return false; }
        float dx = joint.X - eye.X, dy = joint.Y - eye.Y, dz = joint.Z - eye.Z;
        return dx * dx + dy * dy + dz * dz < reach * reach;
    }

    private static float Distance(ClimbPoint a, ClimbPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
