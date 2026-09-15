using System;

namespace TheConcernedCat.Companions.Placement;

/// <summary>Chooses where a companion sits, given an anchor and a probe.
///
/// The search is a deterministic ring sweep around the anchor: fixed radii,
/// fixed angles, fixed order, with each ring rotated by a fixed offset so the
/// rings do not sample the same bearings. Determinism matters more than
/// coverage here - the same world state must put the companion in the same
/// place every session, or they appear to wander for no reason between logins.
///
/// The sweep is hard bounded at <see cref="MaximumCandidates"/> probes inside
/// the configured radius band, so this never becomes a search of the
/// surroundings, let alone the world. When nothing qualifies, the planner
/// defers and reports what blocked it. It never relaxes its own rules to
/// produce an answer.</summary>
internal sealed class PlacementPlanner
{
    private const int RingCount = 4;
    private const int AnglesPerRing = 12;

    /// <summary>Upper bound on probes per plan.</summary>
    public const int MaximumCandidates = RingCount * AnglesPerRing;

    /// <summary>Fixed per-ring bearing offset, in degrees. Coprime-ish with the
    /// 30 degree angular step so successive rings interleave.</summary>
    private const float RingBearingOffsetDegrees = 11f;

    private readonly PlacementRules _rules;

    public PlacementPlanner(PlacementRules? rules = null)
    {
        _rules = rules ?? PlacementRules.Default;
    }

    public PlacementResult Plan(CompanionAnchor anchor, IPlacementProbe probe)
    {
        if (probe == null)
        {
            throw new ArgumentNullException(nameof(probe));
        }

        if (!anchor.IsValid)
        {
            return PlacementResult.Deferred(0, PlacementRejection.NotLoaded);
        }

        float idealRadius = (_rules.MinimumRadius + _rules.MaximumRadius) * 0.5f;

        bool haveBest = false;
        WorldPoint bestPosition = default;
        CompanionPose bestPose = CompanionPose.SitOnGround;
        int bestScore = int.MinValue;
        float bestRadiusError = float.MaxValue;

        PlacementRejection blockedBy = PlacementRejection.None;
        bool sawUnloaded = false;
        int probed = 0;

        for (int ring = 0; ring < RingCount; ring++)
        {
            float radius = RadiusForRing(ring);
            float bearingOffset = ring * RingBearingOffsetDegrees;

            for (int step = 0; step < AnglesPerRing; step++)
            {
                float degrees = bearingOffset + (step * (360f / AnglesPerRing));
                double radians = degrees * Math.PI / 180.0;

                var candidate = new WorldPoint(
                    anchor.Position.X + (float)(Math.Cos(radians) * radius),
                    anchor.Position.Y,
                    anchor.Position.Z + (float)(Math.Sin(radians) * radius));

                PlacementProbeSample sample = probe.Probe(candidate);
                probed++;

                if ((sample.Rejections & PlacementRejection.NotLoaded) != 0)
                {
                    sawUnloaded = true;
                }

                PlacementRejection rejections = sample.Rejections | RangeRejections(anchor, sample);
                if (rejections != PlacementRejection.None)
                {
                    blockedBy |= rejections;
                    continue;
                }

                int score = Score(sample);
                float radiusError = Math.Abs(
                    sample.Position.HorizontalDistanceTo(anchor.Position) - idealRadius);

                // Strictly-better comparisons keep the first candidate in the
                // fixed sweep order when two spots tie, which is what makes the
                // choice reproducible.
                bool better = !haveBest
                    || score > bestScore
                    || (score == bestScore && radiusError < bestRadiusError);
                if (!better)
                {
                    continue;
                }

                haveBest = true;
                bestScore = score;
                bestRadiusError = radiusError;
                bestPosition = sample.Position;
                bestPose = PoseFor(sample);
            }
        }

        // An unloaded candidate is not a hazard, it is an unanswered question.
        // Committing to the best of a partial view makes the chosen spot depend
        // on how far streaming happened to have got, and the companion then
        // appears to move once the rest of the world arrives - the exact
        // wandering this planner is deterministic to avoid. Defer and re-plan.
        if (sawUnloaded)
        {
            return PlacementResult.Deferred(probed, blockedBy | PlacementRejection.NotLoaded);
        }

        return haveBest
            ? PlacementResult.Placed(bestPosition, bestPose, probed)
            : PlacementResult.Deferred(probed, blockedBy);
    }

    /// <summary>True when a companion already placed at <paramref name="current"/>
    /// should be moved because the anchor changed meaningfully. A bed reporting
    /// a fractionally different position between sessions is not a reason to
    /// relocate anyone.</summary>
    public bool ShouldReposition(CompanionAnchor previous, CompanionAnchor current)
    {
        return current.DiffersFrom(previous, _rules.AnchorMoveTolerance);
    }

    /// <summary>Evenly spaces the rings across the radius band, inclusive at
    /// both ends. A band collapsed to a single radius yields that radius for
    /// every ring, which is harmless: the angles still differ.</summary>
    private float RadiusForRing(int ring)
    {
        float span = _rules.MaximumRadius - _rules.MinimumRadius;
        return _rules.MinimumRadius + (span * ring / (RingCount - 1));
    }

    /// <summary>Range checks the planner owns rather than the probe: the probe
    /// reports what is at a position, the planner decides whether that position
    /// is close enough to home.</summary>
    private PlacementRejection RangeRejections(CompanionAnchor anchor, PlacementProbeSample sample)
    {
        // The tolerance is not slack in the rule, it is float arithmetic. Ring
        // candidates are generated exactly ON the band edges, and recovering
        // the distance through cos/sin and a subtraction at world coordinates
        // in the thousands loses enough precision to put a candidate a
        // fraction of a millimetre outside the band it was built inside. Without
        // this, a third of every sweep rejects candidates the planner itself
        // placed.
        const float Tolerance = 0.01f;

        float horizontal = sample.Position.HorizontalDistanceTo(anchor.Position);
        if (horizontal < _rules.MinimumRadius - Tolerance ||
            horizontal > _rules.MaximumRadius + Tolerance)
        {
            return PlacementRejection.OutOfRange;
        }

        return sample.Position.VerticalDistanceTo(anchor.Position) > _rules.MaximumHeightDelta + Tolerance
            ? PlacementRejection.OutOfRange
            : PlacementRejection.None;
    }

    private int Score(PlacementProbeSample sample)
    {
        int score = 0;
        if (sample.Seat == SeatAvailability.Free)
        {
            score += 2;
        }

        if (IsWarm(sample))
        {
            score += 1;
        }

        return score;
    }

    private CompanionPose PoseFor(PlacementProbeSample sample)
    {
        if (sample.Seat == SeatAvailability.Free)
        {
            return CompanionPose.SitOnSeat;
        }

        return IsWarm(sample) ? CompanionPose.SitByFire : CompanionPose.SitOnGround;
    }

    private bool IsWarm(PlacementProbeSample sample)
    {
        return sample.HasFireNearby && sample.DistanceToFire <= _rules.FireComfortRadius;
    }
}
