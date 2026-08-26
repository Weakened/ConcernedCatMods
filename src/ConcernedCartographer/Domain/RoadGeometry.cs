using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure polyline geometry used by the atlas: horizontal
/// point-to-segment distance for segment-based suppression, and
/// Douglas-Peucker simplification for atlas maintenance.</summary>
internal static class RoadGeometry
{
    public static float HorizontalDistanceToSegment(RoadPoint point, RoadPoint start, RoadPoint end)
    {
        float segmentX = end.X - start.X;
        float segmentZ = end.Z - start.Z;
        float lengthSquared = (segmentX * segmentX) + (segmentZ * segmentZ);
        if (lengthSquared <= float.Epsilon)
        {
            return start.HorizontalDistanceTo(point);
        }

        float t = (((point.X - start.X) * segmentX) + ((point.Z - start.Z) * segmentZ)) / lengthSquared;
        t = Math.Max(0f, Math.Min(1f, t));
        float nearestX = start.X + (t * segmentX);
        float nearestZ = start.Z + (t * segmentZ);
        float dx = point.X - nearestX;
        float dz = point.Z - nearestZ;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>Douglas-Peucker on the horizontal plane: returns a subset of
    /// the input points (first and last always kept) whose polyline never
    /// deviates more than <paramref name="toleranceMeters"/> from the
    /// original. Elevation is ignored — the map is two-dimensional.</summary>
    public static List<RoadPoint> Simplify(IReadOnlyList<RoadPoint> points, float toleranceMeters)
    {
        var result = new List<RoadPoint>();
        if (points.Count <= 2 || toleranceMeters <= 0f)
        {
            for (int index = 0; index < points.Count; index++)
            {
                result.Add(points[index]);
            }

            return result;
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            (int start, int end) = ranges.Pop();
            if (end - start < 2)
            {
                continue;
            }

            float worstDistance = -1f;
            int worstIndex = -1;
            for (int index = start + 1; index < end; index++)
            {
                float distance = HorizontalDistanceToSegment(points[index], points[start], points[end]);
                if (distance > worstDistance)
                {
                    worstDistance = distance;
                    worstIndex = index;
                }
            }

            if (worstDistance > toleranceMeters)
            {
                keep[worstIndex] = true;
                ranges.Push((start, worstIndex));
                ranges.Push((worstIndex, end));
            }
        }

        for (int index = 0; index < points.Count; index++)
        {
            if (keep[index])
            {
                result.Add(points[index]);
            }
        }

        return result;
    }
}
