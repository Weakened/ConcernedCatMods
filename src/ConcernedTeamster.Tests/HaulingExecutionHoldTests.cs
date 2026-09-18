using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Contract revision C4: the lost-authority stop (§2.7), the unload
/// hold's liveness (§3.2) and "stop and wait" on a haul that has already
/// stopped. Lost authority is not a teardown: Gunnar stops at once and lets go
/// only where the cart can be left, because CART-06 forbids releasing a loaded
/// cart into a roll.</summary>
public sealed class HaulingExecutionHoldTests
{
    private static readonly ParkingGround Slope =
        new ParkingGround { Measured = true, GradeAlongRatio = 0.2f, GradeAcrossRatio = 0.01f };

    private static readonly ParkingGround Level =
        new ParkingGround { Measured = true, GradeAlongRatio = 0.01f, GradeAcrossRatio = 0.01f };

    [Fact]
    public void LostAuthorityStopsAtOnceAndKeepsTheCartOnGroundItWouldRollDown()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Ground = Slope;

        rig.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        rig.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.AuthorityLost, rig.Executor.Attention);
        Assert.True(rig.Executor.Attached);
        Assert.True(rig.Executor.HoldingCart);
        Assert.Equal(HaulAttentionReason.AuthorityLost, rig.Executor.HoldingCartBecause);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        Assert.False(rig.Body.Facts.MotorCommanded);

        // However long it lasts, he holds it and accepts no leg.
        int steers = rig.Body.SteerCommands;
        rig.Advance(5f);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        Assert.True(rig.Executor.Attached);
        Assert.Equal(steers, rig.Body.SteerCommands);
        Assert.Equal(HaulCommandOutcome.Unavailable, rig.Go().Outcome);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheHoldEndsWhenTheCartCanBeLeftAndKeepsItsPhaseAndReason()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Ground = Slope;
        rig.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        rig.StepOnce();
        rig.Advance(rig.Limits.StillForSeconds + HaulingExecutionRig.Step);
        Assert.True(rig.Executor.Attached);

        // Someone levelled the ground under it (or he was stopped on a shelf of
        // it all along): the cart goes down, and nothing else changes.
        rig.Seam.Ground = Level;
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        Assert.False(rig.Executor.HoldingCart);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.AuthorityLost, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void NothingResumesWhenAuthorityComesBackWhileHeIsHolding()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Ground = Slope;
        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.True(rig.Executor.HoldingCart);

        rig.Authority.Verdict = WorkAuthorityVerdict.Granted;
        int steers = rig.Body.SteerCommands;
        rig.Advance(2f);

        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.OtherPeersConnected, rig.Executor.Attention);
        Assert.True(rig.Executor.Attached);
        Assert.True(rig.Executor.HoldingCart);
        Assert.Equal(steers, rig.Body.SteerCommands);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ThePlayerTakingTheCartEndsTheHold()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Ground = Slope;
        rig.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        rig.StepOnce();
        Assert.True(rig.Executor.HoldingCart);

        // Vanilla's own Use detaches it.
        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.HasJoint = false;
            o.JointConnectedToPuller = false;
            o.LocalPlayerHasJoint = true;
        });
        rig.StepOnce();

        Assert.False(rig.Executor.HoldingCart);
        Assert.False(rig.Executor.Attached);
        Assert.Equal(HaulAttentionReason.PlayerTookOver, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnUnloadingHoldEndsAtOnceWhenAuthorityIsLost()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        Assert.Equal(
            HaulCommandOutcome.Accepted,
            rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true).Outcome);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);

        int revision = rig.Executor.Revision;
        rig.Seam.Ground = Slope;
        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();

        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.OtherPeersConnected, rig.Executor.Attention);
        Assert.True(rig.Executor.Revision > revision);
        Assert.True(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnUnloadHoldEndsWhenTheConsumerGoesQuiet()
    {
        var rig = new HaulingExecutionRig(tuneLimits: l => l.RendezvousTimeoutSeconds = 10f);
        rig.RunToWaiting();
        rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true);
        int revision = rig.Executor.Revision;

        rig.Advance(9f);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);

        rig.Advance(1.5f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.RendezvousTimedOut, rig.Executor.Attention);
        Assert.True(rig.Executor.Revision > revision);
        Assert.True(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AConsumerThatKeepsSayingItIsThereKeepsItsHold()
    {
        var rig = new HaulingExecutionRig(tuneLimits: l => l.RendezvousTimeoutSeconds = 10f);
        rig.RunToWaiting();
        rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true);

        for (int round = 0; round < 6; round++)
        {
            rig.Advance(3f);
            Assert.True(rig.Executor.TouchUnloadHold("haul-1"));
        }

        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);
        Assert.False(rig.Executor.TouchUnloadHold("another-haul"));
        rig.AssertNoBugs();
    }

    [Fact]
    public void ACancelSentDuringUnloadingCompletesAtTheDeadline()
    {
        var rig = new HaulingExecutionRig(tuneLimits: l => l.RendezvousTimeoutSeconds = 10f);
        rig.RunToWaiting();
        rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true);

        HaulCommandResult cancel = rig.Executor.Cancel("haul-1", detachAndPark: true);
        Assert.Equal(HaulCommandOutcome.Accepted, cancel.Outcome);
        Assert.Equal(HaulCommandDetail.Pending, rig.Executor.LastCommandDetail);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);

        // The consumer never says Done; the hold's own deadline finishes it.
        rig.Advance(11f);

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        rig.Advance(rig.Execution.DetachSettleSeconds + HaulingExecutionRig.Step);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void StopAndWaitOnAHaulThatHasAlreadyStoppedChangesNothing()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();

        int revision = rig.Executor.Revision;
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: false).Outcome);
        Assert.Equal(revision, rig.Executor.Revision);
        Assert.Equal(HaulPhase.Waiting, rig.Executor.Phase);
        Assert.Equal(HaulStopIntent.Unspecified, rig.Executor.PendingStop);
        Assert.True(rig.Executor.Attached);

        // The same while paused: nothing resumes, nothing detaches.
        rig.Seam.Ground = Slope;
        rig.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        rig.StepOnce();
        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);

        revision = rig.Executor.Revision;
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: false).Outcome);
        Assert.Equal(revision, rig.Executor.Revision);
        Assert.Equal(HaulPhase.Paused, rig.Executor.Phase);
        Assert.Equal(HaulStopIntent.Unspecified, rig.Executor.PendingStop);
        rig.AssertNoBugs();
    }
}
