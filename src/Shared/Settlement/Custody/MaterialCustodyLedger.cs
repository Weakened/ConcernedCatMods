using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>What the gathered-material ledger said to one request.</summary>
internal enum CustodyLedgerOutcome
{
    Unspecified = 0,

    /// <summary>The ledger changed.</summary>
    Applied = 1,

    /// <summary>The same request with the same payload was already recorded.
    /// Nothing changed; this is what a retry looks like.</summary>
    AlreadySatisfied = 2,

    /// <summary>The same request id carries a different payload. A caller bug,
    /// never waved through.</summary>
    RejectedDifferentPayload = 3,

    /// <summary>Refused: unknown order or request, a place material may not
    /// leave, fewer units recorded than asked for, or a settled record being
    /// settled again.</summary>
    Rejected = 4,

    /// <summary>Based on an older custody revision or a state that has since
    /// changed.</summary>
    Stale = 5,
}

/// <summary>Where one transfer's record stands.</summary>
internal enum TransferStatus
{
    Unspecified = 0,

    /// <summary>The intent is recorded and no receipt is. In session: in
    /// flight. After a reload: interrupted, and treated as uncertain.</summary>
    Open = 1,

    Completed = 2,

    /// <summary>Some units arrived; the rest stayed at the source.</summary>
    Partial = 3,

    /// <summary>Nothing moved, verified from the counts.</summary>
    Refused = 4,

    /// <summary>A mutation may have happened and the counts could not prove
    /// which. Nothing is credited, replayed or compensated; a person decides.
    /// </summary>
    Uncertain = 5,

    /// <summary>A person said which side is true.</summary>
    Resolved = 6,

    /// <summary>The world was loaded from a save that predates it, so the
    /// world rolled it back. Never credited, refunded or replayed.</summary>
    Voided = 7,

    /// <summary>The world-save marker rule could not place it before or after
    /// the loaded save. Treated exactly like <see cref="Uncertain"/>.</summary>
    Ambiguous = 8,
}

/// <summary>One transfer as the ledger knows it.</summary>
internal sealed class TransferRecord
{
    internal TransferRecord(TransferIntent intent)
    {
        Intent = intent;
        Status = TransferStatus.Open;
        Evidence = string.Empty;
    }

    public TransferIntent Intent { get; }

    public TransferStatus Status { get; internal set; }

    /// <summary>Units the ledger actually moved for this transfer.</summary>
    public int Applied { get; internal set; }

    public string Evidence { get; internal set; }

    public RequestId Request => Intent.Request;

    public OrderId Order => Intent.Order;

    /// <summary>Waiting on a person: uncertain, ambiguous, or an intent with no
    /// receipt that is no longer in flight.</summary>
    public bool AwaitsResolution =>
        Status == TransferStatus.Uncertain || Status == TransferStatus.Ambiguous;

    public override string ToString() =>
        Request.Value + " " + Status + " " + Intent.Item + " x" +
        Intent.Count.ToString(CultureInfo.InvariantCulture) + " " + Intent.From + " -> " + Intent.To;
}

/// <summary>One pickup as the ledger knows it.</summary>
internal sealed class PickupRecord
{
    internal PickupRecord(RequestId pickup, OrderId order, SourceKey source)
    {
        Pickup = pickup;
        Order = order;
        Source = source;
    }

    public RequestId Pickup { get; }

    public OrderId Order { get; }

    public SourceKey Source { get; }

    /// <summary>Null while the pick has no recorded result.</summary>
    public PickupResult? Result { get; internal set; }

    /// <summary>True when its result is unknown or ambiguous.</summary>
    public bool IsUncertain { get; internal set; }

    /// <summary>A person acknowledged an uncertain pickup.</summary>
    public bool Acknowledged { get; internal set; }
}

/// <summary>One accepted order and where it has got to.</summary>
internal sealed class CollectionOrderRecord
{
    internal CollectionOrderRecord(CollectionOrderDefinition definition)
    {
        Definition = definition;
        State = CollectionOrderState.Accepted;
    }

    public CollectionOrderDefinition Definition { get; }

    public OrderId Order => Definition.Order;

    public CollectionOrderState State { get; internal set; }

    /// <summary>The reason recorded with the last transition into Paused or
    /// NeedsAttention.</summary>
    public CollectionAttentionReason Reason { get; internal set; }
}

/// <summary>A cart's pre-existing cargo, recorded before an order first used
/// the cart (D8). Never counted, unloaded or refunded as gathered material.
/// </summary>
internal sealed class CartBaseline
{
    internal CartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch, IReadOnlyList<ItemCount> counts)
    {
        Order = order;
        LeaseId = leaseId;
        CartSessionKey = cartSessionKey;
        ProviderEpoch = providerEpoch;
        Counts = counts;
    }

    public OrderId Order { get; }

    public string LeaseId { get; }

    public string CartSessionKey { get; }

    public Guid ProviderEpoch { get; }

    public IReadOnlyList<ItemCount> Counts { get; }

    public int CountOf(MaterialItem item)
    {
        int total = 0;
        foreach (ItemCount count in Counts)
        {
            if (count.Item.Equals(item))
            {
                total += count.Count;
            }
        }

        return total;
    }
}

/// <summary>Who holds which gathered material, on behalf of which order, and
/// where every unit is (DATA-02).
///
/// <b>The only material-custody truth (D8).</b> It is rebuilt from the
/// settlement journal and changed only by the rows the journal records, in the
/// order <c>TransferExecutor</c> writes them: the intent before any engine
/// mutation, the receipt with the actual accepted counts after. The worker's
/// inventory, a cart and a chest are the physical places the ledger describes;
/// when they disagree with it, reconciliation says so and a person decides.
///
/// <b>Holdings are per order, per location, per item.</b> A location is a place
/// and which instance of it (the worker's key, a cart's session key, a chest's
/// key, a traced drop's id). Every recorded unit is in exactly one holding or
/// has a recorded disposition — Destination (credited once, never reversed),
/// Player (handed over) or Lost.
///
/// <b>Idempotence is payload-checked.</b> The same request id with the same
/// payload is <see cref="CustodyLedgerOutcome.AlreadySatisfied"/>; the same id
/// with a different payload is
/// <see cref="CustodyLedgerOutcome.RejectedDifferentPayload"/>. An intent with
/// no receipt is never started again: whether it happened is a person's call.
///
/// <b>The revision counts records, not holdings.</b> It increases with every
/// custody row the ledger has taken, including rows later voided, so it never
/// goes backwards across a reload and <c>RequestId.For(order, Revision)</c> is
/// unique for the life of the journal. A transfer planned against an older
/// revision is stale.</summary>
internal sealed class MaterialCustodyLedger : IMaterialCustodyView
{
    private readonly Dictionary<string, CollectionOrderRecord> _orders =
        new Dictionary<string, CollectionOrderRecord>(StringComparer.Ordinal);
    private readonly List<string> _orderSequence = new List<string>();

    private readonly Dictionary<string, TransferRecord> _transfers =
        new Dictionary<string, TransferRecord>(StringComparer.Ordinal);
    private readonly List<string> _transferSequence = new List<string>();

    private readonly Dictionary<string, PickupRecord> _pickups =
        new Dictionary<string, PickupRecord>(StringComparer.Ordinal);
    private readonly List<string> _pickupSequence = new List<string>();

    private readonly Dictionary<HoldingKey, int> _holdings = new Dictionary<HoldingKey, int>();
    private readonly List<HoldingKey> _holdingSequence = new List<HoldingKey>();

    private readonly List<CartBaseline> _baselines = new List<CartBaseline>();
    private readonly HashSet<string> _losses = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _handovers = new HashSet<string>(StringComparer.Ordinal);

    public int Revision { get; private set; }

    public IReadOnlyList<CollectionOrderRecord> Orders
    {
        get
        {
            var ordered = new List<CollectionOrderRecord>(_orderSequence.Count);
            foreach (string key in _orderSequence)
            {
                ordered.Add(_orders[key]);
            }

            return ordered;
        }
    }

    public IReadOnlyList<TransferRecord> Transfers
    {
        get
        {
            var ordered = new List<TransferRecord>(_transferSequence.Count);
            foreach (string key in _transferSequence)
            {
                ordered.Add(_transfers[key]);
            }

            return ordered;
        }
    }

    public IReadOnlyList<PickupRecord> Pickups
    {
        get
        {
            var ordered = new List<PickupRecord>(_pickupSequence.Count);
            foreach (string key in _pickupSequence)
            {
                ordered.Add(_pickups[key]);
            }

            return ordered;
        }
    }

    public IReadOnlyList<CartBaseline> Baselines => _baselines;

    /// <summary>Counts a custody row the ledger took, applied or voided. See the
    /// class summary for why voided rows count too.</summary>
    internal void CountRecord() => Revision++;

    // ------------------------------------------------------------------
    // Orders
    // ------------------------------------------------------------------

    public bool TryGetOrder(OrderId order, out CollectionOrderRecord record)
    {
        record = null!;
        return !order.IsEmpty && _orders.TryGetValue(order.Value, out record!);
    }

    public CustodyLedgerOutcome Accept(CollectionOrderDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (_orders.TryGetValue(definition.Order.Value, out CollectionOrderRecord? existing))
        {
            return CollectionOrders.SameDefinition(existing!.Definition, definition)
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        _orders.Add(definition.Order.Value, new CollectionOrderRecord(definition));
        _orderSequence.Add(definition.Order.Value);
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>Moves an order along the contract's table. The recorded
    /// <paramref name="from"/> must be the order's current state, so a
    /// transition written against an old view is stale rather than silently
    /// applied over something newer.</summary>
    public CustodyLedgerOutcome Transition(
        OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason)
    {
        if (!TryGetOrder(order, out CollectionOrderRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (record.State == to && from == to)
        {
            return CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (record.State != from)
        {
            return record.State == to ? CustodyLedgerOutcome.AlreadySatisfied : CustodyLedgerOutcome.Stale;
        }

        if (!CollectionOrderStates.CanTransition(from, to))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        record.State = to;
        if (to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention)
        {
            record.Reason = reason;
        }

        return CustodyLedgerOutcome.Applied;
    }

    // ------------------------------------------------------------------
    // Pickups
    // ------------------------------------------------------------------

    public bool TryGetPickup(RequestId pickup, out PickupRecord record)
    {
        record = null!;
        return !pickup.IsEmpty && _pickups.TryGetValue(pickup.Value, out record!);
    }

    public CustodyLedgerOutcome BeginPickup(RequestId pickup, OrderId order, SourceKey source)
    {
        if (pickup.IsEmpty || order.IsEmpty || source.IsEmpty)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (_pickups.TryGetValue(pickup.Value, out PickupRecord? existing))
        {
            return existing!.Order.Equals(order) && existing.Source.Equals(source)
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        if (!_orders.ContainsKey(order.Value))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        _pickups.Add(pickup.Value, new PickupRecord(pickup, order, source));
        _pickupSequence.Add(pickup.Value);
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>Records what a pick produced. Only a <see cref="PickupOutcome.Picked"/>
    /// result puts material into custody: each traced drop becomes a
    /// SourceGround holding under its own id, which the take transfer then
    /// moves to the worker. An uncertain pick credits nothing.</summary>
    public CustodyLedgerOutcome FinishPickup(RequestId pickup, PickupResult result, bool ambiguous = false)
    {
        if (result == null || !TryGetPickup(pickup, out PickupRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (record.Result != null)
        {
            return SamePickupResult(record.Result, result)
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        record.Result = result;
        if (ambiguous || result.Outcome == PickupOutcome.Uncertain)
        {
            record.IsUncertain = true;
            return CustodyLedgerOutcome.Applied;
        }

        if (result.Outcome == PickupOutcome.Picked)
        {
            foreach (SpawnedDrop drop in result.Drops)
            {
                var ground = new CustodyLocation(CustodyPlace.SourceGround, drop.SessionId, record.Source.WorldLoadEpoch);
                Add(record.Order, ground, drop.Item, drop.Count);
            }
        }

        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>A pickup whose start is recorded and whose result is not, once
    /// it is no longer in flight.</summary>
    internal void MarkPickupUncertain(RequestId pickup)
    {
        if (TryGetPickup(pickup, out PickupRecord record) && record.Result == null)
        {
            record.IsUncertain = true;
        }
    }

    // ------------------------------------------------------------------
    // Transfers
    // ------------------------------------------------------------------

    public bool TryGetTransfer(RequestId request, out TransferRecord record)
    {
        record = null!;
        return !request.IsEmpty && _transfers.TryGetValue(request.Value, out record!);
    }

    /// <summary>The ledger half of the executor's check (CONTRACTS.md §5.2 step
    /// 1). <see cref="TransferOutcome.Unspecified"/> means the record allows it;
    /// anything else is the answer, and nothing changes.</summary>
    public TransferOutcome Check(TransferIntent intent, out string reason)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_transfers.TryGetValue(intent.Request.Value, out TransferRecord? existing))
        {
            if (!existing!.Intent.SamePayloadAs(intent))
            {
                reason = "that request id was already used for a different transfer";
                return TransferOutcome.RejectedDifferentPayload;
            }

            switch (existing.Status)
            {
                case TransferStatus.Completed:
                case TransferStatus.Partial:
                case TransferStatus.Refused:
                case TransferStatus.Resolved:
                    reason = "already recorded as " + existing.Status;
                    return TransferOutcome.AlreadySatisfied;

                case TransferStatus.Voided:
                    reason = "that request belongs to work the world rolled back; plan a new one";
                    return TransferOutcome.Stale;

                default:
                    // Started and not finished, uncertain or ambiguous: it may
                    // have happened. Never started again.
                    reason = "that transfer was started and its outcome is not recorded";
                    return TransferOutcome.Uncertain;
            }
        }

        if (!TryGetOrder(intent.Order, out CollectionOrderRecord _))
        {
            reason = "no accepted order " + intent.Order.Value;
            return TransferOutcome.Refused;
        }

        if (intent.ExpectedCustodyRevision != Revision)
        {
            reason = "planned against custody revision " +
                intent.ExpectedCustodyRevision.ToString(CultureInfo.InvariantCulture) +
                ", now " + Revision.ToString(CultureInfo.InvariantCulture);
            return TransferOutcome.Stale;
        }

        if (!MayLeave(intent.From.Place))
        {
            reason = "material recorded as " + intent.From.Place + " is not moved again by the order";
            return TransferOutcome.Refused;
        }

        if (intent.To.Place == CustodyPlace.SourceGround)
        {
            reason = "nothing is put back on the ground as picked material";
            return TransferOutcome.Refused;
        }

        if (HasUnresolvedTransfer(intent.Order))
        {
            reason = "order " + intent.Order.Value + " has a transfer waiting on a person's answer";
            return TransferOutcome.Refused;
        }

        int held = HoldingAt(intent.Order, intent.From, intent.Item);
        if (held < intent.Count)
        {
            reason = "the record holds " + held.ToString(CultureInfo.InvariantCulture) + " " + intent.Item +
                " at " + intent.From + " for this order, not " + intent.Count.ToString(CultureInfo.InvariantCulture);
            return TransferOutcome.Refused;
        }

        reason = string.Empty;
        return TransferOutcome.Unspecified;
    }

    /// <summary>Records a persisted intent. Call only after the
    /// <c>TransferStarted</c> row reached disk.</summary>
    public CustodyLedgerOutcome Begin(TransferIntent intent, bool ambiguous = false)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_transfers.TryGetValue(intent.Request.Value, out TransferRecord? existing))
        {
            return existing!.Intent.SamePayloadAs(intent)
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        var record = new TransferRecord(intent);
        if (ambiguous)
        {
            record.Status = TransferStatus.Ambiguous;
            record.Evidence = "the intent could not be placed before or after the loaded world save";
        }

        _transfers.Add(intent.Request.Value, record);
        _transferSequence.Add(intent.Request.Value);
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>Applies a persisted receipt: moves exactly the accepted units
    /// from the intent's source to its destination. An uncertain receipt moves
    /// nothing and keeps its evidence.</summary>
    public CustodyLedgerOutcome Finish(TransferReceipt receipt, bool ambiguous = false)
    {
        if (receipt == null)
        {
            throw new ArgumentNullException(nameof(receipt));
        }

        if (!TryGetTransfer(receipt.Request, out TransferRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (ambiguous)
        {
            // The marker rule could not place this receipt: whatever it says is
            // evidence for a person, never applied. Its intent is ambiguous too
            // (the rule places both halves of one synchronous call together) or
            // still open; either way the transfer now waits.
            if (record.Status != TransferStatus.Open && record.Status != TransferStatus.Ambiguous)
            {
                return CustodyLedgerOutcome.Rejected;
            }

            record.Status = TransferStatus.Ambiguous;
            record.Evidence = Join(receipt.Evidence, "the result could not be placed before or after the loaded world save");
            return CustodyLedgerOutcome.Applied;
        }

        switch (record.Status)
        {
            case TransferStatus.Open:
                break;

            case TransferStatus.Uncertain:
            case TransferStatus.Ambiguous:
                // A receipt arriving for something already uncertain: either a
                // duplicate of the uncertain receipt, or evidence a person has
                // not looked at yet. Never applied on its own.
                return receipt.Outcome == TransferOutcome.Uncertain
                    ? CustodyLedgerOutcome.AlreadySatisfied
                    : CustodyLedgerOutcome.Rejected;

            case TransferStatus.Voided:
            case TransferStatus.Resolved:
                return CustodyLedgerOutcome.Rejected;

            default:
                return StatusFor(receipt.Outcome) == record.Status && receipt.Accepted == record.Applied
                    ? CustodyLedgerOutcome.AlreadySatisfied
                    : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        switch (receipt.Outcome)
        {
            case TransferOutcome.Completed:
            case TransferOutcome.Partial:
            {
                int accepted = receipt.Accepted;
                if (accepted < 1 || accepted > record.Intent.Count
                    || (receipt.Outcome == TransferOutcome.Completed && accepted != record.Intent.Count)
                    || HoldingAt(record.Order, record.Intent.From, record.Intent.Item) < accepted)
                {
                    // A receipt that cannot be true against the record: more
                    // than intended, or more than the source holds. Kept as
                    // uncertain evidence rather than applied.
                    record.Status = TransferStatus.Uncertain;
                    record.Evidence = Join(receipt.Evidence, "the receipt's counts do not fit the record");
                    return CustodyLedgerOutcome.Applied;
                }

                Move(record.Order, record.Intent.From, record.Intent.To, record.Intent.Item, accepted);
                record.Applied = accepted;
                record.Status = accepted == record.Intent.Count ? TransferStatus.Completed : TransferStatus.Partial;
                record.Evidence = receipt.Evidence;
                return CustodyLedgerOutcome.Applied;
            }

            case TransferOutcome.Refused:
            case TransferOutcome.Stale:
                record.Status = TransferStatus.Refused;
                record.Evidence = receipt.Evidence;
                return CustodyLedgerOutcome.Applied;

            case TransferOutcome.Uncertain:
                record.Status = TransferStatus.Uncertain;
                record.Evidence = receipt.Evidence;
                return CustodyLedgerOutcome.Applied;

            default:
                return CustodyLedgerOutcome.Rejected;
        }
    }

    /// <summary>In memory only: a transfer whose receipt could not be written,
    /// or whose engine step threw. The next load decides from the record and
    /// the actual inventories.</summary>
    public CustodyLedgerOutcome MarkUncertain(RequestId request, string evidence)
    {
        if (!TryGetTransfer(request, out TransferRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (record.Status == TransferStatus.Uncertain)
        {
            return CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (record.Status != TransferStatus.Open)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        record.Status = TransferStatus.Uncertain;
        record.Evidence = evidence ?? string.Empty;
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>Every intent still open becomes uncertain. Called once a record
    /// has been replayed from disk: nothing replayed is still in flight.
    /// </summary>
    internal void CloseOpenIntents()
    {
        foreach (string key in _transferSequence)
        {
            TransferRecord record = _transfers[key];
            if (record.Status == TransferStatus.Open)
            {
                record.Status = TransferStatus.Uncertain;
                record.Evidence = "the session ended after the intent was recorded and before its result was";
            }
        }

        foreach (string key in _pickupSequence)
        {
            MarkPickupUncertain(_pickups[key].Pickup);
        }
    }

    /// <summary>A person's answer to an uncertain or ambiguous transfer.
    /// <paramref name="units"/> is how many arrived when the answer is
    /// <see cref="TransferSide.Destination"/>; the rest stays at the source.
    /// </summary>
    public CustodyLedgerOutcome Resolve(RequestId request, TransferSide side, int units)
    {
        if (!TryGetTransfer(request, out TransferRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (record.Status == TransferStatus.Resolved)
        {
            return CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (!record.AwaitsResolution || side == TransferSide.Unspecified)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        int moved = side == TransferSide.Destination ? units : 0;
        if (moved < 0 || moved > record.Intent.Count
            || HoldingAt(record.Order, record.Intent.From, record.Intent.Item) < moved)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (moved > 0)
        {
            Move(record.Order, record.Intent.From, record.Intent.To, record.Intent.Item, moved);
        }

        record.Applied = moved;
        record.Status = TransferStatus.Resolved;
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>The marker rule's verdict on an intent the world rolled back.
    /// The record is kept so its request id is still known — a retry under it
    /// is answered as stale rather than started — and nothing is applied: a
    /// voided row never moves a unit, so there is nothing to undo.</summary>
    internal CustodyLedgerOutcome RegisterVoided(TransferIntent intent)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_transfers.TryGetValue(intent.Request.Value, out TransferRecord? existing))
        {
            return existing!.Status == TransferStatus.Voided
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.Rejected;
        }

        var record = new TransferRecord(intent)
        {
            Status = TransferStatus.Voided,
            Evidence = "the world was loaded from a save made before this transfer",
        };
        _transfers.Add(intent.Request.Value, record);
        _transferSequence.Add(intent.Request.Value);
        return CustodyLedgerOutcome.Applied;
    }

    /// <summary>A person's acknowledgement of an uncertain pickup: nothing was
    /// granted from it, and any drops it may have left are ordinary drops in the
    /// world. The only answer a pickup has — its material only ever enters
    /// custody through a take transfer.</summary>
    public CustodyLedgerOutcome ResolvePickup(RequestId pickup)
    {
        if (!TryGetPickup(pickup, out PickupRecord record))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (!record.IsUncertain)
        {
            return record.Acknowledged ? CustodyLedgerOutcome.AlreadySatisfied : CustodyLedgerOutcome.Rejected;
        }

        record.IsUncertain = false;
        record.Acknowledged = true;
        return CustodyLedgerOutcome.Applied;
    }

    // ------------------------------------------------------------------
    // Losses, baselines, handovers
    // ------------------------------------------------------------------

    /// <summary>A person accepting observed loss (<c>LossRecorded</c>). Moves the
    /// units from where the record held them to Lost; never below zero.</summary>
    public CustodyLedgerOutcome RecordLoss(RequestId request, OrderId order, CustodyLocation at, MaterialItem item, int count)
    {
        if (request.IsEmpty || count < 1 || !TryGetOrder(order, out CollectionOrderRecord _))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (_losses.Contains(request.Value))
        {
            return CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (!MayLeave(at.Place) || HoldingAt(order, at, item) < count)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        _losses.Add(request.Value);
        Move(order, at, LostLocation(at), item, count);
        return CustodyLedgerOutcome.Applied;
    }

    public CustodyLedgerOutcome RecordBaseline(
        OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch, IReadOnlyList<ItemCount> counts)
    {
        if (!TryGetOrder(order, out CollectionOrderRecord _) || string.IsNullOrEmpty(cartSessionKey))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (TryGetBaseline(order, cartSessionKey, providerEpoch, out CartBaseline existing))
        {
            return string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal)
                ? CustodyLedgerOutcome.AlreadySatisfied
                : CustodyLedgerOutcome.RejectedDifferentPayload;
        }

        _baselines.Add(new CartBaseline(order, leaseId, cartSessionKey, providerEpoch, counts ?? Array.Empty<ItemCount>()));
        return CustodyLedgerOutcome.Applied;
    }

    public bool TryGetBaseline(OrderId order, string cartSessionKey, Guid providerEpoch, out CartBaseline baseline)
    {
        foreach (CartBaseline candidate in _baselines)
        {
            if (candidate.Order.Equals(order)
                && candidate.ProviderEpoch == providerEpoch
                && string.Equals(candidate.CartSessionKey, cartSessionKey, StringComparison.Ordinal))
            {
                baseline = candidate;
                return true;
            }
        }

        baseline = null!;
        return false;
    }

    /// <summary>The first baseline recorded for a cart in one provider epoch,
    /// by any order: the cargo that was there before gathered material was.
    /// </summary>
    public bool TryGetCartBaseline(string cartSessionKey, Guid providerEpoch, out CartBaseline baseline)
    {
        foreach (CartBaseline candidate in _baselines)
        {
            if (candidate.ProviderEpoch == providerEpoch
                && string.Equals(candidate.CartSessionKey, cartSessionKey, StringComparison.Ordinal))
            {
                baseline = candidate;
                return true;
            }
        }

        baseline = null!;
        return false;
    }

    /// <summary>Hold-for-player acknowledgement. Valid only for a transfer to
    /// the Player that actually moved at least this many units.</summary>
    public CustodyLedgerOutcome RecordHandover(RequestId transfer, OrderId order, MaterialItem item, int count)
    {
        if (!TryGetTransfer(transfer, out TransferRecord record)
            || !record.Order.Equals(order)
            || record.Intent.To.Place != CustodyPlace.Player
            || !record.Intent.Item.Equals(item)
            || record.Applied < count)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        return _handovers.Add(transfer.Value) ? CustodyLedgerOutcome.Applied : CustodyLedgerOutcome.AlreadySatisfied;
    }

    // ------------------------------------------------------------------
    // Views
    // ------------------------------------------------------------------

    public int HoldingAt(OrderId order, CustodyLocation location, MaterialItem item) =>
        _holdings.TryGetValue(new HoldingKey(order, location, item), out int count) ? count : 0;

    /// <summary>Every order's holding at one location, per item — what a
    /// physical inventory should contain of gathered material.</summary>
    public int TotalAt(CustodyLocation location, MaterialItem item)
    {
        int total = 0;
        foreach (HoldingKey key in _holdingSequence)
        {
            if (key.Location.Equals(location) && key.Item.Equals(item))
            {
                total += _holdings[key];
            }
        }

        return total;
    }

    /// <summary>Non-zero holdings, in the order they first appeared.</summary>
    public IReadOnlyList<Holding> Holdings
    {
        get
        {
            var list = new List<Holding>();
            foreach (HoldingKey key in _holdingSequence)
            {
                int count = _holdings[key];
                if (count > 0)
                {
                    list.Add(new Holding(key.Order, key.Location, key.Item, count));
                }
            }

            return list;
        }
    }

    public int CountAt(OrderId order, CustodyPlace place, CollectedResource resource)
    {
        if (resource == CollectedResource.Unspecified)
        {
            return 0;
        }

        string prefab = CollectedResources.ItemPrefabName(resource);
        int total = 0;
        foreach (HoldingKey key in _holdingSequence)
        {
            if (key.Order.Equals(order)
                && key.Location.Place == place
                && string.Equals(key.Item.PrefabName, prefab, StringComparison.Ordinal))
            {
                total += _holdings[key];
            }
        }

        return total;
    }

    public ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        int requested = 0;
        foreach (ResourceQuota quota in order.Quotas)
        {
            if (quota.Resource == resource)
            {
                requested = quota.Requested;
            }
        }

        return new ResourceProgress(resource, requested)
        {
            OnGround = CountAt(order.Order, CustodyPlace.SourceGround, resource),
            Carried = CountAt(order.Order, CustodyPlace.Worker, resource),
            InCart = CountAt(order.Order, CustodyPlace.Cart, resource),
            Delivered = CountAt(order.Order, CustodyPlace.Destination, resource),
            HandedOver = CountAt(order.Order, CustodyPlace.Player, resource),
            Lost = CountAt(order.Order, CustodyPlace.Lost, resource),
        };
    }

    /// <summary>True while anything of this order waits on a person: an
    /// uncertain or ambiguous transfer, or a pickup whose result is unknown.
    /// </summary>
    public bool HasUncertainTransfer(OrderId order)
    {
        if (HasUnresolvedTransfer(order))
        {
            return true;
        }

        foreach (string key in _pickupSequence)
        {
            PickupRecord pickup = _pickups[key];
            if (pickup.Order.Equals(order) && pickup.IsUncertain)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Transfers only. An uncertain transfer makes the counts at its
    /// two locations unknown, so no other transfer of the order may rely on
    /// them; an uncertain pickup credited nothing and blocks nothing.</summary>
    private bool HasUnresolvedTransfer(OrderId order)
    {
        foreach (string key in _transferSequence)
        {
            TransferRecord record = _transfers[key];
            if (record.Order.Equals(order) && (record.AwaitsResolution || record.Status == TransferStatus.Open))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Transfers still open in this session: intents whose receipt has
    /// not been recorded yet.</summary>
    public bool HasOpenTransfer(OrderId order)
    {
        foreach (string key in _transferSequence)
        {
            TransferRecord record = _transfers[key];
            if (record.Order.Equals(order) && record.Status == TransferStatus.Open)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    /// <summary>Delivered, handed-over and lost units stay where the record
    /// put them: the order never moves them again, and a later change to the
    /// chest is the player's.</summary>
    private static bool MayLeave(CustodyPlace place) =>
        place == CustodyPlace.SourceGround || place == CustodyPlace.Worker || place == CustodyPlace.Cart;

    private static CustodyLocation LostLocation(CustodyLocation from) =>
        new CustodyLocation(CustodyPlace.Lost, from.Place + ":" + from.Key, from.WorldLoadEpoch);

    private void Move(OrderId order, CustodyLocation from, CustodyLocation to, MaterialItem item, int count)
    {
        Add(order, from, item, -count);
        Add(order, to, item, count);
    }

    private void Add(OrderId order, CustodyLocation location, MaterialItem item, int delta)
    {
        var key = new HoldingKey(order, location, item);
        if (!_holdings.TryGetValue(key, out int current))
        {
            _holdingSequence.Add(key);
        }

        int next = current + delta;
        if (next < 0)
        {
            // Every caller checks the holding first, so this is a defect. The
            // ledger refuses to go negative rather than pretending a unit
            // exists twice somewhere else.
            throw new InvalidOperationException(
                "Custody for " + order.Value + " at " + location + " would go negative.");
        }

        _holdings[key] = next;
    }

    private static TransferStatus StatusFor(TransferOutcome outcome)
    {
        switch (outcome)
        {
            case TransferOutcome.Completed: return TransferStatus.Completed;
            case TransferOutcome.Partial: return TransferStatus.Partial;
            case TransferOutcome.Refused:
            case TransferOutcome.Stale: return TransferStatus.Refused;
            case TransferOutcome.Uncertain: return TransferStatus.Uncertain;
            default: return TransferStatus.Unspecified;
        }
    }

    private static bool SamePickupResult(PickupResult left, PickupResult right)
    {
        if (left.Outcome != right.Outcome || left.Drops.Count != right.Drops.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Drops.Count; index++)
        {
            SpawnedDrop a = left.Drops[index];
            SpawnedDrop b = right.Drops[index];
            if (!string.Equals(a.SessionId, b.SessionId, StringComparison.Ordinal) || !a.Item.Equals(b.Item) || a.Count != b.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static string Join(string first, string second) =>
        string.IsNullOrEmpty(first) ? second : first + "; " + second;

    private readonly struct HoldingKey : IEquatable<HoldingKey>
    {
        public HoldingKey(OrderId order, CustodyLocation location, MaterialItem item)
        {
            Order = order;
            Location = location;
            Item = item;
        }

        public OrderId Order { get; }

        public CustodyLocation Location { get; }

        public MaterialItem Item { get; }

        public bool Equals(HoldingKey other) =>
            Order.Equals(other.Order) && Location.Equals(other.Location) && Item.Equals(other.Item);

        public override bool Equals(object? obj) => obj is HoldingKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((Order.GetHashCode() * 397) ^ (Location.GetHashCode() * 31) ^ Item.GetHashCode());
    }
}

/// <summary>One non-zero holding, for reports and reconciliation.</summary>
internal readonly struct Holding
{
    public Holding(OrderId order, CustodyLocation location, MaterialItem item, int count)
    {
        Order = order;
        Location = location;
        Item = item;
        Count = count;
    }

    public OrderId Order { get; }

    public CustodyLocation Location { get; }

    public MaterialItem Item { get; }

    public int Count { get; }

    public override string ToString() =>
        Order.Value + " " + Item + " x" + Count.ToString(CultureInfo.InvariantCulture) + " at " + Location;
}

/// <summary>Order-definition comparisons for payload-checked idempotence.
/// </summary>
internal static class CollectionOrders
{
    public static bool SameDefinition(CollectionOrderDefinition left, CollectionOrderDefinition right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (!left.Order.Equals(right.Order)
            || !left.Worker.Equals(right.Worker)
            || left.Participation != right.Participation
            || !string.Equals(left.IssuedByCharacter, right.IssuedByCharacter, StringComparison.Ordinal)
            || left.Quotas.Count != right.Quotas.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Quotas.Count; index++)
        {
            if (left.Quotas[index].Resource != right.Quotas[index].Resource
                || left.Quotas[index].Requested != right.Quotas[index].Requested)
            {
                return false;
            }
        }

        WorkScope a = left.Scope;
        WorkScope b = right.Scope;
        if (a.Source != b.Source || !a.Centre.Equals(b.Centre) || !a.RadiusMetres.Equals(b.RadiusMetres)
            || !string.Equals(a.AnchorDescription, b.AnchorDescription, StringComparison.Ordinal)
            || a.SourceRevision != b.SourceRevision || a.WorldLoadEpoch != b.WorldLoadEpoch)
        {
            return false;
        }

        DeliveryTarget d = left.Delivery;
        DeliveryTarget e = right.Delivery;
        return d.Kind == e.Kind
            && string.Equals(d.ContainerKey ?? string.Empty, e.ContainerKey ?? string.Empty, StringComparison.Ordinal)
            && d.WorldLoadEpoch == e.WorldLoadEpoch
            && d.Position.Equals(e.Position);
    }
}
