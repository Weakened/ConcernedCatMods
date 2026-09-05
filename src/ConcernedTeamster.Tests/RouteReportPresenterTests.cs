using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-024: the route report must render every fixture — all-clear,
/// steep sections, unsampled gaps — with numbered (never color-cued)
/// problem ranking, honest gap disclosure with locations, and advice lines
/// that quote LoadModel outputs verbatim (no free-floating advice).</summary>
public class RouteReportPresenterTests
{
    private static RouteProfile Profile(RouteSampleProbe probe, float lengthMeters = 100f)
    {
        var points = new List<CartographerRoutePoint>
        {
            new(0f, 0f, 0f),
            new(lengthMeters, 0f, 0f),
        };
        var profiler = new RouteProfiler(points, probe);
        while (!profiler.IsComplete)
        {
            profiler.Advance(64);
        }

        return profiler.TryBuildProfile()!;
    }

    private static bool FlatProbe(float x, float z, out float height, out TerrainSurfaceKind surface)
    {
        height = 1f;
        surface = TerrainSurfaceKind.Untouched;
        return true;
    }

    private static LoadModel Model()
    {
        LoadCalibrationData? data = LoadCalibrationSource.TryLoadEmbedded();
        Assert.NotNull(data);
        return new LoadModel(data!);
    }

    // -- profiler span extension (CT-024) --

    [Fact]
    public void Profiler_ContiguousGap_BecomesOneSpanWithLocation()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 1f;
                surface = TerrainSurfaceKind.Untouched;
                return x < 40f || x >= 60f;
            });

        Assert.Single(profile.UnsampledSpans);
        Assert.Equal(36f, profile.UnsampledSpans[0].StartMeters, 2);
        Assert.Equal(24f, profile.UnsampledSpans[0].LengthMeters, 2);
        Assert.True(float.IsNaN(profile.UnsampledSpans[0].GradePercent));
    }

    [Fact]
    public void Profiler_ManyGaps_KeepsThreeLongestTotalStaysExact()
    {
        // Fail single positions at 8, 24, 40, 56, 72 → five 8 m spans; only
        // the three longest (all equal here) are listed, but the TOTAL
        // still counts every unsampled meter.
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 1f;
                surface = TerrainSurfaceKind.Untouched;
                int rounded = (int)Math.Round(x);
                return rounded != 8 && rounded != 24 && rounded != 40 && rounded != 56 && rounded != 72;
            });

        Assert.Equal(3, profile.UnsampledSpans.Count);
        Assert.Equal(40f, profile.UnsampledMeters, 2);
        Assert.Equal(
            profile.TotalDistanceMeters, profile.SampledMeters + profile.UnsampledMeters, 2);
    }

    // -- report fixtures --

    [Fact]
    public void Report_NoProfile_ExplicitState()
    {
        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Ore road", null, Model(), 100f);

        Assert.False(viewModel.HasProfile);
        Assert.Equal("Route report: Ore road", viewModel.Title);
        Assert.Single(viewModel.Lines);
        Assert.Contains("finish profiling", viewModel.Lines[0]);
    }

    [Fact]
    public void Report_AllClear_SaysSoAndStillRecommends()
    {
        RouteProfile profile = Profile(FlatProbe);
        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Flat run", profile, Model(), 120f);

        Assert.True(viewModel.HasProfile);
        Assert.Contains(viewModel.Lines, line => line.StartsWith("No problem sections", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Lines, line => line.StartsWith("1.", StringComparison.Ordinal));
        Assert.Contains(viewModel.Lines, line => line.StartsWith("Bottleneck 0%:", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_SteepClimb_RankedWithReasonAndTracedAdvice()
    {
        // 20% ramp: every segment is a problem; top-3 listed, advice quotes
        // the model verbatim.
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.20f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });
        LoadModel model = Model();
        float mass = 150f;

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Mountain pass", profile, model, mass);

        string first = Assert.Single(
            viewModel.Lines, line => line.StartsWith("1. Steep climb +20.0%", StringComparison.Ordinal));
        Assert.Contains(" at ", first);
        Assert.Contains(" long", first);

        LoadVerdict direct = model.Query(Math.Abs(profile.WorstSegments[0].GradePercent), mass);
        bool traced = false;
        if (direct.Climbability != Climbability.Unknown)
        {
            Assert.Contains(viewModel.Lines, line => line.Contains(direct.Explanation));
            traced = true;
        }
        else
        {
            LoadRecommendation? proven =
                model.RecommendedMaxMass(Math.Abs(profile.WorstSegments[0].GradePercent));
            if (proven is not null)
            {
                Assert.Contains(
                    viewModel.Lines,
                    line => line.Contains("keep total mass at or under") &&
                        line.Contains(proven.TotalMass.ToString("F0")));
                traced = true;
            }
        }

        // Non-vacuous either way: when the model answers nothing at this
        // grade, the report must carry no section advice at all.
        if (!traced)
        {
            Assert.DoesNotContain(viewModel.Lines, line => line.Contains("Here:"));
        }
    }

    [Fact]
    public void Report_ExactlyFifteenPercent_RanksAndAgreesWithGradeMixLine()
    {
        // Constructed profile pins the boundary value exactly: 15.0% must
        // rank as a problem AND count as "15% or steeper" in the mix line —
        // a <= regression in either place would contradict the other.
        var profile = new RouteProfile(
            totalDistanceMeters: 8f,
            sampledMeters: 8f,
            unsampledMeters: 0f,
            surfaceMeters: new Dictionary<TerrainSurfaceKind, float>(),
            surfaceUnknownMeters: 8f,
            gradeBandMeters: new[] { 0f, 0f, 0f, 8f, 0f },
            worstUphillGradePercent: 15f,
            worstDownhillGradePercent: float.NaN,
            maxAbsGradePercent: 15f,
            worstSegments: new[] { new RouteProfileSegment(0f, 4f, 15f) },
            unsampledSpans: Array.Empty<RouteProfileSegment>(),
            sampleSpacingMeters: 4f,
            positionCount: 3,
            sampledPositionCount: 3);

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Boundary", profile, Model(), null);

        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("1. Steep climb +15.0%", StringComparison.Ordinal));
        Assert.Contains(viewModel.Lines, line => line.Contains("15% or steeper"));
        Assert.DoesNotContain(
            viewModel.Lines, line => line.StartsWith("No problem sections", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_GradesAndGaps_NumberingContinuesAcrossKinds()
    {
        var profile = new RouteProfile(
            totalDistanceMeters: 100f,
            sampledMeters: 60f,
            unsampledMeters: 40f,
            surfaceMeters: new Dictionary<TerrainSurfaceKind, float>(),
            surfaceUnknownMeters: 60f,
            gradeBandMeters: new[] { 0f, 0f, 0f, 60f, 0f },
            worstUphillGradePercent: 18f,
            worstDownhillGradePercent: -16f,
            maxAbsGradePercent: 18f,
            worstSegments: new[]
            {
                new RouteProfileSegment(10f, 4f, 18f),
                new RouteProfileSegment(30f, 4f, -16f),
            },
            unsampledSpans: new[]
            {
                new RouteProfileSegment(50f, 30f, float.NaN),
                new RouteProfileSegment(90f, 10f, float.NaN),
            },
            sampleSpacingMeters: 4f,
            positionCount: 26,
            sampledPositionCount: 16);

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Mixed", profile, Model(), null);

        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("1. Steep climb +18.0%", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("2. Steep descent -16.0%", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Lines,
            line => line.StartsWith("3. Unprofiled 30 m starting at 50 m", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Lines,
            line => line.StartsWith("4. Unprofiled 10 m starting at 90 m", StringComparison.Ordinal));
        Assert.DoesNotContain(
            viewModel.Lines, line => line.StartsWith("No problem sections", StringComparison.Ordinal));
        // Worst case must always fit the report panel's fixed 18 lines.
        Assert.True(viewModel.Lines.Count <= 18);
    }

    [Fact]
    public void Report_SteepDescent_AdvisedAsReturnClimb()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = -0.18f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Downhill", profile, Model(), 150f);

        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("1. Steep descent -18.0%", StringComparison.Ordinal));
        // Any advice for a pure-descent route must use return-climb phrasing;
        // "Here:" is reserved for sections that are climbs as traversed.
        Assert.DoesNotContain(viewModel.Lines, line => line.Contains("Here:"));
    }

    [Fact]
    public void Report_Gap_ListedAsRankedProblemWithLocation()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 1f;
                surface = TerrainSurfaceKind.Untouched;
                return x < 40f || x >= 60f;
            });

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Gappy", profile, Model(), null);

        Assert.Contains("UNSAMPLED 24 m", viewModel.Lines[0]);
        Assert.Contains(
            viewModel.Lines,
            line => line.StartsWith("1. Unprofiled 24 m starting at 36 m", StringComparison.Ordinal));
        Assert.DoesNotContain(
            viewModel.Lines, line => line.StartsWith("No problem sections", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_NoMass_OmitsCartVerdictKeepsProvenLine()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.05f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("No cart", profile, Model(), null);

        Assert.DoesNotContain(viewModel.Lines, line => line.StartsWith("Your cart", StringComparison.Ordinal));
        Assert.Contains(viewModel.Lines, line => line.StartsWith("Bottleneck", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_NoModel_SaysAdviceUnavailable_NoFreeFloatingAdvice()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.20f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Uncalibrated", profile, null, 150f);

        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("Load advice unavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Lines, line => line.Contains("keep total mass"));
        Assert.DoesNotContain(viewModel.Lines, line => line.Contains("Your cart"));
        // Facts still render: the steep sections are listed without advice.
        Assert.Contains(
            viewModel.Lines, line => line.StartsWith("1. Steep climb", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_CartVerdictQuotesModelExplanationVerbatim()
    {
        RouteProfile profile = Profile(
            (float x, float z, out float height, out TerrainSurfaceKind surface) =>
            {
                height = 0.07f * x;
                surface = TerrainSurfaceKind.Untouched;
                return true;
            });
        LoadModel model = Model();
        RouteLoadBottleneck.Result bottleneck = RouteLoadBottleneck.Evaluate(profile, model, 90f);

        RouteReportPresenter.ViewModel viewModel =
            RouteReportPresenter.Present("Traced", profile, model, 90f);

        Assert.Contains(
            viewModel.Lines, line => line.Contains(bottleneck.Verdict!.Explanation));
    }
}
