using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Contract revision C1 of Gunnar's cart work
/// (docs/settlement/cart-and-collection/CONTRACTS.md §2): the phase table, the
/// lease rules and the wire mirror, pinned before the mechanics, navigation and
/// cooperation agents build on them.</summary>
public class HaulContractTests
{
    private static readonly Guid Epoch = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid NextEpoch = new Guid("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public void AHitchedPhaseNeverJumpsToReadyOrUnassignedWithoutDetaching()
    {
        foreach (HaulPhase phase in HaulPhases.All())
        {
            if (phase == HaulPhase.Detaching || phase == HaulPhase.Paused || phase == HaulPhase.NeedsAttention)
            {
                continue;
            }

            if (HaulPhases.MayHoldJoint(phase))
            {
                Assert.False(HaulPhases.CanTransition(phase, HaulPhase.Ready), phase + " -> Ready");
                Assert.False(HaulPhases.CanTransition(phase, HaulPhase.Unassigned), phase + " -> Unassigned");
            }
        }

        Assert.True(HaulPhases.CanTransition(HaulPhase.Pulling, HaulPhase.Detaching));
        Assert.True(HaulPhases.CanTransition(HaulPhase.Detaching, HaulPhase.Ready));
    }

    [Fact]
    public void TheDocumentedLifecycleIsLegalEndToEnd()
    {
        HaulPhase[] path =
        {
            HaulPhase.Unassigned, HaulPhase.Ready, HaulPhase.Approaching, HaulPhase.Hitching, HaulPhase.Pulling,
            HaulPhase.Stopping, HaulPhase.Waiting, HaulPhase.Unloading, HaulPhase.Waiting, HaulPhase.Pulling,
            HaulPhase.Recovering, HaulPhase.Pulling, HaulPhase.Stopping, HaulPhase.Waiting, HaulPhase.Detaching,
            HaulPhase.Ready, HaulPhase.Unassigned,
        };

        for (int index = 1; index < path.Length; index++)
        {
            Assert.True(HaulPhases.CanTransition(path[index - 1], path[index]), path[index - 1] + " -> " + path[index]);
        }
    }

    [Fact]
    public void NoPhaseTransitionsToItselfOrOutOfUnspecified()
    {
        foreach (HaulPhase phase in HaulPhases.All())
        {
            Assert.False(HaulPhases.CanTransition(phase, phase));
            Assert.False(HaulPhases.CanTransition(HaulPhase.Unspecified, phase));
        }

        Assert.False(HaulPhases.CanTransition(HaulPhase.Unloading, HaulPhase.Pulling));
        Assert.True(HaulPhases.ForbidsMotion(HaulPhase.Unloading));
    }

    [Fact]
    public void EveryPhaseTravelsUnderItsOwnName()
    {
        foreach (HaulPhase phase in HaulPhases.All())
        {
            Assert.True(Enum.TryParse(phase.ToString(), out HaulWirePhase wire), phase.ToString());
            Assert.Equal((int)phase, (int)wire);
        }

        Assert.Equal(Enum.GetNames<HaulPhase>(), Enum.GetNames<HaulWirePhase>());
    }

    [Fact]
    public void OneLeasePerWorkerOneLeasePerCartAndPayloadCheckedIds()
    {
        var book = new CartLeaseBook(Epoch);
        var cart = new CartKey("-123:44", Epoch);
        var otherCart = new CartKey("-123:45", Epoch);
        var otherWorker = new WorkerKey(WorkerKey.TeamsterProduct, "second");

        Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-1", WorkerKey.Gunnar, cart));
        Assert.Equal(LeaseOutcome.AlreadySatisfied, book.Assign("lease-1", WorkerKey.Gunnar, cart));
        Assert.Equal(LeaseOutcome.RejectedDifferentPayload, book.Assign("lease-1", WorkerKey.Gunnar, otherCart));
        Assert.Equal(LeaseOutcome.RefusedWorkerBusy, book.Assign("lease-2", WorkerKey.Gunnar, otherCart));
        Assert.Equal(LeaseOutcome.RefusedCartLeased, book.Assign("lease-3", otherWorker, cart));
        Assert.Equal(LeaseOutcome.RefusedStaleEpoch, book.Assign("lease-4", otherWorker, new CartKey("-123:46", NextEpoch)));

        Assert.True(book.TryGetActiveForCart(cart, out CartLease? lease));
        Assert.Equal("lease-1", lease!.LeaseId);
        Assert.Equal(LeaseOutcome.Released, book.Release("lease-1"));
        Assert.Equal(LeaseOutcome.NotActive, book.Release("lease-1"));
        Assert.Equal(LeaseOutcome.NotActive, book.Assign("lease-1", WorkerKey.Gunnar, cart));
        Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-5", otherWorker, cart));
    }

    [Fact]
    public void AReloadEndsEveryLeaseAndStaleKeysNeverComeBack()
    {
        var book = new CartLeaseBook(Epoch);
        var cart = new CartKey("-123:44", Epoch);
        Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-1", WorkerKey.Gunnar, cart));

        Assert.Equal(1, book.BeginWorldLoad(NextEpoch));
        Assert.True(book.TryGet("lease-1", out CartLease? ended));
        Assert.Equal(CartLeaseState.Invalidated, ended!.State);
        Assert.Equal(LeaseInvalidation.WorldReloaded, ended.Invalidation);
        Assert.False(book.TryGetActiveForWorker(WorkerKey.Gunnar, out _));

        // The same cart after the reload is a different key; the old one is refused.
        Assert.Equal(LeaseOutcome.RefusedStaleEpoch, book.Assign("lease-2", WorkerKey.Gunnar, cart));
        Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-2", WorkerKey.Gunnar, new CartKey("-987:1", NextEpoch)));
        Assert.Throws<ArgumentException>(() => book.BeginWorldLoad(NextEpoch));
    }

    [Fact]
    public void InvalidationNeedsAReasonAndOnlyEndsActiveLeases()
    {
        var book = new CartLeaseBook(Epoch);
        Assert.Equal(LeaseOutcome.Assigned, book.Assign("lease-1", WorkerKey.Gunnar, new CartKey("-1:1", Epoch)));
        Assert.Throws<ArgumentOutOfRangeException>(() => book.Invalidate("lease-1", LeaseInvalidation.Unspecified));
        Assert.Equal(LeaseOutcome.Invalidated, book.Invalidate("lease-1", LeaseInvalidation.PlayerRevoked));
        Assert.Equal(LeaseOutcome.NotActive, book.Invalidate("lease-1", LeaseInvalidation.CartDestroyed));
        Assert.Equal(LeaseOutcome.NotFound, book.Invalidate("missing", LeaseInvalidation.CartDestroyed));
    }

    [Fact]
    public void HaulLimitDefaultsAreInsideTheirDesignedRanges()
    {
        HaulLimits limits = HaulLimits.Default.Validate();
        Assert.True(limits.HitchReachFraction <= 0.9f);
        limits.MaxHitchAttempts = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());
    }

    [Fact]
    public void ARefusedRouteHasNoWaypoints()
    {
        CartRoutePlan refused = CartRoutePlan.Refused(CartRouteVerdict.TooNarrow, 3);
        Assert.False(refused.IsSuitable);
        Assert.Empty(refused.Waypoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => CartRoutePlan.Refused(CartRouteVerdict.Suitable, 3));
    }
}
