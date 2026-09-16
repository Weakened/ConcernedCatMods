using System;
using System.Collections.Generic;

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
/// produce an answer.
///
/// Seats are the one thing the minimum radius does not apply to, and that is a
/// rule rather than a relaxation. The minimum keeps a companion from sitting on
/// the ground in the middle of somebody's bedroom; a stool or a chair is a place
/// somebody built to be sat on, wherever it is. When the probe also implements
/// <see cref="ISeatFinder"/>, up to <see cref="MaximumSeats"/> free seats inside
/// the band's OUTER edge and height limit are candidates in their own right,
/// ranked exactly like ground, and a seat no longer disappears because the
/// ground probed beside it was rejected.
///
/// Ranking, per <see cref="ValueOf"/>: warmth outranks a seat, a seat outranks
/// bare ground, and a seat by the fire outranks everything. Warmth first is the
/// owner's call - break the campfire and build one outside, and he should leave
/// his chair and head for it.</summary>
internal sealed class PlacementPlanner
{
    private const int RingCount = 4;
    private const int AnglesPerRing = 12;

    /// <summary>Upper bound on ground probes per plan.</summary>
    public const int MaximumCandidates = RingCount * AnglesPerRing;

    /// <summary>Upper bound on seats considered per plan, on top of the ground
    /// probes. A camp has a handful; a hall full of benches does not need every
    /// one of them weighed.</summary>
    public const int MaximumSeats = 8;

    /// <summary>Fixed per-ring bearing offset, in degrees. Coprime-ish with the
    /// 30 degree angular step so successive rings interleave.</summary>
    private const float RingBearingOffsetDegrees = 11f;

    private readonly PlacementRules _rules;

    public PlacementPlanner(PlacementRules? rules = null)
    {
        _rules = rules ?? PlacementRules.Default;
    }

    /// <param name="observe">Optional: told about every candidate - ground and
    /// seat - with the rejections that decided it, <c>None</c> for a candidate
    /// that qualified. For diagnostics only; it cannot change the
    /// result.</param>
    /// <param name="accept">Optional: a candidate that passes every rule is
    /// still skipped when this says no - without counting as a rejection, since
    /// nothing is wrong with the spot itself. Used to ask for the best spot a
    /// companion can actually walk to.
    ///
    /// Asked lazily, best candidate first, and not asked again once one says
    /// yes. The answer is exactly the one asking every candidate would give -
    /// the best-ranked candidate it accepts - but a question that costs a
    /// route through the navmesh is asked once in the ordinary case instead of
    /// fifty times.</param>
    public PlacementResult Plan(
        CompanionAnchor anchor,
        IPlacementProbe probe,
        Action<PlacementProbeSample, PlacementRejection, bool>? observe = null,
        Func<PlacementProbeSample, bool>? accept = null)
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

        // Every candidate that passes the rules, in sweep order. Ranked once the
        // sweep is done, and only then offered to `accept`, best first.
        var qualified = new List<Qualified>();

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
                observe?.Invoke(sample, rejections, false);
                if (rejections != PlacementRejection.None)
                {
                    blockedBy |= rejections;
                    continue;
                }

                CompanionPose pose = PoseFor(sample);
                qualified.Add(new Qualified(
                    sample,
                    pose,
                    pose == CompanionPose.SitOnSeat ? sample.SeatOffer : SeatOffer.None,
                    Score(sample),
                    Math.Abs(sample.Position.HorizontalDistanceTo(anchor.Position) - idealRadius),
                    qualified.Count));
            }
        }

        if (probe is ISeatFinder seats)
        {
            IReadOnlyList<PlacementProbeSample> found =
                seats.FindSeats(anchor.Position, _rules.MaximumRadius) ?? Array.Empty<PlacementProbeSample>();

            int considered = 0;
            foreach (PlacementProbeSample seat in found)
            {
                if (considered >= MaximumSeats)
                {
                    break;
                }

                if (!seat.SeatOffer.IsUsable)
                {
                    continue;
                }

                considered++;
                PlacementRejection rejections = seat.Rejections | SeatRangeRejections(anchor, seat);
                observe?.Invoke(seat, rejections, true);
                if (rejections != PlacementRejection.None)
                {
                    blockedBy |= rejections;
                    continue;
                }

                qualified.Add(new Qualified(
                    seat,
                    CompanionPose.SitOnSeat,
                    seat.SeatOffer,
                    Score(seat),
                    Math.Abs(seat.Position.HorizontalDistanceTo(anchor.Position) - idealRadius),
                    qualified.Count));
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

        // Best score first; then nearest the ideal radius; then the fixed sweep
        // order, ground before seats - the same tie-breaks the single pass used,
        // which is what keeps the same world putting him in the same place.
        qualified.Sort((left, right) =>
        {
            int byScore = right.Score.CompareTo(left.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            int byRadius = left.RadiusError.CompareTo(right.RadiusError);
            return byRadius != 0 ? byRadius : left.Order.CompareTo(right.Order);
        });

        foreach (Qualified candidate in qualified)
        {
            if (accept != null && !accept(candidate.Sample))
            {
                continue;
            }

            return PlacementResult.Placed(
                candidate.Sample.Position, candidate.Pose, probed, candidate.Seat, candidate.Score);
        }

        return PlacementResult.Deferred(probed, blockedBy);
    }

    /// <summary>A candidate that passed every rule, with what ranks it.
    /// </summary>
    private readonly struct Qualified
    {
        public Qualified(
            PlacementProbeSample sample, CompanionPose pose, SeatOffer seat, int score, float radiusError,
            int order)
        {
            Sample = sample;
            Pose = pose;
            Seat = seat;
            Score = score;
            RadiusError = radiusError;
            Order = order;
        }

        public PlacementProbeSample Sample { get; }

        public CompanionPose Pose { get; }

        public SeatOffer Seat { get; }

        public int Score { get; }

        public float RadiusError { get; }

        public int Order { get; }
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

    /// <summary>The range rules for a seat found directly: the band's outer
    /// edge and the height limit, but not its inner edge - see the class
    /// summary for why that is a rule and not a relaxation.</summary>
    private PlacementRejection SeatRangeRejections(CompanionAnchor anchor, PlacementProbeSample seat)
    {
        const float Tolerance = 0.01f;

        return seat.Position.HorizontalDistanceTo(anchor.Position) > _rules.MaximumRadius + Tolerance ||
            seat.Position.VerticalDistanceTo(anchor.Position) > _rules.MaximumHeightDelta + Tolerance
            ? PlacementRejection.OutOfRange
            : PlacementRejection.None;
    }

    /// <summary>How good a spot is: 2 for warmth, 1 for a free seat, so a cold
    /// seat is 1, a warm patch of ground 2 and a seat by the fire 3. Public so
    /// the spot a companion already occupies can be measured on the same scale
    /// as the best one on offer.</summary>
    public int ValueOf(PlacementProbeSample sample)
    {
        return Score(sample);
    }

    private int Score(PlacementProbeSample sample)
    {
        int score = 0;
        if (IsWarm(sample))
        {
            score += 2;
        }

        if (sample.SeatOffer.IsUsable)
        {
            score += 1;
        }

        return score;
    }

    private CompanionPose PoseFor(PlacementProbeSample sample)
    {
        if (sample.SeatOffer.IsUsable)
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
