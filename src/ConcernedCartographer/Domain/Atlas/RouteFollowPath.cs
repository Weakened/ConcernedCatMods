using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>
/// Immutable route geometry with cumulative horizontal distances computed once.
/// Construction is O(n); hot-path samples can then measure the full route in O(1)
/// and locate a look-ahead point in O(log n) without scanning the route tail.
/// </summary>
internal sealed class RouteFollowPath
{
    private const double SegmentEpsilon = 0.0001d;

    private readonly RoadPoint[] _points;
    private readonly double[] _distanceAtPoint;

    private RouteFollowPath(RoadPoint[] points, double[] distanceAtPoint)
    {
        _points = points;
        _distanceAtPoint = distanceAtPoint;
    }

    public int PointCount => _points.Length;
    public int LastSegmentIndex => _points.Length - 2;
    public double TotalMeters => _distanceAtPoint[_distanceAtPoint.Length - 1];
    public RoadPoint this[int index] => _points[index];

    public double DistanceAtPoint(int index)
    {
        return _distanceAtPoint[index];
    }

    public static bool TryCreate(
        IReadOnlyList<RoadPoint>? points,
        out RouteFollowPath? path)
    {
        path = null;
        if (points is null || points.Count < 2)
        {
            return false;
        }

        var copiedPoints = new RoadPoint[points.Count];
        var distanceAtPoint = new double[points.Count];
        bool hasTraversableSegment = false;
        double totalMeters = 0d;

        for (int index = 0; index < points.Count; index++)
        {
            RoadPoint point = points[index];
            if (!IsFinite(point))
            {
                return false;
            }

            copiedPoints[index] = point;
            if (index == 0)
            {
                continue;
            }

            RoadPoint previous = copiedPoints[index - 1];
            double dx = (double)point.X - previous.X;
            double dz = (double)point.Z - previous.Z;
            double segmentMeters = Math.Sqrt((dx * dx) + (dz * dz));
            if (double.IsNaN(segmentMeters) || double.IsInfinity(segmentMeters))
            {
                return false;
            }

            hasTraversableSegment |= segmentMeters > SegmentEpsilon;
            totalMeters += segmentMeters;
            if (totalMeters > float.MaxValue)
            {
                return false;
            }

            distanceAtPoint[index] = totalMeters;
        }

        if (!hasTraversableSegment)
        {
            return false;
        }

        path = new RouteFollowPath(copiedPoints, distanceAtPoint);
        return true;
    }

    public bool TryLocateDistance(
        double distanceMeters,
        out int segmentIndex,
        out float segmentFraction,
        out RoadPoint point)
    {
        segmentIndex = 0;
        segmentFraction = 0f;
        point = default;
        if (double.IsNaN(distanceMeters) || double.IsInfinity(distanceMeters) ||
            distanceMeters < 0d || distanceMeters > TotalMeters)
        {
            return false;
        }

        if (distanceMeters <= 0d)
        {
            point = _points[0];
            return true;
        }

        if (distanceMeters >= TotalMeters)
        {
            segmentIndex = LastSegmentIndex;
            segmentFraction = 1f;
            point = _points[_points.Length - 1];
            return true;
        }

        int endPointIndex = LowerBound(_distanceAtPoint, distanceMeters);
        segmentIndex = endPointIndex - 1;
        double segmentStart = _distanceAtPoint[segmentIndex];
        double segmentLength = _distanceAtPoint[endPointIndex] - segmentStart;
        if (!(segmentLength > SegmentEpsilon))
        {
            return false;
        }

        segmentFraction = (float)((distanceMeters - segmentStart) / segmentLength);
        point = Interpolate(_points[segmentIndex], _points[endPointIndex], segmentFraction);
        return IsFinite(point);
    }

    private static int LowerBound(double[] values, double target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (values[middle] < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static bool IsFinite(in RoadPoint point)
    {
        return IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
    private static RoadPoint Interpolate(
        in RoadPoint from,
        in RoadPoint to,
        float fraction)
    {
        return new RoadPoint(
            from.X + ((to.X - from.X) * fraction),
            from.Y + ((to.Y - from.Y) * fraction),
            from.Z + ((to.Z - from.Z) * fraction));
    }
}
