using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>Where a real unit of gathered material physically is (DATA-02).
/// Every accepted unit is in exactly one of these, or has a recorded
/// disposition. Reservations, estimates and journal receipts are not places.
/// </summary>
internal enum CustodyPlace
{
    Unspecified = 0,

    /// <summary>Items this order's own pick spawned, still lying at the source.
    /// Only traceable drops of the order's pick; never any other drop.</summary>
    SourceGround = 1,

    /// <summary>In the worker body's own persisted inventory (D9).</summary>
    Worker = 2,

    /// <summary>In the assigned cart's container, above its recorded
    /// pre-existing cargo.</summary>
    Cart = 3,

    /// <summary>In the order's delivery container. Credited once.</summary>
    Destination = 4,

    /// <summary>Handed to the player in hold-for-player mode.</summary>
    Player = 5,

    /// <summary>Observed destroyed or removed by someone else. Recorded, never
    /// silently reversed.</summary>
    Lost = 6,
}

/// <summary>A custody place plus which instance of it, for one world load.
/// </summary>
internal readonly struct CustodyLocation : IEquatable<CustodyLocation>
{
    public CustodyLocation(CustodyPlace place, string key, Guid worldLoadEpoch)
    {
        if (place == CustodyPlace.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(place), "A location needs a place.");
        }

        Place = place;
        Key = key ?? string.Empty;
        WorldLoadEpoch = worldLoadEpoch;
    }

    public CustodyPlace Place { get; }

    /// <summary>The worker key, cart session key, container key, source key or
    /// player id - whatever names the instance within its place.</summary>
    public string Key { get; }

    public Guid WorldLoadEpoch { get; }

    public bool Equals(CustodyLocation other) =>
        Place == other.Place && string.Equals(Key, other.Key, StringComparison.Ordinal) &&
        WorldLoadEpoch == other.WorldLoadEpoch;

    public override bool Equals(object? obj) => obj is CustodyLocation other && Equals(other);

    public override int GetHashCode() =>
        unchecked(((int)Place * 397) ^ StringComparer.Ordinal.GetHashCode(Key ?? string.Empty) ^ WorldLoadEpoch.GetHashCode());

    public override string ToString() => Place + ":" + Key;
}

/// <summary>The item identity that must survive a transfer unchanged
/// (DATA-03). Stone and Wood carry no crafter or variant today, but the
/// contract does not assume that.</summary>
internal readonly struct MaterialItem : IEquatable<MaterialItem>
{
    public MaterialItem(string prefabName, int quality, int variant)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            throw new ArgumentException("An item needs its prefab name.", nameof(prefabName));
        }

        PrefabName = prefabName;
        Quality = quality < 1 ? 1 : quality;
        Variant = variant < 0 ? 0 : variant;
    }

    public string PrefabName { get; }

    public int Quality { get; }

    public int Variant { get; }

    public static MaterialItem Of(CollectedResource resource) =>
        new MaterialItem(CollectedResources.ItemPrefabName(resource), 1, 0);

    public bool Equals(MaterialItem other) =>
        string.Equals(PrefabName, other.PrefabName, StringComparison.Ordinal) &&
        Quality == other.Quality && Variant == other.Variant;

    public override bool Equals(object? obj) => obj is MaterialItem other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(PrefabName ?? string.Empty) * 397) ^ (Quality * 31) ^ Variant);

    public override string ToString() => PrefabName + (Quality > 1 ? "*q" + Quality : string.Empty);
}

/// <summary>What is about to move, recorded durably BEFORE any engine
/// mutation (DATA-03). The request id is the idempotence key: the same id and
/// the same payload is the same transfer; the same id and a different payload
/// is refused.</summary>
internal sealed class TransferIntent
{
    public TransferIntent(
        RequestId request, OrderId order, CustodyLocation from, CustodyLocation to, MaterialItem item, int count,
        int expectedCustodyRevision)
    {
        if (request.IsEmpty || order.IsEmpty)
        {
            throw new ArgumentException("A transfer needs a request and an order.");
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A transfer moves at least one unit.");
        }

        if (from.Equals(to))
        {
            throw new ArgumentException("A transfer moves between two different locations.");
        }

        Request = request;
        Order = order;
        From = from;
        To = to;
        Item = item;
        Count = count;
        ExpectedCustodyRevision = expectedCustodyRevision;
    }

    public RequestId Request { get; }

    public OrderId Order { get; }

    public CustodyLocation From { get; }

    public CustodyLocation To { get; }

    public MaterialItem Item { get; }

    public int Count { get; }

    /// <summary>The custody revision the planner read. A transfer based on an
    /// older view is refused as stale, never applied.</summary>
    public int ExpectedCustodyRevision { get; }

    public bool SamePayloadAs(TransferIntent other) =>
        Order.Equals(other.Order) && From.Equals(other.From) && To.Equals(other.To) &&
        Item.Equals(other.Item) && Count == other.Count;
}

internal enum TransferOutcome
{
    Unspecified = 0,

    /// <summary>Every unit arrived.</summary>
    Completed = 1,

    /// <summary>Some units arrived; the rest are in a known location named by
    /// the receipt.</summary>
    Partial = 2,

    /// <summary>Nothing moved (checked before any mutation).</summary>
    Refused = 3,

    /// <summary>A mutation may or may not have happened and the actual state
    /// could not prove which. The order goes to NeedsAttention with evidence;
    /// nothing is replayed or compensated.</summary>
    Uncertain = 4,

    /// <summary>Based on an old custody revision; nothing moved.</summary>
    Stale = 5,

    /// <summary>The same request id was already applied with the same payload.
    /// </summary>
    AlreadySatisfied = 6,

    /// <summary>The same request id carries a different payload.</summary>
    RejectedDifferentPayload = 7,
}

/// <summary>What actually happened, recorded after the engine mutations. The
/// only thing that moves counts in the ledger: actual accepted units, never
/// the intended count.</summary>
internal sealed class TransferReceipt
{
    public TransferReceipt(RequestId request, TransferOutcome outcome, int accepted, CustodyLocation remainderAt, string evidence)
    {
        if (outcome == TransferOutcome.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A receipt needs an outcome.");
        }

        if (accepted < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted), "Accepted units cannot be negative.");
        }

        Request = request;
        Outcome = outcome;
        Accepted = accepted;
        RemainderAt = remainderAt;
        Evidence = evidence ?? string.Empty;
    }

    public RequestId Request { get; }

    public TransferOutcome Outcome { get; }

    public int Accepted { get; }

    /// <summary>Where units that did not arrive are (normally the source
    /// location).</summary>
    public CustodyLocation RemainderAt { get; }

    /// <summary>What was observed, for the player and for a reviewer.</summary>
    public string Evidence { get; }
}

/// <summary>An engine inventory behind a seam (DATA-02), so the add/remove
/// ordering of every transfer can be tested and fault-injected without the
/// game. Implementations never throw for game reasons: they answer counts.
/// </summary>
internal interface IInventoryPort
{
    /// <summary>For logs and evidence: "Thorstein", "the cart", "chest at …".
    /// </summary>
    string Describe { get; }

    /// <summary>Whether the inventory still exists, is loaded and may be
    /// written by this process now.</summary>
    bool IsAvailable { get; }

    int Count(MaterialItem item);

    /// <summary>How many of <paramref name="count"/> would fit now, counting
    /// stack limits, free slots, weight limits and pre-existing contents.
    /// </summary>
    int CanAccept(MaterialItem item, int count);

    /// <summary>Adds up to <paramref name="count"/>; returns how many were
    /// actually added.</summary>
    int Add(MaterialItem item, int count);

    /// <summary>Removes up to <paramref name="count"/>; returns how many were
    /// actually removed.</summary>
    int Remove(MaterialItem item, int count);
}

internal enum PickupOutcome
{
    Unspecified = 0,

    /// <summary>The source was picked and its drops identified.</summary>
    Picked = 1,

    /// <summary>Refused before any mutation: gone, exhausted, not loaded, not
    /// allowed, not natural, out of reach, not owned here.</summary>
    Refused = 2,

    /// <summary>The pick may have happened but its drops could not be
    /// identified. NeedsAttention; nothing is granted.</summary>
    Uncertain = 3,
}

/// <summary>What one pick produced: the traceable drops the game itself
/// spawned for it.</summary>
internal sealed class PickupResult
{
    public PickupResult(PickupOutcome outcome, IReadOnlyList<SpawnedDrop> drops, string reason)
    {
        Outcome = outcome;
        Drops = drops ?? Array.Empty<SpawnedDrop>();
        Reason = reason ?? string.Empty;
    }

    public PickupOutcome Outcome { get; }

    public IReadOnlyList<SpawnedDrop> Drops { get; }

    public string Reason { get; }
}

/// <summary>One item stack the game dropped for this order's pick, identified
/// by its in-session id so it can be picked up by the worker and by nothing
/// else.</summary>
internal readonly struct SpawnedDrop
{
    public SpawnedDrop(string sessionId, MaterialItem item, int count)
    {
        if (string.IsNullOrEmpty(sessionId) || count < 1)
        {
            throw new ArgumentException("A spawned drop needs an id and a positive count.");
        }

        SessionId = sessionId;
        Item = item;
        Count = count;
    }

    public string SessionId { get; }

    public MaterialItem Item { get; }

    public int Count { get; }
}

/// <summary>The pickup seam agent C implements over the game.</summary>
internal interface ISourcePickupPort
{
    /// <summary>Revalidates the source (natural, available, loaded, allowed,
    /// within reach, reserved by <paramref name="order"/>), performs the
    /// game's own pick once, and identifies the drops it spawned.</summary>
    PickupResult TryPick(SourceKey source, OrderId order);

    /// <summary>Moves one identified drop into the worker's inventory: the
    /// transfer from SourceGround to Worker, through the game's own pickup.
    /// Returns the units actually taken.</summary>
    int TryTakeDrop(SpawnedDrop drop, IInventoryPort worker);
}

/// <summary>Read-only custody answers for progress and planning (agent D
/// implements over the journal-backed ledger).</summary>
internal interface IMaterialCustodyView
{
    int Revision { get; }

    int CountAt(OrderId order, CustodyPlace place, CollectedResource resource);

    /// <summary>Every order's holding at one location, per item - what a physical
    /// inventory should contain of gathered material, <b>whoever it belongs
    /// to</b>.
    ///
    /// <b>Why this is on the view and not only on the ledger.</b>
    /// <see cref="CountAt"/> answers for one order, which means a caller has to
    /// know which orders exist - and the recovery seam only hands back
    /// <i>non-terminal</i> ones. A CANCELLED collection order is terminal and its
    /// carried material stays exactly where it physically is, recorded, until
    /// somebody moves it (<c>CollectionOrderState.Cancelled</c> says so in as many
    /// words). So "is there gathered material in this worker's inventory that is
    /// not mine" has no answer through <see cref="CountAt"/>, and a second product
    /// that reads his inventory - #380's build order - would spend somebody else's
    /// wood on a wall and never tell custody. This is that answer.</summary>
    int TotalAt(CustodyLocation location, MaterialItem item);

    ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource);

    bool HasUncertainTransfer(OrderId order);
}

/// <summary>Performs one transfer end to end: intent persisted first, engine
/// mutations through ports in a fixed order, receipt persisted after (agent D).
/// </summary>
internal interface ITransferExecutor
{
    TransferReceipt Execute(TransferIntent intent, IInventoryPort from, IInventoryPort to);
}
