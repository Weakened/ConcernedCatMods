using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-013: the confusion matrix — each diagnostic class triggers
/// on its own synthetic trace and on no other; conflicting evidence yields
/// Unclear; unpulled/moving/short-stall traces yield None and clear the
/// window; parked carts cost nothing (the detector holds no state for
/// them).</summary>
public class StuckDetectorTests
{
    private static CartTelemetry Sample(
        double time,
        float speed = 0.1f,
        bool pulled = true,
        float grade = 0f,
        bool gradeAvailable = true,
        float cargo = 300f,
        bool velocityAvailable = true)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: cargo, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: pulled, isPulledByLocalPlayer: pulled);
        return CartTelemetry.Create(
            snapshot, velocityAvailable, speed, 0f, gradeAvailable, grade, grade,
            grade >= 4f ? GradeDirection.Climbing : GradeDirection.Level,
            TerrainSurfaceKind.Untouched, time);
    }

    private static LoadModel Model()
    {
        // Regions are deliberately disjoint so every diagnostic class is
        // reachable: proven climbs cover light carts up to 12%/180 mass,
        // the marginal row covers 11%/250 without being success-dominated,
        // and the failure row owns 20%+/200+.
        return new LoadModel(LoadCalibrationData.Parse(@"data-version: 1
row: 12 | 180 | Climbs | Measured | proven light climb
row: 11 | 250 | Marginal | Measured | barely
row: 20 | 200 | Stalls | Measured | too much"));
    }

    /// <summary>Feeds a stuck trace (low speed past the window) at the
    /// given grade/cargo and returns the diagnosis.</summary>
    private static CartDiagnostic Diagnose(
        StuckDetector detector, float grade, float cargo = 300f)
    {
        CartDiagnostic last = CartDiagnostic.None;
        for (int index = 0; index <= 6; index++)
        {
            last = detector.Update(Sample(index * 0.5, grade: grade, cargo: cargo));
        }

        return last;
    }

    // -- confusion matrix ------------------------------------------------

    [Fact]
    public void Matrix_EachClassTriggersOnItsTraceOnly()
    {
        // (grade, cargo) -> expected class over the model above:
        //   22% / 250 cargo (mass 270): failure row (20,200) dominates -> ImpossibleLoad
        //   11% / 200 cargo (mass 220): marginal row (11,250) dominates,
        //       proven-climb row (12,180) does not (180 < 220) -> MarginalLoad
        //   16% / 30 cargo (mass 50): no row answers, >=15% -> SteepClimb
        //   2%  / 300 cargo: mild grade -> Obstruction
        //   10% / 150 cargo (mass 170): proven climb (12,180) dominates ->
        //       Yes yet stuck -> Obstruction
        //   9%  / 600 cargo (mass 620): no row answers, 9% < 15% -> Unclear
        //   -12% descent while stuck -> Unclear
        var expectations = new (float Grade, float Cargo, CartDiagnosis Expected)[]
        {
            (22f, 250f, CartDiagnosis.ImpossibleLoad),
            (11f, 200f, CartDiagnosis.MarginalLoad),
            (16f, 30f, CartDiagnosis.SteepClimb),
            (2f, 300f, CartDiagnosis.Obstruction),
            (10f, 150f, CartDiagnosis.Obstruction),
            (9f, 600f, CartDiagnosis.Unclear),
            (-12f, 300f, CartDiagnosis.Unclear),
        };

        foreach ((float grade, float cargo, CartDiagnosis expected) in expectations)
        {
            var detector = new StuckDetector(Model());
            CartDiagnostic diagnostic = Diagnose(detector, grade, cargo);
            Assert.Equal(expected, diagnostic.Diagnosis);
            Assert.NotEqual(string.Empty, diagnostic.Evidence);
            Assert.NotEqual(string.Empty, diagnostic.Action);
        }
    }

    [Fact]
    public void Matrix_GradeUnavailable_IsUnclear()
    {
        var detector = new StuckDetector(Model());
        CartDiagnostic last = CartDiagnostic.None;
        for (int index = 0; index <= 6; index++)
        {
            last = detector.Update(Sample(index * 0.5, gradeAvailable: false));
        }

        Assert.Equal(CartDiagnosis.Unclear, last.Diagnosis);
        Assert.Contains("no terrain data", last.Evidence);
    }

    [Fact]
    public void Matrix_NoModel_SteepClimbAndUnclearStillWork()
    {
        var noModel = new StuckDetector(null);
        Assert.Equal(CartDiagnosis.SteepClimb, Diagnose(noModel, 18f).Diagnosis);

        var noModel2 = new StuckDetector(null);
        Assert.Equal(CartDiagnosis.Unclear, Diagnose(noModel2, 10f).Diagnosis);

        var noModel3 = new StuckDetector(null);
        Assert.Equal(CartDiagnosis.Obstruction, Diagnose(noModel3, 1f).Diagnosis);
    }

    // -- signature gating -------------------------------------------------

    [Fact]
    public void Update_MovingCart_NeverDiagnoses()
    {
        var detector = new StuckDetector(Model());
        for (int index = 0; index <= 10; index++)
        {
            Assert.Equal(CartDiagnosis.None,
                detector.Update(Sample(index * 0.5, speed: 1.2f, grade: 20f)).Diagnosis);
        }
    }

    [Fact]
    public void Update_ShortStall_StaysNoneUntilTheWindowElapses()
    {
        var detector = new StuckDetector(Model());

        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(0.0, grade: 2f)).Diagnosis);
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(2.0, grade: 2f)).Diagnosis);
        // 2.5 s elapsed exactly at t=2.5 -> fires.
        Assert.Equal(CartDiagnosis.Obstruction, detector.Update(Sample(2.5, grade: 2f)).Diagnosis);
    }

    [Fact]
    public void Update_MovementResetsTheWindow()
    {
        var detector = new StuckDetector(Model());
        detector.Update(Sample(0.0, grade: 2f));
        detector.Update(Sample(2.0, speed: 1.5f, grade: 2f)); // moved: window clears
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(2.6, grade: 2f)).Diagnosis);
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(4.0, grade: 2f)).Diagnosis);
        Assert.Equal(CartDiagnosis.Obstruction, detector.Update(Sample(5.2, grade: 2f)).Diagnosis);
    }

    [Fact]
    public void Update_NotPulledOrNoVelocity_ReturnsNoneAndClearsState()
    {
        var detector = new StuckDetector(Model());
        detector.Update(Sample(0.0, grade: 2f));
        detector.Update(Sample(2.0, grade: 2f));

        // Detached mid-window: state clears, so re-attaching starts over.
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(2.4, pulled: false)).Diagnosis);
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(2.6, grade: 2f)).Diagnosis);
        Assert.Equal(CartDiagnosis.None, detector.Update(Sample(4.0, grade: 2f)).Diagnosis);
        Assert.Equal(CartDiagnosis.Obstruction, detector.Update(Sample(5.2, grade: 2f)).Diagnosis);

        var detector2 = new StuckDetector(Model());
        Assert.Equal(CartDiagnosis.None,
            detector2.Update(Sample(0.0, velocityAvailable: false)).Diagnosis);
    }

    [Fact]
    public void Update_CartSwitch_StartsAFreshWindow()
    {
        var detector = new StuckDetector(Model());
        detector.Update(Sample(0.0, grade: 2f));
        detector.Update(Sample(2.0, grade: 2f));

        // A different cart id at t=2.5 must NOT inherit the old window.
        CartSnapshot other = CartSnapshot.Create(
            "2:2", 20f, 300f, true, 1f, true, true);
        CartTelemetry otherCart = CartTelemetry.Create(
            other, true, 0.1f, 0f, true, 2f, 2f, GradeDirection.Level,
            TerrainSurfaceKind.Untouched, 2.5);
        Assert.Equal(CartDiagnosis.None, detector.Update(otherCart).Diagnosis);
    }

    [Fact]
    public void ComposeLine_CarriesCueEvidenceAndAction()
    {
        var detector = new StuckDetector(Model());
        CartDiagnostic diagnostic = Diagnose(detector, 22f, 250f);

        string line = diagnostic.ComposeLine();
        Assert.StartsWith("[?] STUCK — overloaded for this grade", line);
        Assert.Contains("failed at 20%", line);
        Assert.Contains("Lighten the load", line);
        Assert.Equal(string.Empty, CartDiagnostic.None.ComposeLine());
    }
}
