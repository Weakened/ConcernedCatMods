using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>DEF-v1.0-006: the four error classes of `cc_roads align live`
/// must be answered separately so a failure localizes itself, with exact
/// boundary behavior at the published thresholds.</summary>
public class AlignmentVerdictsTests
{
    private static string[] Lines(
        bool standingOnRoad = true,
        bool hasNearest = true,
        float nearestMeters = 1f,
        bool hasProjection = true,
        float projectionTexels = 0.3f,
        float pixelsPerTexel = 2f,
        bool vectorActive = false,
        bool hasMarker = true,
        float markerPixels = 1f)
    {
        return AlignmentVerdicts.Evaluate(
            standingOnRoad, hasNearest, nearestMeters,
            hasProjection, projectionTexels,
            pixelsPerTexel, vectorActive,
            hasMarker, markerPixels).Split('\n');
    }

    [Fact]
    public void Report_AlwaysAnswersAllFourClasses_InOrder()
    {
        string[] lines = Lines();

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("A observation:", lines[0]);
        Assert.StartsWith("B projection:", lines[1]);
        Assert.StartsWith("C render resolution:", lines[2]);
        Assert.StartsWith("D marker anchor:", lines[3]);
    }

    [Fact]
    public void A_NotStandingOnRoad_IsNotApplicable()
    {
        Assert.Contains("N/A", Lines(standingOnRoad: false)[0]);
    }

    [Fact]
    public void A_StandingWithNoStoredPoint_ReportsNoData_AndNamesTheV1Rule()
    {
        string line = Lines(hasNearest: false)[0];

        Assert.Contains("NO DATA", line);
        Assert.Contains("Pathen/Paved", line);
    }

    [Fact]
    public void Guidance_MapClosedAndOffRoad_TellsPlayerBothSteps()
    {
        string report = AlignmentVerdicts.Evaluate(
            standingOnRoad: false, hasNearestRoadPoint: false, nearestRoadDistanceMeters: 0f,
            hasProjectionDelta: false, projectionDeltaTexels: 0f,
            screenPixelsPerTexel: 0f, vectorLayerActive: false,
            hasMarkerDelta: false, markerDeltaPixels: 0f);

        Assert.Contains("OPEN THE LARGE MAP", report);
        Assert.Contains("STAND ON", report);
        Assert.Contains("Pathen or Paved", report);
    }

    [Fact]
    public void Guidance_OffRoadOnly_TellsPlayerToStandOnAnExplicitRoad()
    {
        string report = AlignmentVerdicts.Evaluate(
            standingOnRoad: false, hasNearestRoadPoint: false, nearestRoadDistanceMeters: 0f,
            hasProjectionDelta: true, projectionDeltaTexels: 0.2f,
            screenPixelsPerTexel: 2f, vectorLayerActive: true,
            hasMarkerDelta: true, markerDeltaPixels: 0.5f);

        Assert.Contains("STAND ON", report);
        Assert.DoesNotContain("OPEN THE LARGE MAP", report);
    }

    [Fact]
    public void Guidance_MapClosedOnly_TellsPlayerToOpenTheLargeMap()
    {
        string report = AlignmentVerdicts.Evaluate(
            standingOnRoad: true, hasNearestRoadPoint: true, nearestRoadDistanceMeters: 1f,
            hasProjectionDelta: true, projectionDeltaTexels: 0.2f,
            screenPixelsPerTexel: 2f, vectorLayerActive: false,
            hasMarkerDelta: false, markerDeltaPixels: 0f);

        Assert.Contains("OPEN THE LARGE MAP", report);
        Assert.DoesNotContain("STAND ON", report);
    }

    [Fact]
    public void Guidance_AllPreconditionsMet_AddsNothing()
    {
        Assert.Equal(4, Lines().Length);
        Assert.Equal("", AlignmentVerdicts.BuildGuidance(standingOnRoad: true, markerAvailable: true));
    }

    [Theory]
    [InlineData(2.99f, "PASS")]
    [InlineData(3.0f, "PASS")]
    [InlineData(3.01f, "FAIL")]
    public void A_ObservationBoundary_IsThreeMeters(float meters, string verdict)
    {
        Assert.Contains(verdict, Lines(nearestMeters: meters)[0]);
    }

    [Fact]
    public void B_WithoutNativeProjection_IsNotApplicable()
    {
        Assert.Contains("N/A", Lines(hasProjection: false)[1]);
    }

    [Theory]
    [InlineData(1.0f, "PASS")]
    [InlineData(1.01f, "FAIL")]
    public void B_ProjectionBoundary_IsOneTexel(float texels, string verdict)
    {
        Assert.Contains(verdict, Lines(projectionTexels: texels)[1]);
    }

    [Theory]
    [InlineData(4.0f, false, "PASS")]
    [InlineData(4.1f, true, "PASS")]
    [InlineData(4.1f, false, "FAIL")]
    public void C_CoarseTexels_PassOnlyWithVectorLayer(float pixelsPerTexel, bool vectorActive, string verdict)
    {
        Assert.Contains(verdict, Lines(pixelsPerTexel: pixelsPerTexel, vectorActive: vectorActive)[2]);
    }

    [Fact]
    public void C_CoarseZoomWithoutVectorLayer_NamesTheFix()
    {
        Assert.Contains("HighPrecisionLargeMapRoads", Lines(pixelsPerTexel: 12f, vectorActive: false)[2]);
    }

    [Fact]
    public void D_WithoutLiveMarker_IsNotApplicable()
    {
        Assert.Contains("N/A", Lines(hasMarker: false)[3]);
    }

    [Theory]
    [InlineData(2.0f, "PASS")]
    [InlineData(2.01f, "FAIL")]
    public void D_MarkerBoundary_IsTwoPixels(float pixels, string verdict)
    {
        Assert.Contains(verdict, Lines(markerPixels: pixels)[3]);
    }

    [Fact]
    public void Numbers_UseInvariantCulture_UnderCommaDecimalLocale()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            string line = Lines(nearestMeters: 1.25f)[0];

            Assert.Contains("1.25", line);
            Assert.DoesNotContain("1,25", line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
