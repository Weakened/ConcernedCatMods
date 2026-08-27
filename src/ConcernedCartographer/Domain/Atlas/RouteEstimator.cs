using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Distance, road-composition, and travel-time estimates for a
/// polyline. On-road fraction is sampled every few meters against the road
/// atlas; travel time weights on-road and off-road distance with the
/// configured speeds.</summary>
internal static class RouteEstimator
{
    private const float SampleStepMeters = 5f;

    public readonly struct Estimate
    {
        public Estimate(float distanceMeters, float onRoadFraction, float estimatedMinutes)
        {
            DistanceMeters = distanceMeters;
            OnRoadFraction = onRoadFraction;
            EstimatedMinutes = estimatedMinutes;
        }

        public float DistanceMeters { get; }
        public float OnRoadFraction { get; }
        public float EstimatedMinutes { get; }
    }

    public static Estimate Compute(
        IReadOnlyList<RoadPoint> points,
        RoadAtlas roads,
        float onRoadToleranceMeters,
        float offRoadSpeedMetersPerSecond,
        float onRoadSpeedMetersPerSecond)
    {
        float distance = 0f;
        float onRoadDistance = 0f;

        for (int index = 1; index < points.Count; index++)
        {
            RoadPoint start = points[index - 1];
            RoadPoint end = points[index];
            float segment = start.HorizontalDistanceTo(end);
            if (segment <= 0f)
            {
                continue;
            }

            distance += segment;

            int samples = Math.Max(1, (int)(segment / SampleStepMeters));
            int onRoadSamples = 0;
            for (int sample = 0; sample < samples; sample++)
            {
                float t = (sample + 0.5f) / samples;
                var probe = new RoadPoint(
                    start.X + ((end.X - start.X) * t),
                    start.Y,
                    start.Z + ((end.Z - start.Z) * t));
                if (roads.ContainsPointNear(RoadKind.Dirt, probe, onRoadToleranceMeters) ||
                    roads.ContainsPointNear(RoadKind.Paved, probe, onRoadToleranceMeters))
                {
                    onRoadSamples++;
                }
            }

            onRoadDistance += segment * onRoadSamples / samples;
        }

        float fraction = distance <= 0f ? 0f : onRoadDistance / distance;
        float offRoadDistance = distance - onRoadDistance;
        float seconds = 0f;
        if (offRoadSpeedMetersPerSecond > 0f)
        {
            seconds += offRoadDistance / offRoadSpeedMetersPerSecond;
        }

        if (onRoadSpeedMetersPerSecond > 0f)
        {
            seconds += onRoadDistance / onRoadSpeedMetersPerSecond;
        }

        return new Estimate(distance, fraction, seconds / 60f);
    }
}
