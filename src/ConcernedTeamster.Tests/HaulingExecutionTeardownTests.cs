using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-06 and DECISIONS.md D4: detach comes first on every
/// teardown path, with the identity in Recovering; a body is retired only
/// while resting and never before the joint is released; a cart is let go only
/// on suitable ground, and a cart that rolls away is reported.</summary>
public class HaulingExecutionTeardownTests
{
    [Fact]
    public void DetachAlwaysPrecedesBodyRetirement()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();

        // A haul holds Gunnar: retirement is refused and nothing is released.
        Assert.Equal(BodyRetirementOutcome.RefusedBusy, rig.Executor.RetireBody());
        Assert.DoesNotContain("Retire", rig.Journal);
        Assert.Equal(0, rig.Seam.ReleaseCalls);

        rig.Executor.Teardown("the player is retiring Gunnar", LeaseInvalidation.PlayerRevoked);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.Equal(ActorMode.Recovering, rig.Seam.ModeAtLastRelease);

        // Still held by the unanswered haul until a person resolves it.
        Assert.Equal(BodyRetirementOutcome.RefusedBusy, rig.Executor.RetireBody());
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);

        Assert.Equal(BodyRetirementOutcome.Retired, rig.Executor.RetireBody());
        Assert.True(rig.IndexOf("ReleaseJoint") < rig.IndexOf("Retire"));
        rig.AssertNoBugs();
    }

    [Fact]
    public void AWorldUnloadReleasesTheJointBeforeTheLeaseEnds()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        CartLease lease = rig.Executor.ActiveLease!;

        rig.ReloadWorld();

        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.Equal(ActorMode.Recovering, rig.Seam.ModeAtLastRelease);
        Assert.Equal(CartLeaseState.Invalidated, lease.State);
        Assert.Equal(LeaseInvalidation.WorldReloaded, lease.Invalidation);

        // The new world starts with nothing assigned and refuses the old key.
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Null(rig.Executor.ActiveLease);
        CartAssignmentFacts stale = HaulingExecutionRig.SelectableCart();
        stale.SelectedInThisWorldLoad = false;
        Assert.Equal(
            CartAssignmentRefusal.StaleIdentity,
            rig.Executor.AssignCart("lease-2", new CartKey("-100:7", HaulingExecutionRig.Epoch), HaulingExecutionRig.SelectableCart()).Refusal);
        Assert.Equal(CartAssignmentRefusal.StaleIdentity, rig.Executor.AssignCart("lease-2", rig.Cart, stale).Refusal);
        rig.AssertNoBugs();
    }

    [Fact]
    public void EveryLossOfControlStopsTheMotorAndEntersRecoveringBeforeReleasing()
    {
        // Lost authority is not one of these any more: C4 §2.7 makes it a stop
        // with the cart still held (HaulingExecutionHoldTests).
        Action<HaulingExecutionRig>[] losses =
        {
            r => r.Body.Facts = r.Body.Facts.With(f => f.Present = false),
            r => r.Seam.Observation = r.Seam.Observation.With(o => o.BrakeEngaged = true),
            r => r.Seam.Observation = r.Seam.Observation.With(o => o.IsOwner = false),
            r => r.Seam.Observation = r.Seam.Observation.With(o => { o.Resolved = false; o.RecordExists = true; }),
        };

        foreach (Action<HaulingExecutionRig> lose in losses)
        {
            var rig = new HaulingExecutionRig();
            rig.RunToPulling();
            rig.Advance(0.2f);
            rig.Journal.Clear();

            lose(rig);
            rig.StepOnce();

            Assert.True(rig.IndexOf("Stop") < rig.IndexOf("ReleaseJoint"), string.Join(", ", rig.Journal));
            Assert.Equal(ActorMode.Recovering, rig.Seam.ModeAtLastRelease);
            Assert.False(rig.Executor.Attached);
            Assert.True(
                rig.Executor.Phase == HaulPhase.NeedsAttention || rig.Executor.Phase == HaulPhase.Paused,
                rig.Executor.Phase.ToString());
            rig.AssertNoBugs();
        }
    }

    [Fact]
    public void UnsafeGroundRefusesToLetGoAndKeepsTheCartHeld()
    {
        ParkingGround[] unsafeGrounds =
        {
            new ParkingGround { Measured = false },
            new ParkingGround { Measured = true, InWater = true },
            new ParkingGround { Measured = true, GradeAlongRatio = 0.06f },
            new ParkingGround { Measured = true, GradeAcrossRatio = -0.07f },
            new ParkingGround { Measured = true, GradeAlongRatio = float.NaN },
        };

        foreach (ParkingGround ground in unsafeGrounds)
        {
            var rig = new HaulingExecutionRig();
            rig.RunToWaiting();
            rig.Seam.Ground = ground;

            Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);

            Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
            Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
            Assert.True(rig.Executor.Attached);
            Assert.Equal(0, rig.Seam.ReleaseCalls);

            // C4 §3.2: he has already stopped, so "stop and wait" is accepted
            // and changes nothing at all - the reason and the cart stay put.
            int revision = rig.Executor.Revision;
            Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: false).Outcome);
            Assert.Equal(revision, rig.Executor.Revision);
            Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
            Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
            Assert.Equal(HaulStopIntent.Unspecified, rig.Executor.PendingStop);
            Assert.True(rig.Executor.Attached);
            rig.AssertNoBugs();
        }
    }

    [Fact]
    public void AMovingCartIsNotLetGo()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 0.4f);
        rig.StepOnce();

        rig.Executor.Cancel("haul-1", detachAndPark: true);

        Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ACartThatRollsAwayAfterReleaseIsReported()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        rig.Executor.Cancel("haul-1", detachAndPark: true);
        Assert.Equal(HaulPhase.Detaching, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.ReleaseCalls);

        rig.Seam.Observation = rig.Seam.Observation.With(o => o.SpeedMetresPerSecond = 1.2f);
        rig.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        Assert.Equal(1, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ReleasingTheLeaseWhileHitchedDetachesFirstAndThenUnassigns()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();

        Assert.Equal(LeaseReleaseOutcome.Pending, rig.Executor.ReleaseLease());
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.NotNull(rig.Executor.ActiveLease);

        rig.Advance(rig.Execution.DetachSettleSeconds + 0.2f);

        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Null(rig.Executor.ActiveLease);
        Assert.Equal(ActorMode.Resting, rig.Executor.Modes.Mode);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ReleasingTheLeaseMidPullStopsParksDetachesAndUnassigns()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        Assert.Equal(LeaseReleaseOutcome.Pending, rig.Executor.ReleaseLease());
        Assert.Equal(HaulPhase.Stopping, rig.Executor.Phase);
        rig.Advance(rig.Limits.StillForSeconds + rig.Execution.DetachSettleSeconds + 0.5f);

        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        Assert.True(rig.IndexOf("ReadGround") < rig.IndexOf("ReleaseJoint"));
        rig.AssertNoBugs();
    }

    [Fact]
    public void ReleasingAnIdleLeaseIsImmediate()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign();
        Assert.Equal(LeaseReleaseOutcome.Released, rig.Executor.ReleaseLease());
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Equal(LeaseReleaseOutcome.NoLease, rig.Executor.ReleaseLease());
        Assert.Equal(0, rig.Seam.ReleaseCalls);

        var approaching = new HaulingExecutionRig();
        approaching.AssignAndStartLeg();
        Assert.Equal(LeaseReleaseOutcome.Released, approaching.Executor.ReleaseLease());
        Assert.Equal(HaulPhase.Unassigned, approaching.Executor.Phase);
        Assert.Equal(string.Empty, approaching.Executor.HaulId);
        rig.AssertNoBugs();
        approaching.AssertNoBugs();
    }

    [Fact]
    public void ACancelDuringUnloadingWaitsForDoneThenParks()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();
        rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: true);

        HaulCommandResult cancel = rig.Executor.Cancel("haul-1", detachAndPark: true);
        Assert.Equal(HaulCommandOutcome.Accepted, cancel.Outcome);
        Assert.Equal(HaulCommandDetail.Pending, rig.Executor.LastCommandDetail);
        rig.Advance(2f);
        Assert.Equal(HaulPhase.Unloading, rig.Executor.Phase);
        Assert.Equal(0, rig.Seam.ReleaseCalls);

        rig.Executor.AcknowledgeWait("haul-1", rig.Executor.Revision, transferring: false);
        Assert.Equal(HaulPhase.Detaching, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        rig.Advance(rig.Execution.DetachSettleSeconds + 0.2f);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ReleaseNeverReportsAJointItDidNotHold()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToWaiting();

        // Someone else took the joint between the frame's signals and the detach.
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.JointConnectedToPuller = false);
        rig.Executor.Cancel("haul-1", detachAndPark: true);

        Assert.False(rig.Executor.Attached);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.PlayerTookOver, rig.Executor.Attention);
        rig.AssertNoBugs();
    }
}
