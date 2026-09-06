using System.Collections.Generic;
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

    [Fact]
    public void TripComparisonSeriesHeaders_AreDistinctText()
    {
        string headerA = TeamsterStrings.Format("compare.headerA", "1", "10 m, mass 20, worst 5%");
        string headerB = TeamsterStrings.Format("compare.headerB", "1", "10 m, mass 20, worst 5%");

        Assert.NotEqual(headerA, headerB);
    }
}
