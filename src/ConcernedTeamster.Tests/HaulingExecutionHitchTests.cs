using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313 CART-02, CART-03: every precondition of DECISIONS.md D4 refuses
/// on its own, in D4's order, fails closed on unreadable values, and never
/// reaches the cart's attach when refused.</summary>
public class HaulingExecutionHitchTests
{
    private static readonly HaulLimits Limits = HaulLimits.Default;
    private static readonly HaulExecutionLimits Execution = HaulExecutionLimits.Default;

    private static CartObservation ReadyCart() =>
        FakeSeam.HealthyCart().With(o => o.HitchDistanceMetres = 0.5f);

    private static HitchVerdict Evaluate(
        CartObservation? cart = null,
        PullerBodyFacts? body = null,
        bool seam = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool lease = true) =>
        HitchPreconditions.Evaluate(seam, authority, lease, cart ?? ReadyCart(), body ?? FakeBody.Healthy(), Limits, Execution);

    [Fact]
    public void AHealthyCartAndBodyMayHitch()
    {
        HitchVerdict verdict = Evaluate();
        Assert.True(verdict.Allowed, verdict.Detail);
        Assert.Equal(HitchRefusal.Unspecified, verdict.Refusal);
    }

    public static IEnumerable<object[]> EachPrecondition()
    {
        yield return new object[] { "seam missing", HitchRefusal.SeamUnavailable };
        yield return new object[] { "authority", HitchRefusal.NoAuthority };
        yield return new object[] { "lease", HitchRefusal.LeaseNotActive };
        yield return new object[] { "cart gone", HitchRefusal.CartGone };
        yield return new object[] { "view invalid", HitchRefusal.CartGone };
        yield return new object[] { "siege engine", HitchRefusal.CartGone };
        yield return new object[] { "remote owner", HitchRefusal.NotOwnedHere };
        yield return new object[] { "capability unknown", HitchRefusal.NotOwnedHere };
        yield return new object[] { "stale mass", HitchRefusal.MassNotCurrent };
        yield return new object[] { "container open", HitchRefusal.InUse };
        yield return new object[] { "joint exists", HitchRefusal.InUse };
        yield return new object[] { "attach flag", HitchRefusal.InUse };
        yield return new object[] { "teamster brake", HitchRefusal.Braked };
        yield return new object[] { "frozen root", HitchRefusal.Braked };
        yield return new object[] { "player pulling another cart", HitchRefusal.OtherJointOnClient };
        yield return new object[] { "leaning", HitchRefusal.NotUpright };
        yield return new object[] { "out of reach", HitchRefusal.OutOfReach };
        yield return new object[] { "no detach distance", HitchRefusal.OutOfReach };
        yield return new object[] { "no rigidbody", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "kinematic", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "no gravity", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "no collisions", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "not upright locked", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "scaled", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "uncalibrated", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "wrong body mass", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "wrong base mass", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "absent body", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "dead body", HitchRefusal.PullerBodyInvalid };
        yield return new object[] { "faulted body", HitchRefusal.PullerBodyInvalid };
    }

    [Theory]
    [MemberData(nameof(EachPrecondition))]
    public void EachPreconditionRefusesOnItsOwn(string broken, object expectedRefusal)
    {
        var expected = (HitchRefusal)expectedRefusal;
        CartObservation cart = ReadyCart();
        PullerBodyFacts body = FakeBody.Healthy();
        bool seam = true;
        bool lease = true;
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted;
        switch (broken)
        {
            case "seam missing": seam = false; break;
            case "authority": authority = WorkAuthorityVerdict.OtherPeersConnected; break;
            case "lease": lease = false; break;
            case "cart gone": cart = cart.With(o => o.Resolved = false); break;
            case "view invalid": cart = cart.With(o => o.ViewValid = false); break;
            case "siege engine": cart = cart.With(o => o.IsHandCart = false); break;
            case "remote owner": cart = cart.With(o => o.IsOwner = false); break;
            case "capability unknown": cart = cart.With(o => o.CapabilityOk = false); break;
            case "stale mass": cart = cart.With(o => o.BodyMassSumKg = 14f); break;
            case "container open": cart = cart.With(o => o.ContainerOpen = true); break;
            case "joint exists": cart = cart.With(o => o.HasJoint = true); break;
            case "attach flag": cart = cart.With(o => o.AttachFlag = true); break;
            case "teamster brake": cart = cart.With(o => o.BrakeEngaged = true); break;
            case "frozen root": cart = cart.With(o => o.RootFrozen = true); break;
            case "player pulling another cart": cart = cart.With(o => o.AnyJointOnClient = true); break;
            case "leaning": cart = cart.With(o => o.UpDot = 0.49f); break;
            case "out of reach": cart = cart.With(o => o.HitchDistanceMetres = 1.21f); break;
            case "no detach distance": cart = cart.With(o => o.DetachDistanceMetres = 0f); break;
            case "no rigidbody": body = body.With(f => f.HasRigidbody = false); break;
            case "kinematic": body = body.With(f => f.IsKinematic = true); break;
            case "no gravity": body = body.With(f => f.UsesGravity = false); break;
            case "no collisions": body = body.With(f => f.DetectsCollisions = false); break;
            case "not upright locked": body = body.With(f => f.RotationLockedUpright = false); break;
            case "scaled": body = body.With(f => f.UnitScale = false); break;
            case "uncalibrated": body = body.With(f => f.CalibratedMassKg = float.NaN); break;
            case "wrong body mass": body = body.With(f => f.BodyMassKg = 90f); break;
            case "wrong base mass": body = body.With(f => f.BaseMassKg = 55f); break;
            case "absent body": body = body.With(f => f.Present = false); break;
            case "dead body": body = body.With(f => f.Dead = true); break;
            case "faulted body": body = body.With(f => f.Faulted = true); break;
            default: throw new ArgumentOutOfRangeException(nameof(broken));
        }

        HitchVerdict verdict = Evaluate(cart, body, seam, authority, lease);

        Assert.False(verdict.Allowed);
        Assert.Equal(expected, verdict.Refusal);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Detail));
    }

    [Fact]
    public void TheFirstFailingPreconditionInD4OrderIsReported()
    {
        CartObservation everythingWrong = ReadyCart().With(o =>
        {
            o.IsOwner = false;
            o.BodyMassSumKg = 1f;
            o.ContainerOpen = true;
            o.BrakeEngaged = true;
            o.AnyJointOnClient = true;
            o.UpDot = 0f;
            o.HitchDistanceMetres = 50f;
        });
        PullerBodyFacts badBody = FakeBody.Healthy().With(f => f.IsKinematic = true);

        Assert.Equal(HitchRefusal.NotOwnedHere, Evaluate(everythingWrong, badBody).Refusal);
        Assert.Equal(HitchRefusal.MassNotCurrent, Evaluate(everythingWrong.With(o => o.IsOwner = true), badBody).Refusal);
        Assert.Equal(
            HitchRefusal.InUse,
            Evaluate(everythingWrong.With(o => { o.IsOwner = true; o.BodyMassSumKg = 20f; }), badBody).Refusal);
        Assert.Equal(
            HitchRefusal.Braked,
            Evaluate(everythingWrong.With(o => { o.IsOwner = true; o.BodyMassSumKg = 20f; o.ContainerOpen = false; }), badBody).Refusal);
        Assert.Equal(
            HitchRefusal.OtherJointOnClient,
            Evaluate(everythingWrong.With(o => { o.IsOwner = true; o.BodyMassSumKg = 20f; o.ContainerOpen = false; o.BrakeEngaged = false; }), badBody).Refusal);
        Assert.Equal(
            HitchRefusal.NotUpright,
            Evaluate(everythingWrong.With(o => { o.IsOwner = true; o.BodyMassSumKg = 20f; o.ContainerOpen = false; o.BrakeEngaged = false; o.AnyJointOnClient = false; }), badBody).Refusal);
        Assert.Equal(
            HitchRefusal.OutOfReach,
            Evaluate(everythingWrong.With(o => { o.IsOwner = true; o.BodyMassSumKg = 20f; o.ContainerOpen = false; o.BrakeEngaged = false; o.AnyJointOnClient = false; o.UpDot = 1f; }), badBody).Refusal);
        Assert.Equal(HitchRefusal.PullerBodyInvalid, Evaluate(ReadyCart(), badBody).Refusal);
    }

    [Fact]
    public void BoundariesAreExactAndUnreadableValuesRefuse()
    {
        Assert.True(Evaluate(ReadyCart().With(o => o.UpDot = 0.5f)).Allowed);
        Assert.True(Evaluate(ReadyCart().With(o => o.HitchDistanceMetres = 1.2f)).Allowed);
        Assert.Equal(HitchRefusal.NotUpright, Evaluate(ReadyCart().With(o => o.UpDot = float.NaN)).Refusal);
        Assert.Equal(HitchRefusal.OutOfReach, Evaluate(ReadyCart().With(o => o.HitchDistanceMetres = float.NaN)).Refusal);
        Assert.Equal(HitchRefusal.MassNotCurrent, Evaluate(ReadyCart().With(o => o.ExpectedMassKg = float.NaN)).Refusal);
        Assert.Equal(HitchRefusal.MassNotCurrent, Evaluate(ReadyCart().With(o => { o.ExpectedMassKg = 0f; o.BodyMassSumKg = 0f; })).Refusal);
        Assert.Equal(
            HitchRefusal.PullerBodyInvalid,
            Evaluate(body: FakeBody.Healthy().With(f => f.BodyMassKg = float.PositiveInfinity)).Refusal);

        // Within one percent is the same mass; a loaded cart's float sum drifts.
        Assert.True(Evaluate(ReadyCart().With(o => { o.ExpectedMassKg = 620f; o.BodyMassSumKg = 619.5f; })).Allowed);
    }

    [Fact]
    public void OwnershipIsDecidedThroughTheCartAuthorityPolicy()
    {
        foreach (CartAuthority authority in new[] { CartAuthority.Unknown, CartAuthority.Local, CartAuthority.Remote })
        {
            CartObservation cart = ReadyCart().With(o =>
            {
                o.CapabilityOk = authority != CartAuthority.Unknown;
                o.IsOwner = authority == CartAuthority.Local;
            });
            Assert.Equal(authority, cart.Authority);
            Assert.Equal(
                CartAuthorityPolicy.MayMutate(TeamsterFeature.GunnarHauling, authority),
                Evaluate(cart).Allowed);
        }
    }

    [Fact]
    public void ARefusedPreconditionNeverReachesTheCartsAttach()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.BrakeEngaged = true);

        rig.StepOnce();

        Assert.Equal(0, rig.Seam.AttachCalls);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.BrakeEngaged, rig.Executor.Attention);
        Assert.Equal(HitchRefusal.Braked, rig.Executor.LastHitchRefusal);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AFailedVerificationRetriesWithBackoffAndGivesUpWithHitchFailed()
    {
        var rig = new HaulingExecutionRig(limits =>
        {
            limits.MaxHitchAttempts = 3;
            limits.RecoveryBackoffSeconds = 1f;
            limits.RecoveryBackoffMaxSeconds = 4f;
        });
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.AttachWorks = false;

        rig.StepOnce();
        Assert.Equal(1, rig.Seam.AttachCalls);
        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);

        // Backoff: nothing is attempted before the retry time.
        rig.Advance(0.9f);
        Assert.Equal(1, rig.Seam.AttachCalls);
        rig.Advance(0.2f);
        Assert.Equal(2, rig.Seam.AttachCalls);

        rig.Advance(10f);
        Assert.Equal(3, rig.Seam.AttachCalls);
        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.HitchFailed, rig.Executor.Attention);
        Assert.False(rig.Executor.Attached);
        rig.Advance(30f);
        Assert.Equal(3, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AStaleCartMassIsWaitedForWithoutSpendingAttempts()
    {
        var rig = new HaulingExecutionRig(limits => limits.MassSettleSeconds = 6f);
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.BodyMassSumKg = 5f);

        rig.Advance(5f);
        Assert.Equal(HaulPhase.Hitching, rig.Executor.Phase);
        Assert.Equal(0, rig.Executor.HitchFailures);

        // Vanilla's periodic mass update caught up.
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.BodyMassSumKg = 20f);
        rig.StepOnce();
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AMassThatNeverMatchesCountsAsFailedAttempts()
    {
        var rig = new HaulingExecutionRig(limits =>
        {
            limits.MassSettleSeconds = 1f;
            limits.MaxHitchAttempts = 2;
            limits.RecoveryBackoffSeconds = 0.5f;
            limits.RecoveryBackoffMaxSeconds = 1f;
        });
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.BodyMassSumKg = 16f);

        rig.Advance(20f);

        Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
        Assert.Equal(HaulAttentionReason.HitchFailed, rig.Executor.Attention);
        Assert.Equal(0, rig.Seam.AttachCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void OutOfReachAtTheHitchGoesBackToApproaching()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.PutBodyAtHandle();
        rig.StepOnce();
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);

        var second = new HaulingExecutionRig();
        second.AssignAndStartLeg();
        second.PutBodyAtHandle();
        second.Body.CalibrateSucceeds = false;
        second.Body.Facts = second.Body.Facts.With(f => f.BodyMassKg = 40f);
        second.StepOnce();
        Assert.Equal(HaulPhase.Hitching, second.Executor.Phase);
        Assert.Equal(HitchRefusal.PullerBodyInvalid, second.Executor.LastHitchRefusal);
        Assert.Contains("Calibrate", second.Journal);

        var third = new HaulingExecutionRig();
        third.AssignAndStartLeg();
        third.PutBodyAtHandle();
        third.Seam.Observation = third.Seam.Observation.With(o => o.AnyJointOnClient = true);
        third.StepOnce();
        Assert.Equal(HaulPhase.Hitching, third.Executor.Phase);
        third.Seam.Observation = third.Seam.Observation.With(o => { o.AnyJointOnClient = false; o.HitchDistanceMetres = 1.5f; });
        third.Advance(4f);
        Assert.Equal(HaulPhase.Approaching, third.Executor.Phase);
        Assert.Equal(HitchRefusal.OutOfReach, third.Executor.LastHitchRefusal);
        rig.AssertNoBugs();
        second.AssertNoBugs();
        third.AssertNoBugs();
    }

    [Fact]
    public void RefusalsThatNeedAPersonEndInTheirOwnAttentionReason()
    {
        (Action<HaulingExecutionRig> breakIt, HaulAttentionReason expected, bool leaseEnds)[] cases =
        {
            (r => r.Seam.Observation = r.Seam.Observation.With(o => o.UpDot = 0.3f), HaulAttentionReason.CartTipped, false),
            (r => r.Seam.Observation = r.Seam.Observation.With(o => o.RootFrozen = true), HaulAttentionReason.BrakeEngaged, false),
            (r => r.Seam.Observation = r.Seam.Observation.With(o => o.IsOwner = false), HaulAttentionReason.OwnershipLost, true),
            (r => r.Seam.IsAvailable = false, HaulAttentionReason.HitchFailed, false),
        };

        foreach ((Action<HaulingExecutionRig> breakIt, HaulAttentionReason expected, bool leaseEnds) in cases)
        {
            var rig = new HaulingExecutionRig();
            rig.AssignAndStartLeg();
            rig.PutBodyAtHandle();
            breakIt(rig);
            rig.StepOnce();

            Assert.Equal(HaulPhase.NeedsAttention, rig.Executor.Phase);
            Assert.Equal(expected, rig.Executor.Attention);
            Assert.Equal(!leaseEnds, rig.Executor.ActiveLease != null);
            Assert.Equal(0, rig.Seam.AttachCalls);
            rig.AssertNoBugs();
        }
    }

    [Fact]
    public void EveryHitchRefusalHasItsOwnPlayerSentence()
    {
        var sentences = new HashSet<string>();
        foreach (HitchRefusal refusal in Enum.GetValues<HitchRefusal>())
        {
            string sentence = HaulRefusalSentences.Describe(refusal);
            if (refusal == HitchRefusal.Unspecified)
            {
                Assert.Equal(HaulRefusalSentences.BugSentence, sentence);
                continue;
            }

            Assert.NotEqual(HaulRefusalSentences.BugSentence, sentence);
            Assert.True(sentences.Add(sentence), refusal + " shares a sentence");
        }
    }
}
