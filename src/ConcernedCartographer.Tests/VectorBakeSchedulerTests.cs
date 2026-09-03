using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC11 feedback 3: deterministic coverage of the vector-layer
/// rebake decisions across the whole zoom range and every invalidation
/// path. A wrong decision here is an invisible road — a zoom band or a
/// window after a data change where nothing draws.</summary>
public class VectorBakeSchedulerTests
{
    private const float Frame = 1f / 60f;

    private static VectorBakeScheduler Committed(float uvWidth)
    {
        var scheduler = new VectorBakeScheduler();
        Assert.True(scheduler.ShouldRebake(uvWidth, stylingChanged: false));
        scheduler.OnBakeCommitted(uvWidth);
        return scheduler;
    }

    [Fact]
    public void FirstBake_FiresImmediately_AtAnyZoom()
    {
        foreach (float uvWidth in new[] { 0.0178f, 0.1f, 0.5f, 1f, 1.78f })
        {
            var scheduler = new VectorBakeScheduler();
            Assert.True(scheduler.ShouldRebake(uvWidth, stylingChanged: false));
        }
    }

    [Fact]
    public void FullZoomSweep_InAndOut_NeverLeavesWidthMoreThanOneStepStale()
    {
        // Valheim's large map: m_largeZoom 0.01..1, one wheel step is
        // ×1.5; uvWidth = zoom × aspect (~1.78 at 16:9). Sweep fully
        // zoomed IN to fully zoomed OUT and back: at every wheel step the
        // committed width must stay within the step ratio of the live
        // width — otherwise ink is mis-calibrated or, worse, a stale bake
        // from another zoom band is all that renders.
        var scheduler = Committed(0.01f * 1.78f);

        for (int direction = 0; direction < 2; direction++)
        {
            float zoom = direction == 0 ? 0.01f : 1f;
            for (int step = 0; step < 16; step++)
            {
                zoom = direction == 0 ? zoom * 1.5f : zoom / 1.5f;
                float uvWidth = System.Math.Clamp(zoom, 0.01f, 1f) * 1.78f;
                scheduler.Advance(Frame);
                if (scheduler.ShouldRebake(uvWidth, stylingChanged: false))
                {
                    scheduler.OnBakeCommitted(uvWidth);
                }

                float ratio = uvWidth / scheduler.BakedUvWidth;
                Assert.InRange(ratio, 1f / VectorBakeScheduler.ZoomStepRatio, VectorBakeScheduler.ZoomStepRatio);
            }
        }
    }

    [Fact]
    public void WithinOneZoomStep_NoRebakeChurn()
    {
        var scheduler = Committed(0.5f);
        scheduler.Advance(Frame);

        Assert.False(scheduler.ShouldRebake(0.5f * 1.2f, stylingChanged: false));
        Assert.False(scheduler.ShouldRebake(0.5f / 1.2f, stylingChanged: false));
    }

    [Fact]
    public void ExactlyPastTheStepRatio_Rebakes_BothDirections()
    {
        var scheduler = Committed(0.5f);

        Assert.True(scheduler.ShouldRebake(0.5f * VectorBakeScheduler.ZoomStepRatio * 1.001f, false));
        Assert.True(scheduler.ShouldRebake(0.5f / VectorBakeScheduler.ZoomStepRatio / 1.001f, false));
    }

    [Fact]
    public void DataChange_RebakesAfterDebounce_NotBefore()
    {
        var scheduler = Committed(0.5f);
        scheduler.MarkDataDirty();

        scheduler.Advance(VectorBakeScheduler.DebounceSeconds - 0.05f);
        Assert.False(scheduler.ShouldRebake(0.5f, stylingChanged: false));

        scheduler.Advance(0.1f);
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: false));

        scheduler.OnBakeCommitted(0.5f);
        Assert.False(scheduler.DataDirty);
        scheduler.Advance(1f);
        Assert.False(scheduler.ShouldRebake(0.5f, stylingChanged: false));
    }

    [Fact]
    public void StylingChange_RebakesImmediately()
    {
        var scheduler = Committed(0.5f);
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: true));
    }

    [Fact]
    public void IncompleteBake_RetriesWithinTheRetryWindow_NeverWaitsForPeriodic()
    {
        // THE RC11 hole mechanism: a bake with unavailable projection used
        // to clear the dirty flag anyway, leaving nothing drawn until the
        // next zoom step or the 30 s periodic tick.
        var scheduler = Committed(0.5f);
        scheduler.MarkDataDirty();
        scheduler.Advance(VectorBakeScheduler.DebounceSeconds);
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: false));

        scheduler.OnBakeIncomplete();
        Assert.True(scheduler.DataDirty);
        Assert.False(scheduler.ShouldRebake(0.5f, stylingChanged: false));

        scheduler.Advance(VectorBakeScheduler.IncompleteRetrySeconds + Frame);
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: false));
    }

    [Fact]
    public void PeriodicParityRebake_FiresOnSchedule()
    {
        var scheduler = Committed(0.5f);
        scheduler.Advance(VectorBakeScheduler.PeriodicSeconds - 0.1f);
        Assert.False(scheduler.ShouldRebake(0.5f, stylingChanged: false));
        scheduler.Advance(0.2f);
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: false));
    }

    [Fact]
    public void Invalidate_BehavesLikeTheFirstBakeAgain()
    {
        var scheduler = Committed(0.5f);
        scheduler.Invalidate();
        Assert.True(scheduler.ShouldRebake(0.5f, stylingChanged: false));
        Assert.True(scheduler.DataDirty);
    }
}
