using System;
using System.Collections.Generic;
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
        IReadOnlyList<RoadPoint>? points,
        in RoadPoint position,
        RouteFollowDirection direction,
        int cursorSegment,
        int maxSegmentsToSearch,
        float lookAheadMeters,
        float maxCrossTrackMeters,
        float routeEndToleranceMeters, out RouteFollowSample sample)
    {
        sample = default;
        if (points is null || points.Count < 2 ||
            !IsFinite(position) ||
            !IsDirectionValid(direction) ||
            maxSegmentsToSearch <= 0 ||
            !IsFinitePositive(lookAheadMeters) ||
            !IsFiniteNonNegative(maxCrossTrackMeters) ||
            !IsFiniteNonNegative(routeEndToleranceMeters))
        {
            return false;
        }

        int lastSegment = points.Count - 2;
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
            RoadPoint from = points[index];
            RoadPoint to = points[index + 1];
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
                points,
                bestSegment,
                bestFraction,
                direction,
                out float remainingMeters))
        {
            return false;
        }

        if (!TryFindLookAhead(
                points,
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
        IReadOnlyList<RoadPoint> points,
        int segmentIndex,
        float segmentFraction,
        RouteFollowDirection direction,
        out float remaining)
    {
        remaining = 0f;
        int step = (int)direction;
        int index = segmentIndex;
        float fraction = segmentFraction;

        while (index >= 0 && index < points.Count - 1)
        {
            if (!TrySegment(points[index], points[index + 1], out float length))
            {
                return false;
            }

            if (length > SegmentEpsilon)
            {
                remaining += direction == RouteFollowDirection.Forward
                    ? length * (1f - fraction)
                    : length * fraction;
            }

            index += step;
            fraction = direction == RouteFollowDirection.Forward ? 0f : 1f;
        }

        return IsFiniteNonNegative(remaining);
    }

    private static bool TryFindLookAhead(
        IReadOnlyList<RoadPoint> points,
        int segmentIndex,
        float segmentFraction,
        RouteFollowDirection direction,
        float lookAheadMeters,
        out RoadPoint target)
    {
        target = default;
        float distanceLeft = lookAheadMeters;
        int step = (int)direction;
        int index = segmentIndex;
        float fraction = segmentFraction;

        while (index >= 0 && index < points.Count - 1)
        {
            RoadPoint from = points[index];
            RoadPoint to = points[index + 1];
            if (!TrySegment(from, to, out float length))
            {
                return false;
            }

            if (length > SegmentEpsilon)
            {
                float available = direction == RouteFollowDirection.Forward
                    ? length * (1f - fraction)
                    : length * fraction;
                if (distanceLeft <= available)
                {
                    float delta = distanceLeft / length;
                    float targetFraction = direction == RouteFollowDirection.Forward
                        ? fraction + delta
                        : fraction - delta;
                    target = Interpolate(from, to, Clamp01(targetFraction));
                    return true;
                }

                distanceLeft -= available;
            }

            index += step;
            fraction = direction == RouteFollowDirection.Forward ? 0f : 1f;
        }

        target = direction == RouteFollowDirection.Forward
            ? points[points.Count - 1]
            : points[0];
        return IsFinite(target);
    }

    private static bool TrySegment(in RoadPoint from, in RoadPoint to, out float length)
    {
        length = 0f;
        if (!IsFinite(from) || !IsFinite(to))
        {
            return false;
        }

        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        length = (float)Math.Sqrt((dx * dx) + (dz * dz));
        return IsFiniteNonNegative(length);
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
