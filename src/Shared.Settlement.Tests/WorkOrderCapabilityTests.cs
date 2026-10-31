using System;
using System.Collections.Generic;
using TheConcernedCat.Interop.WorkArea;
using TheConcernedCat.Interop.WorkOrders;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>#296: <c>concernedcat.workorders/1</c>, the read-only way the map
/// learns who works in a drawn area, the progress and why the order stopped.
/// Every lookup goes through the real responder; the snapshot is built from the
/// same presenter view as Foreman's panel, so the two cannot disagree.</summary>
public sealed class WorkOrderCapabilityTests
{
    private const string World = "424242";
    private static readonly Guid Epoch = new Guid("99999999-8888-7777-6666-555555555555");
    private const string AreaId = "wa-0123456789abcdef0123456789abcdef";

    private static WorkScope AreaScope()
    {
        var points = new List<WorkAreaPoint> { new(0f, 0f), new(20f, 0f), new(20f, 20f), new(0f, 20f) };
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(points, out WorkAreaPolygon? square));
        Assert.True(WorkScope.TryForWorkArea(AreaId, "Grove", square!.Revision, square, 30f, Epoch, out WorkScope? scope, out string failure), failure);
        return scope!;
    }

    private static CollectionOrderDefinition Order(WorkScope scope) =>
        new(
            new OrderId("collect-9"), new WorkerId("thorstein"),
            new[] { new ResourceQuota(CollectedResource.Stone, 10) },
            scope, DeliveryTarget.HoldForPlayer(), ParticipationMode.Solo, "tester");

    private static OrderPanelFacts Facts(
        CollectionOrderDefinition? order, CollectionOrderState state,
        CollectionAttentionReason reason = CollectionAttentionReason.Unspecified, bool pausedByPlayer = false)
    {
        var progress = new List<ResourceProgress>();
        if (order != null)
        {
            progress.Add(new ResourceProgress(CollectedResource.Stone, 10));
        }

        return new OrderPanelFacts(
            true, WorkAuthorityVerdict.Granted, true, order, state, reason, pausedByPlayer, progress,
            CooperationAvailability.ProviderAbsent, CooperationPhase.Unspecified, string.Empty, 0, false);
    }

    private static PublishedWorkOrders? Snapshot(OrderPanelFacts facts, string world = World) =>
        WorkOrderSnapshot.From(world, facts, OrderPanelPresenter.Present(facts, null, 0f));

    private static WorkOrderLookup Ask(Func<PublishedWorkOrders?> snapshot, string currentWorld = World) =>
        WorkOrderClient.List(request => WorkOrderResponder.Answer(request, "0.1.0", snapshot), currentWorld, "test");

    // ------------------------------------------------------------- snapshot

    [Fact]
    public void AnOrderInAMapAreaIsPublishedWithThePanelsOwnLines()
    {
        OrderPanelFacts facts = Facts(Order(AreaScope()), CollectionOrderState.Collecting);
        OrderPanelView view = OrderPanelPresenter.Present(facts, null, 0f);

        PublishedWorkOrder order = Assert.Single(WorkOrderSnapshot.From(World, facts, view)!.Orders);
        Assert.Equal(AreaId, order.AreaId);
        Assert.Equal("Thorstein", order.Worker);
        Assert.Equal(view.StateLine, order.StateLine);
        Assert.Equal(view.ReasonLine, order.ReasonLine);
        Assert.Equal(view.ProgressLines, order.ProgressLines);
        Assert.False(order.NeedsAttention);
    }

    [Fact]
    public void OnlyALiveOrderInAMapAreaWithItsBoundaryIsPublished()
    {
        var circle = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 0f, 0f), 30f, "your bed", 1, Epoch);
        Assert.Empty(Snapshot(Facts(Order(circle), CollectionOrderState.Collecting))!.Orders);
        Assert.Empty(Snapshot(Facts(null, CollectionOrderState.Unspecified))!.Orders);
        Assert.Empty(Snapshot(Facts(Order(AreaScope()), CollectionOrderState.Completed))!.Orders);
        Assert.Empty(Snapshot(Facts(Order(AreaScope()), CollectionOrderState.Cancelled))!.Orders);

        // A recovered order (journaled scope, no boundary) is not working anywhere yet.
        var recovered = new WorkScope(WorkScopeSource.CartographerWorkArea, new SitePoint(10f, 0f, 10f), 15f, "the work area \"Grove\"", 7, Epoch);
        Assert.Empty(Snapshot(Facts(Order(recovered), CollectionOrderState.Paused, CollectionAttentionReason.ScopeChanged))!.Orders);
    }

    [Fact]
    public void NoWorldIsUnavailable_NeverNoOrders()
    {
        Assert.Null(Snapshot(Facts(Order(AreaScope()), CollectionOrderState.Collecting), world: string.Empty));
        Assert.Equal(WorkOrderLookupStatus.Unavailable, Ask(() => null).Status);
    }

    [Fact]
    public void AttentionIsAStopThePlayerDidNotChoose()
    {
        WorkScope scope = AreaScope();
        Assert.True(WorkOrderSnapshot.NeedsAttention(Facts(Order(scope), CollectionOrderState.NeedsAttention, CollectionAttentionReason.SourcesExhausted)));
        Assert.True(WorkOrderSnapshot.NeedsAttention(Facts(Order(scope), CollectionOrderState.Paused, CollectionAttentionReason.ScopeUnloaded)));
        Assert.False(WorkOrderSnapshot.NeedsAttention(Facts(Order(scope), CollectionOrderState.Paused, CollectionAttentionReason.ScopeUnloaded, pausedByPlayer: true)));
        Assert.False(WorkOrderSnapshot.NeedsAttention(Facts(Order(scope), CollectionOrderState.Collecting)));

        // Unable to work at all: the runtime is off, or work is not authorised.
        OrderPanelFacts working = Facts(Order(scope), CollectionOrderState.Collecting);
        Assert.True(WorkOrderSnapshot.NeedsAttention(new OrderPanelFacts(
            false, WorkAuthorityVerdict.Granted, true, working.Order, working.State, working.Reason, false, working.Progress,
            CooperationAvailability.ProviderAbsent, CooperationPhase.Unspecified, string.Empty, 0, false)));
        Assert.True(WorkOrderSnapshot.NeedsAttention(new OrderPanelFacts(
            true, WorkAuthorityVerdict.OtherPeersConnected, true, working.Order, working.State, working.Reason, false, working.Progress,
            CooperationAvailability.ProviderAbsent, CooperationPhase.Unspecified, string.Empty, 0, false)));

        PublishedWorkOrder order = Assert.Single(
            Snapshot(Facts(Order(scope), CollectionOrderState.NeedsAttention, CollectionAttentionReason.SourcesExhausted))!.Orders);
        Assert.True(order.NeedsAttention);
        Assert.NotEqual(string.Empty, order.ReasonLine);
    }

    // ------------------------------------------------------- round trips

    [Fact]
    public void TheConsumerReadsExactlyWhatWasPublished()
    {
        OrderPanelFacts facts = Facts(Order(AreaScope()), CollectionOrderState.NeedsAttention, CollectionAttentionReason.SourcesExhausted);
        PublishedWorkOrders published = Snapshot(facts)!;

        WorkOrderLookup read = Ask(() => published);
        Assert.True(read.IsOk);
        PublishedWorkOrder order = read.ForArea(AreaId)!;
        PublishedWorkOrder sent = published.Orders[0];
        Assert.Equal(sent.Worker, order.Worker);
        Assert.Equal(sent.StateLine, order.StateLine);
        Assert.Equal(sent.ReasonLine, order.ReasonLine);
        Assert.Equal(sent.ProgressLines, order.ProgressLines);
        Assert.True(order.NeedsAttention);
        Assert.Null(read.ForArea("wa-ffffffffffffffffffffffffffffffff"));
    }

    [Fact]
    public void AnotherWorldAThrowingProviderAndNoProviderShowNothing()
    {
        PublishedWorkOrders published = Snapshot(Facts(Order(AreaScope()), CollectionOrderState.Collecting))!;
        Assert.Equal(WorkOrderLookupStatus.WrongWorld, Ask(() => published, currentWorld: "1").Status);
        Assert.Equal(WorkOrderLookupStatus.WrongWorld, Ask(() => published, currentWorld: string.Empty).Status);
        Assert.Equal(WorkOrderLookupStatus.Unavailable, WorkOrderClient.List(_ => throw new InvalidOperationException(), World, "t").Status);
        Assert.Equal(WorkOrderLookupStatus.Unavailable, WorkOrderClient.List(null, World, "t").Status);
        Assert.Empty(WorkOrderClient.List(null, World, "t").Orders);
    }

    [Fact]
    public void AMalformedReplyIsABadReply()
    {
        PublishedWorkOrders published = Snapshot(Facts(Order(AreaScope()), CollectionOrderState.Collecting))!;
        foreach (string key in new[] { "count", "areaId.0", "needsAttention.0", "progressCount.0", "progress.0.0" })
        {
            WorkOrderLookup read = WorkOrderClient.List(
                request =>
                {
                    var reply = new Dictionary<string, string>(WorkOrderResponder.Answer(request, "0.1.0", () => published));
                    if (key == "count")
                    {
                        reply[key] = "999";
                    }
                    else
                    {
                        reply.Remove(key);
                    }

                    return reply;
                },
                World,
                "t");
            Assert.Equal(WorkOrderLookupStatus.BadReply, read.Status);
        }
    }

    [Fact]
    public void TheContractHasNoOpThatChangesAnything()
    {
        IReadOnlyDictionary<string, string> reply = WorkOrderResponder.Answer(
            new Dictionary<string, string> { ["op"] = "cancelOrder" }, "0.1.0", () => new PublishedWorkOrders(World, Array.Empty<PublishedWorkOrder>()));
        Assert.Equal(WorkOrderContract.Statuses.Unsupported, reply[WorkOrderContract.Keys.Status]);
    }
}
