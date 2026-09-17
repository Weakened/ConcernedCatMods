using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>One ground sample along a straight segment, read by the adapter.
/// A default instance is unloaded, which refuses.</summary>
internal struct StraightSegmentSample
{
    public bool Loaded { get; set; }

    /// <summary>The ground height could be measured here.</summary>
    public bool GroundKnown { get; set; }

    public float GroundHeight { get; set; }

    public bool InWater { get; set; }

    /// <summary>No static obstacle intrudes into the swept box (cart width plus
    /// side clearance, from step height to cart height).</summary>
    public bool Clear { get; set; }
}

/// <summary>The ground port of the placeholder planner.</summary>
internal interface IStraightSegmentProbe
{
    StraightSegmentSample Sample(WorkPoint at, float directionX, float directionZ, float halfWidthMetres, float sampleLengthMetres);

    /// <summary>The leased cart's heading (centre to handle), when it can be
    /// read.</summary>
    bool TryReadCartHeading(out float headingX, out float headingZ);
}

/// <summary><b>PLACEHOLDER</b> for agent B's cart-safe route planner (#314,
/// <c>Domain/Hauling/Navigation</c>). It exists only so Gunnar's mechanics can be
/// exercised end to end before B lands, and it is replaced wholesale at
/// integration.
///
/// It vouches for exactly one thing: a <b>clear straight segment</b> from the
/// cart to the target. Everything else refuses. The segment must start ahead of
/// the cart (a cart that would have to turn in place is refused), fit one leg
/// (<see cref="HaulLimits.MaxLegMetres"/>), lie in loaded ground out of water,
/// have measured ground at every sample, stay under
/// <see cref="HaulLimits.MaxGradeRatio"/> between samples, have no static
/// obstacle inside the cart's swept width plus side clearance, and end on ground
/// flat enough to leave the cart on. Samples are spaced
/// <see cref="HaulLimits.SampleSpacingMetres"/> apart, bounded by
/// <see cref="HaulLimits.ClearanceProbesPerPlan"/>, and plans are bounded by
/// <see cref="HaulLimits.PathQueriesPerMinute"/>. No curve, no door, no bridge,
/// no turning manoeuvre is ever planned.</summary>
internal sealed class StraightLinePlaceholderPlanner : ICartRoutePlanner
{
    /// <summary>How far the leg may start away from the cart's heading.</summary>
    public const float MaxStartTurnDegrees = 60f;

    /// <summary>How far the cart may drift sideways off the segment before the
    /// plan is no longer valid from where it stands.</summary>
    public const float LateralToleranceMetres = 1.5f;

    /// <summary>The steering goal's own radius; the leg's radius decides final
    /// arrival.</summary>
    public const float GoalRadiusMetres = 1.5f;

    private readonly IStraightSegmentProbe _probe;
    private readonly HaulLimits _limits;
    private readonly HaulExecutionLimits _execution;
    private readonly Queue<float> _planTimes = new Queue<float>();

    public StraightLinePlaceholderPlanner(IStraightSegmentProbe probe, HaulLimits limits, HaulExecutionLimits execution)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    public CartRoutePlan Plan(CartRouteRequest request, float now)
    {
        while (_planTimes.Count > 0 && now - _planTimes.Peek() >= 60f)
        {
            _planTimes.Dequeue();
        }

        if (_planTimes.Count >= _limits.PathQueriesPerMinute)
        {
            return CartRoutePlan.Refused(CartRouteVerdict.BudgetExhausted, request.Revision);
        }

        _planTimes.Enqueue(now);

        if (!request.From.IsFinite || !request.To.IsFinite)
        {
            return CartRoutePlan.Refused(CartRouteVerdict.NoPath, request.Revision);
        }

        float dx = request.To.X - request.From.X;
        float dz = request.To.Z - request.From.Z;
        float length = (float)Math.Sqrt((dx * dx) + (dz * dz));
        if (!(length <= _limits.MaxLegMetres))
        {
            return CartRoutePlan.Refused(CartRouteVerdict.NoPath, request.Revision);
        }

        if (length < 0.01f)
        {
            return CartRoutePlan.Refused(CartRouteVerdict.NoPath, request.Revision);
        }

        float directionX = dx / length;
        float directionZ = dz / length;
        if (!_probe.TryReadCartHeading(out float headingX, out float headingZ) ||
            AngleDegrees(directionX, directionZ, headingX, headingZ) > MaxStartTurnDegrees)
        {
            // Pulling from rest starts ahead of the handle; anything else is a
            // turn this placeholder cannot vouch for.
            return CartRoutePlan.Refused(CartRouteVerdict.NoPath, request.Revision);
        }

        int intervals = Math.Max(1, (int)Math.Ceiling(length / _limits.SampleSpacingMetres));
        int samples = intervals + 1;
        if (samples > _limits.ClearanceProbesPerPlan)
        {
            return CartRoutePlan.Refused(CartRouteVerdict.BudgetExhausted, request.Revision);
        }

        float spacing = length / intervals;
        float halfWidth = (request.Footprint.WidthMetres / 2f) + _limits.SideClearanceMetres;
        float steepest = 0f;
        float previousHeight = float.NaN;
        float lastGrade = 0f;
        WorkPoint start = request.From;
        WorkPoint end = request.To;
        for (int index = 0; index < samples; index++)
        {
            float t = (float)index / intervals;
            var at = new WorkPoint(request.From.X + (dx * t), request.From.Y, request.From.Z + (dz * t));
            StraightSegmentSample sample = _probe.Sample(at, directionX, directionZ, halfWidth, spacing);
            if (!sample.Loaded)
            {
                return CartRoutePlan.Refused(CartRouteVerdict.OutsideLoadedArea, request.Revision);
            }

            if (!sample.GroundKnown || !HaulExecutionLimits.IsFinite(sample.GroundHeight))
            {
                return CartRoutePlan.Refused(CartRouteVerdict.UnsupportedGap, request.Revision);
            }

            if (sample.InWater)
            {
                return CartRoutePlan.Refused(CartRouteVerdict.Water, request.Revision);
            }

            if (!sample.Clear)
            {
                return CartRoutePlan.Refused(CartRouteVerdict.TooNarrow, request.Revision);
            }

            if (!float.IsNaN(previousHeight))
            {
                lastGrade = Math.Abs(sample.GroundHeight - previousHeight) / spacing;
                steepest = Math.Max(steepest, lastGrade);
                if (lastGrade > _limits.MaxGradeRatio)
                {
                    return CartRoutePlan.Refused(CartRouteVerdict.TooSteep, request.Revision);
                }
            }

            if (index == 0)
            {
                start = new WorkPoint(at.X, sample.GroundHeight, at.Z);
            }

            if (index == samples - 1)
            {
                end = new WorkPoint(at.X, sample.GroundHeight, at.Z);
            }

            previousHeight = sample.GroundHeight;
        }

        if (lastGrade > _execution.MaxParkingGradeRatio)
        {
            return CartRoutePlan.Refused(CartRouteVerdict.UnsafeStop, request.Revision);
        }

        return new CartRoutePlan(
            CartRouteVerdict.Suitable,
            new[] { start, end },
            length,
            steepest,
            halfWidth * 2f,
            end,
            request.Revision);
    }

    public SteeringGoal? NextGoal(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        if (plan == null || !plan.IsSuitable || !cartPosition.IsFinite)
        {
            return null;
        }

        WorkPoint start = plan.Waypoints[0];
        WorkPoint end = plan.Waypoints[plan.Waypoints.Count - 1];
        if (DistanceFromLine(cartPosition, start, end) > LateralToleranceMetres)
        {
            return null;
        }

        return new SteeringGoal(end, GoalRadiusMetres, isFinalStop: true, plan.Waypoints.Count - 1, plan.RequestRevision);
    }

    private static float AngleDegrees(float ax, float az, float bx, float bz)
    {
        double a = Math.Sqrt((ax * ax) + (az * az));
        double b = Math.Sqrt((bx * bx) + (bz * bz));
        if (!(a > 1e-4) || !(b > 1e-4))
        {
            return 180f;
        }

        double cosine = Math.Max(-1d, Math.Min(1d, ((ax * bx) + (az * bz)) / (a * b)));
        return (float)(Math.Acos(cosine) * 180d / Math.PI);
    }

    private static float DistanceFromLine(WorkPoint point, WorkPoint start, WorkPoint end)
    {
        double dx = end.X - start.X;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared < 1e-6)
        {
            return point.HorizontalDistanceTo(start);
        }

        double t = (((point.X - start.X) * dx) + ((point.Z - start.Z) * dz)) / lengthSquared;
        t = Math.Max(0d, Math.Min(1d, t));
        double closestX = start.X + (dx * t);
        double closestZ = start.Z + (dz * t);
        double ox = point.X - closestX;
        double oz = point.Z - closestZ;
        return (float)Math.Sqrt((ox * ox) + (oz * oz));
    }
}
