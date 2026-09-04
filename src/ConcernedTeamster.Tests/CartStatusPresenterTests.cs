using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-005: every displayed panel string is proven headlessly —
/// field coverage, invariant formatting, sticky cart selection, and the
/// explicit no-cart / telemetry-off / stale states that replace wrong or
/// frozen numbers.</summary>
public class CartStatusPresenterTests
{
    private static CartTelemetry Telemetry(
        string id = "12:34",
        float baseMass = 20f,
        float cargo = 170f,
        bool cargoAvailable = true,
        float factor = 1f,
        bool attached = true,
        bool pulledByLocal = false,
        bool gradeAvailable = true,
        float smoothedGrade = 8.3f,
        GradeDirection direction = GradeDirection.Climbing,
        TerrainSurfaceKind surface = TerrainSurfaceKind.Dirt,
        double sampleTime = 100.0)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            id, baseMass, cargo, cargoAvailable, factor, attached, pulledByLocal);
        return CartTelemetry.Create(
            snapshot, velocityAvailable: true, speedMetersPerSecond: 1f,
            verticalSpeedMetersPerSecond: 0f, gradeAvailable, smoothedGrade, smoothedGrade,
            direction, surface, sampleTime);
    }

    private static Dictionary<string, CartTelemetry> Store(params CartTelemetry[] telemetries)
    {
        var store = new Dictionary<string, CartTelemetry>();
        foreach (CartTelemetry telemetry in telemetries)
        {
            store[telemetry.CartId] = telemetry;
        }

        return store;
    }

    [Fact]
    public void Present_TelemetryOff_ExplainsInsteadOfShowingNumbers()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            null, null, 0d, telemetryActive: false);

        Assert.Equal(CartStatusState.TelemetryOff, viewModel.State);
        Assert.Equal("Cart telemetry is unavailable — see the log for details.", viewModel.SourceLine);
        Assert.Equal(string.Empty, viewModel.MassLine);
        Assert.Equal(string.Empty, viewModel.SelectedCartId);
    }

    [Fact]
    public void Present_NoCarts_SaysSoExplicitly()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(), null, 0d, telemetryActive: true);

        Assert.Equal(CartStatusState.NoCart, viewModel.State);
        Assert.Equal("No cart nearby.", viewModel.SourceLine);
        Assert.Equal(string.Empty, viewModel.FreshnessLine);
    }

    [Fact]
    public void Present_LiveCart_FormatsEveryDisplayedField()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry()), null, nowSeconds: 100.4, telemetryActive: true);

        Assert.Equal(CartStatusState.Live, viewModel.State);
        Assert.Equal("Nearby cart", viewModel.SourceLine);
        Assert.Equal("Total mass: 190.0", viewModel.MassLine);
        Assert.Equal("Base 20.0 + cargo 170.0", viewModel.BreakdownLine);
        Assert.Equal("Grade: 8.3% climbing", viewModel.GradeLine);
        Assert.Equal("Surface: dirt path", viewModel.SurfaceLine);
        Assert.Equal("Attached to another puller", viewModel.PullLine);
        Assert.Equal("Updated 0.4 s ago", viewModel.FreshnessLine);
        Assert.Equal("12:34", viewModel.SelectedCartId);
    }

    [Fact]
    public void Present_PulledCart_ShowsPullingLines()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(pulledByLocal: true)), null, 100.0, telemetryActive: true);

        Assert.Equal("Pulling this cart", viewModel.SourceLine);
        Assert.Equal("Pulled by you", viewModel.PullLine);
    }

    [Fact]
    public void Present_NotAttached_SaysNotAttached()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(attached: false)), null, 100.0, telemetryActive: true);

        Assert.Equal("Not attached", viewModel.PullLine);
    }

    [Fact]
    public void Present_CargoUnavailable_ShowsUnknownNotZero()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(cargoAvailable: false)), null, 100.0, telemetryActive: true);

        Assert.Equal("Base 20.0 + cargo unknown", viewModel.BreakdownLine);
        Assert.Equal("Total mass: 20.0", viewModel.MassLine);
    }

    [Fact]
    public void Present_NonUnitFactor_IsShownForTransparency()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(factor: 0.5f, cargo: 100f)), null, 100.0, telemetryActive: true);

        Assert.Equal("Base 20.0 + cargo 100.0 × 0.50", viewModel.BreakdownLine);
        Assert.Equal("Total mass: 70.0", viewModel.MassLine);
    }

    [Fact]
    public void Present_GradeUnavailable_SaysUnavailable()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(gradeAvailable: false)), null, 100.0, telemetryActive: true);

        Assert.Equal("Grade: unavailable", viewModel.GradeLine);
    }

    [Theory]
    [InlineData(GradeDirection.Climbing, 8.3f, "Grade: 8.3% climbing")]
    [InlineData(GradeDirection.Descending, -6.5f, "Grade: -6.5% descending")]
    [InlineData(GradeDirection.Level, 0.4f, "Grade: 0.4% level")]
    public void Present_GradeDirections_UseTheirWords(
        GradeDirection direction, float smoothed, string expected)
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(direction: direction, smoothedGrade: smoothed)), null, 100.0,
            telemetryActive: true);

        Assert.Equal(expected, viewModel.GradeLine);
    }

    [Theory]
    [InlineData(TerrainSurfaceKind.Untouched, "Surface: untouched ground")]
    [InlineData(TerrainSurfaceKind.Dirt, "Surface: dirt path")]
    [InlineData(TerrainSurfaceKind.Cultivated, "Surface: cultivated soil")]
    [InlineData(TerrainSurfaceKind.Paved, "Surface: paved road")]
    [InlineData(TerrainSurfaceKind.Unavailable, "Surface: unknown")]
    public void Present_SurfaceKinds_UseTheirWords(TerrainSurfaceKind surface, string expected)
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(surface: surface)), null, 100.0, telemetryActive: true);

        Assert.Equal(expected, viewModel.SurfaceLine);
    }

    [Fact]
    public void Present_StaleTelemetry_IsVisiblyStaleNotFrozen()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(sampleTime: 100.0)), null, nowSeconds: 101.8, telemetryActive: true);

        Assert.Equal(CartStatusState.Stale, viewModel.State);
        Assert.Equal("STALE — last update 1.8 s ago", viewModel.FreshnessLine);
        // The numbers are still shown (better than blank) but the state and
        // freshness line mark them old.
        Assert.Equal("Total mass: 190.0", viewModel.MassLine);
    }

    [Fact]
    public void Present_JustUnderThreshold_IsLive()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(sampleTime: 100.0)), null, nowSeconds: 101.4, telemetryActive: true);

        Assert.Equal(CartStatusState.Live, viewModel.State);
    }

    [Fact]
    public void Present_ClockSkew_ClampsAgeToZero()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(sampleTime: 100.0)), null, nowSeconds: 99.0, telemetryActive: true);

        Assert.Equal("Updated 0.0 s ago", viewModel.FreshnessLine);
    }

    [Fact]
    public void Present_PulledCartBeatsLowerIdCart()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(id: "1:1"), Telemetry(id: "9:9", pulledByLocal: true)),
            previouslySelectedCartId: "1:1", nowSeconds: 100.0, telemetryActive: true);

        Assert.Equal("9:9", viewModel.SelectedCartId);
        Assert.Equal("Pulling this cart", viewModel.SourceLine);
    }

    [Fact]
    public void Present_StickySelection_KeepsPreviousCartWhileTracked()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(id: "1:1"), Telemetry(id: "5:5")),
            previouslySelectedCartId: "5:5", nowSeconds: 100.0, telemetryActive: true);

        Assert.Equal("5:5", viewModel.SelectedCartId);
    }

    [Fact]
    public void Present_PreviousGone_FallsBackToLowestId()
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            Store(Telemetry(id: "7:7"), Telemetry(id: "3:3")),
            previouslySelectedCartId: "5:5", nowSeconds: 100.0, telemetryActive: true);

        Assert.Equal("3:3", viewModel.SelectedCartId);
    }
}
