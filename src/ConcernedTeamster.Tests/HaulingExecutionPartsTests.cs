using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313: the smaller pieces the executor stands on — stillness,
/// safe parking, the body census and the defaults.</summary>
public class HaulingExecutionPartsTests
{
    [Fact]
    public void StillnessNeedsTheWholeWindowWithoutABreak()
    {
        var limits = HaulLimits.Default;
        var still = new CartStillnessTracker(limits);
        still.Observe(10f, 0.1f);
        Assert.False(still.IsStill(10.5f));
        still.Observe(10.5f, 0.1f);
        Assert.True(still.IsStill(11f));

        still.Observe(11.125f, 0.2f);
        Assert.False(still.IsStill(11.125f));
        still.Observe(11.25f, 0f);
        Assert.False(still.IsStill(12f));
        Assert.True(still.IsStill(12.25f));

        still.Observe(13f, float.NaN);
        Assert.False(still.IsStill(20f));
    }

    [Fact]
    public void ParkingNeedsMeasuredDryGentleGroundAnUprightCartAndStillness()
    {
        var limits = HaulLimits.Default;
        var execution = HaulExecutionLimits.Default;
        CartObservation cart = FakeSeam.HealthyCart();
        var flat = new ParkingGround { Measured = true, GradeAlongRatio = 0.05f, GradeAcrossRatio = -0.05f };

        Assert.True(ParkingJudge.Evaluate(flat, cart, cartStill: true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart, cartStill: false, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround(), cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, InWater = true }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, GradeAlongRatio = 0.051f }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, GradeAcrossRatio = float.NaN }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart.With(o => o.UpDot = 0.4f), true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart.With(o => o.Resolved = false), true, limits, execution).Safe);
    }

    [Fact]
    public void TheCensusNeverDuplicatesAndNeverGuesses()
    {
        Assert.Equal(WorkerBodyStatus.Searching, WorkerBodyCensus.Decide(false, 0, 0, false));
        Assert.Equal(WorkerBodyStatus.NotFound, WorkerBodyCensus.Decide(true, 0, 0, false));
        Assert.Equal(WorkerBodyStatus.NotLoaded, WorkerBodyCensus.Decide(true, 1, 0, false));
        Assert.Equal(WorkerBodyStatus.Bound, WorkerBodyCensus.Decide(true, 1, 1, false));
        Assert.Equal(WorkerBodyStatus.Bound, WorkerBodyCensus.Decide(false, 1, 1, false));
        Assert.Equal(WorkerBodyStatus.Faulted, WorkerBodyCensus.Decide(true, 1, 1, true));
        Assert.Equal(WorkerBodyStatus.Duplicated, WorkerBodyCensus.Decide(true, 2, 1, false));
        Assert.Equal(WorkerBodyStatus.Duplicated, WorkerBodyCensus.Decide(false, 1, 2, false));

        foreach (WorkerBodyStatus status in Enum.GetValues<WorkerBodyStatus>())
        {
            Assert.Equal(status == WorkerBodyStatus.NotFound, WorkerBodyCensus.MaySpawn(status));
            Assert.Equal(
                status == WorkerBodyStatus.Unspecified,
                WorkerBodyCensus.Describe(status) == HaulRefusalSentences.BugSentence);
        }
    }

    [Fact]
    public void TheDefaultsAreFrozenAndHaulingIsOff()
    {
        Assert.False(GunnarHaulingDefaults.HaulingEnabled);
        Assert.Equal(GunnarPullStrength.MatchPlayer, GunnarHaulingDefaults.PullStrength);
        Assert.Equal("Dverger", GunnarHaulingDefaults.WorkerBaseCreature);
        Assert.Equal("CT_TeamsterWorker", GunnarHaulingDefaults.WorkerPrefabName);
        Assert.Equal("tcc.worker.key", GunnarHaulingDefaults.WorkerKeyField);
        Assert.Equal("teamster/gunnar", WorkerKey.Gunnar.Value);
        Assert.Equal(0, (int)GunnarPullStrength.Unspecified);
        Assert.Equal(new[] { "Unspecified", "MatchPlayer" }, Enum.GetNames<GunnarPullStrength>());
    }

    [Fact]
    public void ExecutionLimitsAreValidatedAndDocumentedValuesHold()
    {
        HaulExecutionLimits limits = HaulExecutionLimits.Default.Validate();
        Assert.Equal(0.05f, HaulLimits.Default.MaxParkingGradeRatio);
        Assert.Equal(0.8f, limits.JointStrainRatio);
        Assert.Equal(0.1f, HaulExecutionLimits.VanillaTipUpDot);

        limits.JointStrainRatio = 1f;
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());

        Assert.True(HaulExecutionLimits.Default.MassesAgree(70f, 70.5f));
        Assert.False(HaulExecutionLimits.Default.MassesAgree(70f, 72f));
        Assert.False(HaulExecutionLimits.Default.MassesAgree(float.NaN, 70f));
    }
}
