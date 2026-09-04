using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace ConcernedTeamster.Tests;

/// <summary>CT-008: parser provenance and malformed-row discipline, the
/// dominance verdict rules, structural monotonicity over a grid, explicit
/// out-of-coverage uncertainty, and reproducibility of every verdict from
/// the shipped data file alone (embedded byte-for-byte in this assembly).</summary>
public class LoadModelTests
{
    private const string SyntheticData = @"data-version: 3
game-version: 0.221.12
protocol: docs/protocol.md
generated: 2026-09-04
row: 0 | 100 | Climbs | Prior | flat prior
row: 10 | 200 | Climbs | Measured | measured moderate
row: 12 | 250 | Marginal | Measured | barely made it
row: 20 | 150 | Stalls | Measured | stalled on steep
row: 40 | 5000 | JointBreak | DerivedConstant | physics bound";

    private static LoadModel SyntheticModel()
    {
        return new LoadModel(LoadCalibrationData.Parse(SyntheticData));
    }

    // -- parser --------------------------------------------------------

    [Fact]
    public void Parse_ReadsProvenanceAndRows()
    {
        LoadCalibrationData data = LoadCalibrationData.Parse(SyntheticData);

        Assert.Equal(3, data.DataVersion);
        Assert.Equal("0.221.12", data.GameVersion);
        Assert.Equal("docs/protocol.md", data.Protocol);
        Assert.Equal("2026-09-04", data.Generated);
        Assert.Equal(5, data.Rows.Count);
        Assert.Equal(3, data.MeasuredRowCount);
        Assert.Empty(data.Errors);
    }

    [Fact]
    public void Parse_SkipsMalformedRowsAndReportsEachOne()
    {
        LoadCalibrationData data = LoadCalibrationData.Parse(@"data-version: 1
row: 0 | 100 | Climbs | Prior | good
row: nonsense | 100 | Climbs | Prior | bad grade
row: 5 | -3 | Climbs | Prior | bad mass
row: 5 | 100 | Flies | Prior | bad outcome
row: 5 | 100 | Climbs | Guesswork | bad basis
row: 5 | 100
gibberish line");

        Assert.Single(data.Rows);
        Assert.Equal(6, data.Errors.Count);
    }

    [Fact]
    public void Parse_MissingVersion_IsReportedNotThrown()
    {
        LoadCalibrationData data = LoadCalibrationData.Parse("row: 0 | 100 | Climbs | Prior | x");

        Assert.Equal(0, data.DataVersion);
        Assert.Contains(data.Errors, error => error.Contains("data-version"));
    }

    // -- dominance verdicts -------------------------------------------

    [Fact]
    public void Query_DominatedBySuccess_IsYesWithDecidingBasis()
    {
        LoadVerdict verdict = SyntheticModel().Query(gradePercent: 8f, totalMass: 180f);

        Assert.Equal(Climbability.Yes, verdict.Climbability);
        Assert.Equal(CalibrationBasis.Measured, verdict.Basis);
        Assert.Contains("climbed at 10% with mass 200", verdict.Explanation);
    }

    [Fact]
    public void Query_DominatingFailure_IsNo()
    {
        LoadVerdict verdict = SyntheticModel().Query(gradePercent: 25f, totalMass: 300f);

        Assert.Equal(Climbability.No, verdict.Climbability);
        Assert.Equal(CalibrationBasis.Measured, verdict.Basis);
        Assert.Contains("failed at 20% with mass 150", verdict.Explanation);
    }

    [Fact]
    public void Query_OnlyMarginalDominates_IsMarginal()
    {
        LoadVerdict verdict = SyntheticModel().Query(gradePercent: 11f, totalMass: 220f);

        Assert.Equal(Climbability.Marginal, verdict.Climbability);
        Assert.Contains("was marginal at 12% with mass 250", verdict.Explanation);
    }

    [Fact]
    public void Query_OutsideCoverage_IsExplicitlyUnknown()
    {
        LoadVerdict verdict = SyntheticModel().Query(gradePercent: 15f, totalMass: 400f);

        Assert.Equal(Climbability.Unknown, verdict.Climbability);
        Assert.Null(verdict.Basis);
        Assert.Contains("outside calibrated coverage", verdict.Explanation);
    }

    [Fact]
    public void Query_ContradictoryRows_RefuseAVerdict()
    {
        LoadCalibrationData contradictory = LoadCalibrationData.Parse(@"data-version: 1
row: 10 | 200 | Climbs | Prior | says yes
row: 5 | 100 | Stalls | Prior | says no below it");
        LoadVerdict verdict = new LoadModel(contradictory).Query(7f, 150f);

        Assert.Equal(Climbability.Unknown, verdict.Climbability);
        Assert.Contains("contradictory", verdict.Explanation);
    }

    [Fact]
    public void Query_InvalidInputs_AreUnknownNotExceptions()
    {
        LoadModel model = SyntheticModel();

        Assert.Equal(Climbability.Unknown, model.Query(float.NaN, 100f).Climbability);
        Assert.Equal(Climbability.Unknown, model.Query(5f, -1f).Climbability);
        Assert.Equal(Climbability.Unknown, model.Query(float.PositiveInfinity, 100f).Climbability);
    }

    [Fact]
    public void Query_StrongestBasisDecides()
    {
        LoadCalibrationData data = LoadCalibrationData.Parse(@"data-version: 1
row: 10 | 300 | Climbs | Prior | weak witness
row: 10 | 250 | Climbs | Measured | strong witness");
        LoadVerdict verdict = new LoadModel(data).Query(5f, 200f);

        Assert.Equal(Climbability.Yes, verdict.Climbability);
        Assert.Equal(CalibrationBasis.Measured, verdict.Basis);
    }

    // -- monotonicity (structural property) ---------------------------

    [Fact]
    public void Query_VerdictRankNeverImprovesWithDifficulty()
    {
        LoadModel model = SyntheticModel();
        float[] grades = { 0f, 2f, 5f, 8f, 10f, 12f, 15f, 20f, 25f, 40f, 60f };
        float[] masses = { 20f, 100f, 150f, 200f, 250f, 300f, 1000f, 5000f, 9000f };

        foreach (float grade in grades)
        {
            foreach (float mass in masses)
            {
                int rank = Rank(model.Query(grade, mass).Climbability);
                foreach (float harderGrade in grades)
                {
                    if (harderGrade < grade)
                    {
                        continue;
                    }

                    foreach (float harderMass in masses)
                    {
                        if (harderMass < mass)
                        {
                            continue;
                        }

                        int harderRank = Rank(model.Query(harderGrade, harderMass).Climbability);
                        Assert.True(harderRank <= rank,
                            $"verdict improved from ({grade}%, {mass}) to ({harderGrade}%, {harderMass})");
                    }
                }
            }
        }
    }

    private static int Rank(Climbability climbability)
    {
        return climbability switch
        {
            Climbability.Yes => 3,
            Climbability.Marginal => 2,
            Climbability.Unknown => 1,
            _ => 0,
        };
    }

    // -- recommended load ---------------------------------------------

    [Fact]
    public void RecommendedMaxMass_IsTheHeaviestProvenClimbAtOrAboveTheGrade()
    {
        LoadModel model = SyntheticModel();

        LoadRecommendation? atFive = model.RecommendedMaxMass(5f);
        Assert.NotNull(atFive);
        Assert.Equal(200f, atFive!.TotalMass);
        Assert.Equal(CalibrationBasis.Measured, atFive.Basis);

        LoadRecommendation? atZero = model.RecommendedMaxMass(0f);
        Assert.NotNull(atZero);
        Assert.Equal(200f, atZero!.TotalMass);

        Assert.Null(model.RecommendedMaxMass(15f));
        Assert.Null(model.RecommendedMaxMass(float.NaN));
    }

    // -- the shipped data file ----------------------------------------

    [Fact]
    public void ShippedFile_LoadsWithProvenanceAndNoErrors()
    {
        LoadCalibrationData? data = LoadCalibrationSource.TryLoadEmbedded();

        Assert.NotNull(data);
        Assert.Equal(1, data!.DataVersion);
        Assert.Equal("0.221.12", data.GameVersion);
        Assert.Equal("2026-09-04", data.Generated);
        Assert.Contains("CALIBRATION_PROTOCOL.md", data.Protocol);
        Assert.Empty(data.Errors);
        Assert.Equal(5, data.Rows.Count);
        Assert.Equal(0, data.MeasuredRowCount); // honest: no runs yet
    }

    [Fact]
    public void ShippedFile_VerdictsAreReproducibleFromTheDataAlone()
    {
        LoadModel model = new(LoadCalibrationSource.TryLoadEmbedded()!);

        // Flat priors prove flat hauling up to the heaviest prior set.
        Assert.Equal(Climbability.Yes, model.Query(0f, 150f).Climbability);
        Assert.Equal(CalibrationBasis.Prior, model.Query(0f, 150f).Basis);

        // The uncalibrated middle is honestly unknown.
        Assert.Equal(Climbability.Unknown, model.Query(10f, 220f).Climbability);
        Assert.Equal(Climbability.Unknown, model.Query(0f, 221f).Climbability);

        // The physics bound refuses impossible hauls with certainty basis.
        LoadVerdict impossible = model.Query(35f, 9000f);
        Assert.Equal(Climbability.No, impossible.Climbability);
        Assert.Equal(CalibrationBasis.DerivedConstant, impossible.Basis);

        // Recommended flat load is the heaviest proven prior set.
        Assert.Equal(220f, model.RecommendedMaxMass(0f)!.TotalMass);
        Assert.Null(model.RecommendedMaxMass(5f));
    }
}
