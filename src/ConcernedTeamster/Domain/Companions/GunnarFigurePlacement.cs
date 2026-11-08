using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Where Gunnar's figure stands at home (#458): a bounded, deterministic
/// ring sweep beside the respawn anchor through the shared planner, on bare
/// ground only (never a seat or a bed: those are #461's camp poses), and clear
/// of every other product's keepsake by the same distance the buckle keeps.
/// Game-free; the probe is the boundary's bounded read-only physics.
///
/// Every unknown defers: an invalid anchor, an unknown keepsake snapshot, a
/// non-finite sample. A deferral shows no figure rather than a guessed spot.</summary>
internal static class GunnarFigurePlacement
{
    /// <summary>Closer than the buckle's band would put him on top of the bed;
    /// further than four metres is not "at home".</summary>
    public static readonly PlacementRules Rules = new(1.5f, 4f, 0f, 1f);
    private static readonly PlacementPlanner Planner = new(Rules);

    public static PlacementResult Plan(CompanionAnchor anchor, IPlacementProbe probe,
        IReadOnlyList<WorldPoint>? otherKeepsakes)
    {
        if (probe == null) throw new ArgumentNullException(nameof(probe));
        if (!anchor.IsValid || !Finite(anchor.Position) || otherKeepsakes == null)
            return PlacementResult.Deferred(0, PlacementRejection.NotLoaded);
        return Planner.Plan(anchor, new GroundOnly(probe), accept: sample =>
        {
            if (!Finite(sample.Position)) return false;
            for (int i = 0; i < otherKeepsakes.Count; i++)
                if (!Finite(otherKeepsakes[i]) ||
                    sample.Position.HorizontalDistanceTo(otherKeepsakes[i]) < GunnarKeepsake.Clearance) return false;
            return true;
        });
    }

    private static bool Finite(WorldPoint p) => !(float.IsNaN(p.X) || float.IsInfinity(p.X) ||
        float.IsNaN(p.Y) || float.IsInfinity(p.Y) || float.IsNaN(p.Z) || float.IsInfinity(p.Z));

    // Hides ISeatFinder even when the boundary's probe implements it.
    private sealed class GroundOnly : IPlacementProbe
    {
        private readonly IPlacementProbe _inner;
        public GroundOnly(IPlacementProbe inner) { _inner = inner; }
        public PlacementProbeSample Probe(WorldPoint point) => _inner.Probe(point);
    }
}
