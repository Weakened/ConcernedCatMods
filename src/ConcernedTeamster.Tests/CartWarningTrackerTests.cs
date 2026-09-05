using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;

namespace ConcernedTeamster.Tests;

/// <summary>CT-009: hysteresis produces one transition pair under
/// oscillating input, levels rise immediately and fall only after the hold,
/// messages are actionable with non-color cues, uncalibrated verdicts never
/// warn, and evaluation happens exactly once per snapshot (reads free).</summary>
public class CartWarningTrackerTests
{
    private static readonly WarningOptions Options =
        WarningOptions.CreateClamped(18f, panelWarningsEnabled: true, hudHintsEnabled: false);

    private static CartTelemetry Telemetry(
        float smoothedGrade, double time, float totalMassCargo = 180f, string id = "1:1")
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            id, baseMass: 20f, cargoWeight: totalMassCargo, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(
            snapshot, velocityAvailable: true, speedMetersPerSecond: 1f,
            verticalSpeedMetersPerSecond: 0f, gradeAvailable: true,
            instantGradePercent: smoothedGrade, smoothedGradePercent: smoothedGrade,
            gradeDirection: smoothedGrade > 2f ? GradeDirection.Climbing : GradeDirection.Level,
            surface: TerrainSurfaceKind.Untouched, sampleTimeSeconds: time);
    }

    private static LoadModel MeasuredModel()
    {
        return new LoadModel(LoadCalibrationData.Parse(@"data-version: 1
row: 10 | 300 | Climbs | Measured | ok here
row: 12 | 400 | Marginal | Measured | barely
row: 20 | 150 | Stalls | Measured | too steep"));
    }

    [Fact]
    public void Update_OscillationAcrossThreshold_OneTransitionPairNotAStream()
    {
        var tracker = new CartWarningTracker(loadModel: null);
        var levels = new List<WarningLevel> { WarningLevel.None }; // pre-warning baseline

        // 0.5 s cadence riding the 18% enter threshold: 19, 16.5, 19, 16.5…
        // (exit is 15, so dips inside the band must not release), then a
        // sustained drop to 5% long enough to pass the 4 s fall hold.
        double time = 0.0;
        for (int index = 0; index < 12; index++)
        {
            float grade = index % 2 == 0 ? 19f : 16.5f;
            CartWarning? warning = tracker.Update(Telemetry(grade, time), Options);
            levels.Add(warning?.Level ?? WarningLevel.None);
            time += 0.5;
        }

        for (int index = 0; index < 12; index++)
        {
            CartWarning? warning = tracker.Update(Telemetry(5f, time), Options);
            levels.Add(warning?.Level ?? WarningLevel.None);
            time += 0.5;
        }

        int transitions = 0;
        for (int index = 1; index < levels.Count; index++)
        {
            if (levels[index] != levels[index - 1])
            {
                transitions++;
            }
        }

        Assert.Equal(WarningLevel.None, levels[0]);    // baseline before any snapshot
        Assert.Equal(WarningLevel.Caution, levels[1]); // rise on the first 19% sample
        Assert.Equal(WarningLevel.None, levels[^1]);
        Assert.Equal(2, transitions); // one rise + one fall — the pair
    }

    [Fact]
    public void Update_RiseIsImmediate_FallWaitsForTheHold()
    {
        var tracker = new CartWarningTracker(loadModel: null);

        Assert.Equal(WarningLevel.Caution, tracker.Update(Telemetry(20f, 0.0), Options)!.Level);

        // Below exit band, but the hold has not elapsed.
        Assert.Equal(WarningLevel.Caution, tracker.Update(Telemetry(5f, 1.0), Options)!.Level);
        Assert.Equal(WarningLevel.Caution, tracker.Update(Telemetry(5f, 4.9), Options)!.Level);

        // 4 s of continuously lower input measured from the first lower
        // sample at t=1.0.
        Assert.Null(tracker.Update(Telemetry(5f, 5.1), Options));
    }

    [Fact]
    public void Update_ReboundDuringTheHold_CancelsTheFall()
    {
        var tracker = new CartWarningTracker(loadModel: null);
        tracker.Update(Telemetry(20f, 0.0), Options);
        tracker.Update(Telemetry(5f, 1.0), Options);   // starts the hold
        tracker.Update(Telemetry(20f, 2.0), Options);  // rebound cancels it
        tracker.Update(Telemetry(5f, 5.5), Options);   // new hold starts here

        Assert.Equal(WarningLevel.Caution, tracker.TryGet("1:1")!.Level);
        Assert.Null(tracker.Update(Telemetry(5f, 9.6), Options));
    }

    [Fact]
    public void Update_ProvenFailure_IsDangerWithActionableText()
    {
        var tracker = new CartWarningTracker(MeasuredModel());

        CartWarning? warning = tracker.Update(Telemetry(22f, 0.0, totalMassCargo: 180f), Options);

        Assert.NotNull(warning);
        Assert.Equal(WarningLevel.Danger, warning!.Level);
        string line = warning.ComposeLine();
        Assert.StartsWith("[!!] DANGER", line);          // non-color cue: symbol + word
        Assert.Contains("cannot climb", line);           // the situation
        Assert.Contains("Lighten the load", line);       // the action
        Assert.Contains("failed at 20% with mass 150", line); // the evidence
    }

    [Fact]
    public void Update_MarginalVerdict_IsCautionWithActionableText()
    {
        var tracker = new CartWarningTracker(MeasuredModel());

        CartWarning? warning = tracker.Update(Telemetry(11f, 0.0, totalMassCargo: 330f), Options);

        Assert.NotNull(warning);
        Assert.Equal(WarningLevel.Caution, warning!.Level);
        string line = warning.ComposeLine();
        Assert.StartsWith("[!] CAUTION", line);
        Assert.Contains("marginal", line);
        Assert.Contains("dropping some cargo", line);
    }

    [Fact]
    public void Update_UnknownVerdict_NeverWarns()
    {
        var tracker = new CartWarningTracker(MeasuredModel());

        // 15% at mass 200: outside every row's dominance — uncalibrated is
        // not danger, and below the steep threshold no terrain caution fires.
        Assert.Null(tracker.Update(Telemetry(15f, 0.0, totalMassCargo: 180f), Options));
    }

    [Fact]
    public void Update_ProvenClimb_SuppressesTheSteepTerrainCaution()
    {
        LoadModel model = new(LoadCalibrationData.Parse(@"data-version: 1
row: 25 | 400 | Climbs | Measured | strong hauler"));
        var tracker = new CartWarningTracker(model);

        // 20% with proven-climbable load: calibration beats the raw
        // terrain threshold — no warning.
        Assert.Null(tracker.Update(Telemetry(20f, 0.0, totalMassCargo: 180f), Options));
    }

    [Fact]
    public void Update_DownhillOrUnavailableGrade_NeverWarns()
    {
        var tracker = new CartWarningTracker(loadModel: null);

        Assert.Null(tracker.Update(Telemetry(-25f, 0.0), Options));

        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", 20f, 100f, cargoDataAvailable: true, itemWeightMassFactor: 1f,
            isAttached: true, isPulledByLocalPlayer: true);
        CartTelemetry noGrade = CartTelemetry.Create(
            snapshot, velocityAvailable: true, speedMetersPerSecond: 0f,
            verticalSpeedMetersPerSecond: 0f, gradeAvailable: false,
            instantGradePercent: 0f, smoothedGradePercent: 0f,
            gradeDirection: GradeDirection.Level,
            surface: TerrainSurfaceKind.Unavailable, sampleTimeSeconds: 1.0);
        Assert.Null(tracker.Update(noGrade, Options));
    }

    [Fact]
    public void EvaluationHappensExactlyOncePerSnapshot_ReadsAreFree()
    {
        var tracker = new CartWarningTracker(loadModel: null);

        tracker.Update(Telemetry(20f, 0.0), Options);
        tracker.Update(Telemetry(20f, 0.5), Options);
        for (int reads = 0; reads < 50; reads++)
        {
            tracker.TryGet("1:1");
        }

        Assert.Equal(2, tracker.EvaluationCount);
    }

    [Fact]
    public void SweepAndReset_ForgetCartState()
    {
        var tracker = new CartWarningTracker(loadModel: null);
        tracker.Update(Telemetry(20f, 0.0), Options);
        Assert.NotNull(tracker.TryGet("1:1"));

        tracker.Sweep(nowSeconds: 10.0, evictAfterSeconds: 2.0);
        Assert.Null(tracker.TryGet("1:1"));

        tracker.Update(Telemetry(20f, 11.0), Options);
        tracker.Reset();
        Assert.Null(tracker.TryGet("1:1"));
    }

    [Fact]
    public void Options_ClampToHardBounds()
    {
        Assert.Equal(5f, WarningOptions.CreateClamped(0f, true, true).SteepGradeCautionPercent);
        Assert.Equal(60f, WarningOptions.CreateClamped(500f, true, true).SteepGradeCautionPercent);
        Assert.Equal(18f, WarningOptions.CreateClamped(float.NaN, true, true).SteepGradeCautionPercent);

        WarningOptions independent = WarningOptions.CreateClamped(18f, panelWarningsEnabled: false, hudHintsEnabled: true);
        Assert.False(independent.PanelWarningsEnabled);
        Assert.True(independent.HudHintsEnabled);
    }
}
