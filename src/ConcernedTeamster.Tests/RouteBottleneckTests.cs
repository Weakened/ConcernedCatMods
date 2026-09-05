using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-019: planted bottlenecks are found and located, hypothetical
/// -load math is pure domain reuse of the LoadModel (verdicts match direct
/// queries exactly), explanations name their constraint, and every honesty
/// path (no data, uncalibrated coverage, invalid mass) renders plainly.</summary>
public class RouteBottleneckTests
{
    /// <summary>200 m straight route, flat except an 18% climb planted in
    /// samples 12–14 (~60% of the way).</summary>
    private static Trip PlantedTrip()
    {
        var samples = new TripSample[21];
        for (int index = 0; index <= 20; index++)
        {
            float grade = index is >= 12 and <= 14 ? 18f : 1f;
            samples[index] = new TripSample(index, index * 10f, 0f, grade, 1.5f, 250f);
        }

        return new Trip(1, "1:1", samples);
    }

    private static LoadModel ModelWithFailureAt15()
    {
        return new LoadModel(LoadCalibrationData.Parse(@"data-version: 1
row: 15 | 200 | Stalls | Measured | fails here
row: 20 | 120 | Climbs | Measured | light carts prove the hill"));
    }

    [Fact]
    public void WorstGrade_IsFoundAndLocated()
    {
        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, null, "250");

        Assert.True(viewModel.Available);
        Assert.Contains("Grade constraint: steepest point is 18.0% at 120 m (60% of the route)",
            viewModel.Lines[0]);
    }

    [Fact]
    public void WorstQuality_FindsThePlantedRoughSegment()
    {
        // Plant roughness into the segment at x≈40 (cell 5,0) via a rough
        // side trip; the analyzed route crosses it at 40 m (20%).
        var roughSamples = new TripSample[6];
        for (int index = 0; index < 6; index++)
        {
            roughSamples[index] = new TripSample(
                index, 40f + index * 0.5f, 0f, index % 2 == 0 ? 9f : -9f, 1f, 100f);
        }

        RoadQualityIndex segments = RoadQualityIndex.ComputeFromTrips(
            new[] { PlantedTrip(), new Trip(2, "1:1", roughSamples) });

        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), segments, null, "250");

        Assert.Contains("Quality constraint: roughest crossed segment", viewModel.Lines[1]);
        Assert.Contains("40 m (20% of the route)", viewModel.Lines[1]);
        Assert.Contains("cell 5,0", viewModel.Lines[1]);
    }

    [Fact]
    public void LoadBinding_MatchesTheLoadModelExactly()
    {
        LoadModel model = ModelWithFailureAt15();
        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, model, "250");

        // The planted 18% climb with mass 250 is dominated by the failure
        // row (15,200) — same verdict the model gives directly.
        LoadVerdict direct = model.Query(18f, 250f);
        Assert.Equal(Climbability.No, direct.Climbability);
        Assert.Contains("Load constraint BINDS", viewModel.Lines[2]);
        Assert.Contains("120 m (60% of the route)", viewModel.Lines[2]);
        Assert.Contains(direct.Explanation, viewModel.Lines[2]);
    }

    [Fact]
    public void LoadBinding_LightMass_ProvenPassable()
    {
        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, ModelWithFailureAt15(), "100");

        // Mass 100 at every climb point (1% and 18%) is dominated by the
        // Climbs row (20,120): proven passable everywhere.
        Assert.Contains("every climb point is proven passable", viewModel.Lines[2]);
    }

    [Fact]
    public void LoadBinding_UncalibratedCoverage_IsReportedHonestly()
    {
        LoadModel sparse = new(LoadCalibrationData.Parse(@"data-version: 1
row: 2 | 300 | Climbs | Prior | flat-ish only"));
        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, sparse, "250");

        // The 1% points are proven; the three 18% points are uncalibrated.
        Assert.Contains("3 of 21 climb points are uncalibrated", viewModel.Lines[2]);
    }

    [Fact]
    public void HonestyPaths_NoTripNoModelNoSegmentsBadMass()
    {
        Assert.Contains("Select trip [A]",
            RouteBottleneckPresenter.Present(null, null, null, "220").Message);

        RouteBottleneckPresenter.ViewModel noMass = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, null, "  ");
        Assert.False(noMass.Available);
        Assert.Contains("Enter a cargo total mass", noMass.Message);

        RouteBottleneckPresenter.ViewModel badMass = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, null, "heavy");
        Assert.Contains("not a usable mass", badMass.Message);

        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            PlantedTrip(), null, null, "250");
        Assert.Contains("no scored segments yet", viewModel.Lines[1]);
        Assert.Contains("no calibration data", viewModel.Lines[2]);
    }

    [Fact]
    public void FlatRoute_LoadIsNotGradeLimited()
    {
        var samples = new TripSample[8];
        for (int index = 0; index < 8; index++)
        {
            samples[index] = new TripSample(index, index * 5f, 0f, -2f, 1.5f, 250f);
        }

        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            new Trip(1, "1:1", samples), null, ModelWithFailureAt15(), "999");

        Assert.Contains("never climbs", viewModel.Lines[2]);
    }
}
