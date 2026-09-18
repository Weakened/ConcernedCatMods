using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Custody of the joint itself (review R-313 B1 and M1): it is
/// released through the cart it is on and never through the lease, a release
/// that does not take is retried rather than forgotten, and nothing walks,
/// unbinds or retires while a cart still holds Gunnar's body.</summary>
public sealed class HaulingExecutionCustodyTests
{
    [Fact]
    public void ALeaseThatEndsWhileHitchedStillLetsGoOfTheCart()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        // The lease ends under him (a seam fault answered "unloaded", a player
        // release, anything): the joint is still on the cart.
        Assert.Equal(LeaseOutcome.Invalidated, rig.Executor.Leases.Invalidate("lease-1", LeaseInvalidation.CartUnloaded));
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        Assert.Null(rig.Executor.AttachedCart);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.LeaseInvalidated, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AReleaseThatDoesNotTakeIsRetriedAndHoldsEverythingElse()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.ReleaseFails = true;

        rig.Body.Facts = rig.Body.Facts.With(f => f.Present = false);
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.True(rig.Executor.Attached);
        Assert.True(rig.Executor.StillHolding);
        Assert.Equal(BodyRetirementOutcome.RefusedBusy, rig.Executor.RetireBody());

        // Retried every frame, and nothing else progresses meanwhile.
        rig.StepOnce();
        Assert.Equal(2, rig.Seam.ReleaseCalls);
        Assert.True(rig.Executor.StillHolding);

        rig.Seam.ReleaseFails = false;
        rig.StepOnce();
        Assert.Equal(3, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.StillHolding);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnUnboundBodyStillOwnsItsJoint()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        // The runtime lost the binding in the same frame the body died: the
        // seam remembers the body its attach connected, so the joint is still
        // Gunnar's to release.
        rig.Seam.BodyBound = false;
        rig.Body.Facts = rig.Body.Facts.With(f => f.Dead = true);
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        Assert.False(rig.Executor.StillHolding);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ALostBodyStopsTheMotorAndLetsGoInTheSameFrame()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.StepOnce();
        Assert.True(rig.Body.Facts.MotorCommanded);
        rig.Journal.Clear();

        rig.Body.Facts = rig.Body.Facts.With(f => f.Present = false);
        rig.StepOnce();

        Assert.True(rig.IndexOf("Stop") < rig.IndexOf("ReleaseJoint"), string.Join(", ", rig.Journal));
        Assert.False(rig.Body.Facts.MotorCommanded);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ADuplicateBodyWhileHitchedIsATeardownPath()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        rig.Body.Facts = rig.Body.Facts.With(f => f.Duplicated = true);
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        Assert.Equal(HaulAttentionReason.WorkerBodyDuplicated, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void NoLeaseIsNoLicenceToKeepWalking()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.StepOnce();
        Assert.True(rig.Body.Facts.MotorCommanded);

        Assert.Equal(LeaseOutcome.Invalidated, rig.Executor.Leases.Invalidate("lease-1", LeaseInvalidation.CartUnloaded));
        rig.Clock.Now += HaulingExecutionRig.Step;
        rig.Executor.Tick();

        // The worker tick alone, with no lease: he stops where he stands.
        Assert.False(rig.Body.Facts.MotorCommanded);
        Assert.Equal(0, rig.Seam.ReleaseCalls);

        // The joint is the frame observer's business, and it lets go.
        rig.StepOnce();
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ASeamFaultIsNotADestroyedCart()
    {
        // Hitched: control ends, the joint goes, the lease does not.
        var hitched = new HaulingExecutionRig();
        hitched.RunToPulling();
        hitched.Seam.Observation = hitched.Seam.Observation.With(o =>
        {
            o.CapabilityOk = false;
            o.Resolved = false;
            o.RecordExists = true;
        });
        hitched.StepOnce();

        Assert.Equal(HaulAttentionReason.HitchFailed, hitched.Executor.Attention);
        Assert.NotNull(hitched.Executor.ActiveLease);
        Assert.False(hitched.Executor.Attached);
        hitched.AssertNoBugs();

        // Walking to the cart: the same, and nothing is reported as destroyed.
        var walking = new HaulingExecutionRig();
        walking.AssignAndStartLeg();
        walking.Seam.Observation = walking.Seam.Observation.With(o =>
        {
            o.CapabilityOk = false;
            o.Resolved = false;
            o.RecordExists = true;
        });
        walking.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, walking.Executor.Phase);
        Assert.Equal(HaulAttentionReason.HitchFailed, walking.Executor.Attention);
        Assert.NotNull(walking.Executor.ActiveLease);
        walking.AssertNoBugs();
    }

    [Fact]
    public void AJointConnectedToNothingIsLetGo()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.JointConnectedToPuller = false;
            o.JointConnectedToNothing = true;
        });
        rig.StepOnce();

        Assert.Equal(HaulAttentionReason.JointBroke, rig.Executor.Attention);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AHitchThatStrainedToItsBreakForceIsReportedAsABrokenHitch()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        // The last frame Gunnar held it, the joint was at its break force; the
        // player happened to be looking at the cart when it let go.
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.JointForceNewtons = 9500f);
        rig.StepOnce();
        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.HasJoint = false;
            o.JointConnectedToPuller = false;
            o.LocalPlayerHoveringCart = true;
        });
        rig.StepOnce();

        Assert.Equal(HaulAttentionReason.JointBroke, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheHitchDecidesFromItsOwnFramesReadNotTheLastOne()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();

        // The signals read at the top of the frame say the cart is free; the
        // read D4 makes its decision from says the brake went on.
        CartObservation free = rig.Seam.Observation;
        CartObservation braked = free.With(o => o.BrakeEngaged = true);
        int baseline = rig.Seam.ObserveCalls;
        rig.Seam.ObserveSequence = call => call <= baseline + 1 ? free : braked;

        rig.StepOnce();

        Assert.Equal(0, rig.Seam.AttachCalls);
        Assert.Equal(HaulAttentionReason.BrakeEngaged, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARollingCartIsNotHitchedUntilItStands()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0.4f);

        rig.StepOnce();

        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);
        Assert.Equal(0, rig.Seam.AttachCalls);
        Assert.Equal(0, rig.Executor.HitchFailures);

        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0f);
        rig.StepOnce();

        Assert.Equal(1, rig.Seam.AttachCalls);
        Assert.True(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARetryTurnsBackToTheCartBeforeItAttachesAgain()
    {
        var rig = new HaulingExecutionRig();
        rig.Seam.AttachWorks = false;
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.StepOnce();

        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.AttachCalls);

        // Something knocked him round while he waited for the next attempt.
        rig.Seam.AttachWorks = true;
        rig.Body.Facts = rig.Body.Facts.With(f =>
        {
            f.ForwardX = 1f;
            f.ForwardZ = 0f;
        });
        rig.Journal.Clear();
        rig.Advance(rig.Limits.RecoveryBackoffSeconds + HaulingExecutionRig.Step);

        // He turned back to the cart's heading first, and only then attached.
        Assert.True(rig.IndexOf("Face") < rig.IndexOf("AttachAndVerify"), string.Join(", ", rig.Journal));
        Assert.True(rig.Executor.Attached);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheDetachSettleNeedsNoWorkerTick()
    {
        var rig = new HaulingExecutionRig(tuneExecution: e => e.WorkerTickStaleSeconds = 10f);
        rig.RunToWaiting();
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulPhase.Detaching, rig.Executor.Phase);
        Assert.False(rig.Executor.Attached);

        // Not one worker tick from here: the settle still runs out (C4 §7).
        rig.AdvanceFramesOnly(rig.Execution.DetachSettleSeconds + (2f * HaulingExecutionRig.Step));

        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        Assert.Equal(string.Empty, rig.Executor.HaulId);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ADetachWhoseBodyStoppedTickingEndsWithThatReason()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulPhase.Detaching, rig.Executor.Phase);

        rig.AdvanceFramesOnly(rig.Execution.WorkerTickStaleSeconds + (2f * HaulingExecutionRig.Step));

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, rig.Executor.Attention);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARollAfterTheReleaseIsUnsafeParkingWhateverIsTicking()
    {
        var rig = new HaulingExecutionRig(tuneExecution: e => e.WorkerTickStaleSeconds = 10f);
        rig.RunToWaiting();
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulPhase.Detaching, rig.Executor.Phase);

        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 1.2f);
        rig.AdvanceFramesOnly(2f * HaulingExecutionRig.Step);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
        rig.AssertNoBugs();
    }
}
