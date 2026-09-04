using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-004 fixture suite. Synthetic terrains prove sign, magnitude
/// (documented tolerance ±0.25% after convergence on clean fixtures), and
/// the no-oscillation guarantee of the smoothing + hysteresis spec on the
/// noisy fixture.</summary>
public class GradeMathTests
{
    // -- instant grade -------------------------------------------------

    [Theory]
    [InlineData(0f, 0f, 3f, 0f)]        // flat
    [InlineData(0.3f, 0f, 3f, 10f)]     // 10% climb toward heading
    [InlineData(0f, 0.3f, 3f, -10f)]    // 10% descent toward heading
    [InlineData(1.5f, -1.5f, 3f, 100f)] // 45 degrees = 100% grade
    [InlineData(-0.15f, 0.15f, 3f, -10f)]
    public void ComputeInstantGradePercent_FixtureTable(
        float heightAhead, float heightBehind, float run, float expectedPercent)
    {
        float grade = GradeMath.ComputeInstantGradePercent(heightAhead, heightBehind, run);

        Assert.Equal(expectedPercent, grade, precision: 3);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 3f)]
    [InlineData(0f, float.NaN, 3f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 0f, -1f)]
    [InlineData(float.PositiveInfinity, 0f, 3f)]
    public void ComputeInstantGradePercent_InvalidInputs_YieldNaN(
        float heightAhead, float heightBehind, float run)
    {
        Assert.True(float.IsNaN(GradeMath.ComputeInstantGradePercent(heightAhead, heightBehind, run)));
    }

    // -- smoothing -----------------------------------------------------

    [Fact]
    public void Smooth_FirstSample_StartsFromInstant()
    {
        Assert.Equal(10f, GradeMath.Smooth(float.NaN, 10f));
    }

    [Fact]
    public void Smooth_NaNInstant_KeepsPrevious()
    {
        Assert.Equal(5f, GradeMath.Smooth(5f, float.NaN));
    }

    [Fact]
    public void Smooth_ConvergesToConstantSlopeWithinTolerance()
    {
        float smoothed = float.NaN;
        for (int sample = 0; sample < 10; sample++)
        {
            smoothed = GradeMath.Smooth(smoothed, 10f);
        }

        Assert.InRange(smoothed, 9.75f, 10.25f);
    }

    // -- direction hysteresis -----------------------------------------

    [Theory]
    [InlineData(0f, GradeDirection.Level, GradeDirection.Level)]
    [InlineData(3.9f, GradeDirection.Level, GradeDirection.Level)]       // below enter
    [InlineData(4f, GradeDirection.Level, GradeDirection.Climbing)]      // enter climb
    [InlineData(-4f, GradeDirection.Level, GradeDirection.Descending)]   // enter descent
    [InlineData(2.5f, GradeDirection.Climbing, GradeDirection.Climbing)] // hold in band
    [InlineData(1.9f, GradeDirection.Climbing, GradeDirection.Level)]    // exit below 2
    [InlineData(-2.5f, GradeDirection.Descending, GradeDirection.Descending)]
    [InlineData(-1.9f, GradeDirection.Descending, GradeDirection.Level)]
    [InlineData(-5f, GradeDirection.Climbing, GradeDirection.Descending)] // hard flip
    public void ClassifyDirection_HysteresisTable(
        float smoothed, GradeDirection previous, GradeDirection expected)
    {
        Assert.Equal(expected, GradeMath.ClassifyDirection(smoothed, previous));
    }

    [Fact]
    public void ClassifyDirection_NaN_KeepsPrevious()
    {
        Assert.Equal(GradeDirection.Climbing,
            GradeMath.ClassifyDirection(float.NaN, GradeDirection.Climbing));
    }

    // -- terrain-shape fixtures ---------------------------------------

    /// <summary>Runs a cart over h(x), sampling ahead/behind like the
    /// adapter (half-run 1.5 m), and returns the per-step smoothed grades
    /// and directions.</summary>
    private static (float[] Smoothed, GradeDirection[] Directions) Traverse(
        Func<float, float> terrain, float startX, float endX, float stepMeters)
    {
        var smoothedValues = new List<float>();
        var directions = new List<GradeDirection>();
        float smoothed = float.NaN;
        GradeDirection direction = GradeDirection.Level;
        for (float x = startX; x <= endX; x += stepMeters)
        {
            float instant = GradeMath.ComputeInstantGradePercent(
                terrain(x + 1.5f), terrain(x - 1.5f), 3f);
            smoothed = GradeMath.Smooth(smoothed, instant);
            direction = GradeMath.ClassifyDirection(smoothed, direction);
            smoothedValues.Add(smoothed);
            directions.Add(direction);
        }

        return (smoothedValues.ToArray(), directions.ToArray());
    }

    [Fact]
    public void Fixture_Flat_StaysLevelAtZero()
    {
        (float[] smoothed, GradeDirection[] directions) = Traverse(_ => 4.2f, 0f, 20f, 1f);

        Assert.All(smoothed, value => Assert.Equal(0f, value, precision: 3));
        Assert.All(directions, direction => Assert.Equal(GradeDirection.Level, direction));
    }

    [Fact]
    public void Fixture_UniformSlopes_CorrectSignAndMagnitude()
    {
        // h(x) = 0.08x is an 8% uphill toward +x.
        (float[] up, GradeDirection[] upDirections) = Traverse(x => 0.08f * x, 0f, 20f, 1f);
        Assert.InRange(up[^1], 7.75f, 8.25f);
        Assert.Equal(GradeDirection.Climbing, upDirections[^1]);

        (float[] down, GradeDirection[] downDirections) = Traverse(x => -0.08f * x, 0f, 20f, 1f);
        Assert.InRange(down[^1], -8.25f, -7.75f);
        Assert.Equal(GradeDirection.Descending, downDirections[^1]);
    }

    [Fact]
    public void Fixture_Crest_TransitionsClimbToDescendExactlyOnce()
    {
        // Parabolic hill peaking at x=0: h(x) = 2 - 0.005x^2 (10% grade at
        // x = -10 falling to -10% at x = +10).
        (float[] _, GradeDirection[] directions) = Traverse(
            x => 2f - 0.005f * x * x, -12f, 12f, 0.5f);

        Assert.Equal(GradeDirection.Climbing, directions[0]);
        Assert.Equal(GradeDirection.Descending, directions[^1]);
        Assert.Equal(1, CountTransitions(directions, GradeDirection.Climbing, GradeDirection.Descending));
    }

    [Fact]
    public void Fixture_Dip_TransitionsDescendToClimbExactlyOnce()
    {
        (float[] _, GradeDirection[] directions) = Traverse(
            x => 0.005f * x * x, -12f, 12f, 0.5f);

        Assert.Equal(GradeDirection.Descending, directions[0]);
        Assert.Equal(GradeDirection.Climbing, directions[^1]);
        Assert.Equal(1, CountTransitions(directions, GradeDirection.Descending, GradeDirection.Climbing));
    }

    [Fact]
    public void Fixture_NoisySlope_NeverOscillatesAndStaysAccurate()
    {
        // 6% base slope with deterministic sinusoidal terrain noise strong
        // enough to swing raw instant grades across both hysteresis
        // thresholds (roughly 0..12%).
        static float Terrain(float x) => 0.06f * x + 0.09f * MathF.Sin(x * 2.7f);

        (float[] smoothed, GradeDirection[] directions) = Traverse(Terrain, 0f, 40f, 1f);

        // Prove the fixture is a real oscillation test: raw instant grades
        // cross both the exit and the enter threshold.
        var rawGrades = new List<float>();
        for (float x = 0f; x <= 40f; x += 1f)
        {
            rawGrades.Add(GradeMath.ComputeInstantGradePercent(Terrain(x + 1.5f), Terrain(x - 1.5f), 3f));
        }

        Assert.Contains(rawGrades, raw => raw < GradeMath.DirectionExitThresholdPercent);
        Assert.Contains(rawGrades, raw => raw > GradeMath.DirectionEnterThresholdPercent);

        // Smoothed output: settles to Climbing and never leaves it again —
        // the no-oscillation guarantee.
        int firstClimb = Array.IndexOf(directions, GradeDirection.Climbing);
        Assert.True(firstClimb >= 0, "the slope was never recognized");
        for (int index = firstClimb; index < directions.Length; index++)
        {
            Assert.Equal(GradeDirection.Climbing, directions[index]);
        }

        // And the settled magnitude tracks the true 6% within the noisy
        // tolerance.
        Assert.InRange(smoothed[^1], 4.5f, 7.5f);
    }

    /// <summary>Counts arrivals into the 'to' state (a brief Level step
    /// between climb and descend is expected and still counts as the same
    /// single transition).</summary>
    private static int CountTransitions(
        GradeDirection[] directions, GradeDirection from, GradeDirection to)
    {
        int transitions = 0;
        GradeDirection previous = directions[0];
        foreach (GradeDirection current in directions)
        {
            if (previous != current && current == to)
            {
                transitions++;
            }

            previous = current;
        }

        return transitions;
    }
}
