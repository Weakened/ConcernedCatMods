using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-023: the route profiler must respect its per-advance sample
/// budget with public bookkeeping, be cancellable, partition every meter
/// into sampled or unsampled (gaps reported, never guessed), compute grades
/// and surfaces only from fully sampled segments, cache by geometry
/// fingerprint, and bind bottleneck verdicts EXACTLY to LoadModel.</summary>
public class RouteProfilerTests
{
    private static IReadOnlyList<CartographerRoutePoint> StraightX(float lengthMeters)
    {
        return new List<CartographerRoutePoint>
        {
            new(0f, 0f, 0f),
            new(lengthMeters, 0f, 0f),
        };
    }

    private static bool FlatProbe(float x, float z, out float height, out TerrainSurfaceKind surface)
    {
        height = 10f;
        surface = TerrainSurfaceKind.Untouched;
        return true;
    }

    // -- geometry, sampling, honesty --

    [Fact]
    public void FlatRoute_FullySampled_TotalsPartitionExactly()
    {
        var profiler = new RouteProfiler(StraightX(100f), FlatProbe);

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile? profile = profiler.TryBuildProfile();

        Assert.NotNull(profile);
        Assert.Equal(100f, profile!.TotalDistanceMeters, 2);
        Assert.Equal(0f, profile.UnsampledMeters, 2);
        Assert.Equal(profile.TotalDistanceMeters, profile.SampledMeters + profile.UnsampledMeters, 2);
        Assert.Equal(profile.PositionCount, profile.SampledPositionCount);
        Assert.True(float.IsNaN(profile.WorstUphillGradePercent));
        Assert.True(float.IsNaN(profile.WorstDownhillGradePercent));
        Assert.Equal(0f, profile.MaxAbsGradePercent, 3);
        Assert.Equal(profile.SampledMeters, profile.GradeBandMeters[0], 2);
        Assert.Equal(profile.SampledMeters, profile.SurfaceMeters[TerrainSurfaceKind.Untouched], 2);
    }

    [Fact]
    public void Ramp_GradeAndWorstSegmentsComputed()
    {
        // Height rises 0.10 per meter of x → a uniform 10% climb.
        var profiler = new RouteProfiler(
            StraightX(100f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.10f * x;
                surface = TerrainSurfaceKind.Paved;
                return true;
            });

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(10f, profile.WorstUphillGradePercent, 1);
        Assert.True(float.IsNaN(profile.WorstDownhillGradePercent));
        Assert.Equal(10f, profile.MaxAbsGradePercent, 1);
        Assert.Equal(3, profile.WorstSegments.Count);
        Assert.Equal(10f, profile.WorstSegments[0].GradePercent, 1);
        // 10% falls in the 8-15% band; every graded meter lands there.
        Assert.Equal(profile.SampledMeters, profile.GradeBandMeters[2], 2);
    }

    [Fact]
    public void GapInMiddle_ReportedAsUnsampled_GradesExcludeGapPairs()
    {
        // Probe fails for 40 <= x < 60: positions 40,44,48,52,56 fail
        // (spacing 4), so segments 36-40 through 56-60 are unsampled.
        var profiler = new RouteProfiler(
            StraightX(100f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 5f;
                surface = TerrainSurfaceKind.Dirt;
                if (x >= 40f && x < 60f)
                {
                    return false;
                }

                return true;
            });

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(24f, profile.UnsampledMeters, 2);
        Assert.Equal(76f, profile.SampledMeters, 2);
        Assert.Equal(profile.TotalDistanceMeters, profile.SampledMeters + profile.UnsampledMeters, 2);
        // Flat where sampled: no grade ever crosses the gap.
        Assert.Equal(0f, profile.MaxAbsGradePercent, 3);
    }

    [Fact]
    public void ThrowingProbe_BecomesUnsampledNotACrash()
    {
        var profiler = new RouteProfiler(
            StraightX(8f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
                throw new InvalidOperationException("world unloading"));

        profiler.Advance(64);
        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(profile.TotalDistanceMeters, profile.UnsampledMeters, 2);
        Assert.Equal(0f, profile.SampledMeters, 2);
        Assert.True(float.IsNaN(profile.MaxAbsGradePercent));
    }

    [Fact]
    public void SurfaceComposition_AttributedToSegmentStartSample()
    {
        // Paved for x < 50, dirt after: with spacing 4 the boundary segment
        // 48-52 counts under its start sample (48 → paved).
        var profiler = new RouteProfiler(
            StraightX(100f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0f;
                surface = x < 50f ? TerrainSurfaceKind.Paved : TerrainSurfaceKind.Dirt;
                return true;
            });

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(52f, profile.SurfaceMeters[TerrainSurfaceKind.Paved], 2);
        Assert.Equal(48f, profile.SurfaceMeters[TerrainSurfaceKind.Dirt], 2);
    }

    [Fact]
    public void UnknownSurfaceWithGoodHeight_CountedSeparately_GradeStillComputed()
    {
        var profiler = new RouteProfiler(
            StraightX(20f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.05f * x;
                surface = TerrainSurfaceKind.Unavailable;
                return true;
            });

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(profile.SampledMeters, profile.SurfaceUnknownMeters, 2);
        Assert.Empty(profile.SurfaceMeters);
        Assert.Equal(5f, profile.MaxAbsGradePercent, 1);
    }

    [Fact]
    public void MultiLegRoute_TotalIsPolylineLength()
    {
        // L-shape: 60 m along X then 80 m along Z = 140 m of route.
        var points = new List<CartographerRoutePoint>
        {
            new(0f, 0f, 0f),
            new(60f, 0f, 0f),
            new(60f, 0f, 80f),
        };
        var profiler = new RouteProfiler(points, FlatProbe);

        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        Assert.Equal(140f, profile.TotalDistanceMeters, 2);
        Assert.Equal(140f, profile.SampledMeters, 2);
    }

    [Fact]
    public void DegenerateRoutes_CompleteImmediatelyWithEmptyProfile()
    {
        foreach (IReadOnlyList<CartographerRoutePoint> points in new[]
        {
            (IReadOnlyList<CartographerRoutePoint>)Array.Empty<CartographerRoutePoint>(),
            new List<CartographerRoutePoint> { new(3f, 1f, 4f) },
        })
        {
            var profiler = new RouteProfiler(points, FlatProbe);

            Assert.True(profiler.IsComplete);
            Assert.Equal(0, profiler.PositionCount);
            RouteProfile profile = profiler.TryBuildProfile()!;
            Assert.Equal(0f, profile.TotalDistanceMeters, 3);
            Assert.True(float.IsNaN(profile.MaxAbsGradePercent));
        }
    }

    [Fact]
    public void AbsurdlyLongRoute_PositionCountIsCapped()
    {
        var profiler = new RouteProfiler(StraightX(100000f), FlatProbe);

        Assert.True(profiler.PositionCount <= RouteProfiler.MaxSamplePositions);
        Assert.True(profiler.SampleSpacingMeters > RouteProfiler.DefaultSampleSpacingMeters);
    }

    // -- budget and cancellation bookkeeping (AC1) --

    [Fact]
    public void Advance_NeverExceedsBudget_BookkeepingExact()
    {
        var profiler = new RouteProfiler(StraightX(100f), FlatProbe);
        Assert.Equal(26, profiler.PositionCount);

        Assert.Equal(10, profiler.Advance(10));
        Assert.Equal(10, profiler.LastAdvanceSampleCount);
        Assert.Equal(10, profiler.PositionsProbed);
        Assert.False(profiler.IsComplete);

        Assert.Equal(10, profiler.Advance(10));
        Assert.Equal(6, profiler.Advance(10));
        Assert.Equal(6, profiler.LastAdvanceSampleCount);
        Assert.True(profiler.IsComplete);
        Assert.Equal(26, profiler.TotalSamplesConsumed);

        Assert.Equal(0, profiler.Advance(10));
        Assert.Equal(26, profiler.TotalSamplesConsumed);
    }

    [Fact]
    public void Advance_ZeroOrNegativeBudget_ConsumesNothing()
    {
        var profiler = new RouteProfiler(StraightX(100f), FlatProbe);

        Assert.Equal(0, profiler.Advance(0));
        Assert.Equal(0, profiler.Advance(-5));
        Assert.Equal(0, profiler.PositionsProbed);
    }

    [Fact]
    public void Cancel_StopsAllWork_NoProfileFromPartialData()
    {
        var profiler = new RouteProfiler(StraightX(100f), FlatProbe);
        profiler.Advance(5);

        profiler.Cancel();

        Assert.True(profiler.IsCancelled);
        Assert.False(profiler.IsComplete);
        Assert.Equal(0, profiler.Advance(10));
        Assert.Equal(5, profiler.TotalSamplesConsumed);
        Assert.Null(profiler.TryBuildProfile());
    }

    [Fact]
    public void TryBuildProfile_Incomplete_ReturnsNull()
    {
        var profiler = new RouteProfiler(StraightX(100f), FlatProbe);
        profiler.Advance(3);

        Assert.Null(profiler.TryBuildProfile());
    }

    // -- fingerprint and cache invalidation (AC4) --

    [Fact]
    public void Fingerprint_StableForSamePoints_ChangesOnAnyEdit()
    {
        var pointsA = new List<CartographerRoutePoint> { new(1f, 2f, 3f), new(4f, 5f, 6f) };
        var pointsB = new List<CartographerRoutePoint> { new(1f, 2f, 3f), new(4f, 5f, 6f) };
        var moved = new List<CartographerRoutePoint> { new(1f, 2f, 3f), new(4f, 5f, 6.5f) };
        var extended = new List<CartographerRoutePoint> { new(1f, 2f, 3f), new(4f, 5f, 6f), new(7f, 8f, 9f) };

        Assert.Equal(RouteGeometry.Fingerprint(pointsA), RouteGeometry.Fingerprint(pointsB));
        Assert.NotEqual(RouteGeometry.Fingerprint(pointsA), RouteGeometry.Fingerprint(moved));
        Assert.NotEqual(RouteGeometry.Fingerprint(pointsA), RouteGeometry.Fingerprint(extended));
    }

    [Fact]
    public void Cache_HitOnSameGeometry_MissAfterGeometryChange()
    {
        var cache = new RouteProfileCache();
        Guid routeId = Guid.NewGuid();
        var profiler = new RouteProfiler(StraightX(40f), FlatProbe);
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;
        cache.Store(routeId, 111UL, profile);

        Assert.True(cache.TryGet(routeId, 111UL, out RouteProfile? hit));
        Assert.Same(profile, hit);
        // Geometry edit → new fingerprint → the stale profile is unreachable.
        Assert.False(cache.TryGet(routeId, 222UL, out _));
        Assert.False(cache.TryGet(Guid.NewGuid(), 111UL, out _));

        cache.Clear();
        Assert.False(cache.TryGet(routeId, 111UL, out _));
        Assert.Equal(0, cache.Count);
    }

    // -- bottleneck equals LoadModel (AC3) --

    [Fact]
    public void Bottleneck_VerdictAndRecommendationMatchLoadModelExactly()
    {
        LoadCalibrationData? data = LoadCalibrationSource.TryLoadEmbedded();
        Assert.NotNull(data);
        var model = new LoadModel(data!);

        var profiler = new RouteProfiler(
            StraightX(100f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.07f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;

        foreach (float mass in new[] { 60f, 150f, 400f, 1200f })
        {
            RouteLoadBottleneck.Result result = RouteLoadBottleneck.Evaluate(profile, model, mass);
            LoadVerdict direct = model.Query(profile.MaxAbsGradePercent, mass);

            Assert.True(result.HasGradeData);
            Assert.Equal(profile.MaxAbsGradePercent, result.BottleneckGradePercent);
            Assert.Equal(direct.Climbability, result.Verdict!.Climbability);
            Assert.Equal(direct.Basis, result.Verdict.Basis);
            Assert.Equal(direct.Explanation, result.Verdict.Explanation);

            LoadRecommendation? directProven = model.RecommendedMaxMass(profile.MaxAbsGradePercent);
            if (directProven is null)
            {
                Assert.Null(result.ProvenMaxMass);
            }
            else
            {
                Assert.Equal(directProven.TotalMass, result.ProvenMaxMass!.TotalMass);
                Assert.Equal(directProven.Basis, result.ProvenMaxMass.Basis);
            }
        }
    }

    [Fact]
    public void Bottleneck_NoGradeData_ExplicitlySaysSo()
    {
        LoadCalibrationData? data = LoadCalibrationSource.TryLoadEmbedded();
        var model = new LoadModel(data!);
        var profiler = new RouteProfiler(
            StraightX(20f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0f;
                surface = TerrainSurfaceKind.Untouched;
                return false;
            });
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteLoadBottleneck.Result result =
            RouteLoadBottleneck.Evaluate(profiler.TryBuildProfile()!, model, 100f);

        Assert.False(result.HasGradeData);
        Assert.Null(result.Verdict);
        Assert.Null(result.ProvenMaxMass);
    }

    // -- display presenter --

    [Fact]
    public void Presenter_ProfilingState_ShowsProgress()
    {
        IReadOnlyList<string> lines = RouteProfilePresenter.Present(
            hasSelection: true, profiling: true, positionsProbed: 12, positionCount: 26,
            profile: null, bottleneck: null);

        Assert.Equal(RouteProfilePresenter.LineCount, lines.Count);
        Assert.Equal("Profiling route… 12/26 samples", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void Presenter_NoSelection_AllLinesEmpty()
    {
        IReadOnlyList<string> lines = RouteProfilePresenter.Present(
            false, false, 0, 0, null, null);

        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void Presenter_CompleteProfile_ShowsGapSurfacesWorstAndLoad()
    {
        var profiler = new RouteProfiler(
            StraightX(100f),
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.10f * x;
                surface = TerrainSurfaceKind.Paved;
                if (x >= 40f && x < 60f)
                {
                    return false;
                }

                return true;
            });
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        RouteProfile profile = profiler.TryBuildProfile()!;
        LoadCalibrationData? data = LoadCalibrationSource.TryLoadEmbedded();
        var model = new LoadModel(data!);
        RouteLoadBottleneck.Result bottleneck = RouteLoadBottleneck.Evaluate(profile, model, 150f);

        IReadOnlyList<string> lines = RouteProfilePresenter.Present(
            true, false, profile.PositionCount, profile.PositionCount, profile, bottleneck);

        Assert.Contains("UNSAMPLED 24 m", lines[0]);
        Assert.Contains("paved 100%", lines[1]);
        Assert.Contains("Worst climb +10.0%", lines[2]);
        Assert.Contains("8-15% 100%", lines[3]);
        Assert.StartsWith("Load check:", lines[4]);
        Assert.Contains("your cart (150):", lines[4]);
    }

    [Fact]
    public void Presenter_NoLoadModel_SaysSo()
    {
        var profiler = new RouteProfiler(StraightX(8f), FlatProbe);
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        IReadOnlyList<string> lines = RouteProfilePresenter.Present(
            true, false, 3, 3, profiler.TryBuildProfile(), null);

        Assert.Equal("Load check: no load model available", lines[4]);
    }
}
