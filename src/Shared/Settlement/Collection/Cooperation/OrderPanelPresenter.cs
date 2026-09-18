using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Everything the order panel needs, gathered by the adapter from the
/// collection loop, custody and the cooperative run. Plain values: the panel
/// and its tests never reach into a runtime.</summary>
internal sealed class OrderPanelFacts
{
    public OrderPanelFacts(
        bool runtimeEnabled,
        WorkAuthorityVerdict authority,
        bool workerPresent,
        CollectionOrderDefinition? order,
        CollectionOrderState state,
        CollectionAttentionReason reason,
        bool pausedByPlayer,
        IReadOnlyList<ResourceProgress> progress,
        CooperationAvailability haulerAvailability,
        CooperationPhase haulerPhase,
        string haulerDetail,
        int deliveryTrips,
        bool hulgiSurveying)
    {
        RuntimeEnabled = runtimeEnabled;
        Authority = authority;
        WorkerPresent = workerPresent;
        Order = order;
        State = state;
        Reason = reason;
        PausedByPlayer = pausedByPlayer;
        Progress = progress ?? Array.Empty<ResourceProgress>();
        HaulerAvailability = haulerAvailability;
        HaulerPhase = haulerPhase;
        HaulerDetail = haulerDetail ?? string.Empty;
        DeliveryTrips = deliveryTrips;
        HulgiSurveying = hulgiSurveying;
    }

    public bool RuntimeEnabled { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool WorkerPresent { get; }

    public CollectionOrderDefinition? Order { get; }

    public CollectionOrderState State { get; }

    public CollectionAttentionReason Reason { get; }

    public bool PausedByPlayer { get; }

    public IReadOnlyList<ResourceProgress> Progress { get; }

    public CooperationAvailability HaulerAvailability { get; }

    public CooperationPhase HaulerPhase { get; }

    public string HaulerDetail { get; }

    public int DeliveryTrips { get; }

    /// <summary>Only true when Hulgi is really here and helping: an absent or
    /// busy companion is never credited (GATHER-03).</summary>
    public bool HulgiSurveying { get; }
}

/// <summary>The panel's text and which buttons may be pressed.</summary>
internal sealed class OrderPanelView
{
    public OrderPanelView(
        string stateLine, string reasonLine, string areaLine, string destinationLine, string participantsLine,
        IReadOnlyList<string> progressLines, bool canStart, bool canPause, bool canResume, bool canCancel,
        bool canReleaseCart, string? notice)
    {
        StateLine = stateLine;
        ReasonLine = reasonLine;
        AreaLine = areaLine;
        DestinationLine = destinationLine;
        ParticipantsLine = participantsLine;
        ProgressLines = progressLines;
        CanStart = canStart;
        CanPause = canPause;
        CanResume = canResume;
        CanCancel = canCancel;
        CanReleaseCart = canReleaseCart;
        Notice = notice;
    }

    public string StateLine { get; }

    /// <summary>The one actionable reason, or empty when nothing is wrong.
    /// </summary>
    public string ReasonLine { get; }

    public string AreaLine { get; }

    public string DestinationLine { get; }

    public string ParticipantsLine { get; }

    /// <summary>One line per resource: requested, delivered, in the cart,
    /// carried, on the ground and the reserved estimate, each counted once.
    /// </summary>
    public IReadOnlyList<string> ProgressLines { get; }

    public bool CanStart { get; }

    public bool CanPause { get; }

    public bool CanResume { get; }

    public bool CanCancel { get; }

    public bool CanReleaseCart { get; }

    /// <summary>A message worth showing the player now, at most once per reason
    /// per cooldown; null when there is nothing new to say.</summary>
    public string? Notice { get; }
}

/// <summary>Turns the order's facts into the panel's lines (COOP-04). Pure, so
/// what a player is told is tested without the game: every unit appears in
/// exactly one bucket, an estimate is never counted as progress, hold-for-player
/// reads as held rather than delivered, and only one reason is ever shown.
/// </summary>
internal static class OrderPanelPresenter
{
    public static OrderPanelView Present(OrderPanelFacts facts, AttentionThrottle? notices, float now)
    {
        if (facts == null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        CollectionOrderDefinition? order = facts.Order;
        bool active = order != null && !CollectionOrderStates.IsTerminal(facts.State);
        bool stopped = facts.State == CollectionOrderState.Paused || facts.State == CollectionOrderState.NeedsAttention;

        string stateLine = order == null
            ? "No order. Set the amounts, look at a chest, and press Start."
            : "Order: " + CooperationSentences.For(facts.State) +
                (facts.HaulerAvailability == CooperationAvailability.Available || facts.HaulerPhase != CooperationPhase.Unspecified
                    ? " - Gunnar is " + CooperationSentences.For(facts.HaulerPhase)
                    : string.Empty) +
                (facts.DeliveryTrips > 0
                    ? " (" + facts.DeliveryTrips.ToString(CultureInfo.InvariantCulture) + " cart trip" +
                        (facts.DeliveryTrips == 1 ? string.Empty : "s") + " so far)"
                    : string.Empty);

        string reasonLine = Reason(facts);
        string areaLine = order != null
            ? CooperationSentences.ForScope(order.Scope)
            : "Area: your harvest area when one is marked, else a 30 m circle on your respawn point. Press Preview area.";
        string destinationLine = order == null
            ? "Delivery: look at a chest before pressing Start, or tick Hold for me."
            : order.Delivery.Kind == DeliveryKind.HoldForPlayer
                ? "Delivery: Thorstein holds it for you until you take it."
                : "Delivery: the chest he was given at " + order.Delivery.Position.ToString() + ".";

        var participants = new List<string>();
        participants.Add("Thorstein" + (facts.WorkerPresent ? string.Empty : " (not here)"));
        participants.Add(
            order != null && order.Participation == ParticipationMode.WithHauler
                ? "Gunnar with his cart"
                : facts.HaulerAvailability == CooperationAvailability.Available
                    ? "Gunnar available"
                    : "no hauler");
        if (facts.HulgiSurveying)
        {
            participants.Add("Hulgi is helping to look over the area");
        }

        string participantsLine = "Working: " + string.Join(", ", participants.ToArray()) + ".";

        var progressLines = new List<string>();
        foreach (ResourceProgress progress in facts.Progress)
        {
            progressLines.Add(Describe(progress, order));
        }

        bool authorised = facts.RuntimeEnabled && facts.Authority == WorkAuthorityVerdict.Granted;
        string? notice = null;
        if (notices != null && order != null && stopped && facts.Reason != CollectionAttentionReason.Unspecified &&
            notices.ShouldNotify(facts.Reason.ToString(), now))
        {
            notice = CooperationSentences.For(facts.Reason);
        }

        return new OrderPanelView(
            stateLine,
            reasonLine,
            areaLine,
            destinationLine,
            participantsLine,
            progressLines,
            canStart: authorised && !active,
            canPause: authorised && active && facts.State != CollectionOrderState.Paused &&
                facts.State != CollectionOrderState.NeedsAttention,
            canResume: authorised && active && stopped,
            canCancel: active,
            canReleaseCart: active && facts.HaulerPhase != CooperationPhase.Unspecified &&
                facts.HaulerPhase != CooperationPhase.Completed && facts.HaulerPhase != CooperationPhase.Cancelled,
            notice: notice);
    }

    private static string Reason(OrderPanelFacts facts)
    {
        if (!facts.RuntimeEnabled)
        {
            return "The settlement runtime is off. Turn it on in Concerned Foreman's settings.";
        }

        if (facts.Authority != WorkAuthorityVerdict.Granted)
        {
            return CooperationSentences.For(facts.Authority);
        }

        if (facts.Order != null && facts.Reason != CollectionAttentionReason.Unspecified)
        {
            string sentence = CooperationSentences.For(facts.Reason);
            return facts.HaulerDetail.Length > 0 &&
                (facts.Reason == CollectionAttentionReason.HaulerUnavailable ||
                    facts.Reason == CollectionAttentionReason.HaulerNeedsAttention ||
                    facts.Reason == CollectionAttentionReason.CartLeaseLost ||
                    facts.Reason == CollectionAttentionReason.RendezvousTimedOut)
                ? sentence + " " + facts.HaulerDetail
                : sentence;
        }

        if (facts.Order != null && facts.PausedByPlayer)
        {
            return CooperationSentences.For(CollectionAttentionReason.PausedByPlayer);
        }

        if (facts.Order == null && facts.HaulerAvailability != CooperationAvailability.Available)
        {
            return CooperationSentences.For(facts.HaulerAvailability);
        }

        return string.Empty;
    }

    /// <summary>One resource's line. Every unit is in exactly one bucket, the
    /// reserved estimate is named as an estimate, and hold-for-player says held,
    /// never delivered.</summary>
    private static string Describe(ResourceProgress progress, CollectionOrderDefinition? order)
    {
        bool holding = order != null && order.Delivery.Kind == DeliveryKind.HoldForPlayer;
        var parts = new List<string>();
        if (holding)
        {
            parts.Add(Count(progress.HandedOver) + " held for you");
        }
        else
        {
            parts.Add(Count(progress.Delivered) + " delivered");
        }

        if (progress.InCart > 0)
        {
            parts.Add(Count(progress.InCart) + " in the cart");
        }

        if (progress.Carried > 0)
        {
            parts.Add(Count(progress.Carried) + " carried");
        }

        if (progress.OnGround > 0)
        {
            parts.Add(Count(progress.OnGround) + " on the ground");
        }

        if (progress.Lost > 0)
        {
            parts.Add(Count(progress.Lost) + " lost");
        }

        string line = CollectedResources.ItemPrefabName(progress.Resource) + " " +
            Count(holding ? progress.HandedOver : progress.Delivered) + "/" + Count(progress.Requested) + ": " +
            string.Join(", ", parts.ToArray()) + ". Still to collect " + Count(progress.StillToCollect) + ".";
        return progress.ReservedEstimate > 0
            ? line + " (" + Count(progress.ReservedEstimate) + " estimated from the sources he picked out, not counted)"
            : line;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
