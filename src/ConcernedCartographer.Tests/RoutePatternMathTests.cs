using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>RC10 feedback 5/6/21: the ONE dash/dot cadence walker both
/// route presentations use. The pattern must be geometric — driven only by
/// distance along the polyline, with phase carried across vertices — so
/// densely stored strokes and sparse waypoint lines wear the same pattern,
/// and scaling the cadence with zoom scales the pattern linearly.</summary>
public class RoutePatternMathTests
{
    private static List<(float X, float Y)> Line(params float[] xs)
    {
        var points = new List<(float X, float Y)>();
        foreach (float x in xs)
        {
            points.Add((x, 0f));
        }

        return points;
    }

    [Fact]
    public void Dashes_CoverExactlyTheOnPhases_AlongAStraightLine()
    {
        var stamps = new List<(float FromX, float ToX)>();
        int count = RoutePatternMath.WalkDashes(
            Line(0f, 100f), dashOn: 10f, dashOff: 5f, int.MaxValue,
            (fromX, _, toX, _) => stamps.Add((fromX, toX)));

        // 100 units of 15-cycle: dashes at [0,10),[15,25),... => 7 dashes.
        Assert.Equal(7, count);
        Assert.Equal(7, stamps.Count);
        Assert.Equal(0f, stamps[0].FromX, 3);
        Assert.Equal(10f, stamps[0].ToX, 3);
        Assert.Equal(15f, stamps[1].FromX, 3);
        Assert.Equal(90f, stamps[6].FromX, 3);
        Assert.Equal(100f, stamps[6].ToX, 3);
    }

    [Fact]
    public void DashPhase_CarriesAcrossVertices_SoStoredPointDensityIsInvisible()
    {
        // The same 100-unit line, stored as 2 points and as 11 points,
        // must produce identical dash geometry (merging adjacent stamps
        // that a vertex split in two).
        var sparse = new List<float>();
        RoutePatternMath.WalkDashes(Line(0f, 100f), 10f, 5f, int.MaxValue,
            (fromX, _, toX, _) => AppendMerged(sparse, fromX, toX));

        var dense = new List<float>();
        RoutePatternMath.WalkDashes(
            Line(0f, 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f), 10f, 5f, int.MaxValue,
            (fromX, _, toX, _) => AppendMerged(dense, fromX, toX));

        Assert.Equal(sparse.Count, dense.Count);
        for (int index = 0; index < sparse.Count; index++)
        {
            Assert.Equal(sparse[index], dense[index], 2);
        }
    }

    private static void AppendMerged(List<float> intervals, float fromX, float toX)
    {
        // intervals holds [start, end, start, end, ...]; a stamp continuing
        // the previous end extends it instead of opening a new interval.
        if (intervals.Count >= 2 && Math.Abs(intervals[intervals.Count - 1] - fromX) < 0.01f)
        {
            intervals[intervals.Count - 1] = toX;
            return;
        }

        intervals.Add(fromX);
        intervals.Add(toX);
    }

    [Fact]
    public void Dots_AreSpacedGeometrically_RegardlessOfVertexSpacing()
    {
        var sparseDots = new List<float>();
        RoutePatternMath.WalkDots(Line(0f, 90f), spacing: 9f, int.MaxValue, (x, _) => sparseDots.Add(x));

        var denseDots = new List<float>();
        RoutePatternMath.WalkDots(
            Line(0f, 7f, 13f, 22f, 40f, 61f, 90f), spacing: 9f, int.MaxValue, (x, _) => denseDots.Add(x));

        Assert.Equal(11, sparseDots.Count); // 0, 9, 18, …, 90
        Assert.Equal(sparseDots.Count, denseDots.Count);
        for (int index = 0; index < sparseDots.Count; index++)
        {
            Assert.Equal(index * 9f, sparseDots[index], 2);
            Assert.Equal(sparseDots[index], denseDots[index], 2);
        }
    }

    [Fact]
    public void Cadence_ScalesLinearlyWithZoomDerivedUnits()
    {
        // Zoom stability: the caller derives cadence from screen pixels;
        // doubling units-per-pixel doubles spacing and halves dot count
        // over the same geometry.
        var normal = new List<float>();
        RoutePatternMath.WalkDots(Line(0f, 180f), 9f, int.MaxValue, (x, _) => normal.Add(x));
        var zoomedOut = new List<float>();
        RoutePatternMath.WalkDots(Line(0f, 180f), 18f, int.MaxValue, (x, _) => zoomedOut.Add(x));

        Assert.Equal(21, normal.Count);
        Assert.Equal(11, zoomedOut.Count);
    }

    [Fact]
    public void PatternFlowsThroughCorners()
    {
        // An L-shaped polyline: dots continue around the corner at the
        // same spacing, no restart at the vertex.
        var dots = new List<(float X, float Y)>();
        RoutePatternMath.WalkDots(
            new List<(float X, float Y)> { (0f, 0f), (10f, 0f), (10f, 10f) },
            spacing: 4f, int.MaxValue, (x, y) => dots.Add((x, y)));

        Assert.Equal(6, dots.Count); // 0,4,8 on the first leg; 12,16,20 continue up.
        Assert.Equal((8f, 0f), (dots[2].X, dots[2].Y));
        Assert.Equal(2f, dots[3].Y, 2);   // 12 units along = 2 up the corner
        Assert.Equal(10f, dots[3].X, 2);
    }

    [Fact]
    public void StampBudgets_AreRespected()
    {
        int dashCount = RoutePatternMath.WalkDashes(Line(0f, 1000f), 1f, 1f, maxStamps: 25, (_, _, _, _) => { });
        int dotCount = RoutePatternMath.WalkDots(Line(0f, 1000f), 1f, maxStamps: 25, (_, _) => { });

        Assert.Equal(25, dashCount);
        Assert.Equal(25, dotCount);
    }

    [Fact]
    public void DegenerateInputs_StampNothing()
    {
        Assert.Equal(0, RoutePatternMath.WalkDashes(Line(5f), 5f, 5f, 10, (_, _, _, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDots(Line(5f), 5f, 10, (_, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDashes(Line(0f, 10f), 0f, 0f, 10, (_, _, _, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDots(Line(0f, 10f), 0f, 10, (_, _) => { }));
    }
}

/// <summary>RC10 feedback 7/21: the one-presentation rule and the honest
/// checkbox, as shipped in both overlay renderers.</summary>
public class OverlayVisibilityRuleTests
{
    [Theory]
    [InlineData(true, false, true)]   // layer on, vector idle -> texture shows (fallback/minimap)
    [InlineData(true, true, false)]   // layer on, vector active -> texture suppressed (no double render)
    [InlineData(false, false, false)] // layer off -> nothing shows
    [InlineData(false, true, false)]
    public void EffectiveTexture_FollowsUserAndSuppression(bool user, bool vectorActive, bool expected)
    {
        Assert.Equal(expected, OverlayVisibilityRule.EffectiveTexture(user, vectorActive));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Checkbox_AlwaysShowsTheUserSwitch_NeverSuppression(bool user, bool expected)
    {
        Assert.Equal(expected, OverlayVisibilityRule.CheckboxShows(user));
        // In particular: user ON + vector active means texture hidden but
        // checkbox ON — the vector ink is what the player is seeing.
        Assert.True(OverlayVisibilityRule.CheckboxShows(userEnabled: true));
    }
}
