using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>Contract revision C1 of the cart and collection slice
/// (docs/settlement/cart-and-collection/CONTRACTS.md): the rules every agent
/// builds on, pinned before any of them codes against them.</summary>
public sealed class WorkContractTests
{
    private static readonly Guid Epoch = new Guid("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherEpoch = new Guid("99999999-2222-3333-4444-555555555555");

    // --- Workers ---------------------------------------------------------------------------

    [Fact]
    public void WorkerKeysRoundTripAndRefuseMalformedText()
    {
        Assert.Equal("foreman/thorstein", WorkerKey.Thorstein.Value);
        Assert.Equal("teamster/gunnar", WorkerKey.Gunnar.Value);
        Assert.True(WorkerKey.TryParse("teamster/gunnar", out WorkerKey parsed));
        Assert.Equal(WorkerKey.Gunnar, parsed);

        foreach (string bad in new[] { "", "gunnar", "/gunnar", "teamster/", "a/b/c", "Teamster/gunnar", "teamster/gun nar" })
        {
            Assert.False(WorkerKey.TryParse(bad, out _), bad);
        }
    }

    [Fact]
    public void WorkPointsRoundTripThroughTheirWireForm()
    {
        var point = new WorkPoint(-36.8f, 84.125f, 23.9f);
        Assert.True(WorkPoint.TryParse(point.Format(), out WorkPoint back));
        Assert.Equal(point, back);
        Assert.False(WorkPoint.TryParse("1;2", out _));
        Assert.False(WorkPoint.TryParse("1;NaN;3", out _));
        Assert.False(WorkPoint.TryParse("1,5;2;3", out _));
    }

    [Theory]
    [InlineData(false, true, true, false, 0, (int)WorkAuthorityVerdict.RuntimeDisabled)]
    [InlineData(true, false, true, false, 0, (int)WorkAuthorityVerdict.NoWorld)]
    [InlineData(true, true, false, false, 0, (int)WorkAuthorityVerdict.NotHost)]
    [InlineData(true, true, true, true, 0, (int)WorkAuthorityVerdict.DedicatedServer)]
    [InlineData(true, true, true, false, 1, (int)WorkAuthorityVerdict.OtherPeersConnected)]
    [InlineData(true, true, true, false, -1, (int)WorkAuthorityVerdict.OtherPeersConnected)]
    [InlineData(true, true, true, false, 0, (int)WorkAuthorityVerdict.Granted)]
    public void AuthorityIsGrantedOnlyToAnOptedInHostWithNobodyElseConnected(
        bool enabled, bool world, bool server, bool dedicated, int peers, int expected)
    {
        Assert.Equal(
            (WorkAuthorityVerdict)expected,
            WorkAuthorityPolicy.Evaluate(new WorkAuthorityFacts(enabled, world, server, dedicated, peers)));
    }

    [Fact]
    public void OneJobHoldsAnIdentityAndHomeMayMoveItOnlyWhileResting()
    {
        var owner = new ActorModeOwner(WorkerKey.Gunnar);
        Assert.True(owner.MayRelocateHome);

        Assert.Equal(ActorModeOutcome.Entered, owner.Enter(ActorMode.Working, "haul-1"));
        Assert.False(owner.MayRelocateHome);
        Assert.False(owner.MayRetireBody);
        Assert.Equal(ActorModeOutcome.AlreadyInMode, owner.Enter(ActorMode.Working, "haul-1"));
        Assert.Equal(ActorModeOutcome.RefusedBusy, owner.Enter(ActorMode.Surveying, "haul-2"));
        Assert.Equal(ActorModeOutcome.Entered, owner.Enter(ActorMode.Recovering, "haul-1"));
        Assert.Equal(ActorModeOutcome.NotHeld, owner.Release("haul-2"));
        Assert.Equal(ActorModeOutcome.Released, owner.Release("haul-1"));
        Assert.Equal(ActorMode.Resting, owner.Mode);
        Assert.True(owner.MayRelocateHome);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Enter(ActorMode.Resting, "haul-3"));
    }

    [Fact]
    public void RetriesBackOffAndStopAtTheirCeiling()
    {
        var retry = new BoundedRetry(maxFailures: 3, firstDelaySeconds: 2f, maxDelaySeconds: 5f);
        RetryDecision first = retry.RecordFailure(10f);
        Assert.False(first.GiveUp);
        Assert.Equal(12f, first.RetryAt);
        Assert.True(retry.IsWaiting(11f));
        Assert.False(retry.IsWaiting(12f));
        Assert.Equal(14f, retry.RecordFailure(10f).RetryAt);
        Assert.True(retry.RecordFailure(10f).GiveUp);
        Assert.True(retry.IsExhausted);
        Assert.True(retry.RecordFailure(20f).GiveUp);
        retry.Reset();
        Assert.False(retry.IsExhausted);
    }

    [Fact]
    public void AttentionIsNotifiedOncePerCooldown()
    {
        var throttle = new AttentionThrottle(30f);
        Assert.True(throttle.ShouldNotify("DestinationFull", 0f));
        Assert.False(throttle.ShouldNotify("DestinationFull", 29f));
        Assert.True(throttle.ShouldNotify("CarryFull", 29f));
        Assert.True(throttle.ShouldNotify("DestinationFull", 30f));
    }

    // --- Interop -------------------------------------------------------------------------------

    [Fact]
    public void EnumsTravelAsExactNamesOnly()
    {
        WireMessage message = WireMessage.Create()
            .SetEnum(HaulContract.Keys.Phase, HaulWirePhase.Waiting)
            .Set("numeric", "7")
            .Set("lower", "waiting")
            .Set("combined", "Waiting, Pulling")
            .Set("future", "Teleporting");

        Assert.True(message.TryGetEnum(HaulContract.Keys.Phase, out HaulWirePhase phase));
        Assert.Equal(HaulWirePhase.Waiting, phase);
        Assert.False(message.TryGetEnum("numeric", out HaulWirePhase _));
        Assert.False(message.TryGetEnum("lower", out HaulWirePhase _));
        Assert.False(message.TryGetEnum("combined", out HaulWirePhase _));
        Assert.False(message.TryGetEnum("future", out HaulWirePhase _));
        Assert.Throws<ArgumentOutOfRangeException>(() => WireMessage.Create().SetEnum("x", (HaulWirePhase)99));
    }

    [Fact]
    public void WireFieldsParseInvariantAndStrictly()
    {
        WireMessage message = WireMessage.From(new Dictionary<string, string>
        {
            ["int"] = "42",
            ["bool"] = "true",
            ["Bool"] = "True",
            ["point"] = "1.5;-2;3e2",
        });

        Assert.True(message.TryGetInt("int", out int number));
        Assert.Equal(42, number);
        Assert.True(message.TryGetBool("bool", out bool flag));
        Assert.True(flag);
        Assert.False(message.TryGetBool("Bool", out _));
        Assert.True(message.TryGetPoint("point", out float x, out float y, out float z));
        Assert.Equal((1.5f, -2f, 300f), (x, y, z));
        Assert.False(WireMessage.From(null).Has("int"));
    }

    [Fact]
    public void AnEndpointIsFoundOnlyUnderItsContractMajorAndOnlyAsTheBclFunc()
    {
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> echo = request => request;
        var map = new Dictionary<string, object>
        {
            [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major)] = echo,
            [CapabilityMap.KeyFor("concernedcat.other", 1)] = "not a delegate",
        };

        Assert.Equal("concernedcat.haul/1", HaulContract.Id + "/" + HaulContract.Major);
        Assert.True(CapabilityMap.TryGetEndpoint(map, HaulContract.Id, 1, out var endpoint));
        Assert.False(CapabilityMap.TryGetEndpoint(map, HaulContract.Id, 2, out _));
        Assert.False(CapabilityMap.TryGetEndpoint(map, "concernedcat.other", 1, out _));
        Assert.False(CapabilityMap.TryGetEndpoint(null, HaulContract.Id, 1, out _));

        IReadOnlyDictionary<string, string>? reply = CapabilityMap.TryCall(
            endpoint, WireMessage.Create().Set(HaulContract.Keys.Op, HaulContract.Ops.Hello).ToWire());
        Assert.NotNull(reply);
        Assert.Null(CapabilityMap.TryCall(_ => throw new InvalidOperationException("provider bug"), reply!));
    }

    // --- Collection and custody ----------------------------------------------------------------

    [Fact]
    public void OnlyStoneAndWoodAreCollectableAndQuotasAreBounded()
    {
        Assert.Equal("Stone", CollectedResources.ItemPrefabName(CollectedResource.Stone));
        Assert.True(CollectedResources.TryFromItemPrefabName("Wood", out CollectedResource wood));
        Assert.Equal(CollectedResource.Wood, wood);
        Assert.False(CollectedResources.TryFromItemPrefabName("Flint", out _));
        Assert.False(CollectedResources.TryFromItemPrefabName("RoundLog", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceQuota(CollectedResource.Stone, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceQuota(CollectedResource.Stone, CollectedResources.MaxQuota + 1));
    }

    [Fact]
    public void AnOrderNeedsDistinctQuotasADeliveryAndAMode()
    {
        var scope = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 30f, 0f), 30f, "your bed", 3, Epoch);
        CollectionOrderDefinition Order(ResourceQuota[] quotas, DeliveryTarget delivery, ParticipationMode mode) =>
            new CollectionOrderDefinition(new OrderId("collect-1"), new WorkerId("thorstein"), quotas, scope, delivery, mode, "HULGISMOKE");

        var both = new[] { new ResourceQuota(CollectedResource.Stone, 20), new ResourceQuota(CollectedResource.Wood, 30) };
        DeliveryTarget chest = DeliveryTarget.ToContainer("123:45", Epoch, new SitePoint(2f, 30f, 2f));

        Assert.Equal(CollectionOrderRefusal.Unspecified, Order(both, chest, ParticipationMode.Solo).CheckShape());
        Assert.Equal(CollectionOrderRefusal.NoQuotas, Order(new ResourceQuota[0], chest, ParticipationMode.Solo).CheckShape());
        Assert.Equal(
            CollectionOrderRefusal.DuplicateResource,
            Order(new[] { both[0], new ResourceQuota(CollectedResource.Stone, 5) }, chest, ParticipationMode.Solo).CheckShape());
        Assert.Equal(CollectionOrderRefusal.DeliveryMissing, Order(both, default, ParticipationMode.Solo).CheckShape());
        Assert.True(scope.Contains(new SitePoint(29.9f, 99f, 0f)));
        Assert.False(scope.Contains(new SitePoint(30.1f, 30f, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkScope(WorkScopeSource.HarvestDesignation, default, 60f, "", 0, Epoch));
    }

    [Fact]
    public void CollectionOrdersFollowOnlyTheirDesignedTransitions()
    {
        Assert.True(CollectionOrderStates.CanTransition(CollectionOrderState.Accepted, CollectionOrderState.Surveying));
        Assert.True(CollectionOrderStates.CanTransition(CollectionOrderState.Collecting, CollectionOrderState.HoldingForPlayer));
        Assert.True(CollectionOrderStates.CanTransition(CollectionOrderState.NeedsAttention, CollectionOrderState.Paused));
        Assert.False(CollectionOrderStates.CanTransition(CollectionOrderState.NeedsAttention, CollectionOrderState.Collecting));
        Assert.False(CollectionOrderStates.CanTransition(CollectionOrderState.Accepted, CollectionOrderState.Completed));
        Assert.False(CollectionOrderStates.CanTransition(CollectionOrderState.HoldingForPlayer, CollectionOrderState.Delivering));

        foreach (CollectionOrderState terminal in new[] { CollectionOrderState.Completed, CollectionOrderState.Cancelled })
        {
            Assert.True(CollectionOrderStates.IsTerminal(terminal));
            foreach (CollectionOrderState target in Enum.GetValues<CollectionOrderState>())
            {
                Assert.False(CollectionOrderStates.CanTransition(terminal, target));
            }
        }
    }

    [Fact]
    public void ProgressCountsEachUnitOnceAndEstimatesNever()
    {
        var progress = new ResourceProgress(CollectedResource.Wood, 30)
        {
            ReservedEstimate = 50,
            Carried = 8,
            InCart = 12,
            Delivered = 6,
        };

        Assert.Equal(26, progress.Committed);
        Assert.Equal(4, progress.StillToCollect);
        Assert.False(progress.IsDelivered);
        progress.Delivered = 30;
        progress.Carried = 0;
        progress.InCart = 0;
        Assert.True(progress.IsDelivered);
        Assert.Equal(0, progress.StillToCollect);
    }

    [Fact]
    public void ASourceIsClaimedByOneOrderInOneWorldLoad()
    {
        var book = new SourceReservationBook(Epoch);
        var stone = new SourceKey("Pickable_Stone", "123:9", Epoch, new SitePoint(1f, 30f, 1f));
        var first = new OrderId("collect-1");
        var second = new OrderId("collect-2");

        Assert.Equal(ReservationOutcome.Reserved, book.Reserve(stone, first));
        Assert.Equal(ReservationOutcome.AlreadySatisfied, book.Reserve(stone, first));
        Assert.Equal(ReservationOutcome.HeldByAnotherOrder, book.Reserve(stone, second));
        Assert.Equal(
            ReservationOutcome.StaleEpoch,
            book.Reserve(new SourceKey("Pickable_Stone", "123:9", OtherEpoch, default), first));
        Assert.Equal(ReservationOutcome.NotHeld, book.Release(stone, second));
        Assert.Equal(1, book.ReleaseAll(first));
        Assert.Equal(ReservationOutcome.Reserved, book.Reserve(stone, second));
    }

    [Fact]
    public void ATransferMovesAtLeastOneUnitBetweenTwoPlacesAndKnowsItsPayload()
    {
        var worker = new CustodyLocation(CustodyPlace.Worker, "foreman/thorstein", Epoch);
        var chest = new CustodyLocation(CustodyPlace.Destination, "123:45", Epoch);
        var intent = new TransferIntent(new RequestId("collect-1-4"), new OrderId("collect-1"), worker, chest, MaterialItem.Of(CollectedResource.Stone), 20, 7);

        Assert.True(intent.SamePayloadAs(new TransferIntent(new RequestId("collect-1-4"), new OrderId("collect-1"), worker, chest, MaterialItem.Of(CollectedResource.Stone), 20, 8)));
        Assert.False(intent.SamePayloadAs(new TransferIntent(new RequestId("collect-1-4"), new OrderId("collect-1"), worker, chest, MaterialItem.Of(CollectedResource.Stone), 19, 7)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferIntent(new RequestId("collect-1-5"), new OrderId("collect-1"), worker, chest, MaterialItem.Of(CollectedResource.Stone), 0, 7));
        Assert.Throws<ArgumentException>(() => new TransferIntent(new RequestId("collect-1-6"), new OrderId("collect-1"), worker, worker, MaterialItem.Of(CollectedResource.Stone), 1, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferReceipt(new RequestId("collect-1-4"), TransferOutcome.Unspecified, 0, worker, ""));
    }
}
