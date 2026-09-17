using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-01 and DECISIONS.md D6: a lease is revalidated before
/// every leg, ends exactly on a lost cart, ownership, authority, body or world
/// load, survives what a person can answer (a pause, a takeover, a broken
/// hitch), and a lease that ended is never silently replaced.</summary>
public class HaulingExecutionLeaseTests
{
    [Fact]
    public void ACartDestroyedWhileIdleEndsTheLeaseAndUnassigns()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign();
        CartLease lease = rig.Executor.ActiveLease!;

        rig.Seam.Observation = rig.Seam.Observation.With(o => { o.Resolved = false; o.RecordExists = false; });
        rig.StepOnce();

        Assert.Equal(CartLeaseState.Invalidated, lease.State);
        Assert.Equal(LeaseInvalidation.CartDestroyed, lease.Invalidation);
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ACartUnloadedMidHaulEndsTheLeaseAndNeedsAttention()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        CartLease lease = rig.Executor.ActiveLease!;

        rig.Seam.Observation = rig.Seam.Observation.With(o => { o.Resolved = false; o.RecordExists = true; });
        rig.StepOnce();

        Assert.Equal(LeaseInvalidation.CartUnloaded, lease.Invalidation);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.CartUnloaded, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);

        // A person resolves it; with no lease left it returns to Unassigned.
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AuthorityDisabledEndsTheLeaseButAPeerOnlyPausesIt()
    {
        var disabled = new HaulingExecutionRig();
        disabled.Assign();
        CartLease lease = disabled.Executor.ActiveLease!;
        disabled.Authority.Verdict = WorkAuthorityVerdict.RuntimeDisabled;
        disabled.StepOnce();
        Assert.Equal(LeaseInvalidation.AuthorityLost, lease.Invalidation);
        Assert.Equal(HaulPhase.Unassigned, disabled.Executor.Phase);

        var peer = new HaulingExecutionRig();
        peer.Assign();
        peer.Authority.Verdict = WorkAuthorityVerdict.OtherPeersConnected;
        peer.Advance(3f);
        Assert.NotNull(peer.Executor.ActiveLease);
        Assert.Equal(HaulPhase.Ready, peer.Executor.Phase);

        disabled.AssertNoBugs();
        peer.AssertNoBugs();
    }

    [Fact]
    public void OwnershipAndTheBodyEndTheLeaseATakeoverAndABrokenHitchDoNot()
    {
        (Action<HaulingExecutionRig> end, HaulAttentionReason reason, bool leaseSurvives)[] cases =
        {
            (r => r.Seam.Observation = r.Seam.Observation.With(o => o.IsOwner = false), HaulAttentionReason.OwnershipLost, false),
            (r => r.Body.Facts = r.Body.Facts.With(f => f.Dead = true), HaulAttentionReason.WorkerBodyLost, false),
            (r => r.Seam.Observation = r.Seam.Observation.With(o => { o.HasJoint = false; o.JointConnectedToPuller = false; o.LocalPlayerHasJoint = true; }), HaulAttentionReason.PlayerTookOver, true),
            (r => r.Seam.Observation = r.Seam.Observation.With(o => { o.HasJoint = false; o.JointConnectedToPuller = false; }), HaulAttentionReason.JointBroke, true),
            (r => r.Seam.Observation = r.Seam.Observation.With(o => o.BrakeEngaged = true), HaulAttentionReason.BrakeEngaged, true),
        };

        foreach ((Action<HaulingExecutionRig> end, HaulAttentionReason reason, bool leaseSurvives) in cases)
        {
            var rig = new HaulingExecutionRig();
            rig.RunToPulling();
            end(rig);
            rig.StepOnce();

            Assert.Equal(reason, rig.Executor.Attention);
            Assert.Equal(leaseSurvives, rig.Executor.ActiveLease != null);
            rig.AssertNoBugs();
        }
    }

    [Fact]
    public void AfterATakeoverTheSameLeaseCanHaulAgainOnlyThroughAFreshHitch()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.HasJoint = false;
            o.JointConnectedToPuller = false;
            o.LocalPlayerHoveringCart = true;
        });
        rig.StepOnce();
        Assert.Equal(HaulAttentionReason.PlayerTookOver, rig.Executor.Attention);

        // The player resolves it; the lease is still Gunnar's.
        rig.Executor.Cancel("haul-1", detachAndPark: true);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);

        // The player is pulling the cart now: the next hitch refuses (in use).
        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.LocalPlayerHoveringCart = false;
            o.HasJoint = true;
            o.JointConnectedToLocalPlayer = true;
            o.InUse = true;
            o.AnyJointOnClient = true;
            o.LocalPlayerHasJoint = true;
        });
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Go("haul-2").Outcome);
        rig.PutBodyAtHandle();
        rig.StepOnce();
        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);
        Assert.Equal(HitchRefusal.InUse, rig.Executor.LastHitchRefusal);
        Assert.Equal(1, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ALegRevalidatesTheCartAndEndsALeaseWhoseCartIsGone()
    {
        var rig = new HaulingExecutionRig();
        rig.Assign();
        CartLease lease = rig.Executor.ActiveLease!;
        rig.Seam.Observation = rig.Seam.Observation.With(o => { o.Resolved = false; o.RecordExists = false; });

        HaulCommandResult leg = rig.Go();

        Assert.Equal(HaulCommandOutcome.Rejected, leg.Outcome);
        Assert.Equal(HaulAttentionReason.CartDestroyed, leg.Reason);
        Assert.Equal(HaulCommandDetail.CartUnavailable, rig.Executor.LastCommandDetail);
        Assert.Equal(LeaseInvalidation.CartDestroyed, lease.Invalidation);
        Assert.Equal(HaulPhase.Unassigned, rig.Executor.Phase);
        Assert.Equal(0, rig.Planner.PlanCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AnEndedLeaseIsNeverReplacedByAnotherCart()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o => { o.Resolved = false; o.RecordExists = false; });
        rig.StepOnce();
        Assert.Null(rig.Executor.ActiveLease);

        // Nothing picks up "the nearest cart": the executor waits for a person.
        rig.Seam.Observation = FakeSeam.HealthyCart();
        rig.Advance(10f);
        Assert.Null(rig.Executor.ActiveLease);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(1, rig.Seam.AttachCalls);

        // A new explicit assignment replaces the old attention.
        rig.Executor.Cancel("haul-1", detachAndPark: true);
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign("lease-2").Outcome);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        rig.AssertNoBugs();
    }
}
