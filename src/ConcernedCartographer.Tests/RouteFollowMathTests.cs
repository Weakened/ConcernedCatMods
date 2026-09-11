using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RouteFollowMathTests
{
    private static RoadPoint P(float x, float z, float y = 30f)
    {
        return new RoadPoint(x, y, z);
    }

    private static bool TrySample(
        IReadOnlyList<RoadPoint>? points,
        in RoadPoint position,
        RouteFollowDirection direction,
        int cursorSegment,
        int maxSegmentsToSearch,
        float lookAheadMeters,
        float maxCrossTrackMeters,
        float routeEndToleranceMeters,
        out RouteFollowSample sample)
    {
        sample = default;
        return RouteFollowPath.TryCreate(points, out RouteFollowPath? path) &&
            RouteFollowMath.TrySample(
                path,
                position,
                direction,
                cursorSegment,
                maxSegmentsToSearch,
                lookAheadMeters,
                maxCrossTrackMeters,
                routeEndToleranceMeters,
                out sample);
    }

    [Fact]
    public void StraightForward_ProjectsAndLooksAhead()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };

        bool success = TrySample(
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

        bool success = TrySample(
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

        bool success = TrySample(
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

        Assert.True(TrySample(
            points, P(10f, 0f), RouteFollowDirection.Forward,
            0, 2, 1f, 2f, 0.1f, out RouteFollowSample forward));
        Assert.Equal(1, forward.SegmentIndex);

        Assert.True(TrySample(
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

        Assert.False(TrySample(
            points, P(25f, 0f), RouteFollowDirection.Forward,
            0, 1, 2f, 10f, 0.1f, out _));

        Assert.True(TrySample(
            points, P(25f, 0f), RouteFollowDirection.Forward,
            0, 3, 2f, 10f, 0.1f, out RouteFollowSample acquired));
        Assert.Equal(2, acquired.SegmentIndex);

        Assert.True(TrySample(
            points, P(5f, 0f), RouteFollowDirection.Forward,
            1, 2, 2f, 20f, 0.1f, out RouteFollowSample advanced));
        Assert.True(advanced.SegmentIndex >= 1);

        Assert.True(TrySample(
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

        bool success = TrySample(
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

        bool success = TrySample(
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

        Assert.False(TrySample(
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

        Assert.False(TrySample(
            nonFinite, P(0f, 0f), RouteFollowDirection.Forward, 0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            repeated, P(1f, 1f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
    }

    [Fact]
    public void InvalidInputs_FailClosed()
    {
        var points = new List<RoadPoint> { P(0f, 0f), P(10f, 0f) };
        var shortRoute = new List<RoadPoint> { P(0f, 0f) };

        Assert.False(TrySample(
            null, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            shortRoute, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            points, P(0f, 0f), (RouteFollowDirection)0,
            0, 1, 1f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward,
            0, 0, 1f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward, 0, 1, 0f, 2f, 0.1f, out _));
        Assert.False(TrySample(
            points, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 1f, -1f, 0.1f, out _));
        Assert.False(TrySample(
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

        Assert.True(TrySample(
            points, P(15f, 0f), RouteFollowDirection.Forward,
            0, int.MaxValue, 1f, 2f, 0.1f, out RouteFollowSample sample));
        Assert.Equal(1, sample.SegmentIndex);
    }

    [Fact]
    public void PrecomputedMetrics_HandleAdversarialLongRoute()
    {
        var points = new RoadPoint[100_002];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = P(index, 0f);
        }

        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));
        Assert.True(RouteFollowMath.TrySample(
            path, P(0.25f, 0f), RouteFollowDirection.Forward,
            0, 1, 75_000f, 1f, 0.1f, out RouteFollowSample sample));

        Assert.Equal(75_000.25f, sample.LookAheadPoint.X, 2);
        Assert.Equal(100_000.75f, sample.RemainingMeters, 2);
    }

    [Fact]
    public void DegenerateSearchWindow_FailsClosed()
    {
        var points = new RoadPoint[100_002];
        for (int index = 0; index < points.Length - 1; index++)
        {
            points[index] = P(0f, 0f);
        }

        points[points.Length - 1] = P(10f, 0f);
        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));

        Assert.False(RouteFollowMath.TrySample(
            path, P(0f, 0f), RouteFollowDirection.Forward,
            0, 64, 1f, 1f, 0.1f, out _));
        Assert.True(RouteFollowMath.TrySample(
            path, P(9f, 0f), RouteFollowDirection.Reverse,
            path!.LastSegmentIndex, 1, 1f, 1f, 0.1f, out _));
    }

    [Fact]
    public void Reverse_BendAndRepeatedPoint_UseExactRouteMetrics()
    {
        var points = new[]
        {
            P(0f, 0f),
            P(10f, 0f),
            P(10f, 0f),
            P(10f, 10f),
            P(20f, 10f),
        };

        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));
        Assert.True(RouteFollowMath.TrySample(
            path, P(10f, 8f), RouteFollowDirection.Reverse,
            3, 4, 10f, 1f, 0.1f, out RouteFollowSample sample));

        Assert.Equal(2, sample.SegmentIndex);
        Assert.Equal(8f, sample.LookAheadPoint.X, 3);
        Assert.Equal(0f, sample.LookAheadPoint.Z, 3);
        Assert.Equal(18f, sample.RemainingMeters, 3);
    }

    [Fact]
    public void Reverse_RouteEndClampsAndReportsTolerance()
    {
        var points = new[] { P(0f, 0f), P(10f, 0f) };

        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));
        Assert.True(RouteFollowMath.TrySample(
            path, P(0.05f, 0f), RouteFollowDirection.Reverse,
            0, 1, 5f, 1f, 0.1f, out RouteFollowSample sample));

        Assert.Equal(0f, sample.LookAheadPoint.X, 3);
        Assert.True(sample.AtRouteEnd);
        Assert.InRange(sample.RemainingMeters, 0.049f, 0.051f);
    }

    [Fact]
    public void LookAhead_AtRepeatedVertex_ResolvesDeterministically()
    {
        var points = new[]
        {
            P(0f, 0f),
            P(10f, 0f),
            P(10f, 0f),
            P(20f, 0f),
        };

        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));
        Assert.True(RouteFollowMath.TrySample(
            path, P(0f, 0f), RouteFollowDirection.Forward,
            0, 1, 10f, 1f, 0.1f, out RouteFollowSample sample));

        Assert.Equal(10f, sample.LookAheadPoint.X, 3);
        Assert.Equal(0f, sample.LookAheadPoint.Z, 3);
    }

    [Fact]
    public void SamplingHotPath_AllocatesNothing()
    {
        var points = new[]
        {
            P(0f, 0f),
            P(10f, 0f),
            P(20f, 10f),
            P(30f, 10f),
        };
        Assert.True(RouteFollowPath.TryCreate(points, out RouteFollowPath? path));
        var scenario = new AllocationScenario(path!, P(12f, 2f));
        Action sample = scenario.Sample;

        long allocated = MeasureSteadyStateAllocations(
            sample,
            warmupIterations: 1_000,
            measuredIterations: 10_000);

        Assert.True(scenario.Success);
        Assert.True(scenario.Result.RemainingMeters > 0f);
        Assert.Equal(0, allocated);
    }

    private static long MeasureSteadyStateAllocations(
        Action action,
        int warmupIterations,
        int measuredIterations)
    {
        long allocated = -1;
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                Run(action, warmupIterations);
                _ = Measure(action, measuredIterations);
                Run(action, warmupIterations);
                allocated = Measure(action, measuredIterations);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new AggregateException(failure);
        }

        return allocated;
    }

    private static void Run(Action action, int iterations)
    {
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            action();
        }
    }

    private static long Measure(Action action, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(action, iterations);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class AllocationScenario
    {
        private readonly RouteFollowPath _path;
        private readonly RoadPoint _position;

        public AllocationScenario(RouteFollowPath path, RoadPoint position)
        {
            _path = path;
            _position = position;
        }

        public bool Success { get; private set; }
        public RouteFollowSample Result { get; private set; }

        public void Sample()
        {
            Success = RouteFollowMath.TrySample(
                _path,
                _position,
                RouteFollowDirection.Forward,
                0,
                4,
                5f,
                10f,
                0.1f,
                out RouteFollowSample result);
            Result = result;
        }
    }
}
