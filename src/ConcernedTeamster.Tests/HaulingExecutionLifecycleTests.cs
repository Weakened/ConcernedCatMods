using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-03, CART-04, CART-06: the executor drives the contract's
/// phase table (CONTRACTS.md §2.1) and nothing else. Every scenario here also
/// asserts that no illegal transition was ever attempted.</summary>
public class HaulingExecutionLifecycleTests
{
    [Fact]
    public void AStandaloneLegRunsUnassignedReadyApproachHitchPullStopWaitDetachReady()
    {
        var rig = new HaulingExecutionRig();
        var phases = new List<HaulPhase> { rig.Executor.Phase };
        void Record()
        {
            if (phases[phases.Count - 1] != rig.Executor.Phase)
            {
                phases.Add(rig.Executor.Phase);
            }
        }

        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        Record();
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Go().Outcome);
        Record();
        rig.Advance(0.5f);
        Record();
        Assert.True(rig.Body.WalkCommands > 0, "Gunnar walks to the handle through the motor");

        rig.PutBodyAtHandle();
        rig.StepOnce();
        Record();
        Assert.True(rig.Executor.Attached);
        rig.Advance(0.5f);
        Assert.True(rig.Body.SteerCommands > 0, "he steers along the vetted segment");

        rig.ArriveAtTarget();
        rig.StepOnce();
        Record();
        rig.Advance(rig.Limits.StillForSeconds + 0.1f);
        Record();

        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Record();
        rig.Advance(rig.Execution.DetachSettleSeconds + 0.1f);
        Record();

        Assert.Equal(
            new[]
            {
                HaulPhase.Unassigned, HaulPhase.Ready, HaulPhase.Approaching, HaulPhase.Hitching, HaulPhase.Pulling,
                HaulPhase.Stopping, HaulPhase.Waiting, HaulPhase.Detaching, HaulPhase.Ready,
            }.Where(phase => phase != HaulPhase.Hitching),
            phases);
        Assert.False(rig.Executor.Attached);
        Assert.Equal(string.Empty, rig.Executor.HaulId);
        Assert.NotNull(rig.Executor.ActiveLease);
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        rig.AssertNoBugs();
    }

    [Fact]
    public void EveryTransitionOutsideTheTableIsRefusedAndReportedAsABug()
    {
        var log = new FakeLog();
        foreach (HaulPhase from in HaulPhases.All())
        {
            foreach (HaulPhase to in HaulPhases.All())
            {
                Assert.Equal(HaulPhases.CanTransition(from, to), HaulTransitionGuard.Allows(from, to, log));
            }
        }

        int illegal = HaulPhases.All().Count() * HaulPhases.All().Count() -
            HaulPhases.All().Sum(from => HaulPhases.All().Count(to => HaulPhases.CanTransition(from, to)));
        Assert.Equal(illegal, log.Bugs.Count);
        Assert.Contains("Illegal haul transition Unloading -> Pulling refused.", log.Bugs);
    }

    [Fact]
    public void HitchingHappensInTheSameTickAsArrivalAndOnlyOnce()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.AttachCalls);
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);

        // A repeated request for the same haul while hitched and pulling is busy:
        // no second joint, no second lease, no second puller.
        HaulCommandResult again = rig.Executor.RequestLeg(
            new HaulLegRequest("haul-1", string.Empty, "lease-1", true, HaulingExecutionRig.Target, 2f), rig.Executor.Revision);
        Assert.Equal(HaulCommandOutcome.Rejected, again.Outcome);
        Assert.Equal(HaulCommandDetail.HaulBusy, rig.Executor.LastCommandDetail);
        Assert.Equal(AssignmentOutcome.AlreadyAssigned, rig.Assign("lease-2").Outcome);
        rig.Advance(2f);
        Assert.Equal(1, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void GunnarAlignsWithTheCartBeforeHitching()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.HitchDistanceMetres = 0.5f);
        rig.Body.Facts = rig.Body.Facts.With(f =>
        {
            f.ForwardX = 0f;
            f.ForwardZ = -1f;
        });

        rig.StepOnce();
        Assert.Equal(HaulPhase.Approaching, rig.Executor.Phase);
        Assert.Contains("Face", rig.Journal);
        Assert.Equal(0, rig.Seam.AttachCalls);

        // The fake body turns at once; the next tick hitches.
        rig.StepOnce();
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ApproachEndsInApproachTimedOutAtTheDeadline()
    {
        var rig = new HaulingExecutionRig(limits => limits.ApproachTimeoutSeconds = 10f);
        rig.AssignAndStartLeg();

        rig.Advance(9.5f);
        Assert.Equal(HaulPhase.Approaching, rig.Executor.Phase);
        rig.Advance(1f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.ApproachTimedOut, rig.Executor.Attention);
        Assert.Equal(0, rig.Seam.AttachCalls);
        Assert.False(rig.Body.Facts.MotorCommanded);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnApproachWithNoPathGivesUpAfterTheBoundedRecoveriesWithNoRoute()
    {
        var rig = new HaulingExecutionRig(limits =>
        {
            limits.MaxRecoveryAttempts = 2;
            limits.RecoveryBackoffSeconds = 1f;
            limits.RecoveryBackoffMaxSeconds = 2f;
            limits.ApproachTimeoutSeconds = 600f;
        });
        rig.AssignAndStartLeg();
        rig.Body.Facts = rig.Body.Facts.With(f => f.HasPath = false);

        // Grace 5 s per attempt, three failures allowed to happen (two recoveries).
        rig.Advance(5.2f);
        Assert.Equal(HaulPhase.Approaching, rig.Executor.Phase);
        rig.Advance(40f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.NoRoute, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void UnloadingForbidsEveryMotionAndEveryLegUntilDone()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true).Outcome);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);

        int steersBefore = rig.Body.SteerCommands;
        int walksBefore = rig.Body.WalkCommands;
        HaulCommandResult leg = rig.Executor.RequestLeg(
            new HaulLegRequest("haul-1", string.Empty, "lease-1", true, new WorkPoint(0f, 0f, 50f), 2f), rig.Executor.Revision);
        Assert.Equal(HaulCommandOutcome.Rejected, leg.Outcome);
        Assert.Equal(HaulCommandDetail.MotionForbidden, rig.Executor.LastCommandDetail);
        rig.Advance(3f);
        Assert.Equal(steersBefore, rig.Body.SteerCommands);
        Assert.Equal(walksBefore, rig.Body.WalkCommands);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);

        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: false).Outcome);
        Assert.Equal(HaulPhase.Waiting, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ALegFromWaitingPullsOnWithoutASecondHitch()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        rig.Body.Facts = rig.Body.Facts.With(f => f.Position = new WorkPoint(0f, 0f, 30f));

        HaulCommandResult next = rig.Executor.RequestLeg(
            new HaulLegRequest("haul-1", string.Empty, "lease-1", true, new WorkPoint(0f, 0f, 50f), 2f), rig.Executor.Revision);

        Assert.Equal(HaulCommandOutcome.Accepted, next.Outcome);
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void APeerConnectingLetsGoPausesKeepsTheLeaseAndReadiesWhenAlone()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();

        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.OtherPeersConnected, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.NotNull(rig.Executor.ActiveLease);
        Assert.Equal("haul-1", rig.Executor.HaulId);

        // Nothing moves while paused.
        int steers = rig.Body.SteerCommands;
        rig.Advance(2f);
        Assert.Equal(steers, rig.Body.SteerCommands);

        rig.Authority.Verdict = WorkAuthorityVerdict.Granted;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Go().Outcome);
        rig.AssertNoBugs();
    }

    [Fact]
    public void WirePhasesStillMirrorTheExecutedPhases()
    {
        foreach (HaulPhase phase in HaulPhases.All())
        {
            Assert.True(Enum.TryParse(phase.ToString(), out HaulWirePhase _));
        }
    }

    [Fact]
    public void RecoveringTheActorModesFollowThePhases()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        rig.AssignAndStartLeg();
        Assert.Equal(ActorMode.Working, rig.Executor.Modes.Mode);
        Assert.True(rig.Executor.Modes.IsHeldBy("haul-1"));
        Assert.False(rig.Executor.Modes.MayRetireBody);

        rig.PutBodyAtHandle();
        rig.StepOnce();
        rig.Monitor.Verdict = HaulMotion.Stalled;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        Assert.Equal(ActorMode.Recovering, rig.Executor.Modes.Mode);
        rig.AssertNoBugs();
    }
}
