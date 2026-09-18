using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The attach seam's own decisions, tested directly rather than
/// through a fake that re-implements them (review R-313 M2): the two
/// last-instant guards before the cart's attach, the verification after it, and
/// whether a joint is Gunnar's to release.</summary>
public sealed class HaulingSeamRulesTests
{
    private static HaulExecutionLimits Execution => HaulExecutionLimits.Default.Validate();

    [Fact]
    public void AnAttachNeedsAValidOwnedViewAndNoJointAnywhereOnTheClient()
    {
        Assert.Null(HitchSeamRules.GuardBeforeAttach(viewValid: true, isOwner: true, anyJointOnClient: false));

        AttachResult? invalid = HitchSeamRules.GuardBeforeAttach(viewValid: false, isOwner: true, anyJointOnClient: false);
        Assert.Equal(HitchRefusal.NotOwnedHere, invalid!.Value.Refusal);

        AttachResult? notOwner = HitchSeamRules.GuardBeforeAttach(viewValid: true, isOwner: false, anyJointOnClient: false);
        Assert.Equal(HitchRefusal.NotOwnedHere, notOwner!.Value.Refusal);

        // The cart's own attach detaches every loaded cart first, so another
        // cart's joint - the player's own, most of the time - refuses it.
        AttachResult? otherJoint = HitchSeamRules.GuardBeforeAttach(viewValid: true, isOwner: true, anyJointOnClient: true);
        Assert.Equal(HitchRefusal.OtherJointOnClient, otherJoint!.Value.Refusal);
        Assert.False(string.IsNullOrWhiteSpace(otherJoint.Value.Detail));
    }

    [Fact]
    public void AVerifiedAttachIsAJointToGunnarAFlagAndTheRightMass()
    {
        Assert.Null(Verify());
        Assert.Contains("no joint", Verify(jointExists: false)!);
        Assert.Contains("not connected to Gunnar", Verify(connectedToPuller: false)!);
        Assert.Contains("does not report itself attached", Verify(cartReportsAttached: false)!);
        Assert.Contains("attach flag", Verify(attachFlagSet: false)!);

        // D5: the puller weighs its calibration plus the cart's extra pull mass.
        Assert.Contains("kg attached", Verify(pullerMassKg: 70f)!);
        Assert.Null(Verify(pullerMassKg: 90f, expectedMassKg: 90f));
    }

    [Fact]
    public void GunnarsJointIsReleasedEvenWhenHisBodyIsNoLongerBound()
    {
        // The seam remembers the body its verified attach connected: an unbound,
        // dying or duplicated Gunnar is still Gunnar (review R-313 B1).
        Assert.Equal(
            JointReleaseDecision.Detach,
            HitchSeamRules.DecideRelease(new JointHolderFacts(
                jointExists: true,
                connectedToNothing: false,
                connectedToAttachedBody: true,
                connectedToBoundBody: false,
                connectedToWorkerBody: false)));

        // Any worker body counts, even one this runtime never attached.
        Assert.Equal(
            JointReleaseDecision.Detach,
            HitchSeamRules.DecideRelease(new JointHolderFacts(true, false, false, false, true)));

        // The bound body, the ordinary case.
        Assert.Equal(
            JointReleaseDecision.Detach,
            HitchSeamRules.DecideRelease(new JointHolderFacts(true, false, true, true, true)));
    }

    [Fact]
    public void AJointToNothingOrNoJointAtAllIsReleasedAndAnotherBodysIsNot()
    {
        // No joint: the detach clears a stale replicated flag, exactly as
        // vanilla's own next update would.
        Assert.Equal(
            JointReleaseDecision.Detach,
            HitchSeamRules.DecideRelease(new JointHolderFacts(false, false, false, false, false)));

        // A joint connected to nothing holds nobody (review R-313 m4).
        Assert.Equal(
            JointReleaseDecision.Detach,
            HitchSeamRules.DecideRelease(new JointHolderFacts(true, true, false, false, false)));

        // The player's, or any other living body's: never Gunnar's to release.
        Assert.Equal(
            JointReleaseDecision.LeaveAlone,
            HitchSeamRules.DecideRelease(new JointHolderFacts(true, false, false, false, false)));
    }

    [Fact]
    public void TheBindingNeverMovesWhileACartHoldsTheBody()
    {
        Assert.False(WorkerBodyCensus.MayChangeBinding(jointHeld: true));
        Assert.True(WorkerBodyCensus.MayChangeBinding(jointHeld: false));
    }

    [Fact]
    public void SomeoneSittingInTheCartRefusesTheHitch()
    {
        // The cart carries a Chair, and a seated player would be hauled away
        // with it (review R-313 m5).
        HitchVerdict verdict = Evaluate(FakeSeam.HealthyCart().With(o => o.SeatOccupied = true));
        Assert.False(verdict.Allowed);
        Assert.Equal(HitchRefusal.InUse, verdict.Refusal);
        Assert.Contains("sitting", verdict.Detail);
    }

    [Fact]
    public void AnEmptyIdleCartInReachIsHitchable()
    {
        Assert.True(Evaluate(FakeSeam.HealthyCart().With(o => o.HitchDistanceMetres = 0.5f)).Allowed);
    }

    private static HitchVerdict Evaluate(CartObservation cart) =>
        HitchPreconditions.Evaluate(
            seamAvailable: true,
            WorkAuthorityVerdict.Granted,
            leaseActive: true,
            cart.With(o => o.HitchDistanceMetres = 0.5f),
            FakeBody.Healthy(),
            HaulLimits.Default.Validate(),
            Execution);

    private static string? Verify(
        bool jointExists = true,
        bool connectedToPuller = true,
        bool cartReportsAttached = true,
        bool attachFlagSet = true,
        float pullerMassKg = 90f,
        float expectedMassKg = 90f) =>
        HitchSeamRules.VerifyAfterAttach(
            jointExists, connectedToPuller, cartReportsAttached, attachFlagSet, pullerMassKg, expectedMassKg, Execution);
}
