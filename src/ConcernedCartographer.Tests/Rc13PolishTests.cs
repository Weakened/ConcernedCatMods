using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC13 polish 1: the softened large-map road ink profile. The
/// feather must not change what the owner approved in RC12 — color,
/// centerline, perceived width — only the edge quality.</summary>
public class RoadInkSofteningTests
{
    [Fact]
    public void CoreIsFullyOpaque_SoColorsAreUnchanged()
    {
        Assert.Equal(1f, RoadInkSoftening.Alpha(0f));
        Assert.Equal(1f, RoadInkSoftening.Alpha(RoadInkSoftening.OpaqueCoreFraction));
    }

    [Fact]
    public void EdgeIsFullyTransparent()
    {
        Assert.Equal(0f, RoadInkSoftening.Alpha(1f));
        Assert.Equal(0f, RoadInkSoftening.Alpha(1.5f));
    }

    [Fact]
    public void ProfileIsSymmetric_SoTheCenterlineNeverShifts()
    {
        for (float offset = 0f; offset <= 1.2f; offset += 0.05f)
        {
            Assert.Equal(RoadInkSoftening.Alpha(offset), RoadInkSoftening.Alpha(-offset));
        }
    }

    [Fact]
    public void ProfileFallsMonotonically_FromCoreToEdge()
    {
        float previous = 1f;
        for (float offset = 0f; offset <= 1f; offset += 0.01f)
        {
            float alpha = RoadInkSoftening.Alpha(offset);
            Assert.True(alpha <= previous + 1e-6f, $"alpha rose at offset {offset}");
            Assert.InRange(alpha, 0f, 1f);
            previous = alpha;
        }
    }

    [Fact]
    public void PerceivedWidthIsPreserved_HalfAlphaFallsExactlyAtTheCrispHalfWidth()
    {
        // The feathered quad is WidenFactor times the crisp RC12 quad; the
        // profile must cross 50% alpha exactly where the crisp edge used
        // to be, so the road reads as wide as RC12 at every zoom.
        float crispEdgeOffset = 1f / RoadInkSoftening.WidenFactor;
        Assert.Equal(0.5f, RoadInkSoftening.Alpha(crispEdgeOffset), 5);
    }

    [Fact]
    public void SofteningIsModest_NeverFuzzyEnoughToHideGeometry()
    {
        Assert.InRange(RoadInkSoftening.WidenFactor, 1.0f, 1.5f);
        Assert.InRange(RoadInkSoftening.OpaqueCoreFraction, 0.4f, 0.8f);
    }
}

/// <summary>RC13 polish 2: the palette wheel step target — roughly 2–3×
/// the RC12 step, with a sane floor if the base is degenerate.</summary>
public class PaletteScrollTuningTests
{
    [Fact]
    public void WheelFactor_IsInTheOwnersTwoToThreeWindow()
    {
        Assert.InRange(PaletteScrollTuning.WheelFactor, 2f, 3f);
    }

    [Fact]
    public void HealthyBaseSensitivity_ScalesMultiplicatively()
    {
        Assert.Equal(30f * PaletteScrollTuning.WheelFactor, PaletteScrollTuning.Scaled(30f));
        Assert.Equal(40f * PaletteScrollTuning.WheelFactor, PaletteScrollTuning.Scaled(40f));
    }

    [Fact]
    public void DegenerateBaseSensitivity_FloorsToThreeRowsPerNotch()
    {
        Assert.Equal(PaletteScrollTuning.MinimumStepPixels, PaletteScrollTuning.Scaled(0f));
        Assert.Equal(PaletteScrollTuning.MinimumStepPixels, PaletteScrollTuning.Scaled(-5f));
        Assert.Equal(PaletteScrollTuning.MinimumStepPixels, PaletteScrollTuning.Scaled(1f));
    }

    [Fact]
    public void ScaledStep_IsAlwaysAtLeastTheFloor_AndNeverShrinks()
    {
        float previous = 0f;
        for (float baseSensitivity = 0f; baseSensitivity <= 100f; baseSensitivity += 5f)
        {
            float scaled = PaletteScrollTuning.Scaled(baseSensitivity);
            Assert.True(scaled >= PaletteScrollTuning.MinimumStepPixels);
            Assert.True(scaled >= previous);
            previous = scaled;
        }
    }
}

/// <summary>RC13 polish 4: the Markers default panel fires once per fresh
/// map-open and can never fight the user afterwards.</summary>
public class DefaultPanelRuleTests
{
    [Fact]
    public void FreshMapOpen_OpensMarkers_ExactlyOnce()
    {
        var rule = new DefaultPanelRule();
        rule.NoteMapClosed();

        Assert.True(rule.IsArmed);
        Assert.True(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: false));
        Assert.False(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: false));
        Assert.False(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: true));
    }

    [Fact]
    public void OnceDisarmed_CallersCanSkipTheAvailabilityWorkEntirely()
    {
        // The runtime gates on IsArmed before computing availability (the
        // NoMap gate can scan loaded instances) — a disarmed rule must
        // read as disarmed for the whole rest of the map-open.
        var rule = new DefaultPanelRule();
        Assert.True(rule.ShouldAutoOpen(true, false));
        Assert.False(rule.IsArmed);

        rule.NoteMapClosed();
        Assert.True(rule.IsArmed);
    }

    [Fact]
    public void VeryFirstMapOpenOfTheSession_FiresWithoutAPriorClosedFrame()
    {
        var rule = new DefaultPanelRule();

        Assert.True(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: false));
    }

    [Fact]
    public void UserClosesOrSwitches_NeverFoughtForTheRestOfTheMapOpen()
    {
        var rule = new DefaultPanelRule();
        Assert.True(rule.ShouldAutoOpen(true, false));

        // The user closed the palette (or switched panels): every later
        // frame of this map-open stays quiet, whatever is visible.
        Assert.False(rule.ShouldAutoOpen(true, false));
        Assert.False(rule.ShouldAutoOpen(true, true));
        Assert.False(rule.ShouldAutoOpen(true, false));
    }

    [Fact]
    public void SurfaceAlreadyVisibleAtOpen_DisarmsInsteadOfFighting()
    {
        var rule = new DefaultPanelRule();
        rule.NoteMapClosed();

        Assert.False(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: true));
        // Even after that surface closes, this map-open stays the user's.
        Assert.False(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: false));
    }

    [Fact]
    public void PaletteUnavailableAtOpen_DisarmsAndNeverPopsLate()
    {
        var rule = new DefaultPanelRule();
        rule.NoteMapClosed();

        Assert.False(rule.ShouldAutoOpen(paletteAvailable: false, anySurfaceVisible: false));
        // A mid-session availability flip must not pop a panel minutes
        // into the same map-open.
        Assert.False(rule.ShouldAutoOpen(paletteAvailable: true, anySurfaceVisible: false));
    }

    [Fact]
    public void ClosingAndReopeningTheMap_RearmsTheDefaultPanel()
    {
        var rule = new DefaultPanelRule();
        Assert.True(rule.ShouldAutoOpen(true, false));
        Assert.False(rule.ShouldAutoOpen(true, false));

        rule.NoteMapClosed();
        Assert.True(rule.ShouldAutoOpen(true, false));
    }
}

/// <summary>RC13 polish 3: the orphan-chrome verdict — only pure, empty
/// decoration whose framed controls are already hidden may be hidden,
/// and any vanilla fallback restores everything.</summary>
public class OrphanChromeRuleTests
{
    private static OrphanChromeRule.CandidateFacts Facts(
        bool isLargeRootOrAbove = false,
        bool containsProtectedObject = false,
        bool hasLiveControl = false,
        bool hasLiveTextGraphic = false)
    {
        return new OrphanChromeRule.CandidateFacts(
            isLargeRootOrAbove, containsProtectedObject, hasLiveControl, hasLiveTextGraphic);
    }

    [Fact]
    public void EmptyDecoration_MayHide()
    {
        Assert.True(OrphanChromeRule.MayHide(Facts()));
    }

    [Fact]
    public void TheLargeRootItself_IsNeverHideable()
    {
        Assert.False(OrphanChromeRule.MayHide(Facts(isLargeRootOrAbove: true)));
    }

    [Fact]
    public void AnythingFramingAProtectedObject_StaysVisible()
    {
        // Map image, bottom hint bars, shared-map hint, pin roots, biome
        // label — all arrive through this single fact.
        Assert.False(OrphanChromeRule.MayHide(Facts(containsProtectedObject: true)));
    }

    [Fact]
    public void AnythingWithALiveControl_StaysVisible()
    {
        Assert.False(OrphanChromeRule.MayHide(Facts(hasLiveControl: true)));
    }

    [Fact]
    public void AnythingWithLiveText_StaysVisible()
    {
        Assert.False(OrphanChromeRule.MayHide(Facts(hasLiveTextGraphic: true)));
    }

    [Fact]
    public void EveryBlockingFactCombination_StaysVisible()
    {
        Assert.False(OrphanChromeRule.MayHide(
            Facts(containsProtectedObject: true, hasLiveControl: true, hasLiveTextGraphic: true)));
    }

    [Fact]
    public void AnyVanillaFallback_ForcesExactRestore()
    {
        Assert.True(OrphanChromeRule.MustRestore(anyVanillaControlsWanted: true));
        Assert.False(OrphanChromeRule.MustRestore(anyVanillaControlsWanted: false));
    }

    [Fact]
    public void TheClimbIsTightlyBounded()
    {
        Assert.InRange(OrphanChromeRule.MaxClimbSteps, 1, 4);
    }
}
