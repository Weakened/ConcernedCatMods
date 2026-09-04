using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace ConcernedTeamster.Tests;

/// <summary>CT-003: the options are the hard budget boundary — every config
/// value clamps into the documented range, non-finite floats fall back to
/// defaults, and the eviction window derives from the interval with its
/// 2-second floor.</summary>
public class TelemetrySamplerOptionsTests
{
    [Fact]
    public void CreateDefault_MatchesTheDocumentedDefaults()
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateDefault();

        Assert.Equal(0.5f, options.SampleIntervalSeconds);
        Assert.Equal(30f, options.SearchRadiusMeters);
        Assert.Equal(2, options.MaxCartsPerTick);
        Assert.Equal(8, options.MaxTrackedCarts);
        Assert.Equal(2.0d, options.EvictAfterSeconds);
    }

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(-3f, 0.1f)]
    [InlineData(0.1f, 0.1f)]
    [InlineData(99f, 10f)]
    public void CreateClamped_IntervalClampsToHardBounds(float configured, float expected)
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            configured, 30f, 2, 8);

        Assert.Equal(expected, options.SampleIntervalSeconds);
    }

    [Theory]
    [InlineData(0f, 5f)]
    [InlineData(1000f, 100f)]
    public void CreateClamped_RadiusClampsToHardBounds(float configured, float expected)
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            0.5f, configured, 2, 8);

        Assert.Equal(expected, options.SearchRadiusMeters);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, 8)]
    public void CreateClamped_PerTickBudgetClampsToHardBounds(int configured, int expected)
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            0.5f, 30f, configured, 8);

        Assert.Equal(expected, options.MaxCartsPerTick);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1000, 32)]
    public void CreateClamped_TrackedCapClampsToHardBounds(int configured, int expected)
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            0.5f, 30f, 2, configured);

        Assert.Equal(expected, options.MaxTrackedCarts);
    }

    [Fact]
    public void CreateClamped_NonFiniteFloats_FallBackToDefaults()
    {
        TelemetrySamplerOptions options = TelemetrySamplerOptions.CreateClamped(
            float.NaN, float.PositiveInfinity, 2, 8);

        Assert.Equal(0.5f, options.SampleIntervalSeconds);
        Assert.Equal(30f, options.SearchRadiusMeters);
    }

    [Fact]
    public void EvictAfterSeconds_IsThreeIntervalsWithATwoSecondFloor()
    {
        Assert.Equal(2.0d, TelemetrySamplerOptions.CreateClamped(0.5f, 30f, 2, 8).EvictAfterSeconds);
        Assert.Equal(4.5d, TelemetrySamplerOptions.CreateClamped(1.5f, 30f, 2, 8).EvictAfterSeconds, precision: 5);
    }
}
