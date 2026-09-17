using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>#317 COOP-02/COOP-03: the cooperative delivery loop against a
/// simulated Gunnar, an in-memory custody executor and a fake Thorstein. Every
/// scenario also checks the invariants that must hold on any path: no cart
/// transfer without an accepted hold, no transfer into the cart before its
/// baseline, the cart never asked to move while held, no request id reused with
/// another payload, and no material credited or refunded by bookkeeping.
/// </summary>
public sealed class CooperationLoopTests
{
    [Fact]
    public void TheHappyPathSurveysStagesLoadsHaulsUnloadsAndCompletes()
    {
        var run = new CooperationScenario(cartPreExistingUnits: 10);

        CooperationTick last = run.RunUntil(tick => tick.Step == CooperationStep.Delivered);

        Assert.Equal(CooperationPhase.Completed, last.Phase);
        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        run.AssertLedger(CustodyPlace.Cart, stone: 0, wood: 0);
        run.AssertLedger(CustodyPlace.Worker, stone: 0, wood: 0);
        Assert.Equal(20, run.Custody.Chest.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(30, run.Custody.Chest.Count(MaterialItem.Of(CollectedResource.Wood)));
        Assert.Equal(50, run.Custody.Chest.Total);
        Assert.Equal(0, run.Custody.Cart.Total);
        Assert.Equal(1, run.Custody.BaselineRecords);
        Assert.Equal(1, run.Loop.DeliveryTrips);
        Assert.Equal(2, run.Gunnar.Holds);
        Assert.Equal(2, run.Gunnar.Releases);
        Assert.Equal(new[] { HaulCancelDisposition.DetachAndPark }, run.Gunnar.Cancels);
        Assert.Equal(HaulWirePhase.Ready, run.Gunnar.Phase);
        Assert.False(run.Gunnar.Attached);
        Assert.Contains(run.Ticks, tick => tick.Step == CooperationStep.CollectMore);
        Assert.Contains(run.Ticks, tick => tick.Phase == CooperationPhase.AwaitingLoad);
        Assert.Contains(run.Ticks, tick => tick.Phase == CooperationPhase.Unloading);
        run.AssertInvariants();
    }

    [Theory]
    [InlineData(50, 100, 1)]
    [InlineData(20, 25, 2)]
    public void TickedOnlyAtTheCollectionLoopsCheckpointsTheRunNeverPingPongsAndCompletes(int pack, int cart, int trips)
    {
        var run = new CooperationScenario(packUnits: pack, cartUnits: cart) { OwnerTicksOnlyAtCheckpoints = true };

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered, maxTicks: 2000);

        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        Assert.Equal(trips, run.Loop.DeliveryTrips);
        int handBacks = run.Ticks.Count(tick => tick.Step == CooperationStep.CollectMore);
        Assert.True(handBacks <= 2 * trips + 2, "handed back " + handBacks + " times");
        run.AssertInvariants();
    }

    [Fact]
    public void ACapacityCheckpointTakesASecondTrip()
    {
        var run = new CooperationScenario(packUnits: 20, cartUnits: 25);

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);

        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        Assert.Equal(2, run.Loop.DeliveryTrips);
        Assert.True(run.Gunnar.Legs >= 4, "rendezvous, chest, rendezvous, chest");
        Assert.Equal(50, run.Custody.Chest.Total);
        run.AssertInvariants();
    }

    [Fact]
    public void AnExhaustedAreaEndsInAttentionAndCancellingRefundsNothing()
    {
        var run = new CooperationScenario { GroundUnits = 12 };

        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);

        Assert.Equal(CollectionAttentionReason.RendezvousTimedOut, stopped.Reason);
        Assert.True(run.Now >= CooperationLimits.Default.RendezvousTimeoutSeconds);
        Assert.Contains(HaulCancelDisposition.StopAndWait, run.Gunnar.Cancels);
        run.AssertLedger(CustodyPlace.Worker, stone: 12, wood: 0);

        run.Loop.Cancel(detachAndPark: true, run.Now);
        Assert.Equal(CooperationStep.Cancelled, run.Step().Step);
        Assert.Contains(HaulCancelDisposition.DetachAndPark, run.Gunnar.Cancels);
        Assert.Equal(HaulWirePhase.Ready, run.Gunnar.Phase);
        run.AssertLedger(CustodyPlace.Worker, stone: 12, wood: 0);
        run.AssertLedger(CustodyPlace.Destination, stone: 0, wood: 0);
        run.AssertInvariants();
    }

    [Fact]
    public void LosingTheProviderMidHaulPausesAfterReconcilingTheCart()
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.Hauling);
        int receipts = run.Custody.Receipts.Count;

        run.Gunnar.Silent = true;
        CooperationTick paused = run.RunUntil(tick => tick.Step == CooperationStep.Paused);

        Assert.Equal(CollectionAttentionReason.HaulerUnavailable, paused.Reason);
        Assert.Contains("stay in the cart", paused.Detail);
        Assert.Equal(receipts, run.Custody.Receipts.Count);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        Assert.Equal(0, run.Custody.Chest.Total);
        run.AssertInvariants();
    }

    [Fact]
    public void ADestroyedCartWithMaterialAboardNeedsAttentionWithEvidenceAndNothingMoves()
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.Hauling);
        int receipts = run.Custody.Receipts.Count;

        run.Gunnar.EndControl(HaulWireReason.CartDestroyed);
        run.Custody.CartResolvable = false;
        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);

        Assert.Equal(CollectionAttentionReason.CartLeaseLost, stopped.Reason);
        Assert.Contains("20 Stone and 30 Wood", stopped.Detail);
        Assert.Equal(receipts, run.Custody.Receipts.Count);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void ACartThatNoLongerMatchesTheRecordIsAMismatchNeverACredit()
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.Hauling);

        run.Custody.Cart.TakeBehindTheRecordsBack(CollectedResource.Stone, 5);
        run.Gunnar.EndControl(HaulWireReason.PlayerTookOver);
        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);

        Assert.Equal(CollectionAttentionReason.ReconciliationMismatch, stopped.Reason);
        Assert.Contains("20 Stone", stopped.Detail);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);

        // The record keeps what it saw; the five taken are for a person to
        // resolve, never silently written off or re-credited.
        run.AssertInvariants(materialConserved: false);
    }

    [Fact]
    public void ARendezvousThatNeverHappensTimesOutAndStopsGunnar()
    {
        var run = new CooperationScenario();
        run.Gunnar.TravelSeconds = 100000f;

        CooperationTick stopped = run.RunUntil(tick => tick.Step != CooperationStep.CollectMore && tick.Step != CooperationStep.Working);

        Assert.Equal(CooperationStep.NeedsAttention, stopped.Step);
        Assert.Equal(CollectionAttentionReason.RendezvousTimedOut, stopped.Reason);
        Assert.Contains(HaulCancelDisposition.StopAndWait, run.Gunnar.Cancels);
        Assert.Empty(run.Custody.Receipts);
        run.AssertInvariants();
    }

    [Fact]
    public void AFullChestPausesWithTheRestRetainedInTheCart()
    {
        var run = new CooperationScenario(chestUnits: 30);

        CooperationTick paused = run.RunUntil(tick => tick.Step == CooperationStep.Paused);

        Assert.Equal(CollectionAttentionReason.DestinationFull, paused.Reason);
        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 10);
        run.AssertLedger(CustodyPlace.Cart, stone: 0, wood: 20);
        Assert.Equal(20, run.Custody.Cart.Count(MaterialItem.Of(CollectedResource.Wood)));
        Assert.Equal(HaulWirePhase.Waiting, run.Gunnar.Phase);
        run.AssertInvariants();
    }

    [Fact]
    public void AChestThatRefusesPausesWithItsOwnReason()
    {
        var run = new CooperationScenario();
        run.Custody.ContainerRefusal = CollectionAttentionReason.DestinationAccessDenied;

        CooperationTick paused = run.RunUntil(tick => tick.Step == CooperationStep.Paused);

        Assert.Equal(CollectionAttentionReason.DestinationAccessDenied, paused.Reason);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        Assert.Equal(HaulWirePhase.Waiting, run.Gunnar.Phase);
        run.AssertInvariants();
    }

    [Fact]
    public void AStaleRevisionOnTheHoldIsReadAgainAndNeverForced()
    {
        var run = new CooperationScenario();
        bool bumped = false;
        run.Gunnar.BeforeApply = message =>
        {
            if (!bumped && message is AcknowledgeWaitMessage { Activity: HaulWaitActivity.Transferring })
            {
                bumped = true;
                run.Gunnar.BumpRevision();
            }
        };

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);

        Assert.True(bumped);
        Assert.Equal(2, run.Gunnar.Holds);
        Assert.True(run.Gunnar.Log.Count(op => op == "acknowledgeWait") >= 5, "a stale hold, two holds, two releases");
        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void ALostReplyIsRetriedWithTheSameRequestAndAppliedOnce()
    {
        var run = new CooperationScenario();
        run.Gunnar.LoseRepliesAfterApplying = 1;

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);

        Assert.Equal(2, run.Gunnar.Legs);
        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingMidHaulStopsOrParksGunnarAndRefundsNothing(bool detachAndPark)
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.Hauling && run.Gunnar.Phase == HaulWirePhase.Pulling);

        run.Loop.Cancel(detachAndPark, run.Now);

        Assert.Equal(CooperationPhase.Cancelled, run.Loop.Phase);
        Assert.Equal(detachAndPark ? HaulCancelDisposition.DetachAndPark : HaulCancelDisposition.StopAndWait, run.Gunnar.Cancels.Last());
        Assert.Equal(detachAndPark ? HaulWirePhase.Ready : HaulWirePhase.Waiting, run.Gunnar.Phase);
        Assert.Equal(!detachAndPark, run.Gunnar.Attached);
        Assert.Equal(CooperationStep.Cancelled, run.Step().Step);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void ARouteRefusalMovesToTheNextMeetingPointAndAllRefusedNeedsAttention()
    {
        var run = new CooperationScenario();
        int refusals = 0;
        run.Gunnar.RouteCheck = (target, toChest) => !toChest && refusals++ == 0 ? HaulWireReason.TooNarrow : (HaulWireReason?)null;

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);
        Assert.True(run.Loop.RendezvousRevision >= 2);
        run.AssertInvariants();

        var blocked = new CooperationScenario();
        blocked.Gunnar.RouteCheck = (target, toChest) => HaulWireReason.Water;
        CooperationTick stopped = blocked.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);
        Assert.Equal(CollectionAttentionReason.HaulerNeedsAttention, stopped.Reason);
        Assert.Contains("Water", stopped.Detail);
        Assert.Equal(0, blocked.Gunnar.Legs);
    }

    [Fact]
    public void AChestOutOfTheCartsReachIsServedByCarryingAcross()
    {
        var run = new CooperationScenario();
        run.Gunnar.StopShortMetres = 8f;

        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);

        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        Assert.Contains(run.Custody.Intents, intent => intent.From.Place == CustodyPlace.Cart && intent.To.Place == CustodyPlace.Worker);
        Assert.DoesNotContain(run.Custody.Intents, intent => intent.From.Place == CustodyPlace.Cart && intent.To.Place == CustodyPlace.Destination);
        run.AssertInvariants();
    }

    [Fact]
    public void ARetriedCartTransferNeverRunsOnAHoldThatEndedInBetween()
    {
        var run = new CooperationScenario();
        run.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Refused);
        run.RunUntil(tick => run.Custody.Receipts.Count == 1);
        Assert.Equal(HaulWirePhase.Unloading, run.Gunnar.Phase);

        run.Gunnar.EndControl(HaulWireReason.PlayerTookOver);
        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.Paused || tick.Step == CooperationStep.NeedsAttention);

        Assert.Single(run.Custody.Receipts);
        Assert.Equal(CollectionAttentionReason.HaulerNeedsAttention, stopped.Reason);
        run.AssertLedger(CustodyPlace.Worker, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void AnUncertainTransferStopsWithoutReplayOrCompensation()
    {
        var run = new CooperationScenario();
        run.Custody.ForcedOutcomes.Enqueue(TransferOutcome.Uncertain);

        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);

        Assert.Equal(CollectionAttentionReason.TransferUncertain, stopped.Reason);
        int intents = run.Custody.Intents.Count;
        for (int index = 0; index < 20; index++)
        {
            run.Step();
        }

        Assert.Equal(intents, run.Custody.Intents.Count);
        Assert.Equal(HaulWirePhase.Waiting, run.Gunnar.Phase);
        run.AssertInvariants();
    }

    [Fact]
    public void PausingAndResumingContinuesTheSameHaul()
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.AwaitingLoad);

        run.Loop.Pause(run.Now);
        CooperationTick paused = run.Step();
        Assert.Equal(CooperationStep.Paused, paused.Step);
        Assert.Equal(CollectionAttentionReason.PausedByPlayer, paused.Reason);
        Assert.True(run.Loop.PausedByPlayer);

        Assert.True(run.Loop.Resume(run.Now));
        run.RunUntil(tick => tick.Step == CooperationStep.Delivered);
        run.AssertLedger(CustodyPlace.Destination, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void WithoutAUsableHaulerTheRunPausesWithAnActionableReason()
    {
        var absent = new CooperationScenario();
        absent.Gunnar.Discovery = HaulDiscovery.Hidden(CapabilityStatus.Absent, "unknown", "not installed");
        Assert.Equal(CollectionAttentionReason.HaulerUnavailable, absent.Step().Reason);

        var noCart = new CooperationScenario();
        noCart.Gunnar.LeaseId = string.Empty;
        CooperationTick noLease = noCart.Step();
        Assert.Equal(CooperationStep.Paused, noLease.Step);
        Assert.Contains("No cart", noLease.Detail);

        var peers = new CooperationScenario();
        peers.Gunnar.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        Assert.Equal(CollectionAttentionReason.OtherPeersConnected, peers.Step().Reason);

        var holdForPlayer = new CooperationScenario(holdForPlayer: true);
        CooperationTick hold = holdForPlayer.Step();
        Assert.Equal(CooperationStep.NeedsAttention, hold.Step);
        Assert.Equal(CollectionAttentionReason.DestinationUnavailable, hold.Reason);

        var reloaded = new CooperationScenario();
        reloaded.RunUntil(tick => tick.Phase == CooperationPhase.Hauling);
        reloaded.Gunnar.Reload(new Guid("ee000000-0000-0000-0000-000000000099"));
        CooperationTick lost = reloaded.RunUntil(tick => tick.Step == CooperationStep.NeedsAttention);
        Assert.Equal(CollectionAttentionReason.CartLeaseLost, lost.Reason);
        reloaded.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        reloaded.AssertInvariants();
    }

    [Fact]
    public void AvailabilityNamesWhyGunnarCannotJoinAndReadsNothingElse()
    {
        CooperationAvailability Probe(CooperationScenario scenario, out CollectionAttentionReason reason) =>
            CooperationAvailabilityProbe.Evaluate(scenario.Client, 1f, out reason, out _);

        var ready = new CooperationScenario();
        Assert.Equal(CooperationAvailability.Available, Probe(ready, out CollectionAttentionReason none));
        Assert.Equal(CollectionAttentionReason.Unspecified, none);
        Assert.DoesNotContain(ready.Gunnar.Log, op => op != "hello" && op != "describeLease");

        var absent = new CooperationScenario();
        absent.Gunnar.Discovery = HaulDiscovery.Hidden(CapabilityStatus.VersionTooLow, "1.0.4", "below 1.0.5");
        Assert.Equal(CooperationAvailability.ProviderTooOld, Probe(absent, out _));

        var noCart = new CooperationScenario();
        noCart.Gunnar.LeaseId = string.Empty;
        Assert.Equal(CooperationAvailability.NoCartAssigned, Probe(noCart, out CollectionAttentionReason noCartReason));
        Assert.Equal(CollectionAttentionReason.HaulerUnavailable, noCartReason);

        var peers = new CooperationScenario();
        peers.Gunnar.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        Assert.Equal(CooperationAvailability.OtherPeersConnected, Probe(peers, out CollectionAttentionReason peersReason));
        Assert.Equal(CollectionAttentionReason.OtherPeersConnected, peersReason);

        var stopped = new CooperationScenario();
        stopped.Gunnar.EndControl(HaulWireReason.BrakeEngaged);
        Assert.Equal(CooperationAvailability.GunnarNeedsAttention, Probe(stopped, out CollectionAttentionReason stoppedReason));
        Assert.Equal(CollectionAttentionReason.HaulerNeedsAttention, stoppedReason);

        var tipped = new CooperationScenario();
        tipped.Gunnar.CartUpright = false;
        Assert.Equal(CooperationAvailability.CartNotUpright, Probe(tipped, out _));
    }

    [Fact]
    public void AMissingThorsteinStopsTheRunWhereItWouldHaveMovedHim()
    {
        var run = new CooperationScenario();
        run.RunUntil(tick => tick.Phase == CooperationPhase.Hauling);

        run.Thorstein.IsPresent = false;
        CooperationTick stopped = run.RunUntil(tick => tick.Step == CooperationStep.Paused || tick.Step == CooperationStep.NeedsAttention);

        Assert.Equal(CollectionAttentionReason.WorkerBodyLost, stopped.Reason);
        run.AssertLedger(CustodyPlace.Cart, stone: 20, wood: 30);
        run.AssertInvariants();
    }

    [Fact]
    public void RendezvousCandidatesAreOrderedDistinctAndBounded()
    {
        var scope = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 30f, 0f), 30f, "your bed", 1, CooperationScenario.Epoch);

        IReadOnlyList<SitePoint> candidates = RendezvousPlanner.Candidates(scope, new SitePoint(100f, 30f, 0f), new SitePoint(0f, 31f, -40f), 4);
        Assert.Equal(new[] { new SitePoint(15f, 30f, 0f), new SitePoint(30f, 30f, 0f), new SitePoint(0f, 30f, 0f), new SitePoint(0f, 31f, -40f) }, candidates);

        Assert.Equal(2, RendezvousPlanner.Candidates(scope, new SitePoint(100f, 30f, 0f), null, 2).Count);
        Assert.Single(RendezvousPlanner.Candidates(scope, new SitePoint(0.2f, 30f, 0f), null, 4));

        SitePoint standOff = RendezvousPlanner.StandOff(new SitePoint(10f, 30f, 0f), new SitePoint(0f, 30f, 0f), 1.2f);
        Assert.Equal(1.2f, standOff.HorizontalDistanceTo(new SitePoint(10f, 30f, 0f)), 3);
    }
}

/// <summary>One cooperative order with its world of fakes: 20 Stone and 30 Wood
/// by default, a chest 40 m from the camp, Gunnar's cart parked nearby.
/// While the loop answers CollectMore, a stand-in for the collection loop
/// picks up to five units a tick into Thorstein's pack.</summary>
internal sealed class CooperationScenario
{
    public static readonly Guid Epoch = new Guid("ee000000-0000-0000-0000-000000000001");

    public CooperationScenario(
        int stone = 20, int wood = 30, int packUnits = 50, int cartUnits = 100, int chestUnits = 200,
        int cartPreExistingUnits = 0, bool holdForPlayer = false)
    {
        var scope = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 30f, 0f), 30f, "your bed", 1, Epoch);
        DeliveryTarget delivery = holdForPlayer
            ? DeliveryTarget.HoldForPlayer()
            : DeliveryTarget.ToContainer("chest-1", Epoch, new SitePoint(40f, 30f, 0f));
        Order = new CollectionOrderDefinition(
            new OrderId("collect-1"), new WorkerId("thorstein"),
            new[] { new ResourceQuota(CollectedResource.Stone, stone), new ResourceQuota(CollectedResource.Wood, wood) },
            scope, delivery, ParticipationMode.WithHauler, "tester");
        Gunnar = new CooperationFakeGunnar(Epoch, new WorkPoint(5f, 30f, 5f));
        Custody = new CooperationFakeCustody(
            Order,
            new CooperationFakeInventory("Thorstein's pack", packUnits),
            new CooperationFakeInventory("the cart", cartUnits, cartPreExistingUnits),
            new CooperationFakeInventory("the chest", chestUnits));
        Custody.CartIsHeld = () => Gunnar.Phase == HaulWirePhase.Unloading;
        Custody.CurrentProviderEpoch = () => Gunnar.Epoch;
        Thorstein = new CooperationFakeWorker(new SitePoint(0f, 30f, 0f));
        Client = new HaulClient(Gunnar, CooperationLimits.Default, "0.2.0");
        Loop = new CooperativeDeliveryLoop(
            Order, Client, Thorstein, Custody, CooperationLimits.Default, new Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"));
    }

    public float Now { get; private set; }

    /// <summary>How much more the ground can give.</summary>
    public int GroundUnits { get; set; } = int.MaxValue;

    public CollectionOrderDefinition Order { get; }

    public CooperationFakeGunnar Gunnar { get; }

    public CooperationFakeCustody Custody { get; }

    public CooperationFakeWorker Thorstein { get; }

    public HaulClient Client { get; }

    public CooperativeDeliveryLoop Loop { get; }

    public List<CooperationTick> Ticks { get; } = new List<CooperationTick>();

    /// <summary>Tick the run only the way agent C's collection loop does: at a
    /// carry checkpoint (pack full, or everything still needed covered) with a
    /// hand-over, and then every frame while it answers Working. Between
    /// checkpoints time passes without a tick.</summary>
    public bool OwnerTicksOnlyAtCheckpoints { get; set; }

    public CooperationTick Step(float seconds = 0.6f)
    {
        CooperationStep last = Ticks.Count == 0 ? CooperationStep.Unspecified : Ticks[Ticks.Count - 1].Step;
        if (OwnerTicksOnlyAtCheckpoints && (last == CooperationStep.Unspecified || last == CooperationStep.CollectMore))
        {
            for (int guard = 0; guard < 200 && CollectSome(); guard++)
            {
                Now += seconds;
            }

            Loop.HandOver();
        }

        Now += seconds;
        Gunnar.Now = Now;
        Gunnar.Advance();
        CooperationTick tick = Loop.Tick(Now);
        Ticks.Add(tick);
        if (tick.Step == CooperationStep.CollectMore && !OwnerTicksOnlyAtCheckpoints)
        {
            CollectSome();
        }

        return tick;
    }

    public CooperationTick RunUntil(Func<CooperationTick, bool> stop, int maxTicks = 4000)
    {
        for (int index = 0; index < maxTicks; index++)
        {
            CooperationTick tick = Step();
            if (stop(tick))
            {
                return tick;
            }
        }

        throw new Xunit.Sdk.XunitException(
            "The run did not reach the expected tick: " + Loop.Phase + " " + Loop.Reason + " - " + Loop.Detail);
    }

    public void AssertLedger(CustodyPlace place, int stone, int wood)
    {
        Assert.Equal(stone, Custody.CountAt(Order.Order, place, CollectedResource.Stone));
        Assert.Equal(wood, Custody.CountAt(Order.Order, place, CollectedResource.Wood));
    }

    public void AssertInvariants(bool materialConserved = true)
    {
        Assert.Empty(Gunnar.Violations);
        Assert.Equal(0, Custody.CartTransfersWithoutHold);
        Assert.Equal(0, Custody.TransfersBeforeBaseline);
        Assert.Equal(0, Custody.StaleCartResolutions);

        // Every unit gathered is in exactly one place; nothing was minted or
        // refunded by the record alone.
        foreach (CollectedResource resource in new[] { CollectedResource.Stone, CollectedResource.Wood })
        {
            MaterialItem item = MaterialItem.Of(resource);
            int recorded = Custody.CountAt(Order.Order, CustodyPlace.Worker, resource) +
                Custody.CountAt(Order.Order, CustodyPlace.Cart, resource) +
                Custody.CountAt(Order.Order, CustodyPlace.Destination, resource);
            int actual = Custody.Pack.Count(item) + Custody.Cart.Count(item) + Custody.Chest.Count(item);
            if (materialConserved && !Custody.HasUncertainTransfer(Order.Order))
            {
                Assert.Equal(actual, recorded);
            }
        }
    }

    private bool CollectSome()
    {
        foreach (ResourceQuota quota in Order.Quotas)
        {
            int still = Custody.ProgressFor(Order, quota.Resource).StillToCollect;
            int count = Math.Min(Math.Min(5, still), GroundUnits);
            if (count <= 0)
            {
                continue;
            }

            int added = Custody.Collect(quota.Resource, count);
            GroundUnits -= added;
            if (added > 0)
            {
                return true;
            }
        }

        return false;
    }
}
