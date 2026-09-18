using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>#317 COOP-04: what the order panel tells the player. Progress
/// counts every unit once, an estimate is never progress, hold-for-player reads
/// as held, exactly one reason is shown, every reason has a sentence, and
/// notices are throttled.</summary>
public sealed class CooperationPanelTests
{
    private static readonly Guid Epoch = new Guid("ff000000-0000-0000-0000-000000000001");

    [Fact]
    public void EveryReasonStateAndPhaseHasItsOwnPlayerSentence()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CollectionAttentionReason reason in Enum.GetValues<CollectionAttentionReason>())
        {
            string sentence = CooperationSentences.For(reason);
            Assert.False(string.IsNullOrWhiteSpace(sentence), reason.ToString());
            Assert.DoesNotContain(reason.ToString(), sentence, StringComparison.Ordinal);
            Assert.True(seen.Add(sentence), "two reasons share a sentence: " + reason);
        }

        foreach (CollectionOrderState state in Enum.GetValues<CollectionOrderState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CooperationSentences.For(state)));
        }

        foreach (CooperationPhase phase in Enum.GetValues<CooperationPhase>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CooperationSentences.For(phase)));
        }

        foreach (CooperationAvailability availability in Enum.GetValues<CooperationAvailability>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CooperationSentences.For(availability)));
        }
    }

    [Fact]
    public void ProgressCountsEveryUnitOnceAndNamesTheEstimateAsAnEstimate()
    {
        CollectionOrderDefinition order = Order();
        var stone = new ResourceProgress(CollectedResource.Stone, 20)
        {
            Delivered = 8, InCart = 5, Carried = 3, OnGround = 2, ReservedEstimate = 9,
        };
        var wood = new ResourceProgress(CollectedResource.Wood, 30) { Delivered = 30 };

        OrderPanelView view = OrderPanelPresenter.Present(
            Facts(order, CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, stone, wood), null, 0f);

        Assert.Equal(2, view.ProgressLines.Count);
        string line = view.ProgressLines[0];
        Assert.Contains("Stone 8/20", line);
        Assert.Contains("8 delivered", line);
        Assert.Contains("5 in the cart", line);
        Assert.Contains("3 carried", line);
        Assert.Contains("2 on the ground", line);
        Assert.Contains("Still to collect 2", line);
        Assert.Contains("9 estimated", line);
        Assert.Contains("not counted", line);
        Assert.Contains("Wood 30/30", view.ProgressLines[1]);
        Assert.DoesNotContain("estimated", view.ProgressLines[1]);
        Assert.Equal(string.Empty, view.ReasonLine);
        Assert.True(view.CanPause && view.CanCancel);
        Assert.False(view.CanStart || view.CanResume);
    }

    [Fact]
    public void HoldingForPlayerReadsAsHeldNotDelivered()
    {
        CollectionOrderDefinition order = Order(holdForPlayer: true);
        var stone = new ResourceProgress(CollectedResource.Stone, 20) { HandedOver = 20 };

        OrderPanelView view = OrderPanelPresenter.Present(
            Facts(order, CollectionOrderState.HoldingForPlayer, CollectionAttentionReason.Unspecified, stone), null, 0f);

        Assert.Contains("20 held for you", view.ProgressLines[0]);
        Assert.DoesNotContain("delivered", view.ProgressLines[0]);
        Assert.Contains("Thorstein holds it for you", view.DestinationLine);
    }

    [Fact]
    public void OnlyOneReasonIsShownAndAuthorityComesFirst()
    {
        CollectionOrderDefinition order = Order();
        var stone = new ResourceProgress(CollectedResource.Stone, 20);

        OrderPanelView off = OrderPanelPresenter.Present(
            new OrderPanelFacts(
                false, WorkAuthorityVerdict.RuntimeDisabled, true, order, CollectionOrderState.Paused,
                CollectionAttentionReason.HaulerUnavailable, false, new[] { stone },
                CooperationAvailability.Available, CooperationPhase.Paused, "detail", 0, false),
            null, 0f);
        Assert.Contains("settlement runtime is off", off.StateLine.Length > 0 ? off.ReasonLine : off.ReasonLine);
        Assert.False(off.CanStart);

        OrderPanelView peers = OrderPanelPresenter.Present(
            new OrderPanelFacts(
                true, WorkAuthorityVerdict.OtherPeersConnected, true, order, CollectionOrderState.Paused,
                CollectionAttentionReason.HaulerUnavailable, false, new[] { stone },
                CooperationAvailability.Available, CooperationPhase.Paused, string.Empty, 0, false),
            null, 0f);
        Assert.Equal(CooperationSentences.For(WorkAuthorityVerdict.OtherPeersConnected), peers.ReasonLine);

        OrderPanelView stopped = OrderPanelPresenter.Present(
            Facts(order, CollectionOrderState.NeedsAttention, CollectionAttentionReason.CartLeaseLost, stone, detail: "20 Stone stay in the cart."),
            null, 0f);
        Assert.StartsWith(CooperationSentences.For(CollectionAttentionReason.CartLeaseLost), stopped.ReasonLine);
        Assert.Contains("20 Stone stay in the cart.", stopped.ReasonLine);
        Assert.True(stopped.CanResume && stopped.CanCancel);
        Assert.False(stopped.CanPause);
    }

    [Fact]
    public void ParticipantsNameOnlyWhoIsReallyThere()
    {
        var stone = new ResourceProgress(CollectedResource.Stone, 20);

        OrderPanelView solo = OrderPanelPresenter.Present(
            Facts(Order(), CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, stone), null, 0f);
        Assert.Contains("Thorstein", solo.ParticipantsLine);
        Assert.DoesNotContain("Hulgi", solo.ParticipantsLine);

        OrderPanelView together = OrderPanelPresenter.Present(
            new OrderPanelFacts(
                true, WorkAuthorityVerdict.Granted, true, Order(withHauler: true), CollectionOrderState.WaitingForHauler,
                CollectionAttentionReason.Unspecified, false, new[] { stone }, CooperationAvailability.Available,
                CooperationPhase.Hauling, string.Empty, 1, true),
            null, 0f);
        Assert.Contains("Gunnar with his cart", together.ParticipantsLine);
        Assert.Contains("Hulgi is helping", together.ParticipantsLine);
        Assert.Contains("hauling to the chest", together.StateLine);
        Assert.Contains("1 cart trip so far", together.StateLine);
        Assert.True(together.CanReleaseCart);
    }

    [Fact]
    public void ANoticeIsShownOncePerReasonPerCooldown()
    {
        CollectionOrderDefinition order = Order();
        var stone = new ResourceProgress(CollectedResource.Stone, 20);
        var notices = new AttentionThrottle(30f);
        OrderPanelFacts full = Facts(order, CollectionOrderState.Paused, CollectionAttentionReason.DestinationFull, stone);

        Assert.Equal(CooperationSentences.For(CollectionAttentionReason.DestinationFull), OrderPanelPresenter.Present(full, notices, 0f).Notice);
        Assert.Null(OrderPanelPresenter.Present(full, notices, 10f).Notice);
        Assert.Null(OrderPanelPresenter.Present(full, notices, 29f).Notice);
        Assert.NotNull(OrderPanelPresenter.Present(full, notices, 31f).Notice);

        OrderPanelFacts quiet = Facts(order, CollectionOrderState.Collecting, CollectionAttentionReason.Unspecified, stone);
        Assert.Null(OrderPanelPresenter.Present(quiet, notices, 40f).Notice);
    }

    [Fact]
    public void WithoutAnOrderThePanelExplainsWhatToDoAndWhyGunnarCannotHelp()
    {
        OrderPanelView view = OrderPanelPresenter.Present(
            new OrderPanelFacts(
                true, WorkAuthorityVerdict.Granted, true, null, CollectionOrderState.Unspecified,
                CollectionAttentionReason.Unspecified, false, Array.Empty<ResourceProgress>(),
                CooperationAvailability.NoCartAssigned, CooperationPhase.Unspecified, string.Empty, 0, false),
            null, 0f);

        Assert.Contains("press Start", view.StateLine);
        Assert.Equal(CooperationSentences.For(CooperationAvailability.NoCartAssigned), view.ReasonLine);
        Assert.True(view.CanStart);
        Assert.False(view.CanPause || view.CanResume || view.CanCancel || view.CanReleaseCart);
        Assert.Contains("Preview area", view.AreaLine);
        Assert.Contains("look at a chest", view.DestinationLine);
    }

    private static CollectionOrderDefinition Order(bool holdForPlayer = false, bool withHauler = false)
    {
        var scope = new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(0f, 30f, 0f), 30f, "your bed", 1, Epoch);
        return new CollectionOrderDefinition(
            new OrderId("collect-1"), new WorkerId("thorstein"),
            new[] { new ResourceQuota(CollectedResource.Stone, 20), new ResourceQuota(CollectedResource.Wood, 30) },
            scope,
            holdForPlayer ? DeliveryTarget.HoldForPlayer() : DeliveryTarget.ToContainer("chest-1", Epoch, new SitePoint(40f, 30f, 0f)),
            withHauler ? ParticipationMode.WithHauler : ParticipationMode.Solo,
            "tester");
    }

    private static OrderPanelFacts Facts(
        CollectionOrderDefinition order, CollectionOrderState state, CollectionAttentionReason reason,
        ResourceProgress first, ResourceProgress? second = null, string detail = "")
    {
        var progress = new List<ResourceProgress> { first };
        if (second != null)
        {
            progress.Add(second);
        }

        return new OrderPanelFacts(
            true, WorkAuthorityVerdict.Granted, true, order, state, reason, false, progress,
            CooperationAvailability.Available, CooperationPhase.Unspecified, detail, 0, false);
    }
}
