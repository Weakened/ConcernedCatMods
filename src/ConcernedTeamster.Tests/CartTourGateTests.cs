using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The gates on Gunnar's cart-assisted collection tour (#381, owner
/// decision 2026-10-31, `DECISIONS.md` D22): whether a tour may start, whether
/// it may go on, and whether one load may go into the cart, decided over values
/// before the cart's container is touched; and the reading of the cart off a
/// <b>real</b> haul executor.
///
/// What this deliberately does <b>not</b> test is the container's own access -
/// ownership, in-use, ward, privacy, reach, id and drift - which the pinned
/// cart-load port asks through the shared permit mint at the moment of the
/// move, and which binds Unity.</summary>
public sealed class CartTourGateTests
{
    private static readonly CartKey Cart = new CartKey("-100:7", HaulingExecutionRig.Epoch);
    private static readonly WorkPoint ParkedAt = new WorkPoint(10f, 2f, 10f);

    private static AssignedCartFacts Parked(
        bool leaseHeld = true,
        CartKey? cart = null,
        bool resolved = true,
        bool idle = true,
        bool identityFree = true,
        bool hitched = false,
        bool containerOpen = false,
        WorkPoint? position = null) =>
        new AssignedCartFacts(
            leaseHeld,
            cart ?? Cart,
            resolved,
            idle,
            identityFree,
            hitched,
            containerOpen,
            position ?? ParkedAt,
            new WorkPoint(10f, 2f, 12f));

    private static CartTourRequest Start(
        bool cartTourEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool workerPresent = true,
        bool somethingInFlight = false,
        AssignedCartFacts? cart = null,
        bool areaSetThisWorld = true,
        bool cartInsideArea = true,
        bool carriedIsReadable = true,
        bool carryingCollected = false,
        int capacityUnits = 225) =>
        new CartTourRequest(
            cartTourEnabled,
            worldIsUp,
            authority,
            seamAvailable,
            workerRecordUnwritable,
            workerRecordUnreadable,
            workerPresent,
            somethingInFlight,
            cart ?? Parked(),
            areaSetThisWorld,
            cartInsideArea,
            carriedIsReadable,
            carryingCollected,
            capacityUnits);

    private static CartLoadRequest Load(
        bool cartTourEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool workerPresent = true,
        bool transferInFlight = false,
        CartTourRefusal cartWatch = CartTourRefusal.None,
        bool carriedIsReadable = true,
        bool somethingToLoad = true) =>
        new CartLoadRequest(
            cartTourEnabled,
            worldIsUp,
            authority,
            seamAvailable,
            workerRecordUnwritable,
            workerRecordUnreadable,
            workerPresent,
            transferInFlight,
            cartWatch,
            carriedIsReadable,
            somethingToLoad);

    // --- the switch -----------------------------------------------------------------------------

    [Fact]
    public void FillingACartIsOffByDefault()
    {
        // D22: off by default, a switch of its own, and the switches it also
        // needs are off by default too.
        Assert.False(GunnarCollectionDefaults.CartTourEnabled);
        Assert.False(GunnarCollectionDefaults.CollectionEnabled);
        Assert.False(GunnarHaulingDefaults.HaulingEnabled);
    }

    [Fact]
    public void DefaultedRequestsAreRefusedOnTheSwitch()
    {
        Assert.Equal(CartTourRefusal.FeatureOff, CartTourGate.EvaluateStart(default));
        Assert.Equal(CartTourRefusal.FeatureOff, CartTourGate.EvaluateLoad(default));
    }

    [Fact]
    public void AReadyStartAndAReadyLoadAreAdmitted()
    {
        Assert.Equal(CartTourRefusal.None, CartTourGate.EvaluateStart(Start()));
        Assert.Equal(CartTourRefusal.None, CartTourGate.EvaluateLoad(Load()));
    }

    [Fact]
    public void TheSwitchIsAskedBeforeAnythingElse()
    {
        Assert.Equal(
            CartTourRefusal.FeatureOff,
            CartTourGate.EvaluateStart(Start(
                cartTourEnabled: false,
                worldIsUp: false,
                authority: WorkAuthorityVerdict.Unspecified,
                seamAvailable: false,
                workerPresent: false,
                cart: default(AssignedCartFacts),
                areaSetThisWorld: false,
                capacityUnits: 0)));
        Assert.Equal(
            CartTourRefusal.FeatureOff,
            CartTourGate.EvaluateLoad(Load(cartTourEnabled: false, cartWatch: CartTourRefusal.CartMoved)));
    }

    [Fact]
    public void AnyAuthorityButGrantedRefusesBoth()
    {
        foreach (WorkAuthorityVerdict authority in System.Enum.GetValues(typeof(WorkAuthorityVerdict)))
        {
            CartTourRefusal expected = authority == WorkAuthorityVerdict.Granted
                ? CartTourRefusal.None
                : CartTourRefusal.WorkRefused;
            Assert.Equal(expected, CartTourGate.EvaluateStart(Start(authority: authority)));
            Assert.Equal(expected, CartTourGate.EvaluateLoad(Load(authority: authority)));
        }
    }

    // --- Gunnar and the room --------------------------------------------------------------------

    [Theory]
    [InlineData("world", (int)CartTourRefusal.WorldIsGoingAway)]
    [InlineData("seam", (int)CartTourRefusal.SeamUnavailable)]
    [InlineData("unwritable", (int)CartTourRefusal.WorkerRecordUnwritable)]
    [InlineData("unreadable", (int)CartTourRefusal.WorkerRecordUnreadable)]
    [InlineData("absent", (int)CartTourRefusal.NoWorker)]
    [InlineData("busy", (int)CartTourRefusal.AlreadyWorking)]
    public void GunnarAndTheRoomRefuseBoth(string what, int expectedValue)
    {
        var expected = (CartTourRefusal)expectedValue;
        Assert.Equal(expected, CartTourGate.EvaluateStart(Start(
            worldIsUp: what != "world",
            seamAvailable: what != "seam",
            workerRecordUnwritable: what == "unwritable",
            workerRecordUnreadable: what == "unreadable",
            workerPresent: what != "absent",
            somethingInFlight: what == "busy")));
        Assert.Equal(expected, CartTourGate.EvaluateLoad(Load(
            worldIsUp: what != "world",
            seamAvailable: what != "seam",
            workerRecordUnwritable: what == "unwritable",
            workerRecordUnreadable: what == "unreadable",
            workerPresent: what != "absent",
            transferInFlight: what == "busy")));
    }

    // --- the cart, at the start -----------------------------------------------------------------

    [Fact]
    public void NoAssignedCartRefusesAndNothingLooksForOne()
    {
        // There is no nearest-cart fallback: without a lease there is no cart.
        Assert.Equal(CartTourRefusal.NoCart, CartTourGate.EvaluateStart(Start(cart: Parked(leaseHeld: false))));
        Assert.Equal(CartTourRefusal.NoCart, CartTourGate.EvaluateStart(Start(cart: default(AssignedCartFacts))));
    }

    [Fact]
    public void ACartThatDoesNotResolveRefuses()
    {
        Assert.Equal(CartTourRefusal.CartNotHere, CartTourGate.EvaluateStart(Start(cart: Parked(resolved: false))));
    }

    [Fact]
    public void ACartTheHaulingRuntimeIsUsingRefuses()
    {
        Assert.Equal(CartTourRefusal.CartBusy, CartTourGate.EvaluateStart(Start(cart: Parked(idle: false))));
    }

    [Fact]
    public void SomebodyElseHoldingHisIdentityRefusesTheStart()
    {
        Assert.Equal(CartTourRefusal.CartBusy, CartTourGate.EvaluateStart(Start(cart: Parked(identityFree: false))));
    }

    [Fact]
    public void AHitchedCartRefuses()
    {
        Assert.Equal(CartTourRefusal.CartHitched, CartTourGate.EvaluateStart(Start(cart: Parked(hitched: true))));
    }

    [Fact]
    public void AnOpenCartRefusesTheStart()
    {
        Assert.Equal(CartTourRefusal.CartOpen, CartTourGate.EvaluateStart(Start(cart: Parked(containerOpen: true))));
    }

    [Fact]
    public void TheAreaMustBeSetAndTheCartMustStandInIt()
    {
        Assert.Equal(CartTourRefusal.NoArea, CartTourGate.EvaluateStart(Start(areaSetThisWorld: false)));
        Assert.Equal(CartTourRefusal.CartOutsideArea, CartTourGate.EvaluateStart(Start(cartInsideArea: false)));
    }

    [Fact]
    public void HeStartsEmptyHandedOfCollectedMaterial()
    {
        // Only what THIS tour picks goes in the cart, so a load he brought with
        // him refuses the start rather than becoming its first trip.
        Assert.Equal(CartTourRefusal.AlreadyCarrying, CartTourGate.EvaluateStart(Start(carryingCollected: true)));
        Assert.Equal(CartTourRefusal.CarriedUnreadable, CartTourGate.EvaluateStart(Start(carriedIsReadable: false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoCapacityRefuses(int capacity)
    {
        Assert.Equal(CartTourRefusal.NoCapacity, CartTourGate.EvaluateStart(Start(capacityUnits: capacity)));
    }

    // --- the cart, while the tour runs ----------------------------------------------------------

    [Fact]
    public void TheSameParkedCartGoesOn()
    {
        Assert.Equal(CartTourRefusal.None, CartTourGate.Watch(Parked(), Cart, ParkedAt));
    }

    [Fact]
    public void TheWatchDoesNotNeedHisIdentityFreeBecauseTheTourHoldsIt()
    {
        Assert.Equal(CartTourRefusal.None, CartTourGate.Watch(Parked(identityFree: false), Cart, ParkedAt));
    }

    [Fact]
    public void AnAssignmentThatEndedStopsTheTour()
    {
        Assert.Equal(CartTourRefusal.NoCart, CartTourGate.Watch(Parked(leaseHeld: false), Cart, ParkedAt));
        Assert.Equal(CartTourRefusal.NoCart, CartTourGate.Watch(default, Cart, ParkedAt));
    }

    [Fact]
    public void AnotherCartIsNeverFollowed()
    {
        var other = new CartKey("-100:8", HaulingExecutionRig.Epoch);
        var reloaded = new CartKey("-100:7", HaulingExecutionRig.NextEpoch);
        Assert.Equal(CartTourRefusal.CartChanged, CartTourGate.Watch(Parked(cart: other), Cart, ParkedAt));
        Assert.Equal(CartTourRefusal.CartChanged, CartTourGate.Watch(Parked(cart: reloaded), Cart, ParkedAt));
        Assert.Equal(CartTourRefusal.CartChanged, CartTourGate.Watch(Parked(), default, ParkedAt));
    }

    [Fact]
    public void ACartThatStoppedResolvingOrIsInUseStopsTheTour()
    {
        Assert.Equal(CartTourRefusal.CartNotHere, CartTourGate.Watch(Parked(resolved: false), Cart, ParkedAt));
        Assert.Equal(CartTourRefusal.CartBusy, CartTourGate.Watch(Parked(idle: false), Cart, ParkedAt));
        Assert.Equal(CartTourRefusal.CartHitched, CartTourGate.Watch(Parked(hitched: true), Cart, ParkedAt));
    }

    [Theory]
    [InlineData(0.9f, 0f, (int)CartTourRefusal.None)]
    [InlineData(1.1f, 0f, (int)CartTourRefusal.CartMoved)]
    [InlineData(0f, 1.1f, (int)CartTourRefusal.CartMoved)]
    [InlineData(float.NaN, 0f, (int)CartTourRefusal.CartMoved)]
    public void ACartThatMovedStopsTheTour(float dx, float dy, int expectedValue)
    {
        var expected = (CartTourRefusal)expectedValue;
        var moved = new WorkPoint(ParkedAt.X + dx, ParkedAt.Y + dy, ParkedAt.Z);
        Assert.Equal(expected, CartTourGate.Watch(Parked(position: moved), Cart, ParkedAt));
    }

    [Fact]
    public void AnOpenContainerIsLeftToTheLoadsOwnRefusal()
    {
        // The port refuses it as in use, and the tour stops on that refusal,
        // which names the right fix.
        Assert.Equal(CartTourRefusal.None, CartTourGate.Watch(Parked(containerOpen: true), Cart, ParkedAt));
    }

    [Fact]
    public void ACartTourArrivesWellInsideTheLoadsReach()
    {
        // The port measures reach from the live handle; arriving at the edge of
        // it would refuse a cart that merely settled (review of #381 item 2).
        CollectionLimits limits = CollectionLimits.Default;
        CollectionTourLimits atTheCart = CollectionTourLimits.AtTheCart(limits);
        Assert.True(atTheCart.DepositReachMetres < limits.PickupReachMetres);
        Assert.True(atTheCart.DepositReachMetres >= limits.ArrivalToleranceMetres);

        // Sources keep the ordinary reach (re-review of #381 item 2), and a chest
        // tour is unchanged.
        Assert.Equal(limits.PickupReachMetres, atTheCart.ReachMetres);
        Assert.Equal(limits.PickupReachMetres, CollectionTourLimits.Default(limits).ReachMetres);
        Assert.Equal(limits.PickupReachMetres, CollectionTourLimits.Default(limits).DepositReachMetres);
    }

    [Fact]
    public void ADepositReachBeyondTheReachIsRefused()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new CollectionTourLimits(2f, 12f, 10, depositReachMetres: 3f));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new CollectionTourLimits(2f, 12f, 10, depositReachMetres: 0f));
    }

    // --- one load -------------------------------------------------------------------------------

    [Fact]
    public void TheCartWatchRefusesTheLoad()
    {
        foreach (CartTourRefusal watch in new[]
                 {
                     CartTourRefusal.NoCart, CartTourRefusal.CartChanged, CartTourRefusal.CartNotHere,
                     CartTourRefusal.CartBusy, CartTourRefusal.CartHitched, CartTourRefusal.CartMoved,
                 })
        {
            Assert.Equal(watch, CartTourGate.EvaluateLoad(Load(cartWatch: watch)));
        }
    }

    [Fact]
    public void NothingThisTourPickedIsNothingToLoad()
    {
        Assert.Equal(CartTourRefusal.NothingToLoad, CartTourGate.EvaluateLoad(Load(somethingToLoad: false)));
        Assert.Equal(CartTourRefusal.CarriedUnreadable, CartTourGate.EvaluateLoad(Load(carriedIsReadable: false)));
    }

    [Fact]
    public void EveryRefusalHasASentence()
    {
        foreach (CartTourRefusal refusal in System.Enum.GetValues(typeof(CartTourRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(CartTourGate.Describe(refusal, WorkAuthorityVerdict.NotHost)));
        }

        foreach (NpcContainerRefusal refusal in System.Enum.GetValues(typeof(NpcContainerRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(CartLoadSentences.Describe(refusal)));
        }
    }

    // --- the cart, read off a real executor -----------------------------------------------------

    [Fact]
    public void NoLeaseReadsAsNoCart()
    {
        var rig = new HaulingExecutionRig();
        rig.Executor.ObserveFrame();
        AssignedCartFacts facts = AssignedCartFacts.Read(rig.Executor);
        Assert.False(facts.LeaseHeld);
        Assert.True(facts.Cart.IsEmpty);
        Assert.False(AssignedCartFacts.IsIdle(rig.Executor));
        Assert.False(AssignedCartFacts.Read(null).LeaseHeld);
        Assert.False(AssignedCartFacts.IsIdle(null));
    }

    [Fact]
    public void AnAssignedParkedCartReadsAsIdleWithHisIdentityFree()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        rig.Executor.ObserveFrame();

        AssignedCartFacts facts = AssignedCartFacts.Read(rig.Executor);
        Assert.True(facts.LeaseHeld);
        Assert.Equal(rig.Cart, facts.Cart);
        Assert.True(facts.Resolved);
        Assert.True(facts.Idle);
        Assert.True(facts.IdentityFree);
        Assert.False(facts.Hitched);
        Assert.Equal(rig.Seam.Observation.CartPosition, facts.Position);
        Assert.Equal(rig.Seam.Observation.ApproachPoint, facts.StandAt);
        Assert.True(AssignedCartFacts.IsIdle(rig.Executor));
        Assert.Equal(CartTourRefusal.None, CartTourGate.EvaluateStart(Start(cart: facts)));
    }

    [Fact]
    public void TheTourHoldingHisIdentityKeepsTheCartIdleButRefusesASecondStart()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        rig.Executor.ObserveFrame();
        var lease = new CollectionIdentityLease();
        Assert.True(lease.TryAcquire(rig.Executor.Modes));

        AssignedCartFacts facts = AssignedCartFacts.Read(rig.Executor);
        Assert.True(facts.Idle);
        Assert.False(facts.IdentityFree);
        Assert.Equal(CartTourRefusal.None, CartTourGate.Watch(facts, rig.Cart, facts.Position));
        Assert.Equal(CartTourRefusal.CartBusy, CartTourGate.EvaluateStart(Start(cart: facts)));

        // And the hauling runtime cannot start a haul under it: the identity is
        // the tour's.
        HaulCommandResult go = rig.Go();
        Assert.NotEqual(HaulCommandOutcome.Accepted, go.Outcome);
        lease.Release();
    }

    [Fact]
    public void AHaulInProgressIsNotIdle()
    {
        var rig = new HaulingExecutionRig();
        rig.AssignAndStartLeg();
        rig.Executor.ObserveFrame();

        AssignedCartFacts facts = AssignedCartFacts.Read(rig.Executor);
        Assert.True(facts.LeaseHeld);
        Assert.False(facts.Idle);
        Assert.False(AssignedCartFacts.IsIdle(rig.Executor));
        Assert.Equal(CartTourRefusal.CartBusy, CartTourGate.Watch(facts, rig.Cart, facts.Position));
    }

    [Fact]
    public void AHitchedCartReadsAsHitched()
    {
        var rig = new HaulingExecutionRig();
        Assert.Equal(AssignmentOutcome.Assigned, rig.Assign().Outcome);
        rig.Seam.Observation = rig.Seam.Observation.With(o => o.HasJoint = true);
        rig.Executor.ObserveFrame();

        AssignedCartFacts facts = AssignedCartFacts.Read(rig.Executor);
        Assert.True(facts.Hitched);
        Assert.NotEqual(CartTourRefusal.None, CartTourGate.Watch(facts, rig.Cart, facts.Position));
    }
}
