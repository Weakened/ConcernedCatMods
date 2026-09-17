using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-06: every way a hitched haul ends is classified into its
/// one reason, releases the joint only when it is Gunnar's to release, and ends
/// the lease exactly where DECISIONS.md D6 says (CONTRACTS.md §2.5, §3.3).
/// </summary>
public class HaulingExecutionSignalTests
{
    private static CartObservation Hitched() => FakeSeam.HealthyCart().With(o =>
    {
        o.HitchDistanceMetres = 0.4f;
        o.HasJoint = true;
        o.JointConnectedToPuller = true;
        o.AttachFlag = true;
        o.InUse = true;
        o.AnyJointOnClient = true;
    });

    private static SignalVerdict Classify(
        CartObservation cart,
        PullerBodyFacts? body = null,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted) =>
        JointSignalClassifier.Classify(authority, cart, body ?? FakeBody.Healthy(), HaulLimits.Default);

    [Fact]
    public void AHealthyHitchIsFine()
    {
        SignalVerdict verdict = Classify(Hitched());
        Assert.True(verdict.Healthy);
        Assert.Equal(HaulAttentionReason.Unspecified, verdict.Reason);
    }

    public static IEnumerable<object[]> EachEnding()
    {
        // name, reason, releases joint, lease invalidation, pauses
        yield return new object[] { "peer connected", HaulAttentionReason.OtherPeersConnected, true, LeaseInvalidation.Unspecified, true };
        yield return new object[] { "runtime disabled", HaulAttentionReason.AuthorityLost, true, LeaseInvalidation.AuthorityLost, false };
        yield return new object[] { "not host", HaulAttentionReason.AuthorityLost, true, LeaseInvalidation.AuthorityLost, false };
        yield return new object[] { "body gone", HaulAttentionReason.WorkerBodyLost, true, LeaseInvalidation.WorkerBodyLost, false };
        yield return new object[] { "body faulted", HaulAttentionReason.WorkerBodyLost, true, LeaseInvalidation.WorkerBodyLost, false };
        yield return new object[] { "body dead", HaulAttentionReason.WorkerBodyLost, true, LeaseInvalidation.WorkerBodyLost, false };
        yield return new object[] { "cart destroyed", HaulAttentionReason.CartDestroyed, true, LeaseInvalidation.CartDestroyed, false };
        yield return new object[] { "cart unloaded", HaulAttentionReason.CartUnloaded, true, LeaseInvalidation.CartUnloaded, false };
        yield return new object[] { "joint gone, ownership moved", HaulAttentionReason.OwnershipLost, true, LeaseInvalidation.OwnershipLost, false };
        yield return new object[] { "joint gone, player grabbed a cart", HaulAttentionReason.PlayerTookOver, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "joint gone, player using the cart", HaulAttentionReason.PlayerTookOver, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "joint gone, cart tipped", HaulAttentionReason.CartTipped, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "joint gone otherwise", HaulAttentionReason.JointBroke, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "joint now the player's", HaulAttentionReason.PlayerTookOver, false, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "joint now another body's", HaulAttentionReason.JointBroke, false, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "ownership moved, joint still here", HaulAttentionReason.OwnershipLost, true, LeaseInvalidation.OwnershipLost, false };
        yield return new object[] { "brake engaged", HaulAttentionReason.BrakeEngaged, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "root frozen", HaulAttentionReason.BrakeEngaged, true, LeaseInvalidation.Unspecified, false };
        yield return new object[] { "leaning, still held", HaulAttentionReason.CartTipped, false, LeaseInvalidation.Unspecified, false };
    }

    [Theory]
    [MemberData(nameof(EachEnding))]
    public void EachEndingHasItsReasonReleaseAndLeaseRule(
        string ending, object expectedReason, bool releases, object expectedInvalidation, bool pauses)
    {
        var reason = (HaulAttentionReason)expectedReason;
        var invalidation = (LeaseInvalidation)expectedInvalidation;
        CartObservation cart = Hitched();
        PullerBodyFacts body = FakeBody.Healthy();
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted;
        CartObservation jointGone = cart.With(o => { o.HasJoint = false; o.JointConnectedToPuller = false; });
        switch (ending)
        {
            case "peer connected": authority = WorkAuthorityVerdict.OtherPeersConnected; break;
            case "runtime disabled": authority = WorkAuthorityVerdict.RuntimeDisabled; break;
            case "not host": authority = WorkAuthorityVerdict.NotHost; break;
            case "body gone": body = body.With(f => f.Present = false); break;
            case "body faulted": body = body.With(f => f.Faulted = true); break;
            case "body dead": body = body.With(f => f.Dead = true); break;
            case "cart destroyed": cart = cart.With(o => { o.Resolved = false; o.RecordExists = false; }); break;
            case "cart unloaded": cart = cart.With(o => { o.Resolved = false; o.RecordExists = true; }); break;
            case "joint gone, ownership moved": cart = jointGone.With(o => o.IsOwner = false); break;
            case "joint gone, player grabbed a cart": cart = jointGone.With(o => o.LocalPlayerHasJoint = true); break;
            case "joint gone, player using the cart": cart = jointGone.With(o => o.LocalPlayerHoveringCart = true); break;
            case "joint gone, cart tipped": cart = jointGone.With(o => o.UpDot = 0.05f); break;
            case "joint gone otherwise": cart = jointGone; break;
            case "joint now the player's": cart = cart.With(o => { o.JointConnectedToPuller = false; o.JointConnectedToLocalPlayer = true; }); break;
            case "joint now another body's": cart = cart.With(o => o.JointConnectedToPuller = false); break;
            case "ownership moved, joint still here": cart = cart.With(o => o.IsOwner = false); break;
            case "brake engaged": cart = cart.With(o => o.BrakeEngaged = true); break;
            case "root frozen": cart = cart.With(o => o.RootFrozen = true); break;
            case "leaning, still held": cart = cart.With(o => o.UpDot = 0.3f); break;
            default: throw new ArgumentOutOfRangeException(nameof(ending));
        }

        SignalVerdict verdict = Classify(cart, body, authority);

        Assert.False(verdict.Healthy);
        Assert.Equal(reason, verdict.Reason);
        Assert.Equal(releases, verdict.ReleaseJoint);
        Assert.Equal(invalidation, verdict.Invalidation);
        Assert.Equal(pauses, verdict.Pause);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Detail));
    }

    [Fact]
    public void AuthorityOutranksEverythingAndTheBodyOutranksTheCart()
    {
        CartObservation gone = Hitched().With(o => o.Resolved = false);
        PullerBodyFacts noBody = FakeBody.Healthy().With(f => f.Present = false);

        Assert.Equal(HaulAttentionReason.AuthorityLost, Classify(gone, noBody, WorkAuthorityVerdict.NoWorld).Reason);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, Classify(gone, noBody).Reason);

        // Evidence of a player grab outranks a tip: the grab is what removed it.
        CartObservation grabbedWhileTipped = Hitched().With(o =>
        {
            o.HasJoint = false;
            o.JointConnectedToPuller = false;
            o.UpDot = 0.05f;
            o.LocalPlayerHasJoint = true;
        });
        Assert.Equal(HaulAttentionReason.PlayerTookOver, Classify(grabbedWhileTipped).Reason);
    }

    [Fact]
    public void AVanishedJointEndsTheHaulImmediatelyAndIsNeverReHitched()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();

        rig.Seam.Observation = rig.Seam.Observation.With(o => { o.HasJoint = false; o.JointConnectedToPuller = false; });
        rig.Clock.Now += HaulingExecutionRig.Step;
        rig.Executor.ObserveFrame();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.JointBroke, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        Assert.True(rig.IndexOf("Stop") < rig.IndexOf("ReleaseJoint"));

        rig.Advance(30f);
        Assert.Equal(1, rig.Seam.AttachCalls);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ThePlayersJointIsNeverReleasedByGunnar()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o =>
        {
            o.JointConnectedToPuller = false;
            o.JointConnectedToLocalPlayer = true;
            o.LocalPlayerHasJoint = true;
        });

        rig.StepOnce();

        Assert.Equal(0, rig.Seam.ReleaseCalls);
        Assert.Equal(HaulAttentionReason.PlayerTookOver, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        Assert.NotNull(rig.Executor.ActiveLease);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ALeaningCartStaysHeldUntilAPersonDecides()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.UpDot = 0.3f);

        rig.StepOnce();

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.CartTipped, rig.Executor.Attention);
        Assert.True(rig.Executor.Attached);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        Assert.False(rig.Body.Facts.MotorCommanded);

        // Detaching needs suitable ground and an upright cart: refused, still held.
        Assert.Equal(HaulCommandOutcome.Accepted, rig.Executor.Cancel("haul-1", detachAndPark: true).Outcome);
        Assert.Equal(HaulAttentionReason.UnsafeParking, rig.Executor.Attention);
        Assert.True(rig.Executor.Attached);
        Assert.Equal(0, rig.Seam.ReleaseCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void EveryHitchedEndingIsCaughtFromTheWorkerTickToo()
    {
        var rig = new HaulingExecutionRig();
        rig.RunToPulling();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.BrakeEngaged = true);

        rig.Clock.Now += HaulingExecutionRig.Step;
        rig.Executor.Tick();

        Assert.Equal(HaulAttentionReason.BrakeEngaged, rig.Executor.Attention);
        Assert.Equal(1, rig.Seam.ReleaseCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AWorkerThatStopsTickingIsABodyThatIsNotWorking()
    {
        var rig = new HaulingExecutionRig(tuneExecution: execution => execution.WorkerTickStaleSeconds = 1f);
        rig.RunToPulling();

        // Frames keep coming, the worker's own tick does not.
        for (int frame = 0; frame < 30; frame++)
        {
            rig.Clock.Now += HaulingExecutionRig.Step;
            rig.Executor.ObserveFrame();
        }

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.WorkerBodyLost, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        Assert.Null(rig.Executor.ActiveLease);
        rig.AssertNoBugs();
    }
}
