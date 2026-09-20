using System;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Work;

// Contract revision C1 (docs/settlement/cart-and-collection/CONTRACTS.md, TASKS.md §3).
// The seams between Foreman's collection loop (agent C), custody and recovery
// (agent D) and cooperative delivery (agent E). Each is implemented by exactly one
// agent and consumed by the others; none of them lets a consumer mutate an
// inventory or a body except through the owner's implementation.

internal enum WorkerWalkStatus
{
    Unspecified = 0,

    /// <summary>No goal.</summary>
    Idle = 1,

    Walking = 2,

    /// <summary>Within the requested tolerance of the goal (by distance, never
    /// by BaseAI.MoveTo's return value).</summary>
    Arrived = 3,

    /// <summary>The movement planner gave up; see
    /// <see cref="IWorkerMotion.LastDeferral"/>.</summary>
    Deferred = 4,
}

/// <summary>Walking Thorstein (agent C implements over ForemanWorkerAI). Only
/// the job holding the worker's actor mode may command it.</summary>
internal interface IWorkerMotion
{
    WorkerKey Worker { get; }

    /// <summary>The body exists, is owned here and is not faulted.</summary>
    bool IsPresent { get; }

    Vector3 Position { get; }

    WorkerWalkStatus Status { get; }

    WorkerDeferralReason LastDeferral { get; }

    /// <summary>Starts or replaces a walk. False when the job does not hold the
    /// worker or the body is not present.</summary>
    bool WalkTo(Vector3 point, float arrivalTolerance, string jobId);

    void Stop(string jobId);
}

/// <summary>Custody and recovery for Foreman (agent D implements). Every
/// material movement and every order record goes through here, journal first.
/// </summary>
internal interface ICustodyRuntime
{
    IMaterialCustodyView View { get; }

    ITransferExecutor Executor { get; }

    /// <summary>The journal is loaded, writable and not awaiting a person's
    /// resolution.</summary>
    bool IsWritable { get; }

    /// <summary>C2: the one epoch minted for this world load. Every SourceKey,
    /// WorkScope, DeliveryTarget and CustodyLocation made during the load uses it.
    /// </summary>
    Guid WorldLoadEpoch { get; }

    /// <summary>C2: after replay, the non-terminal collection order recorded for
    /// <paramref name="worker"/>, so it can be adopted (Paused) after a reload.
    /// Its scope and delivery target still carry the previous load's epoch until
    /// the player confirms a rebind.</summary>
    bool TryRecoverOrder(WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state);

    /// <summary>C2: journals a player-confirmed post-reload rebind of an order's
    /// scope snapshot and delivery container (<c>CollectionRebound</c>). Quotas,
    /// progress and custody are unchanged.</summary>
    bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery);

    /// <summary>Where the record says the worker's own inventory is, for asking
    /// the view what it holds for anybody. One source of truth for that key: a
    /// consumer that built it again would agree with the record until somebody
    /// changed how the place is named.</summary>
    CustodyLocation WorkerLocation { get; }

    /// <summary>The worker body's persisted inventory.</summary>
    bool TryResolveWorker(WorkerKey worker, out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>The order's delivery container in this world load: owned here,
    /// not in use, ward and privacy access, reachable. Never another chest.
    /// </summary>
    bool TryResolveContainer(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>A leased cart's container, by the cart's in-session key and the
    /// provider epoch, above the recorded baseline.</summary>
    bool TryResolveCart(string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>Journals an accepted order (<c>CollectionAccepted</c>).</summary>
    bool RecordAccepted(CollectionOrderDefinition order);

    /// <summary>Journals a state change with its reason.</summary>
    bool RecordTransition(OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason);

    /// <summary>Records the pre-existing cargo of a cart before an order first
    /// uses it (<c>CartBaselineRecorded</c>).</summary>
    bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch);

    /// <summary>Journals and persists a pickup intent before the pick
    /// (<c>PickupStarted</c>). Null when it could not be persisted.</summary>
    RequestId? BeginPickup(OrderId order, SourceKey source);

    /// <summary>Journals the pick's traced drops (<c>PickupFinished</c>).
    /// </summary>
    bool FinishPickup(RequestId pickup, PickupResult result);

    /// <summary>Journals and persists a transfer intent for moves that happen in
    /// one engine call outside <see cref="Executor"/> (taking a drop).
    /// Unspecified means "go ahead"; anything else means do not mutate.
    /// </summary>
    TransferOutcome BeginTransfer(TransferIntent intent);

    bool FinishTransfer(TransferReceipt receipt);
}

internal enum CooperativeStep
{
    Unspecified = 0,

    /// <summary>Rendezvous, loading, hauling or unloading is in progress.</summary>
    Working = 1,

    /// <summary>Thorstein should go and collect more; the cart waits.</summary>
    CollectMore = 2,

    /// <summary>Everything requested is delivered.</summary>
    Delivered = 3,

    Paused = 4,

    NeedsAttention = 5,
}

/// <summary>Cooperative delivery with Gunnar (agent E implements). The solo
/// loop (agent C) hands control here while the order's participation is
/// WithHauler, and takes it back on CollectMore.</summary>
internal interface ICooperativeDelivery
{
    /// <summary>The haul provider is present and compatible, Gunnar is ready,
    /// and an assigned cart is usable. False gives a reason (COOP-03).</summary>
    bool IsAvailable(out CollectionAttentionReason reason);

    CooperativeStep Tick(CollectionOrderDefinition order, float now, out CollectionAttentionReason reason);

    /// <summary>Stops cooperation safely: no transfer left half-done, the haul
    /// cancelled with the requested disposition.</summary>
    void Cancel(CollectionOrderDefinition order, bool detachAndPark);
}
