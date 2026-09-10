using TheConcernedCat.ConcernedCartographer.Ui;

namespace ConcernedCartographer.Tests;

/// <summary>Jötunn's CustomGUIFront canvas never sets
/// CanvasScaler.uiScaleMode, so it stays at Unity's default
/// ConstantPixelSize — panels parented on it (the Atlas Drawer, the Pin
/// Workbench, every CcSidePanel) render in literal screen pixels with no
/// relationship to the player's actual render resolution, and shrink
/// proportionally on any display larger than the 1920x1080 reference they
/// were designed against. UiDisplayScale is the automatic baseline that
/// fixes that; these tests pin its behavior, especially that it is a
/// no-op at the reference resolution so this cannot change anything for
/// anyone already at a normal resolution.</summary>
public class UiDisplayScaleTests
{
    [Theory]
    [InlineData(1920f, 1080f)] // exactly the reference canvas
    [InlineData(1280f, 720f)] // smaller than the reference on both axes
    [InlineData(0f, 1080f)] // unusable width
    [InlineData(1920f, 0f)] // unusable height
    [InlineData(float.NaN, 1080f)]
    [InlineData(1920f, float.PositiveInfinity)]
    public void ResolveBaseline_AtOrBelowReferenceOrUnusable_NeverShrinksBelowOne(float canvasWidth, float canvasHeight)
    {
        // The fix must never make an already-correctly-sized (or
        // unmeasurable) canvas smaller than the design already assumes —
        // only high-resolution displays should get bigger panels.
        Assert.Equal(1f, UiDisplayScale.ResolveBaseline(canvasWidth, canvasHeight));
    }

    [Fact]
    public void ResolveBaseline_DoubleTheReferenceResolution_DoublesTheBaseline()
    {
        Assert.Equal(2f, UiDisplayScale.ResolveBaseline(3840f, 2160f));
    }

    [Fact]
    public void ResolveBaseline_UsesTheNarrowerAxisSoAPanelNeverOverflowsEitherDimension()
    {
        // 3840x1080: width says 2x, height says 1x. Taking the wider ratio
        // would grow a panel until it no longer fits the shorter axis.
        Assert.Equal(1f, UiDisplayScale.ResolveBaseline(3840f, 1080f));
    }

    [Fact]
    public void ResolveBaseline_ExtremeResolution_CapsAtMaxDisplayScale()
    {
        Assert.Equal(UiDisplayScale.MaxDisplayScale, UiDisplayScale.ResolveBaseline(19_200f, 10_800f));
    }

    [Theory]
    [InlineData(0.8f)]
    [InlineData(1.0f)]
    [InlineData(1.6f)]
    public void ResolveEffectiveScale_AtReferenceResolution_MatchesTheRawPreference(float userPreference)
    {
        // At the reference resolution the baseline is exactly 1, so the
        // combined result must equal the raw preference byte for byte —
        // this fix must not change anything for anyone already at a normal
        // resolution.
        Assert.Equal(
            userPreference,
            UiDisplayScale.ResolveEffectiveScale(UiDisplayScale.ReferenceWidth, UiDisplayScale.ReferenceHeight, userPreference));
    }

    [Fact]
    public void ResolveEffectiveScale_CombinesBaselineAndUserPreference()
    {
        Assert.Equal(2f * 1.6f, UiDisplayScale.ResolveEffectiveScale(3840f, 2160f, 1.6f));
    }
}
