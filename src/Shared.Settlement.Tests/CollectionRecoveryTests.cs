using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>R2 B1: what happens to a collection order that the settlement
/// record kept across a reload. The loop is new and holds nothing; unless it
/// takes the order up again, every command that could end it answers "there is
/// no order" while the record says there is one — and the material in the
/// worker's body and the body itself can then never be released.</summary>
public sealed class CollectionRecoveryTests
{
    private static readonly Guid PreviousLoad = new Guid("aaaaaaaa-1111-2222-3333-444444444444");

    /// <summary>The order as the record kept it: its work area and its chest
    /// were snapshotted in the world load before this one.</summary>
    private static CollectionOrderDefinition StaleOrder(bool hold = false, string id = "collect-ab12cd34")
    {
        var scope = new WorkScope(
            WorkScopeSource.DefaultCampCircle, CollectionRig.Anchor, 30f, "your bed",
            ScopeRevision.ForAnchor("bed", CollectionRig.Anchor), PreviousLoad);
        DeliveryTarget delivery = hold
            ? DeliveryTarget.HoldForPlayer()
            : DeliveryTarget.ToContainer("00000000cafe0000:00000042", PreviousLoad, CollectionRig.ChestAt);
        return new CollectionOrderDefinition(
            new OrderId(id), new WorkerId("thorstein"),
            new[] { new ResourceQuota(CollectedResource.Stone, 10) }, scope, delivery, ParticipationMode.Solo, "TESTER");
    }

    private static WorkScope FreshScope() =>
        new WorkScope(
            WorkScopeSource.DefaultCampCircle, CollectionRig.Anchor, 30f, "your bed",
            ScopeRevision.ForAnchor("bed", CollectionRig.Anchor), CollectionRig.Epoch);

    private static DeliveryTarget FreshChest() =>
        DeliveryTarget.ToContainer("00000000cafe0000:00000099", CollectionRig.Epoch, CollectionRig.ChestAt);

    private static CollectionRig Recovered(bool hold = false, CollectionOrderState state = CollectionOrderState.Paused)
    {
        var rig = new CollectionRig();
        CollectionOrderDefinition order = StaleOrder(hold);
        rig.Custody.RecoverableOrder = order;
        rig.Custody.RecoveredState = state;
        rig.Custody.Accepted.Add(order);

        // He really is holding seven stone from before the reload.
        ResourceProgress progress = rig.Custody.Bucket(order, CollectedResource.Stone);
        progress.Carried = 7;
        rig.WorkerInventory.Set(CollectedResource.Stone, 7);
        rig.Pickup.Order = order;
        return rig;
    }

    [Fact]
    public void AnOrderTheRecordKeptIsTakenUpStoppedAndHoldingHisIdentity()
    {
        CollectionRig rig = Recovered();

        Assert.True(rig.Loop.AdoptRecovered(rig.Now));

        Assert.True(rig.Loop.HasActiveOrder);
        Assert.True(rig.Loop.Adopted);
        Assert.Equal("collect-ab12cd34", rig.Loop.Order!.Order.Value);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.Equal(CollectionAttentionReason.DestinationStale, rig.Loop.Reason);
        Assert.Equal(ActorMode.Paused, rig.Modes.Mode);
        Assert.True(rig.Loop.NeedsRebind);
        Assert.Contains("Taken up again from the settlement record", rig.Loop.Describe());

        // Adoption states what the record already says; it writes nothing.
        Assert.Empty(rig.Custody.Transitions);

        // And it never starts working by itself.
        rig.Tick(200);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.Equal(0, rig.Pickup.Picks);
    }

    [Fact]
    public void AnAdoptedOrderCanAlwaysBeCancelledSoTheMaterialAndTheBodyComeBack()
    {
        CollectionRig rig = Recovered();
        rig.Loop.AdoptRecovered(rig.Now);

        ControlResult cancel = rig.Loop.Cancel(rig.Now);

        Assert.Equal(ControlOutcome.Done, cancel.Outcome);
        Assert.Equal(CollectionOrderState.Cancelled, rig.Loop.State);
        Assert.Contains(
            rig.Custody.Transitions,
            step => step.To == CollectionOrderState.Cancelled && step.From == CollectionOrderState.Paused);

        // Not a refund: the seven stone stay on him, now with no order holding
        // them, which is what lets the settlement release and then retire him.
        Assert.Equal(7, rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone)));
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
        Assert.False(rig.Loop.HasActiveOrder);
    }

    [Fact]
    public void AnAdoptedOrderRefusesToResumeUntilItIsReboundInThisWorldLoad()
    {
        CollectionRig rig = Recovered();
        rig.Loop.AdoptRecovered(rig.Now);

        ControlResult refused = rig.Loop.Resume(rig.Now);

        Assert.Equal(ControlOutcome.Refused, refused.Outcome);
        Assert.Contains("rebind", refused.Message);
        Assert.Equal(CollectionOrderState.Paused, rig.Loop.State);
        Assert.Empty(rig.Custody.Rebinds);

        ControlResult rebound = rig.Loop.Rebind(FreshScope(), FreshChest(), rig.Now);

        Assert.Equal(ControlOutcome.Done, rebound.Outcome);
        Assert.Single(rig.Custody.Rebinds);
        Assert.False(rig.Loop.NeedsRebind);
        Assert.Equal(CollectionRig.Epoch, rig.Loop.Order!.Scope.WorldLoadEpoch);
        Assert.Equal("00000000cafe0000:00000099", rig.Loop.Order.Delivery.ContainerKey);

        // Quotas and what he carries are untouched by a rebind.
        Assert.Equal(10, rig.Loop.Order.Quotas[0].Requested);
        Assert.Equal(7, rig.Progress(CollectedResource.Stone).Carried);

        Assert.Equal(ControlOutcome.Done, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(CollectionOrderState.Surveying, rig.Loop.State);
    }

    [Fact]
    public void ARebindKeepsTheKindOfWorkAreaAndOfDelivery()
    {
        CollectionRig rig = Recovered();
        rig.Loop.AdoptRecovered(rig.Now);

        var harvest = new WorkScope(
            WorkScopeSource.HarvestDesignation, CollectionRig.Anchor, 20f, "the harvest area", 5, CollectionRig.Epoch);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Rebind(harvest, FreshChest(), rig.Now).Outcome);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Rebind(FreshScope(), DeliveryTarget.HoldForPlayer(), rig.Now).Outcome);
        Assert.Empty(rig.Custody.Rebinds);

        // A rebind the record refuses changes nothing here either.
        rig.Custody.AllowRebind = false;
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Rebind(FreshScope(), FreshChest(), rig.Now).Outcome);
        Assert.True(rig.Loop.NeedsRebind);
    }

    [Fact]
    public void AHoldOrderComesBackNeedingItsWorkAreaAgainAndSaysHowToEndIt()
    {
        CollectionRig rig = Recovered(hold: true);

        Assert.True(rig.Loop.AdoptRecovered(rig.Now));
        Assert.Equal(CollectionAttentionReason.ScopeChanged, rig.Loop.Reason);
        Assert.True(rig.Loop.NeedsRebind);

        Assert.Equal(ControlOutcome.Done, rig.Loop.Rebind(FreshScope(), DeliveryTarget.HoldForPlayer(), rig.Now).Outcome);
        Assert.False(rig.Loop.NeedsRebind);
    }

    [Fact]
    public void AnOrderTheRecordSaysNeedsAttentionComesBackNeedingAttention()
    {
        CollectionRig rig = Recovered(state: CollectionOrderState.NeedsAttention);

        Assert.True(rig.Loop.AdoptRecovered(rig.Now));

        Assert.Equal(CollectionOrderState.NeedsAttention, rig.Loop.State);
        Assert.Equal(CollectionAttentionReason.ReconciliationMismatch, rig.Loop.Reason);
        Assert.Equal(ControlOutcome.Done, rig.Loop.Cancel(rig.Now).Outcome);
    }

    [Fact]
    public void AnUncertainTransferIsTheReasonAnAdoptedOrderNeedsAttention()
    {
        CollectionRig rig = Recovered(state: CollectionOrderState.NeedsAttention);
        rig.Custody.Uncertain = true;

        rig.Loop.AdoptRecovered(rig.Now);

        Assert.Equal(CollectionAttentionReason.TransferUncertain, rig.Loop.Reason);
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
    }

    [Fact]
    public void StartingAnotherOrderIsRefusedWithTheRecordsOwnReason()
    {
        var rig = new CollectionRig();
        rig.Custody.RecoverableOrder = StaleOrder();

        // The loop has not adopted it yet: the refusal must still be the true
        // one, not "it could not be written down".
        CollectionIntakeRefusal refusal = rig.Accept(rig.Order(stone: 5, wood: 0, id: "collect-2"));

        Assert.Equal(CollectionIntakeRefusal.AnotherOrderActive, refusal);
        Assert.Contains("already has a collection order", CollectionIntake.Describe(refusal));
        Assert.Equal(ActorMode.Resting, rig.Modes.Mode);
    }

    [Fact]
    public void AdoptionNeverTakesTheWorkerFromAnotherJobOrReplacesALiveOrder()
    {
        CollectionRig busy = Recovered();
        busy.Modes.Enter(ActorMode.Working, "haul-9");
        Assert.False(busy.Loop.AdoptRecovered(busy.Now));
        Assert.Equal("haul-9", busy.Modes.JobId);

        var live = new CollectionRig();
        CollectionLoopTestsHelpers.AddStones(live, 5);
        live.Accept(live.Order(stone: 5, wood: 0));
        live.Custody.RecoverableOrder = StaleOrder();
        Assert.False(live.Loop.AdoptRecovered(live.Now));
        Assert.Equal("collect-1", live.Loop.Order!.Order.Value);
    }

    [Fact]
    public void WithNothingInTheRecordThereIsNothingToTakeUp()
    {
        var rig = new CollectionRig();

        Assert.False(rig.Loop.AdoptRecovered(rig.Now));
        Assert.False(rig.Loop.HasActiveOrder);

        rig.Custody.RecoverableOrder = StaleOrder();
        rig.Custody.RecoveredState = CollectionOrderState.Completed;
        Assert.False(rig.Loop.AdoptRecovered(rig.Now));
    }
}

internal static class CollectionLoopTestsHelpers
{
    internal static void AddStones(CollectionRig rig, int count)
    {
        for (int index = 0; index < count; index++)
        {
            rig.AddSource(CollectedResource.Stone, 4f + (index % 10), -10f + ((index / 10) * 3f));
        }
    }
}
