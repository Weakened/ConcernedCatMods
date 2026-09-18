using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-06: a stall is judged from both bodies, recovered only by
/// replanning from where the cart actually stands, bounded per leg with a
/// doubling backoff, never reset by a moment of progress (no oscillation), and
/// ends by stopping and reporting one reason instead of escalating.</summary>
public class HaulingExecutionRecoveryTests
{
    private static HaulingExecutionRig Rig(int maxRecoveries = 2) =>
        new HaulingExecutionRig(limits =>
        {
            limits.MaxRecoveryAttempts = maxRecoveries;
            limits.RecoveryBackoffSeconds = 1f;
            limits.RecoveryBackoffMaxSeconds = 3f;
        });

    [Fact]
    public void AStallRecoversByReplanningFromTheCartAndResumes()
    {
        HaulingExecutionRig rig = Rig();
        rig.RunToPulling();
        int plansBefore = rig.Planner.PlanCalls;
        rig.Monitor.Verdict = HaulMotion.Wedged;

        rig.StepOnce();
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        Assert.False(rig.Body.Facts.MotorCommanded);

        rig.Advance(0.9f);
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        Assert.Equal(plansBefore, rig.Planner.PlanCalls);

        rig.Advance(0.3f);
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        Assert.Equal(plansBefore + 1, rig.Planner.PlanCalls);
        Assert.True(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheRecoveryCeilingStopsAndReportsWedged()
    {
        HaulingExecutionRig rig = Rig(maxRecoveries: 2);
        rig.RunToPulling();

        for (int stall = 0; stall < 2; stall++)
        {
            rig.Monitor.Verdict = HaulMotion.Stalled;
            rig.StepOnce();
            Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
            rig.Advance(4f);
            Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        }

        // Progress in between does not refill the budget.
        rig.Monitor.Verdict = HaulMotion.Progressing;
        rig.Advance(5f);
        rig.Monitor.Verdict = HaulMotion.Stalled;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Stopping, rig.Executor.Phase);

        rig.Advance(rig.Limits.StillForSeconds + 0.1f);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.Wedged, rig.Executor.Attention);
        Assert.True(rig.Executor.Attached, "a wedged cart stays held until a person decides");
        Assert.Equal(3, rig.Executor.RecoveryFailures);

        int steers = rig.Body.SteerCommands;
        rig.Advance(60f);
        Assert.Equal(steers, rig.Body.SteerCommands);
        rig.AssertNoBugs();
    }

    [Fact]
    public void RecoveryBackoffDoublesAndIsCapped()
    {
        HaulingExecutionRig rig = Rig(maxRecoveries: 5);
        rig.RunToPulling();
        var waits = new List<float>();

        for (int stall = 0; stall < 4; stall++)
        {
            rig.Monitor.Verdict = HaulMotion.Stalled;
            rig.StepOnce();
            Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
            float started = rig.Clock.Now;
            while (rig.Executor.Phase == HaulPhase.Recovering)
            {
                rig.StepOnce();
            }

            waits.Add(rig.Clock.Now - started);
        }

        Assert.InRange(waits[0], 0.95f, 1.1f);
        Assert.InRange(waits[1], 1.95f, 2.1f);
        Assert.InRange(waits[2], 2.95f, 3.1f);
        Assert.InRange(waits[3], 2.95f, 3.1f);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARefusedReplanStopsThenReportsTheRouteReason()
    {
        HaulingExecutionRig rig = Rig();
        rig.RunToPulling();
        rig.Planner.GoalsValid = false;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);

        rig.Planner.NextVerdict = CartRouteVerdict.TooNarrow;
        while (rig.Executor.Phase == HaulPhase.Recovering)
        {
            rig.StepOnce();
        }

        Assert.Equal(HaulPhase.Stopping, rig.Executor.Phase);
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.TooNarrow, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AStrainingHitchStopsThePullBeforeItSnaps()
    {
        HaulingExecutionRig rig = Rig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.JointForceNewtons = 8500f);

        rig.StepOnce();

        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        Assert.False(rig.Body.Facts.MotorCommanded);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnOutOfBudgetPlannerCountsAgainstTheCeiling()
    {
        HaulingExecutionRig rig = Rig(maxRecoveries: 1);
        rig.RunToPulling();
        rig.Monitor.Verdict = HaulMotion.Stalled;
        rig.StepOnce();
        rig.Planner.NextVerdict = CartRouteVerdict.BudgetExhausted;

        rig.Advance(10f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.Wedged, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ACartThatWillNotComeToRestIsReportedAtTheStoppingDeadline()
    {
        var rig = new HaulingExecutionRig(tuneExecution: execution => execution.StoppingTimeoutSeconds = 5f);
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0.8f);
        rig.ArriveAtTarget();
        rig.StepOnce();
        Assert.Equal(HaulPhase.Stopping, rig.Executor.Phase);

        rig.Advance(4.5f);
        Assert.Equal(HaulPhase.Stopping, rig.Executor.Phase);
        rig.Advance(1f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ANewLegHasAFreshRecoveryBudget()
    {
        HaulingExecutionRig rig = Rig(maxRecoveries: 1);
        rig.RunToPulling();
        rig.Monitor.Verdict = HaulMotion.Stalled;
        rig.StepOnce();
        rig.Advance(2f);
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        Assert.Equal(1, rig.Executor.RecoveryFailures);

        rig.ArriveAtTarget();
        rig.StepOnce();
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);
        Assert.Equal(HaulPhase.Waiting, rig.Executor.Phase);

        Assert.Equal(
            HaulCommandOutcome.Accepted,
            rig.Executor.RequestLeg(new HaulLegRequest("haul-1", string.Empty, "lease-1", true, new WorkPoint(0f, 0f, 55f), 2f), rig.Executor.Revision).Outcome);
        Assert.Equal(0, rig.Executor.RecoveryFailures);
        rig.AssertNoBugs();
    }
}
