using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>Which side of an uncertain transfer a person says is true.
/// </summary>
internal enum TransferSide
{
    Unspecified = 0,

    /// <summary>Nothing arrived: the units are still where they started.
    /// </summary>
    Source = 1,

    /// <summary>The units arrived at the destination.</summary>
    Destination = 2,
}

/// <summary>A count of one item, for baselines and evidence.</summary>
internal readonly struct ItemCount
{
    public ItemCount(MaterialItem item, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A count is never negative.");
        }

        Item = item;
        Count = count;
    }

    public MaterialItem Item { get; }

    public int Count { get; }

    public override string ToString() => Item + " x" + Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The payload of one schema-v3 custody row (CONTRACTS.md §5.4).
///
/// Immutable. Each kind has exactly one payload type, and the entry refuses a
/// payload of the wrong type, so a row can never claim to be a transfer while
/// carrying a pickup.</summary>
internal abstract class CustodyRow
{
    public abstract JournalEntryKind Kind { get; }

    /// <summary>The order the row belongs to. Empty only for a world-save
    /// marker, which belongs to the whole record.</summary>
    public abstract OrderId Order { get; }

    /// <summary>The idempotence key, for the kinds that have one.</summary>
    public virtual RequestId Request => default;

    /// <summary>Fields a newer build wrote that this one does not know. Kept
    /// and written back unchanged.</summary>
    public JournalFields? Unknown { get; internal set; }
}

internal sealed class CollectionAcceptedRow : CustodyRow
{
    public CollectionAcceptedRow(CollectionOrderDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public CollectionOrderDefinition Definition { get; }

    public override JournalEntryKind Kind => JournalEntryKind.CollectionAccepted;

    public override OrderId Order => Definition.Order;
}

internal sealed class CollectionTransitionRow : CustodyRow
{
    public CollectionTransitionRow(
        OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("A transition needs its order.", nameof(order));
        }

        if (from == CollectionOrderState.Unspecified || to == CollectionOrderState.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "A transition needs both states.");
        }

        OrderId = order;
        From = from;
        To = to;
        Reason = reason;
    }

    private OrderId OrderId { get; }

    public CollectionOrderState From { get; }

    public CollectionOrderState To { get; }

    public CollectionAttentionReason Reason { get; }

    public override JournalEntryKind Kind => JournalEntryKind.CollectionTransition;

    public override OrderId Order => OrderId;
}

internal sealed class PickupStartedRow : CustodyRow
{
    public PickupStartedRow(RequestId pickup, OrderId order, SourceKey source)
    {
        if (pickup.IsEmpty || order.IsEmpty || source.IsEmpty)
        {
            throw new ArgumentException("A pickup intent needs its id, its order and its source.");
        }

        Pickup = pickup;
        OrderId = order;
        Source = source;
    }

    public RequestId Pickup { get; }

    private OrderId OrderId { get; }

    public SourceKey Source { get; }

    public override JournalEntryKind Kind => JournalEntryKind.PickupStarted;

    public override OrderId Order => OrderId;

    public override RequestId Request => Pickup;
}

internal sealed class PickupFinishedRow : CustodyRow
{
    public PickupFinishedRow(RequestId pickup, OrderId order, PickupResult result)
    {
        if (pickup.IsEmpty || order.IsEmpty)
        {
            throw new ArgumentException("A pickup result needs its id and its order.");
        }

        if (result == null || result.Outcome == PickupOutcome.Unspecified)
        {
            throw new ArgumentException("A pickup result needs an outcome.", nameof(result));
        }

        Pickup = pickup;
        OrderId = order;
        Result = result;
    }

    public RequestId Pickup { get; }

    private OrderId OrderId { get; }

    public PickupResult Result { get; }

    public override JournalEntryKind Kind => JournalEntryKind.PickupFinished;

    public override OrderId Order => OrderId;

    public override RequestId Request => Pickup;
}

internal sealed class TransferStartedRow : CustodyRow
{
    public TransferStartedRow(TransferIntent intent)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
    }

    public TransferIntent Intent { get; }

    public override JournalEntryKind Kind => JournalEntryKind.TransferStarted;

    public override OrderId Order => Intent.Order;

    public override RequestId Request => Intent.Request;
}

internal sealed class TransferFinishedRow : CustodyRow
{
    public TransferFinishedRow(OrderId order, TransferReceipt receipt)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("A receipt row needs its order.", nameof(order));
        }

        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        if (receipt.Request.IsEmpty)
        {
            throw new ArgumentException("A receipt row needs its request.", nameof(receipt));
        }

        OrderId = order;
    }

    private OrderId OrderId { get; }

    public TransferReceipt Receipt { get; }

    public override JournalEntryKind Kind => JournalEntryKind.TransferFinished;

    public override OrderId Order => OrderId;

    public override RequestId Request => Receipt.Request;
}

internal sealed class TransferResolvedRow : CustodyRow
{
    public TransferResolvedRow(RequestId request, OrderId order, TransferSide side, int units, string note)
    {
        if (request.IsEmpty || order.IsEmpty)
        {
            throw new ArgumentException("A resolution needs the transfer and its order.");
        }

        if (side == TransferSide.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(side), "A resolution says which side is true.");
        }

        if (units < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(units), "Units are never negative.");
        }

        RequestId = request;
        OrderId = order;
        Side = side;
        Units = units;
        Note = note ?? string.Empty;
    }

    private RequestId RequestId { get; }

    private OrderId OrderId { get; }

    public TransferSide Side { get; }

    /// <summary>For <see cref="TransferSide.Destination"/>, how many units the
    /// person confirms arrived. Zero for <see cref="TransferSide.Source"/>.
    /// </summary>
    public int Units { get; }

    public string Note { get; }

    public override JournalEntryKind Kind => JournalEntryKind.TransferResolved;

    public override OrderId Order => OrderId;

    public override RequestId Request => RequestId;
}

internal sealed class CartBaselineRow : CustodyRow
{
    public CartBaselineRow(
        OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch, IReadOnlyList<ItemCount> counts)
    {
        if (order.IsEmpty || string.IsNullOrEmpty(leaseId) || string.IsNullOrEmpty(cartSessionKey))
        {
            throw new ArgumentException("A baseline needs its order, lease and cart.");
        }

        if (providerEpoch == Guid.Empty)
        {
            throw new ArgumentException("A baseline belongs to one provider epoch.", nameof(providerEpoch));
        }

        OrderId = order;
        LeaseId = leaseId;
        CartSessionKey = cartSessionKey;
        ProviderEpoch = providerEpoch;
        Counts = counts ?? Array.Empty<ItemCount>();
    }

    private OrderId OrderId { get; }

    public string LeaseId { get; }

    public string CartSessionKey { get; }

    public Guid ProviderEpoch { get; }

    public IReadOnlyList<ItemCount> Counts { get; }

    public override JournalEntryKind Kind => JournalEntryKind.CartBaselineRecorded;

    public override OrderId Order => OrderId;
}

internal sealed class LossRecordedRow : CustodyRow
{
    public LossRecordedRow(
        RequestId request, OrderId order, CustodyLocation at, MaterialItem item, int count, string reason)
    {
        if (request.IsEmpty || order.IsEmpty)
        {
            throw new ArgumentException("A loss needs its id and its order.");
        }

        if (at.Place == CustodyPlace.Unspecified || count < 1)
        {
            throw new ArgumentException("A loss names where it was and at least one unit.");
        }

        RequestId = request;
        OrderId = order;
        At = at;
        Item = item;
        Count = count;
        Reason = reason ?? string.Empty;
    }

    private RequestId RequestId { get; }

    private OrderId OrderId { get; }

    public CustodyLocation At { get; }

    public MaterialItem Item { get; }

    public int Count { get; }

    public string Reason { get; }

    public override JournalEntryKind Kind => JournalEntryKind.LossRecorded;

    public override OrderId Order => OrderId;

    public override RequestId Request => RequestId;
}

internal sealed class HandoverFinishedRow : CustodyRow
{
    public HandoverFinishedRow(RequestId transfer, OrderId order, MaterialItem item, int count)
    {
        if (transfer.IsEmpty || order.IsEmpty || count < 1)
        {
            throw new ArgumentException("A handover names its transfer, its order and at least one unit.");
        }

        Transfer = transfer;
        OrderId = order;
        Item = item;
        Count = count;
    }

    public RequestId Transfer { get; }

    private OrderId OrderId { get; }

    public MaterialItem Item { get; }

    public int Count { get; }

    public override JournalEntryKind Kind => JournalEntryKind.HandoverFinished;

    public override OrderId Order => OrderId;

    public override RequestId Request => Transfer;
}

/// <summary>C2: after a reload, the player confirmed an order's scope
/// re-snapshotted from the same source and its delivery container selected
/// again. Only the scope and the delivery target change; the rest of the
/// definition — quotas, participation, the issuer — and every unit of custody
/// stay exactly as recorded.</summary>
internal sealed class CollectionReboundRow : CustodyRow
{
    public CollectionReboundRow(OrderId order, WorkScope scope, DeliveryTarget delivery)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("A rebind needs its order.", nameof(order));
        }

        OrderId = order;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));

        if (delivery.Kind == DeliveryKind.Unspecified)
        {
            throw new ArgumentException("A rebind names a delivery.", nameof(delivery));
        }

        Delivery = delivery;
    }

    private OrderId OrderId { get; }

    public WorkScope Scope { get; }

    public DeliveryTarget Delivery { get; }

    public override JournalEntryKind Kind => JournalEntryKind.CollectionRebound;

    public override OrderId Order => OrderId;
}

/// <summary><c>WorldSaveMarker{generation}</c>; the world time is the row's
/// own column. See <c>SaveTimeline</c> for the two ways a marker is read: a
/// save (generation one above the live chain) or a load restatement
/// (a generation already in the chain).</summary>
internal sealed class WorldSaveMarkerRow : CustodyRow
{
    public WorldSaveMarkerRow(int generation)
    {
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Generations start at zero.");
        }

        Generation = generation;
    }

    public int Generation { get; }

    public override JournalEntryKind Kind => JournalEntryKind.WorldSaveMarker;

    public override OrderId Order => default;
}

/// <summary>Reads and writes custody payloads as named fields. Every required
/// field is checked, and a payload that does not satisfy its types' own
/// invariants is damage, never repaired by guessing.</summary>
internal static class CustodyRowCodec
{
    public static JournalFields Encode(CustodyRow row)
    {
        var fields = new JournalFields();
        switch (row)
        {
            case CollectionAcceptedRow accepted:
                WriteDefinition(fields, accepted.Definition);
                break;

            case CollectionTransitionRow transition:
                fields.Add("order", transition.Order.Value)
                    .AddName("from", transition.From)
                    .AddName("to", transition.To)
                    .AddName("reason", transition.Reason);
                break;

            case PickupStartedRow started:
                fields.Add("request", started.Pickup.Value).Add("order", started.Order.Value);
                WriteSource(fields, "source", started.Source);
                break;

            case PickupFinishedRow finished:
                fields.Add("request", finished.Pickup.Value)
                    .Add("order", finished.Order.Value)
                    .AddName("outcome", finished.Result.Outcome)
                    .Add("reason", finished.Result.Reason)
                    .Add("drops", finished.Result.Drops.Count);
                for (int index = 0; index < finished.Result.Drops.Count; index++)
                {
                    SpawnedDrop drop = finished.Result.Drops[index];
                    string prefix = "drop" + index.ToString(CultureInfo.InvariantCulture);
                    fields.Add(prefix + ".id", drop.SessionId).Add(prefix + ".n", drop.Count);
                    WriteItem(fields, prefix, drop.Item);
                }

                break;

            case TransferStartedRow intent:
                fields.Add("request", intent.Intent.Request.Value)
                    .Add("order", intent.Intent.Order.Value);
                WriteLocation(fields, "from", intent.Intent.From);
                WriteLocation(fields, "to", intent.Intent.To);
                WriteItem(fields, "item", intent.Intent.Item);
                fields.Add("count", intent.Intent.Count).Add("rev", intent.Intent.ExpectedCustodyRevision);
                break;

            case TransferFinishedRow receipt:
                fields.Add("request", receipt.Receipt.Request.Value)
                    .Add("order", receipt.Order.Value)
                    .AddName("outcome", receipt.Receipt.Outcome)
                    .Add("accepted", receipt.Receipt.Accepted);
                WriteLocation(fields, "rem", receipt.Receipt.RemainderAt);
                fields.Add("evidence", receipt.Receipt.Evidence);
                break;

            case TransferResolvedRow resolved:
                fields.Add("request", resolved.Request.Value)
                    .Add("order", resolved.Order.Value)
                    .AddName("side", resolved.Side)
                    .Add("units", resolved.Units)
                    .Add("note", resolved.Note);
                break;

            case CartBaselineRow baseline:
                fields.Add("order", baseline.Order.Value)
                    .Add("lease", baseline.LeaseId)
                    .Add("cart", baseline.CartSessionKey)
                    .Add("epoch", baseline.ProviderEpoch)
                    .Add("items", baseline.Counts.Count);
                for (int index = 0; index < baseline.Counts.Count; index++)
                {
                    string prefix = "item" + index.ToString(CultureInfo.InvariantCulture);
                    WriteItem(fields, prefix, baseline.Counts[index].Item);
                    fields.Add(prefix + ".n", baseline.Counts[index].Count);
                }

                break;

            case LossRecordedRow loss:
                fields.Add("request", loss.Request.Value).Add("order", loss.Order.Value);
                WriteLocation(fields, "at", loss.At);
                WriteItem(fields, "item", loss.Item);
                fields.Add("count", loss.Count).Add("reason", loss.Reason);
                break;

            case HandoverFinishedRow handover:
                fields.Add("request", handover.Transfer.Value).Add("order", handover.Order.Value);
                WriteItem(fields, "item", handover.Item);
                fields.Add("count", handover.Count);
                break;

            case WorldSaveMarkerRow marker:
                fields.Add("generation", marker.Generation);
                break;

            case CollectionReboundRow rebound:
                fields.Add("order", rebound.Order.Value);
                WriteScope(fields, rebound.Scope);
                WriteDelivery(fields, rebound.Delivery);
                break;

            default:
                throw new ArgumentException("Not a custody payload this build can write.", nameof(row));
        }

        if (row.Unknown != null)
        {
            foreach (KeyValuePair<string, string> pair in row.Unknown.Pairs)
            {
                if (!fields.Has(pair.Key))
                {
                    fields.Add(pair.Key, pair.Value);
                }
            }
        }

        return fields;
    }

    public static bool TryDecode(JournalEntryKind kind, JournalFields fields, out CustodyRow row)
    {
        row = null!;
        try
        {
            CustodyRow? decoded = Decode(kind, fields, out HashSet<string> used);
            if (decoded == null)
            {
                return false;
            }

            var unknown = new JournalFields();
            foreach (KeyValuePair<string, string> pair in fields.Pairs)
            {
                if (!used.Contains(pair.Key))
                {
                    unknown.Add(pair.Key, pair.Value);
                }
            }

            if (unknown.Count > 0)
            {
                decoded.Unknown = unknown;
            }

            row = decoded;
            return true;
        }
        catch (ArgumentException)
        {
            // A payload that breaks its types' own rules is damage.
            // ArgumentOutOfRangeException derives from ArgumentException.
            return false;
        }
    }

    private static CustodyRow? Decode(JournalEntryKind kind, JournalFields fields, out HashSet<string> used)
    {
        var reader = new Reader(fields);
        used = reader.Used;

        switch (kind)
        {
            case JournalEntryKind.CollectionAccepted:
                return reader.Definition(out CollectionOrderDefinition? definition)
                    ? new CollectionAcceptedRow(definition!)
                    : null;

            case JournalEntryKind.CollectionTransition:
                return reader.Order("order", out OrderId order)
                    && reader.Name("from", out CollectionOrderState from)
                    && reader.Name("to", out CollectionOrderState to)
                    && reader.NameOrUnspecified("reason", out CollectionAttentionReason reason)
                        ? new CollectionTransitionRow(order, from, to, reason)
                        : null;

            case JournalEntryKind.PickupStarted:
                return reader.Request("request", out RequestId pickup)
                    && reader.Order("order", out OrderId pickupOrder)
                    && reader.Source("source", out SourceKey source)
                        ? new PickupStartedRow(pickup, pickupOrder, source)
                        : null;

            case JournalEntryKind.PickupFinished:
            {
                if (!reader.Request("request", out RequestId finishedPickup)
                    || !reader.Order("order", out OrderId finishedOrder)
                    || !reader.Name("outcome", out PickupOutcome outcome)
                    || !reader.Text("reason", out string pickupReason)
                    || !reader.Int("drops", out int dropCount)
                    || dropCount < 0)
                {
                    return null;
                }

                var drops = new List<SpawnedDrop>(dropCount);
                for (int index = 0; index < dropCount; index++)
                {
                    string prefix = "drop" + index.ToString(CultureInfo.InvariantCulture);
                    if (!reader.Required(prefix + ".id", out string id)
                        || !reader.Int(prefix + ".n", out int n)
                        || !reader.Item(prefix, out MaterialItem item))
                    {
                        return null;
                    }

                    drops.Add(new SpawnedDrop(id, item, n));
                }

                return new PickupFinishedRow(
                    finishedPickup, finishedOrder, new PickupResult(outcome, drops, pickupReason));
            }

            case JournalEntryKind.TransferStarted:
                return reader.Request("request", out RequestId transfer)
                    && reader.Order("order", out OrderId transferOrder)
                    && reader.Location("from", out CustodyLocation fromLocation)
                    && reader.Location("to", out CustodyLocation toLocation)
                    && reader.Item("item", out MaterialItem transferItem)
                    && reader.Int("count", out int count)
                    && reader.Int("rev", out int revision)
                        ? new TransferStartedRow(new TransferIntent(
                            transfer, transferOrder, fromLocation, toLocation, transferItem, count, revision))
                        : null;

            case JournalEntryKind.TransferFinished:
                return reader.Request("request", out RequestId receiptRequest)
                    && reader.Order("order", out OrderId receiptOrder)
                    && reader.Name("outcome", out TransferOutcome transferOutcome)
                    && reader.Int("accepted", out int accepted)
                    && reader.LocationOrDefault("rem", out CustodyLocation remainder)
                    && reader.Text("evidence", out string evidence)
                        ? new TransferFinishedRow(
                            receiptOrder,
                            new TransferReceipt(receiptRequest, transferOutcome, accepted, remainder, evidence))
                        : null;

            case JournalEntryKind.TransferResolved:
                return reader.Request("request", out RequestId resolvedRequest)
                    && reader.Order("order", out OrderId resolvedOrder)
                    && reader.Name("side", out TransferSide side)
                    && reader.Int("units", out int units)
                    && reader.Text("note", out string note)
                        ? new TransferResolvedRow(resolvedRequest, resolvedOrder, side, units, note)
                        : null;

            case JournalEntryKind.CartBaselineRecorded:
            {
                if (!reader.Order("order", out OrderId baselineOrder)
                    || !reader.Required("lease", out string lease)
                    || !reader.Required("cart", out string cart)
                    || !reader.Guid("epoch", out Guid epoch)
                    || !reader.Int("items", out int itemCount)
                    || itemCount < 0)
                {
                    return null;
                }

                var counts = new List<ItemCount>(itemCount);
                for (int index = 0; index < itemCount; index++)
                {
                    string prefix = "item" + index.ToString(CultureInfo.InvariantCulture);
                    if (!reader.Item(prefix, out MaterialItem baselineItem) || !reader.Int(prefix + ".n", out int n))
                    {
                        return null;
                    }

                    counts.Add(new ItemCount(baselineItem, n));
                }

                return new CartBaselineRow(baselineOrder, lease, cart, epoch, counts);
            }

            case JournalEntryKind.LossRecorded:
                return reader.Request("request", out RequestId lossRequest)
                    && reader.Order("order", out OrderId lossOrder)
                    && reader.Location("at", out CustodyLocation at)
                    && reader.Item("item", out MaterialItem lossItem)
                    && reader.Int("count", out int lossCount)
                    && reader.Text("reason", out string lossReason)
                        ? new LossRecordedRow(lossRequest, lossOrder, at, lossItem, lossCount, lossReason)
                        : null;

            case JournalEntryKind.HandoverFinished:
                return reader.Request("request", out RequestId handoverRequest)
                    && reader.Order("order", out OrderId handoverOrder)
                    && reader.Item("item", out MaterialItem handoverItem)
                    && reader.Int("count", out int handoverCount)
                        ? new HandoverFinishedRow(handoverRequest, handoverOrder, handoverItem, handoverCount)
                        : null;

            case JournalEntryKind.WorldSaveMarker:
                return reader.Int("generation", out int generation)
                    ? new WorldSaveMarkerRow(generation)
                    : null;

            case JournalEntryKind.CollectionRebound:
                return reader.Order("order", out OrderId reboundOrder)
                    && reader.Scope(out WorkScope? reboundScope)
                    && reader.Delivery(out DeliveryTarget reboundDelivery)
                    && reboundDelivery.Kind != DeliveryKind.Unspecified
                        ? new CollectionReboundRow(reboundOrder, reboundScope!, reboundDelivery)
                        : null;

            default:
                return null;
        }
    }

    private static void WriteLocation(JournalFields fields, string prefix, CustodyLocation location)
    {
        fields.Add(prefix + ".place", location.Place == CustodyPlace.Unspecified ? string.Empty : location.Place.ToString())
            .Add(prefix + ".key", location.Key)
            .Add(prefix + ".epoch", location.WorldLoadEpoch);
    }

    private static void WriteItem(JournalFields fields, string prefix, MaterialItem item)
    {
        fields.Add(prefix + ".prefab", item.PrefabName)
            .Add(prefix + ".q", item.Quality)
            .Add(prefix + ".v", item.Variant);
    }

    private static void WriteSource(JournalFields fields, string prefix, SourceKey source)
    {
        fields.Add(prefix + ".prefab", source.PrefabName)
            .Add(prefix + ".id", source.SessionId)
            .Add(prefix + ".epoch", source.WorldLoadEpoch)
            .Add(prefix + ".x", source.Position.X)
            .Add(prefix + ".y", source.Position.Y)
            .Add(prefix + ".z", source.Position.Z);
    }

    private static void WriteScope(JournalFields fields, WorkScope scope)
    {
        fields.AddName("scope.source", scope.Source)
            .Add("scope.x", scope.Centre.X)
            .Add("scope.y", scope.Centre.Y)
            .Add("scope.z", scope.Centre.Z)
            .Add("scope.r", scope.RadiusMetres)
            .Add("scope.anchor", scope.AnchorDescription)
            .Add("scope.rev", scope.SourceRevision)
            .Add("scope.epoch", scope.WorldLoadEpoch);
    }

    private static void WriteDelivery(JournalFields fields, DeliveryTarget delivery)
    {
        fields.Add("delivery.kind", delivery.Kind == DeliveryKind.Unspecified ? string.Empty : delivery.Kind.ToString());
        if (delivery.Kind == DeliveryKind.Container)
        {
            fields.Add("delivery.key", delivery.ContainerKey)
                .Add("delivery.epoch", delivery.WorldLoadEpoch)
                .Add("delivery.x", delivery.Position.X)
                .Add("delivery.y", delivery.Position.Y)
                .Add("delivery.z", delivery.Position.Z);
        }
    }

    private static void WriteDefinition(JournalFields fields, CollectionOrderDefinition definition)
    {
        fields.Add("order", definition.Order.Value)
            .Add("worker", definition.Worker.Value)
            .Add("quotas", definition.Quotas.Count);
        for (int index = 0; index < definition.Quotas.Count; index++)
        {
            string prefix = "quota" + index.ToString(CultureInfo.InvariantCulture);
            fields.AddName(prefix + ".resource", definition.Quotas[index].Resource)
                .Add(prefix + ".n", definition.Quotas[index].Requested);
        }

        WriteScope(fields, definition.Scope);
        WriteDelivery(fields, definition.Delivery);

        fields.Add("participation", definition.Participation == ParticipationMode.Unspecified
                ? string.Empty
                : definition.Participation.ToString())
            .Add("issuer", definition.IssuedByCharacter);
    }

    /// <summary>Reads named fields and remembers which ones it used, so the
    /// rest can be preserved as forward data.</summary>
    private sealed class Reader
    {
        private readonly JournalFields _fields;

        public Reader(JournalFields fields)
        {
            _fields = fields;
        }

        public HashSet<string> Used { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool Text(string key, out string value)
        {
            Used.Add(key);
            return _fields.TryGetText(key, out value);
        }

        public bool Required(string key, out string value)
        {
            Used.Add(key);
            return _fields.TryGetRequired(key, out value);
        }

        public bool Int(string key, out int value)
        {
            Used.Add(key);
            return _fields.TryGetInt(key, out value);
        }

        public bool Float(string key, out float value)
        {
            Used.Add(key);
            return _fields.TryGetFloat(key, out value);
        }

        public bool Guid(string key, out Guid value)
        {
            Used.Add(key);
            return _fields.TryGetGuid(key, out value);
        }

        public bool Name<TEnum>(string key, out TEnum value)
            where TEnum : struct
        {
            Used.Add(key);
            return _fields.TryGetName(key, out value) && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
        }

        /// <summary>An enum that may legitimately be its zero value (a
        /// transition without an attention reason).</summary>
        public bool NameOrUnspecified<TEnum>(string key, out TEnum value)
            where TEnum : struct
        {
            Used.Add(key);
            return _fields.TryGetName(key, out value);
        }

        public bool Order(string key, out OrderId value)
        {
            value = default;
            if (!Required(key, out string text))
            {
                return false;
            }

            value = new OrderId(text);
            return true;
        }

        public bool Request(string key, out RequestId value)
        {
            value = default;
            if (!Required(key, out string text))
            {
                return false;
            }

            value = new RequestId(text);
            return true;
        }

        public bool Item(string prefix, out MaterialItem item)
        {
            item = default;
            if (!Required(prefix + ".prefab", out string prefab)
                || !Int(prefix + ".q", out int quality)
                || !Int(prefix + ".v", out int variant)
                || quality < 1
                || variant < 0)
            {
                return false;
            }

            item = new MaterialItem(prefab, quality, variant);
            return true;
        }

        public bool Location(string prefix, out CustodyLocation location)
        {
            location = default;
            if (!Name(prefix + ".place", out CustodyPlace place)
                || !Text(prefix + ".key", out string key)
                || !Guid(prefix + ".epoch", out Guid epoch))
            {
                return false;
            }

            location = new CustodyLocation(place, key, epoch);
            return true;
        }

        /// <summary>A location that may be absent (a receipt with no
        /// remainder).</summary>
        public bool LocationOrDefault(string prefix, out CustodyLocation location)
        {
            location = default;
            if (!Text(prefix + ".place", out string placeText))
            {
                return false;
            }

            if (placeText.Length == 0)
            {
                Used.Add(prefix + ".key");
                Used.Add(prefix + ".epoch");
                return true;
            }

            return Location(prefix, out location);
        }

        public bool Source(string prefix, out SourceKey source)
        {
            source = default;
            if (!Required(prefix + ".prefab", out string prefab)
                || !Required(prefix + ".id", out string id)
                || !Guid(prefix + ".epoch", out Guid epoch)
                || !Float(prefix + ".x", out float x)
                || !Float(prefix + ".y", out float y)
                || !Float(prefix + ".z", out float z))
            {
                return false;
            }

            source = new SourceKey(prefab, id, epoch, new SitePoint(x, y, z));
            return true;
        }

        public bool Definition(out CollectionOrderDefinition? definition)
        {
            definition = null;
            if (!Order("order", out OrderId order)
                || !Required("worker", out string worker)
                || !Int("quotas", out int quotaCount)
                || quotaCount < 0)
            {
                return false;
            }

            var quotas = new List<ResourceQuota>(quotaCount);
            for (int index = 0; index < quotaCount; index++)
            {
                string prefix = "quota" + index.ToString(CultureInfo.InvariantCulture);
                if (!Name(prefix + ".resource", out CollectedResource resource) || !Int(prefix + ".n", out int n))
                {
                    return false;
                }

                quotas.Add(new ResourceQuota(resource, n));
            }

            if (!Scope(out WorkScope? scope) || !Delivery(out DeliveryTarget delivery))
            {
                return false;
            }

            if (!Text("participation", out string participationText))
            {
                return false;
            }

            ParticipationMode participation = ParticipationMode.Unspecified;
            if (participationText.Length > 0 && !Name("participation", out participation))
            {
                return false;
            }

            if (!Text("issuer", out string issuer))
            {
                return false;
            }

            definition = new CollectionOrderDefinition(
                order, new WorkerId(worker), quotas, scope!, delivery, participation, issuer);
            return true;
        }

        public bool Scope(out WorkScope? scope)
        {
            scope = null;
            if (!Name("scope.source", out WorkScopeSource scopeSource)
                || !Float("scope.x", out float sx)
                || !Float("scope.y", out float sy)
                || !Float("scope.z", out float sz)
                || !Float("scope.r", out float radius)
                || !Text("scope.anchor", out string anchor)
                || !Int("scope.rev", out int scopeRevision)
                || !Guid("scope.epoch", out Guid scopeEpoch))
            {
                return false;
            }

            scope = new WorkScope(scopeSource, new SitePoint(sx, sy, sz), radius, anchor, scopeRevision, scopeEpoch);
            return true;
        }

        public bool Delivery(out DeliveryTarget delivery)
        {
            delivery = default;
            if (!Text("delivery.kind", out string deliveryText))
            {
                return false;
            }

            if (deliveryText.Length == 0)
            {
                return true;
            }

            if (!Name("delivery.kind", out DeliveryKind deliveryKind))
            {
                return false;
            }

            if (deliveryKind == DeliveryKind.HoldForPlayer)
            {
                delivery = DeliveryTarget.HoldForPlayer();
                return true;
            }

            if (Required("delivery.key", out string containerKey)
                && Guid("delivery.epoch", out Guid deliveryEpoch)
                && Float("delivery.x", out float dx)
                && Float("delivery.y", out float dy)
                && Float("delivery.z", out float dz))
            {
                delivery = DeliveryTarget.ToContainer(containerKey, deliveryEpoch, new SitePoint(dx, dy, dz));
                return true;
            }

            return false;
        }
    }
}
