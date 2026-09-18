using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 AUTH-01 and CART-06 before the hitch: authority, peers and the
/// body are re-checked every frame while Gunnar walks to the cart and between
/// hitch attempts, not only while he pulls; and every way out of those phases
/// follows the phase table.</summary>
public class HaulingExecutionAuthorityTests
{
    [Fact]
    public void AuthorityLostWhileApproachingStopsEndsTheLeaseAndAsksAPerson()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.Advance(0.5f);
        CartLease lease = rig.Executor.ActiveLease!;

        rig.Authority.Verdict = WorkAuthorityVerdict.NotHost;
        rig.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.AuthorityLost, rig.Executor.Attention);
        Assert.Equal(LeaseInvalidation.AuthorityLost, lease.Invalidation);
        Assert.False(rig.Body.Facts.MotorCommanded);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        int walks = rig.Body.WalkCommands;
        rig.Advance(2f);
        Assert.Equal(walks, rig.Body.WalkCommands);
        rig.AssertNoBugs();
    }

    [Fact]
    public void NoMotorStepIsTakenWithoutAuthorityEvenBeforeTheFrameReactsToIt()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        int walks = rig.Body.WalkCommands;

        // The worker tick runs before any frame has seen the change.
        rig.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        rig.Clock.Now += HaulingExecutionRig.Step;
        rig.Executor.Tick();

        Assert.Equal(walks, rig.Body.WalkCommands);
        Assert.Equal(0, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void APeerConnectingWhileApproachingPausesAndKeepsTheLease()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();

        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();

        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.OtherPeersConnected, rig.Executor.Attention);
        Assert.NotNull(rig.Executor.ActiveLease);
        Assert.Equal(ActorMode.Paused, rig.Executor.Modes.Mode);

        rig.Authority.Verdict = WorkAuthorityVerdict.Granted;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal("haul-1", rig.Executor.HaulId);
        rig.AssertNoBugs();
    }

    [Fact]
    public void APeerConnectingBetweenHitchAttemptsGoesBackThroughApproachingToPaused()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.AttachWorks = false;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);

        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();

        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void GunnarsBodyLostWhileApproachingEndsTheLease()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        CartLease lease = rig.Executor.ActiveLease!;

        rig.Body.Facts = rig.Body.Facts.With(f => f.Present = false);
        rig.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, rig.Executor.Attention);
        Assert.Equal(LeaseInvalidation.WorkerBodyLost, lease.Invalidation);
        rig.AssertNoBugs();
    }

    [Fact]
    public void CancellingBetweenHitchAttemptsDetachesNothingAndReturnsToReady()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.AttachWorks = false;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);

        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: false).Outcome);

        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal(string.Empty, rig.Executor.HaulId);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        Assert.NotNull(rig.Executor.ActiveLease);
        rig.AssertNoBugs();
    }

    [Fact]
    public void CancellingAPausedUnhitchedHaulReturnsToReady()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);

        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);

        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnAssignmentIsRefusedWhileAnyoneElseIsConnected()
    {
        var rig = new HaulingExecutionRig();
        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;

        AssignmentVerdict verdict = rig.Assign();

        Assert.Equal(CartAssignmentRefusal.NoAuthority, verdict.Refusal);
        Assert.Null(rig.Executor.ActiveLease);
    }
}
