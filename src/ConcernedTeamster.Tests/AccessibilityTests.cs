using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-033: the UI scale factor clamps to a sane range, every panel
/// text color meets the WCAG AA contrast target against the documented
/// approximate wood-panel background, and every severity/effort/diagnosis
/// state that would otherwise read as "just a color" carries distinct,
/// non-empty text instead.</summary>
public class AccessibilityTests
{
    // ---- UiScaleOptions -----------------------------------------------------

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Clamp_NonFiniteInput_FallsBackToDefault(float input)
    {
        Assert.Equal(UiScaleOptions.DefaultScale, UiScaleOptions.Clamp(input));
    }

    [Theory]
    [InlineData(0f, UiScaleOptions.MinScale)]
    [InlineData(0.5f, UiScaleOptions.MinScale)]
    [InlineData(UiScaleOptions.MinScale, UiScaleOptions.MinScale)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(UiScaleOptions.MaxScale, UiScaleOptions.MaxScale)]
    [InlineData(3f, UiScaleOptions.MaxScale)]
    public void Clamp_ClampsIntoDocumentedRange(float input, float expected)
    {
        Assert.Equal(expected, UiScaleOptions.Clamp(input));
    }

    [Fact]
    public void ScaleConstants_DefaultSitsInsideMinMax()
    {
        Assert.True(UiScaleOptions.MinScale < UiScaleOptions.DefaultScale);
        Assert.True(UiScaleOptions.DefaultScale < UiScaleOptions.MaxScale);
    }

    [Fact]
    public void MaxScale_TallestPanelStaysUnderConservativeReferenceCanvasHeight()
    {
        // TripHistoryPanel.PanelHeight is private to that class; pinned here
        // as a known fact (same rationale as TeamsterStringsTests's
        // ManifestPanelCompositionKeys_ArePinnedByteExact) since it cannot be
        // referenced directly. 1080 is a commonly-cited Valheim UI reference
        // height, and is now CONFIRMED (not just assumed) to match Jötunn's
        // actual CustomGUIFront canvas behavior: CustomGUIFront never sets
        // CanvasScaler.uiScaleMode, so it stays at Unity's default
        // ConstantPixelSize — a panel really is sized in literal screen
        // pixels, with 1920x1080 the reference this mod's panels were
        // designed against (confirmed by decompiling the shipped Jotunn.dll).
        // The point of this test is to fail loudly if a future change to
        // MaxScale or to the tallest panel's height stops leaving margin
        // against that reference.
        const float tallestPanelHeight = 760f; // TripHistoryPanel.PanelHeight
        const float approximateReferenceCanvasHeight = 1080f;
        float worstCaseHeight = tallestPanelHeight * UiScaleOptions.MaxScale;

        Assert.True(worstCaseHeight < approximateReferenceCanvasHeight,
            $"Tallest panel at MaxScale ({worstCaseHeight}) should stay under the approximate " +
            $"{approximateReferenceCanvasHeight} reference canvas height, leaving margin for other HUD chrome.");
    }

    // ---- UiScaleOptions.ResolveDisplayBaseline / ResolveEffectiveScale ------

    [Theory]
    [InlineData(1920f, 1080f)] // exactly the reference canvas
    [InlineData(1280f, 720f)] // smaller than the reference on both axes
    [InlineData(0f, 1080f)] // unusable width
    [InlineData(1920f, 0f)] // unusable height
    [InlineData(float.NaN, 1080f)]
    [InlineData(1920f, float.PositiveInfinity)]
    public void ResolveDisplayBaseline_AtOrBelowReferenceOrUnusable_NeverShrinksBelowOne(
        float canvasWidth, float canvasHeight)
    {
        // The fix must never make an already-correctly-sized (or
        // unmeasurable) canvas smaller than the design already assumes —
        // only high-resolution displays should get bigger panels.
        Assert.Equal(UiScaleOptions.DefaultScale, UiScaleOptions.ResolveDisplayBaseline(canvasWidth, canvasHeight));
    }

    [Fact]
    public void ResolveDisplayBaseline_DoubleTheReferenceResolution_DoublesTheBaseline()
    {
        Assert.Equal(2f, UiScaleOptions.ResolveDisplayBaseline(3840f, 2160f));
    }

    [Fact]
    public void ResolveDisplayBaseline_UsesTheNarrowerAxisSoAPanelNeverOverflowsEitherDimension()
    {
        // 3840x1080: width says 2x, height says 1x. Taking the wider ratio
        // would grow a panel until it no longer fits the shorter axis.
        Assert.Equal(UiScaleOptions.DefaultScale, UiScaleOptions.ResolveDisplayBaseline(3840f, 1080f));
    }

    [Fact]
    public void ResolveDisplayBaseline_ExtremeResolution_CapsAtMaxDisplayScale()
    {
        Assert.Equal(UiScaleOptions.MaxDisplayScale, UiScaleOptions.ResolveDisplayBaseline(19_200f, 10_800f));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(UiScaleOptions.MaxScale)]
    public void ResolveEffectiveScale_AtReferenceResolution_MatchesThePreAutoScaleBehavior(float userPreference)
    {
        // At the reference resolution the baseline is exactly 1, so the
        // combined result must equal the old Clamp-only behavior byte for
        // byte — this fix must not change anything for anyone already at a
        // normal resolution.
        Assert.Equal(
            UiScaleOptions.Clamp(userPreference),
            UiScaleOptions.ResolveEffectiveScale(
                UiScaleOptions.ReferenceWidth, UiScaleOptions.ReferenceHeight, userPreference));
    }

    [Fact]
    public void ResolveEffectiveScale_CombinesDisplayBaselineAndUserPreference()
    {
        float expected = 2f * UiScaleOptions.MaxScale;
        Assert.Equal(expected, UiScaleOptions.ResolveEffectiveScale(3840f, 2160f, UiScaleOptions.MaxScale));
    }

    [Theory]
    [InlineData(1920f, 1080f)]
    [InlineData(3840f, 2160f)]
    [InlineData(19_200f, 10_800f)]
    public void ResolveEffectiveScale_WorstCase_TallestPanelStaysProportionallyUnderItsOwnCanvas(
        float canvasWidth, float canvasHeight)
    {
        // Generalizes MaxScale_TallestPanelStaysUnderConservativeReferenceCanvasHeight:
        // whatever the display baseline turns out to be, the tallest panel at
        // MaxScale must stay within the same margin of the canvas it was
        // actually measured against, not just the 1920x1080 reference case.
        const float tallestPanelHeight = 760f; // TripHistoryPanel.PanelHeight
        float baseline = UiScaleOptions.ResolveDisplayBaseline(canvasWidth, canvasHeight);
        float effectiveScale = UiScaleOptions.ResolveEffectiveScale(canvasWidth, canvasHeight, UiScaleOptions.MaxScale);
        float worstCaseHeight = tallestPanelHeight * effectiveScale;
        float scaledReferenceCanvasHeight = 1080f * baseline;

        Assert.True(worstCaseHeight < scaledReferenceCanvasHeight,
            $"Tallest panel at the worst-case combined scale ({worstCaseHeight}) should stay under " +
            $"{scaledReferenceCanvasHeight} (the reference canvas height scaled by the same baseline), " +
            "leaving margin for other HUD chrome.");
    }

    // ---- ContrastRatio --------------------------------------------------------

    [Fact]
    public void Compute_BlackOnWhite_IsMaximumTwentyOne()
    {
        Assert.Equal(21.0, ContrastRatio.Compute((0f, 0f, 0f), (1f, 1f, 1f)), precision: 1);
    }

    [Fact]
    public void Compute_IdenticalColors_IsMinimumOne()
    {
        Assert.Equal(1.0, ContrastRatio.Compute((0.5f, 0.3f, 0.7f), (0.5f, 0.3f, 0.7f)), precision: 6);
    }

    [Fact]
    public void Compute_IsSymmetric()
    {
        (float, float, float) a = (0.9f, 0.2f, 0.4f);
        (float, float, float) b = (0.1f, 0.6f, 0.8f);
        Assert.Equal(ContrastRatio.Compute(a, b), ContrastRatio.Compute(b, a), precision: 9);
    }

    // ---- PanelPalette audit: every panel text color vs. the documented ------
    // ---- approximate wood-panel background meets the WCAG AA target. --------

    [Theory]
    [InlineData(nameof(PanelPalette.Header))]
    [InlineData(nameof(PanelPalette.Body))]
    [InlineData(nameof(PanelPalette.HudHint))]
    public void PanelTextColor_MeetsAaContrastAgainstApproximateBackground(string colorName)
    {
        (float R, float G, float B) color = colorName switch
        {
            nameof(PanelPalette.Header) => PanelPalette.Header,
            nameof(PanelPalette.HudHint) => PanelPalette.HudHint,
            _ => PanelPalette.Body,
        };

        double ratio = ContrastRatio.Compute(color, PanelPalette.ApproximateWoodBackground);

        Assert.True(ratio >= ContrastRatio.AaNormalTextMinimum,
            $"{colorName} contrast {ratio:0.00}:1 is below the {ContrastRatio.AaNormalTextMinimum}:1 AA target " +
            "against the approximate wood background.");
    }

    // ---- Non-color cues: every state distinguishable by text alone ---------

    [Fact]
    public void WarningSeverityCues_AreDistinctNonEmptyText()
    {
        string caution = TeamsterStrings.Get("warn.cueCaution");
        string danger = TeamsterStrings.Get("warn.cueDanger");

        Assert.NotEmpty(caution);
        Assert.NotEmpty(danger);
        Assert.NotEqual(caution, danger);
    }

    [Fact]
    public void StuckDiagnosisLabels_AreDistinctNonEmptyText()
    {
        // CartDiagnosis.None has no label (not stuck); the other five values
        // each carry a distinct diag.label* key — asserted here so a future
        // edit cannot collapse two causes onto identical wording and lean on
        // color to tell them apart.
        var labels = new[]
        {
            TeamsterStrings.Get("diag.labelImpossibleLoad"),
            TeamsterStrings.Get("diag.labelMarginalLoad"),
            TeamsterStrings.Get("diag.labelSteepClimb"),
            TeamsterStrings.Get("diag.labelObstruction"),
            TeamsterStrings.Get("diag.labelUnclear"),
        };

        Assert.All(labels, label => Assert.NotEmpty(label));
        Assert.Equal(labels.Length, new HashSet<string>(labels).Count);
    }

    [Fact]
    public void CooperativeEffortCounts_AreDistinctNonEmptyText()
    {
        var templates = new[]
        {
            TeamsterStrings.Get("coop.helpingCount"),
            TeamsterStrings.Get("coop.hinderingCount"),
            TeamsterStrings.Get("coop.unclearCount"),
        };

        Assert.All(templates, template => Assert.NotEmpty(template));
        Assert.Equal(templates.Length, new HashSet<string>(templates).Count);
    }

    // Trip series A/B attribution (the real presenter, not just the catalog
    // templates) is proven by TripHistoryUiTests
    // .Comparison_AlignsDifferentLengthsByNormalizedDistance, which asserts
    // ViewModel.HeaderA/HeaderB against two genuinely distinct trips.

    // ---- Outline regression guard ------------------------------------------

    [Fact]
    public void PanelTextCalls_NeverDisableTheContrastOutline()
    {
        // outline:true is the actual contrast fix (ACCESSIBILITY.md): a
        // sensitivity check found unoutlined Header drops to 4.15:1 — below
        // the 4.5:1 AA target — under a plausible lighter background
        // estimate. PanelTextColor_MeetsAaContrastAgainstApproximateBackground
        // only checks raw RGB values and cannot see this, so it is enforced
        // here by scanning the shipped Ui source directly.
        foreach (string file in Directory.EnumerateFiles(UiDirectory, "*.cs"))
        {
            string text = File.ReadAllText(file);
            Assert.False(text.Contains("outline: false"),
                $"{Path.GetFileName(file)} passes outline: false to CreateText, " +
                "reopening the contrast risk documented in ACCESSIBILITY.md.");
        }
    }

    private static readonly Lazy<string> _repoRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above test output.");
    });

    private static string UiDirectory =>
        Path.Combine(_repoRoot.Value, "src", "ConcernedTeamster", "Ui");
}
