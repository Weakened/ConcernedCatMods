namespace ConcernedTeamster.Tests;

public class AllocationProbeTests
{
    [Fact]
    public void MeasureAfterWarmup_ZeroAllocatingOperation_ReturnsZero()
    {
        int value = 0;

        long allocated = AllocationProbe.MeasureAfterWarmup(
            () => value++,
            warmupIterations: 10_000,
            measuredIterations: 10_000);

        Assert.Equal(20_000, value);
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void MeasureAfterWarmup_AllocatingOperation_StillReportsAllocations()
    {
        object? retained = null;

        long allocated = AllocationProbe.MeasureAfterWarmup(
            () => retained = new object(),
            warmupIterations: 100,
            measuredIterations: 100);

        Assert.NotNull(retained);
        Assert.True(allocated > 0);
    }
}
