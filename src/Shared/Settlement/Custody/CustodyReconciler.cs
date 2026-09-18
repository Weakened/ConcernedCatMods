using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>The actual state of the places the record describes, as the game
/// can see them now.</summary>
internal interface ICustodyObserver
{
    /// <summary>How many units of <paramref name="item"/> are actually at
    /// <paramref name="location"/> — for a cart, above its recorded baseline —
    /// or null when the place cannot be observed now: not loaded, from another
    /// world load, no body.</summary>
    int? Observe(CustodyLocation location, MaterialItem item);
}

/// <summary>One row of the reconciliation matrix (CONTRACTS.md §5.5).</summary>
internal enum ReconciliationFindingKind
{
    Unspecified = 0,

    /// <summary>The place holds exactly what the record says.</summary>
    Matches = 1,

    /// <summary>An uncertain transfer whose effect the counts show: the source
    /// lost the units and the destination gained them. A person confirms; it is
    /// never applied automatically.</summary>
    EffectVisible = 2,

    /// <summary>An uncertain transfer the counts show did not happen. A person
    /// confirms.</summary>
    NoEffect = 3,

    /// <summary>Fewer units than the record expects. A person may record the
    /// difference as lost; nothing is refunded.</summary>
    BelowExpected = 4,

    /// <summary>More units than the record expects. Never credited.</summary>
    AboveExpected = 5,

    /// <summary>The place cannot be observed now, or several uncertain changes
    /// touch it at once, so the counts cannot say which happened.</summary>
    Unclear = 6,
}

/// <summary>One finding, with the sentence a player sees and the command that
/// answers it.</summary>
internal sealed class ReconciliationFinding
{
    internal ReconciliationFinding(
        ReconciliationFindingKind kind, IReadOnlyList<OrderId> orders, CustodyLocation location, MaterialItem item,
        int expected, int? actual, RequestId request, string sentence, string resolution)
    {
        Kind = kind;
        Orders = orders;
        Location = location;
        Item = item;
        Expected = expected;
        Actual = actual;
        Request = request;
        Sentence = sentence;
        Resolution = resolution;
    }

    public ReconciliationFindingKind Kind { get; }

    /// <summary>The orders the finding concerns: the transfer's order, or every
    /// order the record says holds material at the place.</summary>
    public IReadOnlyList<OrderId> Orders { get; }

    public CustodyLocation Location { get; }

    public MaterialItem Item { get; }

    public int Expected { get; }

    public int? Actual { get; }

    /// <summary>The uncertain transfer the finding is about; empty for a place
    /// finding.</summary>
    public RequestId Request { get; }

    public string Sentence { get; }

    /// <summary>The console command that answers it, or empty when nothing is
    /// to be answered.</summary>
    public string Resolution { get; }

    public bool NeedsAttention => Kind != ReconciliationFindingKind.Matches;

    public override string ToString() => Sentence;
}

/// <summary>What reconciliation found.</summary>
internal sealed class ReconciliationReport
{
    internal ReconciliationReport(IReadOnlyList<ReconciliationFinding> findings, bool ordersWereRunning)
    {
        Findings = findings;
        OrdersWereRunning = ordersWereRunning;
    }

    public IReadOnlyList<ReconciliationFinding> Findings { get; }

    public bool OrdersWereRunning { get; }

    public bool AllMatch
    {
        get
        {
            foreach (ReconciliationFinding finding in Findings)
            {
                if (finding.NeedsAttention)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The one attention reason for an order, or Unspecified when
    /// nothing concerns it. An uncertain transfer outranks a count mismatch:
    /// the mismatch may be that transfer.</summary>
    public CollectionAttentionReason ReasonFor(OrderId order)
    {
        CollectionAttentionReason reason = CollectionAttentionReason.Unspecified;
        foreach (ReconciliationFinding finding in Findings)
        {
            if (!finding.NeedsAttention || !Concerns(finding, order))
            {
                continue;
            }

            if (!finding.Request.IsEmpty)
            {
                return CollectionAttentionReason.TransferUncertain;
            }

            if (finding.Kind == ReconciliationFindingKind.BelowExpected && !OrdersWereRunning)
            {
                reason = CollectionAttentionReason.PlayerRemovedMaterial;
            }
            else if (reason == CollectionAttentionReason.Unspecified)
            {
                reason = CollectionAttentionReason.ReconciliationMismatch;
            }
        }

        return reason;
    }

    /// <summary>Shortfalls the record could note as lost for an order: every
    /// below-expected place it holds material at, capped at what it holds.
    /// </summary>
    public IReadOnlyList<ReconciliationFinding> ShortfallsFor(OrderId order)
    {
        var list = new List<ReconciliationFinding>();
        foreach (ReconciliationFinding finding in Findings)
        {
            if (finding.Kind == ReconciliationFindingKind.BelowExpected && Concerns(finding, order))
            {
                list.Add(finding);
            }
        }

        return list;
    }

    private static bool Concerns(ReconciliationFinding finding, OrderId order)
    {
        foreach (OrderId candidate in finding.Orders)
        {
            if (candidate.Equals(order))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Compares what the record expects with what the places actually
/// hold (CONTRACTS.md §5.5), after the world-save marker rule has voided what
/// the world rolled back.
///
/// Only the worker and a cart are compared. A delivery chest also holds the
/// player's own things and later changes to it are the player's, so delivered
/// credit is never checked against it and never reversed; handed-over and lost
/// units are dispositions, not places. A traced drop's id means nothing after a
/// reload.
///
/// Nothing here writes anything or applies anything. Every finding that is not
/// a match waits for a person, with the evidence and the command that answers
/// it.</summary>
internal static class CustodyReconciler
{
    public static ReconciliationReport Reconcile(MaterialCustodyLedger ledger, ICustodyObserver observer, bool ordersWereRunning)
    {
        if (ledger == null)
        {
            throw new ArgumentNullException(nameof(ledger));
        }

        if (observer == null)
        {
            throw new ArgumentNullException(nameof(observer));
        }

        var findings = new List<ReconciliationFinding>();
        var places = new List<PlaceItem>();
        var seen = new HashSet<PlaceItem>();

        // Everything the record holds now, and everything a running order has
        // ever held: a place the record has emptied is still worth looking at,
        // or "more than the record expects" could never be seen there at all
        // (review R2, m1).
        foreach (Holding holding in Concat(ledger.Holdings, ledger.EverHeld))
        {
            if (IsObservable(holding.Location.Place) && seen.Add(new PlaceItem(holding.Location, holding.Item)))
            {
                places.Add(new PlaceItem(holding.Location, holding.Item));
            }
        }

        var awaiting = new List<TransferRecord>();
        foreach (TransferRecord transfer in ledger.Transfers)
        {
            if (!transfer.AwaitsResolution && transfer.Status != TransferStatus.Open)
            {
                continue;
            }

            awaiting.Add(transfer);
            foreach (CustodyLocation side in new[] { transfer.Intent.From, transfer.Intent.To })
            {
                var key = new PlaceItem(side, transfer.Intent.Item);
                if (IsObservable(side.Place) && seen.Add(key))
                {
                    places.Add(key);
                }
            }
        }

        var actuals = new Dictionary<PlaceItem, int?>();
        foreach (PlaceItem place in places)
        {
            int? actual;
            try
            {
                actual = observer.Observe(place.Location, place.Item);
            }
            catch (Exception)
            {
                actual = null;
            }

            actuals[place] = actual;

            int touching = CountTouching(awaiting, place);
            if (touching > 0)
            {
                // Judged below, per transfer.
                continue;
            }

            int expected = ledger.TotalAt(place.Location, place.Item);
            IReadOnlyList<OrderId> holders = HoldersAt(ledger, place);
            findings.Add(PlaceFinding(place, holders, expected, actual));
        }

        foreach (TransferRecord transfer in awaiting)
        {
            findings.Add(TransferFinding(ledger, transfer, awaiting, actuals));
        }

        return new ReconciliationReport(findings, ordersWereRunning);
    }

    private static bool IsObservable(CustodyPlace place) =>
        place == CustodyPlace.Worker || place == CustodyPlace.Cart;

    private static int CountTouching(List<TransferRecord> awaiting, PlaceItem place)
    {
        int count = 0;
        foreach (TransferRecord transfer in awaiting)
        {
            if (!transfer.Intent.Item.Equals(place.Item))
            {
                continue;
            }

            if (transfer.Intent.From.Equals(place.Location) || transfer.Intent.To.Equals(place.Location))
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<OrderId> HoldersAt(MaterialCustodyLedger ledger, PlaceItem place)
    {
        var orders = new List<OrderId>();
        foreach (Holding holding in Concat(ledger.Holdings, ledger.EverHeld))
        {
            if (holding.Location.Equals(place.Location) && holding.Item.Equals(place.Item) && !orders.Contains(holding.Order))
            {
                orders.Add(holding.Order);
            }
        }

        return orders;
    }

    private static IEnumerable<Holding> Concat(IReadOnlyList<Holding> first, IReadOnlyList<Holding> second)
    {
        foreach (Holding holding in first)
        {
            yield return holding;
        }

        foreach (Holding holding in second)
        {
            yield return holding;
        }
    }

    private static ReconciliationFinding PlaceFinding(PlaceItem place, IReadOnlyList<OrderId> holders, int expected, int? actual)
    {
        string where = Describe(place.Location);
        string expectedText = expected.ToString(CultureInfo.InvariantCulture) + " " + place.Item;

        if (!actual.HasValue)
        {
            return new ReconciliationFinding(
                ReconciliationFindingKind.Unclear, holders, place.Location, place.Item, expected, null, default,
                "The record says " + where + " holds " + expectedText + ", and " + where + " cannot be checked now. " +
                "Nothing was credited or taken back; check again once it can be reached.",
                string.Empty);
        }

        int difference = actual.Value - expected;
        if (difference == 0)
        {
            return new ReconciliationFinding(
                ReconciliationFindingKind.Matches, holders, place.Location, place.Item, expected, actual, default,
                where + " holds " + expectedText + ", as recorded.", string.Empty);
        }

        string actualText = actual.Value.ToString(CultureInfo.InvariantCulture);
        if (difference < 0)
        {
            string resolution = holders.Count == 1 ? "cf_settle resolve " + holders[0].Value + " lost" : string.Empty;
            return new ReconciliationFinding(
                ReconciliationFindingKind.BelowExpected, holders, place.Location, place.Item, expected, actual, default,
                "The record says " + where + " holds " + expectedText + ", but " + actualText + " are there. " +
                "Nothing has been refunded or replaced. If the missing " +
                (-difference).ToString(CultureInfo.InvariantCulture) + " were taken or destroyed, record them as lost" +
                (resolution.Length == 0 ? "." : " with: " + resolution),
                resolution);
        }

        return new ReconciliationFinding(
            ReconciliationFindingKind.AboveExpected, holders, place.Location, place.Item, expected, actual, default,
            "The record says " + where + " holds " + expectedText + ", but " + actualText + " are there — " +
            difference.ToString(CultureInfo.InvariantCulture) + " more than recorded. None of the extra is credited " +
            "to any order. If they are yours, take them back; if a transfer was interrupted, it is listed separately.",
            string.Empty);
    }

    private static ReconciliationFinding TransferFinding(
        MaterialCustodyLedger ledger, TransferRecord transfer, List<TransferRecord> awaiting, Dictionary<PlaceItem, int?> actuals)
    {
        TransferIntent intent = transfer.Intent;
        var orders = new[] { transfer.Order };
        string what = intent.Count.ToString(CultureInfo.InvariantCulture) + " " + intent.Item + " from " +
            Describe(intent.From) + " to " + Describe(intent.To) + " (request \"" + intent.Request.Value + "\")";

        var from = new PlaceItem(intent.From, intent.Item);
        var to = new PlaceItem(intent.To, intent.Item);
        bool shared = (IsObservable(intent.From.Place) && CountTouching(awaiting, from) > 1)
            || (IsObservable(intent.To.Place) && CountTouching(awaiting, to) > 1);

        int? fromDelta = Delta(ledger, actuals, from);
        int? toDelta = Delta(ledger, actuals, to);

        if (shared || (!fromDelta.HasValue && !toDelta.HasValue))
        {
            return new ReconciliationFinding(
                ReconciliationFindingKind.Unclear, orders, intent.From, intent.Item, intent.Count, null, intent.Request,
                "Moving " + what + " has no certain outcome, and the counts cannot show which way it went" +
                (shared ? " because another uncertain change touches the same place" : " because neither place can be checked now") +
                ". Nothing was credited, replayed or given back. Look at both places, then answer with: cf_settle resolve " +
                intent.Request.Value + " source|destination",
                "cf_settle resolve " + intent.Request.Value + " source|destination");
        }

        bool fromMoved = !fromDelta.HasValue || fromDelta.Value == -intent.Count;
        bool toMoved = !toDelta.HasValue || toDelta.Value == intent.Count;
        bool fromStayed = !fromDelta.HasValue || fromDelta.Value == 0;
        bool toStayed = !toDelta.HasValue || toDelta.Value == 0;
        string partly = !fromDelta.HasValue || !toDelta.HasValue
            ? " (only " + Describe(fromDelta.HasValue ? intent.From : intent.To) + " could be checked)"
            : string.Empty;

        if (fromMoved && toMoved)
        {
            return new ReconciliationFinding(
                ReconciliationFindingKind.EffectVisible, orders, intent.From, intent.Item, intent.Count, fromDelta, intent.Request,
                "Moving " + what + " was interrupted, and the counts show it arrived" + partly + ". It is not applied " +
                "until you confirm: cf_settle resolve " + intent.Request.Value + " destination",
                "cf_settle resolve " + intent.Request.Value + " destination");
        }

        if (fromStayed && toStayed)
        {
            return new ReconciliationFinding(
                ReconciliationFindingKind.NoEffect, orders, intent.From, intent.Item, intent.Count, fromDelta, intent.Request,
                "Moving " + what + " was interrupted, and the counts show nothing moved" + partly + ". Confirm with: " +
                "cf_settle resolve " + intent.Request.Value + " source",
                "cf_settle resolve " + intent.Request.Value + " source");
        }

        return new ReconciliationFinding(
            ReconciliationFindingKind.Unclear, orders, intent.From, intent.Item, intent.Count, fromDelta, intent.Request,
            "Moving " + what + " was interrupted, and the counts fit neither outcome (" + Describe(intent.From) + " " +
            Signed(fromDelta) + ", " + Describe(intent.To) + " " + Signed(toDelta) + " against the record). Nothing was " +
            "credited or given back. Look at both places, then answer with: cf_settle resolve " + intent.Request.Value +
            " source|destination",
            "cf_settle resolve " + intent.Request.Value + " source|destination");
    }

    private static int? Delta(MaterialCustodyLedger ledger, Dictionary<PlaceItem, int?> actuals, PlaceItem place)
    {
        if (!IsObservable(place.Location.Place) || !actuals.TryGetValue(place, out int? actual) || !actual.HasValue)
        {
            return null;
        }

        return actual.Value - ledger.TotalAt(place.Location, place.Item);
    }

    private static string Signed(int? value) =>
        !value.HasValue ? "unchecked" : (value.Value >= 0 ? "+" : string.Empty) + value.Value.ToString(CultureInfo.InvariantCulture);

    private static string Describe(CustodyLocation location)
    {
        switch (location.Place)
        {
            case CustodyPlace.Worker: return "the worker (" + location.Key + ")";
            case CustodyPlace.Cart: return "the cart";
            case CustodyPlace.Destination: return "the delivery chest";
            case CustodyPlace.Player: return "you";
            case CustodyPlace.SourceGround: return "the ground";
            case CustodyPlace.Lost: return "lost";
            default: return "an unknown place";
        }
    }

    private readonly struct PlaceItem : IEquatable<PlaceItem>
    {
        public PlaceItem(CustodyLocation location, MaterialItem item)
        {
            Location = location;
            Item = item;
        }

        public CustodyLocation Location { get; }

        public MaterialItem Item { get; }

        public bool Equals(PlaceItem other) => Location.Equals(other.Location) && Item.Equals(other.Item);

        public override bool Equals(object? obj) => obj is PlaceItem other && Equals(other);

        public override int GetHashCode() => unchecked((Location.GetHashCode() * 397) ^ Item.GetHashCode());
    }
}
