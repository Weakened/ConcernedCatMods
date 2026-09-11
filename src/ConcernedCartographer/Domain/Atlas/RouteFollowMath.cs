using System;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

internal enum RouteFollowDirection
{
    Forward = 1,
    Reverse = -1,
}

/// <summary>A game-free snapshot of the closest route position and a
/// bounded target ahead of it. Runtime adapters own input and steering.</summary>
internal readonly struct RouteFollowSample
{
    public RouteFollowSample(
        int segmentIndex,
        float segmentFraction,
        RoadPoint projectedPoint,
        RoadPoint lookAheadPoint,
        float crossTrackMeters,
        float remainingMeters,
        bool atRouteEnd)
    {
        SegmentIndex = segmentIndex;
        SegmentFraction = segmentFraction;
        ProjectedPoint = projectedPoint;
        LookAheadPoint = lookAheadPoint;
        CrossTrackMeters = crossTrackMeters;
        RemainingMeters = remainingMeters;
        AtRouteEnd = atRouteEnd;
    }

    public int SegmentIndex { get; }
    public float SegmentFraction { get; }
    public RoadPoint ProjectedPoint { get; }
    public RoadPoint LookAheadPoint { get; }
    public float CrossTrackMeters { get; }
    public float RemainingMeters { get; }
    public bool AtRouteEnd { get; }
}

/// <summary>Allocation-free route projection and look-ahead math shared by
/// future walking and sailing adapters. It never reads or changes game state.</summary>
internal static class RouteFollowMath
{
    private const float SegmentEpsilon = 0.0001f;
    private const float TieEpsilon = 0.000001f;

    public static bool TrySample(
        RouteFollowPath? route,
        in RoadPoint position,
        RouteFollowDirection direction,
        int cursorSegment,
        int maxSegmentsToSearch,
        float lookAheadMeters,
        float maxCrossTrackMeters,
        float routeEndToleranceMeters, out RouteFollowSample sample)
    {
        sample = default;
        if (route is null ||
            !IsFinite(position) ||
            !IsDirectionValid(direction) ||
            maxSegmentsToSearch <= 0 ||
            !IsFinitePositive(lookAheadMeters) ||
            !IsFiniteNonNegative(maxCrossTrackMeters) ||
            !IsFiniteNonNegative(routeEndToleranceMeters))
        {
            return false;
        }

        int lastSegment = route.LastSegmentIndex;
        int start = Clamp(cursorSegment, 0, lastSegment);
        int finish = direction == RouteFollowDirection.Forward
            ? (int)Math.Min(lastSegment, (long)start + maxSegmentsToSearch - 1L)
            : (int)Math.Max(0L, (long)start - maxSegmentsToSearch + 1L);

        bool found = false;
        int bestSegment = -1;
        float bestFraction = 0f;
        float bestDistanceSquared = float.MaxValue;
        RoadPoint bestProjection = default;

        for (int index = start;
             direction == RouteFollowDirection.Forward ? index <= finish : index >= finish;
             index += (int)direction)
        {
            RoadPoint from = route[index];
            RoadPoint to = route[index + 1];
            if (!IsFinite(from) || !IsFinite(to))
            {
                return false;
            }

            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float lengthSquared = (dx * dx) + (dz * dz);
            if (!(lengthSquared > SegmentEpsilon * SegmentEpsilon))
            {
                continue;
            }

            float px = position.X - from.X;
            float pz = position.Z - from.Z;
            float fraction = Clamp01(((px * dx) + (pz * dz)) / lengthSquared);
            RoadPoint projection = Interpolate(from, to, fraction);
            float distanceX = position.X - projection.X;
            float distanceZ = position.Z - projection.Z;
            float distanceSquared = (distanceX * distanceX) + (distanceZ * distanceZ);

            bool betterDistance = distanceSquared < bestDistanceSquared - TieEpsilon;
            bool progressTie = Math.Abs(distanceSquared - bestDistanceSquared) <= TieEpsilon &&
                IsFurtherAlong(index, bestSegment, direction);
            if (!found || betterDistance || progressTie)
            {
                found = true;
                bestSegment = index;
                bestFraction = fraction;
                bestDistanceSquared = distanceSquared;
                bestProjection = projection;
            }
        }

        if (!found)
        {
            return false;
        }

        float crossTrackMeters = (float)Math.Sqrt(bestDistanceSquared);
        if (crossTrackMeters > maxCrossTrackMeters)
        {
            return false;
        }

        if (!TryMeasureRemaining(
                route,
                bestSegment,
                bestFraction,
                direction,
                out float remainingMeters))
        {
            return false;
        }

        if (!TryFindLookAhead(
                route,
                bestSegment,
                bestFraction,
                direction,
                lookAheadMeters,
                out RoadPoint lookAheadPoint))
        {
            return false;
        }

        sample = new RouteFollowSample(
            bestSegment,
            bestFraction,
            bestProjection,
            lookAheadPoint,
            crossTrackMeters,
            remainingMeters,
            remainingMeters <= routeEndToleranceMeters);
        return true;
    }

    private static bool TryMeasureRemaining(
        RouteFollowPath route,
        int segmentIndex,
        float segmentFraction,
        RouteFollowDirection direction,
        out float remaining)
    {
        remaining = 0f;
        double segmentStart = route.DistanceAtPoint(segmentIndex);
        double segmentEnd = route.DistanceAtPoint(segmentIndex + 1);
        double projectedMeters =
            segmentStart + ((segmentEnd - segmentStart) * segmentFraction);
        double remainingMeters = direction == RouteFollowDirection.Forward
            ? route.TotalMeters - projectedMeters
            : projectedMeters;

        if (double.IsNaN(remainingMeters) ||
            double.IsInfinity(remainingMeters) ||
            remainingMeters < 0d ||
            remainingMeters > float.MaxValue)
        {
            return false;
        }

        remaining = (float)remainingMeters;
        return true;
    }

    private static bool TryFindLookAhead(
        RouteFollowPath route,
        int segmentIndex,
        float segmentFraction,
        RouteFollowDirection direction,
        float lookAheadMeters,
        out RoadPoint target)
    {
        double segmentStart = route.DistanceAtPoint(segmentIndex);
        double segmentEnd = route.DistanceAtPoint(segmentIndex + 1);
        double projectedMeters =
            segmentStart + ((segmentEnd - segmentStart) * segmentFraction);
        double targetMeters = direction == RouteFollowDirection.Forward
            ? Math.Min(route.TotalMeters, projectedMeters + lookAheadMeters)
            : Math.Max(0d, projectedMeters - lookAheadMeters);

        return route.TryLocateDistance(
            targetMeters,
            out _,
            out _,
            out target);
    }

    private static bool IsFurtherAlong(
        int candidate,
        int current,
        RouteFollowDirection direction)
    {
        return current < 0 ||
            (direction == RouteFollowDirection.Forward
                ? candidate > current
                : candidate < current);
    }

    private static bool IsDirectionValid(RouteFollowDirection direction)
    {
        return direction == RouteFollowDirection.Forward ||
            direction == RouteFollowDirection.Reverse;
    }

    private static bool IsFinite(in RoadPoint point)
    {
        return IsFiniteNumber(point.X) &&
            IsFiniteNumber(point.Y) &&
            IsFiniteNumber(point.Z);
    }

    private static bool IsFiniteNumber(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f && IsFiniteNumber(value);
    }

    private static bool IsFiniteNonNegative(float value)
    {
        return value >= 0f && IsFiniteNumber(value);
    }

    private static RoadPoint Interpolate(in RoadPoint from, in RoadPoint to, float fraction)
    {
        return new RoadPoint(
            from.X + ((to.X - from.X) * fraction),
            from.Y + ((to.Y - from.Y) * fraction),
            from.Z + ((to.Z - from.Z) * fraction));
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }
}
