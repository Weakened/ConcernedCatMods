using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

namespace ConcernedTeamster.Tests;

/// <summary>How the executor uses agent B's cart navigation (#314): the lease
/// hands it the cart and takes it back, legs are planned from the cart with
/// Gunnar's own position once he is hitched, the measured footprint is what the
/// route is checked against, every stall is remembered where it happened, and a
/// refresh that no longer holds ends the leg.</summary>
public sealed class HaulingNavigationWiringTests
{
    [Fact]
    public void TheLeaseHandsTheCartToNavigationAndEndingItTakesItBack()
    {
        var navigation = new FakeNavigation();
        var rig = new HaulingExecutionRig(navigation: navigation);

        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        Assert.Equal(1, navigation.UseCartCalls);
        Assert.Equal(0, navigation.ReleaseCartCalls);

        Assert.Equal(LeaseReleaseOutcome.Released, rig.Executor.ReleaseLease());
        Assert.Equal(1, navigation.ReleaseCartCalls);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ACartNavigationCouldNotMeasureIsNotPlannedFor()
    {
        var navigation = new FakeNavigation { UseCartSucceeds = false };
        var rig = new HaulingExecutionRig(navigation: navigation);
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);

        HaulCommandResult result = rig.Go();

        Assert.Equal(HaulCommandOutcome.Rejected, result.Outcome);
        Assert.Equal(HaulAttentionReason.NoRoute, result.Reason);
        Assert.Equal(HaulCommandDetail.FootprintUnknown, rig.Executor.LastCommandDetail);
        Assert.Empty(navigation.Requests);
        Assert.Equal(HaulPhase.Ready, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ALegIsPlannedFromTheCartWithTheHandleFirstAndGunnarOnceHitched()
    {
        var navigation = new FakeNavigation();
        var rig = new HaulingExecutionRig(navigation: navigation);
        rig.AssignAndStartLeg();

        // C3: the route is the cart's, and the first turn is predicted from
        // where the puller will be - the standing point at the handle.
        Assert.Single(navigation.Requests);
        Assert.Equal(rig.Seam.Observation.CartPosition, navigation.Requests[0].From);
        Assert.Equal(rig.Seam.Observation.ApproachPoint, navigation.PlannedFrom[0]);

        rig.PutBodyAtHandle();
        rig.StepOnce();
        rig.ArriveAtTarget();
        rig.StepOnce();
        rig.Advance(rig.Limits.StillForSeconds + (2f * HaulingExecutionRig.Step));
        Assert.Equal(HaulPhase.Waiting, rig.Executor.Phase);

        Assert.Equal(HaulCommandOutcome.Accepted, rig.Go().Outcome);
        Assert.Equal(2, navigation.Requests.Count);
        Assert.Equal(rig.Body.Facts.Position, navigation.PlannedFrom[1]);
        rig.AssertNoBugs();
    }

    [Fact]
    public void TheRouteIsCheckedAgainstTheMeasuredFootprint()
    {
        var navigation = new FakeNavigation { MeasuredFootprint = new CartFootprint(1.9f, 3.1f, 1.7f) };
        var rig = new HaulingExecutionRig(navigation: navigation);
        rig.AssignAndStartLeg();

        Assert.Equal(1.9f, navigation.Requests[0].Footprint.WidthMetres);
        Assert.Equal(3.1f, navigation.Requests[0].Footprint.LengthMetres);
        rig.AssertNoBugs();
    }

    [Fact]
    public void AStrainedHitchIsRememberedWhereItHappened()
    {
        var navigation = new FakeNavigation();
        var rig = new HaulingExecutionRig(navigation: navigation);
        rig.RunToPulling();

        rig.Seam.Observation = rig.Seam.Observation.With(o => o.JointForceNewtons = 9000f);
        rig.StepOnce();

        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        Assert.Single(navigation.Stalls);
        Assert.Equal(HaulingExecutionRig.Target, navigation.Stalls[0].Target);
        Assert.Equal(rig.Seam.Observation.CartPosition, navigation.Stalls[0].Cart);
        Assert.Equal(rig.Body.Facts.Position, navigation.Stalls[0].Puller);
        rig.AssertNoBugs();
    }

    [Fact]
    public void ARefreshThatNoLongerHoldsEndsTheLegAndOneThatDoesReplacesThePlan()
    {
        var navigation = new FakeNavigation();
        var rig = new HaulingExecutionRig(navigation: navigation);
        rig.RunToPulling();

        navigation.RefreshDue = true;
        rig.StepOnce();
        Assert.Equal(1, navigation.RefreshCalls);
        Assert.Equal(HaulPhase.Pulling, rig.Executor.Phase);
        Assert.Equal(navigation.LastRefreshed!.LengthMetres, rig.Executor.Plan!.LengthMetres);

        navigation.RefreshDue = true;
        navigation.RefreshVerdict = CartRouteVerdict.TooSteep;
        rig.StepOnce();

        Assert.Equal(2, navigation.RefreshCalls);
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void LeavingTheVerifiedCorridorRecoversAndFinishingStops()
    {
        var left = new FakeNavigation { SteeringStatus = HaulSteeringStatus.LeftCorridor };
        var rig = new HaulingExecutionRig(navigation: left);
        rig.RunToPulling();
        rig.StepOnce();
        Assert.Equal(HaulPhase.Recovering, rig.Executor.Phase);
        rig.AssertNoBugs();

        var finished = new FakeNavigation { SteeringStatus = HaulSteeringStatus.Finished };
        var arriving = new HaulingExecutionRig(navigation: finished);
        arriving.RunToPulling();
        arriving.StepOnce();
        Assert.Equal(HaulPhase.Stopping, arriving.Executor.Phase);
        arriving.AssertNoBugs();
    }
}
