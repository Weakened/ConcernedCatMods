using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-011: descent parser discipline, three-dimensional dominance
/// verdicts, structural risk monotonicity in every input, bounded lookahead
/// budgets, explicit uncertainty outside coverage, and reproducibility of
/// every verdict from the shipped descent data file alone.</summary>
public class RiskModelTests
{
    private const string SyntheticData = @"data-version: 2
game-version: 0.221.12
protocol: docs/protocol.md
generated: 2026-09-04
row: 8 | 220 | 2 | Held | Measured | controlled walk down moderate
row: 14 | 320 | 3 | Dragged | Measured | dragged on steep-ish
row: 22 | 320 | 2 | Runaway | Measured | ran away on steep
row: 40 | 5000 | 0 | JointBreak | DerivedConstant | physics bound";

    private static RiskModel SyntheticModel()
    {
        return new RiskModel(DescentCalibrationData.Parse(SyntheticData));
    }

    // -- parser --------------------------------------------------------

    [Fact]
    public void Parse_ReadsProvenanceAndRows()
    {
        DescentCalibrationData data = DescentCalibrationData.Parse(SyntheticData);

        Assert.Equal(2, data.DataVersion);
        Assert.Equal("0.221.12", data.GameVersion);
        Assert.Equal(4, data.Rows.Count);
        Assert.Equal(3, data.MeasuredRowCount);
        Assert.Empty(data.Errors);
    }

    [Fact]
    public void Parse_SkipsMalformedRowsAndReportsEachOne()
    {
        DescentCalibrationData data = DescentCalibrationData.Parse(@"data-version: 1
row: 5 | 100 | 1 | Held | Prior | good
row: -3 | 100 | 1 | Held | Prior | negative downgrade
row: 5 | 0 | 1 | Held | Prior | bad mass
row: 5 | 100 | -1 | Held | Prior | negative speed
row: 5 | 100 | 1 | Flew | Prior | bad outcome
row: 5 | 100 | 1 | Held | Vibes | bad basis
row: 5 | 100 | 1");

        Assert.Single(data.Rows);
        Assert.Equal(6, data.Errors.Count);
    }

    // -- dominance verdicts -------------------------------------------

    [Fact]
    public void Query_DominatedByHeld_IsSafe()
    {
        RiskVerdict verdict = SyntheticModel().Query(5f, 200f, 1.5f);

        Assert.Equal(RiskLevel.Safe, verdict.Level);
        Assert.Equal(CalibrationBasis.Measured, verdict.Basis);
        Assert.Contains("stayed controlled at 8% down with mass 220 at 2.0 m/s", verdict.Explanation);
    }

    [Fact]
    public void Query_DominatingRunaway_IsDanger()
    {
        RiskVerdict verdict = SyntheticModel().Query(25f, 400f, 3f);

        Assert.Equal(RiskLevel.Danger, verdict.Level);
        Assert.Contains("ran away at 22% down with mass 320 at 2.0 m/s", verdict.Explanation);
    }

    [Fact]
    public void Query_DominatingDragged_IsCaution()
    {
        RiskVerdict verdict = SyntheticModel().Query(16f, 350f, 3.5f);

        Assert.Equal(RiskLevel.Caution, verdict.Level);
        Assert.Contains("was dragged at 14% down", verdict.Explanation);
    }

    [Fact]
    public void Query_OutsideCoverage_IsExplicitlyUnknown()
    {
        RiskVerdict verdict = SyntheticModel().Query(10f, 220f, 2.5f);

        Assert.Equal(RiskLevel.Unknown, verdict.Level);
        Assert.Null(verdict.Basis);
        Assert.Contains("outside calibrated coverage", verdict.Explanation);
    }

    [Fact]
    public void Query_ContradictoryWitnesses_RefuseAVerdict()
    {
        DescentCalibrationData contradictory = DescentCalibrationData.Parse(@"data-version: 1
row: 10 | 300 | 3 | Held | Prior | harder was held
row: 5 | 100 | 1 | Runaway | Prior | easier ran away");
        RiskVerdict verdict = new RiskModel(contradictory).Query(7f, 200f, 2f);

        Assert.Equal(RiskLevel.Unknown, verdict.Level);
        Assert.Contains("contradictory", verdict.Explanation);
    }

    [Fact]
    public void Query_InvalidInputs_AreUnknownNotExceptions()
    {
        RiskModel model = SyntheticModel();

        Assert.Equal(RiskLevel.Unknown, model.Query(float.NaN, 100f, 1f).Level);
        Assert.Equal(RiskLevel.Unknown, model.Query(-1f, 100f, 1f).Level);
        Assert.Equal(RiskLevel.Unknown, model.Query(5f, 0f, 1f).Level);
        Assert.Equal(RiskLevel.Unknown, model.Query(5f, 100f, -1f).Level);
    }

    // -- monotonicity in every input (structural property) -------------

    [Fact]
    public void Query_RiskNeverDecreasesWithAnyInput()
    {
        RiskModel model = SyntheticModel();
        float[] grades = { 0f, 4f, 8f, 10f, 14f, 22f, 30f, 45f };
        float[] masses = { 20f, 150f, 220f, 320f, 1000f, 6000f };
        float[] speeds = { 0f, 1f, 2f, 3f, 5f };

        var queries = new List<(float Grade, float Mass, float Speed, int Risk)>();
        foreach (float grade in grades)
        {
            foreach (float mass in masses)
            {
                foreach (float speed in speeds)
                {
                    queries.Add((grade, mass, speed, (int)model.Query(grade, mass, speed).Level));
                }
            }
        }

        foreach ((float grade, float mass, float speed, int risk) in queries)
        {
            foreach ((float harderGrade, float harderMass, float harderSpeed, int harderRisk) in queries)
            {
                if (harderGrade >= grade && harderMass >= mass && harderSpeed >= speed)
                {
                    Assert.True(harderRisk >= risk,
                        $"risk decreased from ({grade}%, {mass}, {speed}) to ({harderGrade}%, {harderMass}, {harderSpeed})");
                }
            }
        }
    }

    // -- lookahead bounds ----------------------------------------------

    [Fact]
    public void LookaheadOptions_ClampAndPrecomputeOffsets()
    {
        LookaheadOptions defaults = LookaheadOptions.CreateClamped(3);
        Assert.Equal(3, defaults.Points);
        Assert.Equal(new[] { 4f, 8f, 12f }, defaults.OffsetsMeters);
        Assert.Equal(4, defaults.MaxHeightQueriesPerEvaluation);

        Assert.Equal(0, LookaheadOptions.CreateClamped(-5).Points);
        Assert.Equal(0, LookaheadOptions.CreateClamped(0).MaxHeightQueriesPerEvaluation);
        Assert.Equal(5, LookaheadOptions.CreateClamped(99).Points);
        Assert.Equal(6, LookaheadOptions.CreateClamped(99).MaxHeightQueriesPerEvaluation);
        Assert.Equal(new[] { 4f, 8f, 12f, 16f, 20f }, LookaheadOptions.CreateClamped(5).OffsetsMeters);
    }

    // -- evaluator -------------------------------------------------------

    private static CartTelemetry Telemetry(float smoothedGrade, float speed = 2f)
    {
        // Total mass 370 (20 base + 350 cargo): heavy enough that the
        // synthetic Runaway row (mass 320) easier-dominates it.
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: 350f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(
            snapshot, velocityAvailable: true, speedMetersPerSecond: speed,
            verticalSpeedMetersPerSecond: 0f, gradeAvailable: true,
            instantGradePercent: smoothedGrade, smoothedGradePercent: smoothedGrade,
            gradeDirection: GradeDirection.Level, surface: TerrainSurfaceKind.Untouched,
            sampleTimeSeconds: 1.0);
    }

    [Fact]
    public void EvaluateCurrent_NotDescending_IsSafe()
    {
        RiskVerdict verdict = DescentRiskEvaluator.EvaluateCurrent(SyntheticModel(), Telemetry(6f));

        Assert.Equal(RiskLevel.Safe, verdict.Level);
        Assert.Equal("not descending", verdict.Explanation);
    }

    [Fact]
    public void EvaluateCurrent_Descending_QueriesTheModelWithTheDownMagnitude()
    {
        RiskVerdict verdict = DescentRiskEvaluator.EvaluateCurrent(
            SyntheticModel(), Telemetry(-25f, speed: 3f));

        Assert.Equal(RiskLevel.Danger, verdict.Level);
    }

    [Fact]
    public void EvaluateCurrent_NoModelOrNoGrade_IsUnknown()
    {
        Assert.Equal(RiskLevel.Unknown,
            DescentRiskEvaluator.EvaluateCurrent(null, Telemetry(-10f)).Level);

        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", 20f, 0f, true, 1f, false, false);
        CartTelemetry noGrade = CartTelemetry.Create(
            snapshot, true, 0f, 0f, gradeAvailable: false, 0f, 0f,
            GradeDirection.Level, TerrainSurfaceKind.Unavailable, 0d);
        Assert.Equal(RiskLevel.Unknown,
            DescentRiskEvaluator.EvaluateCurrent(SyntheticModel(), noGrade).Level);
    }

    [Fact]
    public void EvaluateLookahead_UnavailableIsNull_FlatAheadIsSafe()
    {
        Assert.Null(DescentRiskEvaluator.EvaluateLookahead(
            SyntheticModel(), Telemetry(0f), lookaheadAvailable: false, 0f));

        RiskVerdict? flat = DescentRiskEvaluator.EvaluateLookahead(
            SyntheticModel(), Telemetry(0f), lookaheadAvailable: true, 0f);
        Assert.NotNull(flat);
        Assert.Equal(RiskLevel.Safe, flat!.Level);
        Assert.Contains("no descent ahead", flat.Explanation);
    }

    // -- the shipped data file ----------------------------------------

    [Fact]
    public void ShippedDescentFile_LoadsWithProvenanceAndNoErrors()
    {
        DescentCalibrationData? data = DescentCalibrationSource.TryLoadEmbedded();

        Assert.NotNull(data);
        Assert.Equal(1, data!.DataVersion);
        Assert.Equal("0.221.12", data.GameVersion);
        Assert.Empty(data.Errors);
        Assert.Equal(4, data.Rows.Count);
        Assert.Equal(0, data.MeasuredRowCount); // honest: no descent runs yet
    }

    [Fact]
    public void ShippedDescentFile_VerdictsAreReproducibleFromTheDataAlone()
    {
        RiskModel model = new(DescentCalibrationSource.TryLoadEmbedded()!);

        // Stationary flat priors prove the trivial safe cases.
        Assert.Equal(RiskLevel.Safe, model.Query(0f, 200f, 0f).Level);
        Assert.Equal(CalibrationBasis.Prior, model.Query(0.5f, 60f, 0f).Basis);

        // The uncalibrated middle is honestly unknown.
        Assert.Equal(RiskLevel.Unknown, model.Query(10f, 220f, 2f).Level);
        Assert.Equal(RiskLevel.Unknown, model.Query(0f, 200f, 0.1f).Level);

        // The physics bounds refuse impossible descents with certainty.
        RiskVerdict impossible = model.Query(35f, 9000f, 1f);
        Assert.Equal(RiskLevel.Danger, impossible.Level);
        Assert.Equal(CalibrationBasis.DerivedConstant, impossible.Basis);
    }
}
