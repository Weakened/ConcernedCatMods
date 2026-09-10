using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RouteFollowMathTests
{
    private static RoadPoint P(float x, float z, float y = 30f)
    {
        return new RoadPoint(x, y, z);
    }

    [Fact]
    public void StraightForward_ProjectsAndLooksAhead()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };

        bool success = RouteFollowMath.TrySample(
            points, P(25f, 10f), RouteFollowDirection.Forward,
            0, 1, 20f, 20f, 0.5f, out RouteFollowSample sample);

        Assert.True(success);
        Assert.Equal(0, sample.SegmentIndex);
        Assert.Equal(0.25f, sample.SegmentFraction, 3);
        Assert.Equal(25f, sample.ProjectedPoint.X, 3);
        Assert.Equal(45f, sample.LookAheadPoint.X, 3);
        Assert.Equal(10f, sample.CrossTrackMeters, 3);
        Assert.Equal(75f, sample.RemainingMeters, 3);
        Assert.False(sample.AtRouteEnd);
    }

    [Fact]
    public void Reverse_UsesTheBeginningAsRouteEnd()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };

        bool success = RouteFollowMath.TrySample(
            points, P(75f, 0f), RouteFollowDirection.Reverse,
            0, 1, 20f, 5f, 0.5f, out RouteFollowSample sample);

        Assert.True(success);
        Assert.Equal(0.75f, sample.SegmentFraction, 3);
        Assert.Equal(55f, sample.LookAheadPoint.X, 3);
        Assert.Equal(75f, sample.RemainingMeters, 3);
    }

    [Fact]
    public void LookAhead_WalksAcrossPolylineBend()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(10f, 10f),
        };

        bool success = RouteFollowMath.TrySample(
            points, P(8f, 0f), RouteFollowDirection.Forward,
            0, 2, 10f, 5f, 0.1f, out RouteFollowSample sample);

        Assert.True(success);
        Assert.Equal(0, sample.SegmentIndex);
        Assert.Equal(10f, sample.LookAheadPoint.X, 3);
        Assert.Equal(8f, sample.LookAheadPoint.Z, 3);
        Assert.Equal(12f, sample.RemainingMeters, 3);
    }

    [Fact]
    public void VertexTie_PrefersProgressInTravelDirection()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(20f, 0f),
        };

        Assert.True(RouteFollowMath.TrySample(
            points, P(10f, 0f), RouteFollowDirection.Forward,
            0, 2, 1f, 2f, 0.1f, out RouteFollowSample forward));
        Assert.Equal(1, forward.SegmentIndex);

        Assert.True(RouteFollowMath.TrySample(
            points, P(10f, 0f), RouteFollowDirection.Reverse, 1, 2, 1f, 2f, 0.1f, out RouteFollowSample reverse));
        Assert.Equal(0, reverse.SegmentIndex);
    }

    [Fact]
    public void CursorWindow_CannotJumpBackwardOrPastItsBound()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(20f, 0f),
            P(30f, 0f),
        };

        Assert.False(RouteFollowMath.TrySample(
            points, P(25f, 0f), RouteFollowDirection.Forward,
            0, 1, 2f, 10f, 0.1f, out _));

        Assert.True(RouteFollowMath.TrySample(
            points, P(25f, 0f), RouteFollowDirection.Forward,
            0, 3, 2f, 10f, 0.1f, out RouteFollowSample acquired));
        Assert.Equal(2, acquired.SegmentIndex);

        Assert.True(RouteFollowMath.TrySample(
            points, P(5f, 0f), RouteFollowDirection.Forward,
            1, 2, 2f, 20f, 0.1f, out RouteFollowSample advanced));
        Assert.True(advanced.SegmentIndex >= 1);

        Assert.True(RouteFollowMath.TrySample(
            points, P(25f, 0f), RouteFollowDirection.Reverse,
            1, 2, 2f, 20f, 0.1f, out RouteFollowSample reverse));
        Assert.True(reverse.SegmentIndex <= 1);
    }

    [Fact]
    public void RepeatedPoints_AreSkippedWithoutChangingDistance()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(10f, 0f),
            P(20f, 0f),
        };

        bool success = RouteFollowMath.TrySample(
            points, P(9f, 0f), RouteFollowDirection.Forward,
            0, 3, 5f, 2f, 0.1f, out RouteFollowSample sample);

        Assert.True(success);
        Assert.Equal(14f, sample.LookAheadPoint.X, 3);
        Assert.Equal(11f, sample.RemainingMeters, 3);
    }

    [Fact]
    public void RouteEnd_ClampsTargetAndReportsTolerance()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };

        bool success = RouteFollowMath.TrySample(
            points, P(99.9f, 0f), RouteFollowDirection.Forward, 0, 1, 10f, 2f, 0.2f, out RouteFollowSample sample);

        Assert.True(success);
        Assert.Equal(100f, sample.LookAheadPoint.X, 3);
        Assert.True(sample.AtRouteEnd);
        Assert.InRange(sample.RemainingMeters, 0.09f, 0.11f);
    }

    [Fact]
    public void ExcessiveCrossTrackError_FailsClosed()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };

        Assert.False(RouteFollowMath.TrySample(
            points, P(50f, 11f), RouteFollowDirection.Forward,
            0, 1, 10f, 10f, 0.1f, out _));
    }

    [Fact]
    public void NonFiniteOrDegenerateGeometry_FailsClosed()
    {
        var nonFinite = new List<RoadPoint>
        {
            P(0f, 0f),
            new RoadPoint(float.NaN, 30f, 10f),
        };
        var repeated = new List<RoadPoint> { P(1f, 1f), P(1f, 1f) };

        Assert.False(RouteFollowMath.TrySample(
            nonFinite, P(0f, 0f), RouteFollowDirection.Forward, 0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            repeated, P(1f, 1f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
    }

    [Fact]
    public void InvalidInputs_FailClosed()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(10f, 0f) };
        var shortRoute = new List<RoadPoint> { P(0f, 0f) };

        Assert.False(RouteFollowMath.TrySample(
            null, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            shortRoute, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            points, P(0f, 0f), (RouteFollowDirection)0,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward,
            0, 0, 1f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward, 0, 1, 0f, 2f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, -1f, 0.1f, out _));
        Assert.False(RouteFollowMath.TrySample(
            points, new RoadPoint(float.PositiveInfinity, 0f, 0f),
            RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
    }

    [Fact]
    public void MaxSearchWindow_DoesNotOverflow()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(20f, 0f),
        };

        Assert.True(RouteFollowMath.TrySample(
            points, P(15f, 0f), RouteFollowDirection.Forward,
            0, int.MaxValue, 1f, 2f, 0.1f, out RouteFollowSample sample));
        Assert.Equal(1, sample.SegmentIndex);
    }

    [Fact]
    public void SamplingHotPath_AllocatesNothing()
    {
        var points = new List<RoadPoint>
        {
            P(0f, 0f),
            P(10f, 0f),
            P(20f, 10f),
            P(30f, 10f),
        };
        RoadPoint position = P(12f, 2f);
        RouteFollowSample sample = default;
        bool success = false;

        for (int warmup = 0; warmup < 100; warmup++)
        {
            success = RouteFollowMath.TrySample(
                points, position, RouteFollowDirection.Forward,
                0, 4, 5f, 10f, 0.1f, out sample);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            success = RouteFollowMath.TrySample(
                points, position, RouteFollowDirection.Forward,
                0, 4, 5f, 10f, 0.1f, out sample);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(success);
        Assert.True(sample.RemainingMeters > 0f);
        Assert.Equal(0, allocated);
    }
}
