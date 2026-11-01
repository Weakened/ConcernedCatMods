using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedNPC.Keepsakes;

namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

internal enum GunnarPickupResult { Unavailable, AlreadyCollected, NotRecorded, Collected }

/// <summary>The boundary supplies occupied memento centres from its current,
/// complete local snapshot. Null means unknown, not an empty world.</summary>
internal static class GunnarKeepsake
{
    public const string Inscription = "A fair load. A clear road. Everyone home. - G.";
    public const float Clearance = 1.25f;
    public static readonly NpcKeepsakeSpec Spec = new("Worn harness buckle", "Examine",
        new[] { "BeltStrength" }, 0.25f);
    private static readonly PlacementPlanner Planner = new(new PlacementRules(2f, 4f, 0f, 1.5f));

    public static bool Eligible(GunnarCompanionRecord? record, bool enabled, bool? hasWorkerBody) =>
        record != null && enabled && hasWorkerBody == false && record.ShouldPresentMemento(true);

    public static PlacementResult Plan(CompanionAnchor anchor, IPlacementProbe probe,
        IReadOnlyList<WorldPoint>? occupiedMementos)
    {
        if (!anchor.IsValid || !Finite(anchor.Position) || occupiedMementos == null)
            return PlacementResult.Deferred(0, PlacementRejection.NotLoaded);
        // Never rank seats for a small object on the ground. The wrapper exposes
        // only ground probes even if the supplied probe is also an ISeatFinder.
        return Planner.Plan(anchor, new GroundProbe(probe), accept: sample =>
        {
            if (!Finite(sample.Position)) return false;
            for (int i = 0; i < occupiedMementos.Count; i++)
                if (!Finite(occupiedMementos[i]) ||
                    sample.Position.HorizontalDistanceTo(occupiedMementos[i]) < Clearance) return false;
            return true;
        });
    }

    public static GunnarPickupResult Examine(GunnarCompanionRecord? record, bool enabled, bool? worker)
    {
        if (record != null && !record.ShouldPresentMemento(true) && (record.CanReplayStory || record.State == TheConcernedCat.Companions.Quest.QuestState.Collected ||
                record.State == TheConcernedCat.Companions.Quest.QuestState.IntroPending))
            return GunnarPickupResult.AlreadyCollected;
        if (!Eligible(record, enabled, worker)) return GunnarPickupResult.Unavailable;
        // Two durable stages, both before the presentation is removed or the
        // inscription/introduction is announced. A retry after Discover is safe.
        if (!record!.Discover() || !record.Collect()) return GunnarPickupResult.NotRecorded;
        return GunnarPickupResult.Collected;
    }

    private static bool Finite(WorldPoint p) => !(float.IsNaN(p.X) || float.IsInfinity(p.X) ||
        float.IsNaN(p.Y) || float.IsInfinity(p.Y) || float.IsNaN(p.Z) || float.IsInfinity(p.Z));
    private sealed class GroundProbe : IPlacementProbe
    {
        private readonly IPlacementProbe _inner;
        public GroundProbe(IPlacementProbe inner) { _inner = inner ?? throw new ArgumentNullException(nameof(inner)); }
        public PlacementProbeSample Probe(WorldPoint point) => _inner.Probe(point);
    }
}
