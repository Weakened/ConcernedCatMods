using System;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Where a walk Thorstein was given stands.</summary>
internal enum CooperationWalkStatus
{
    Unspecified = 0,
    Idle = 1,
    Walking = 2,

    /// <summary>Within the requested tolerance, by distance.</summary>
    Arrived = 3,

    /// <summary>The movement planner gave up on this goal.</summary>
    Deferred = 4,
}

/// <summary>Thorstein's body as the cooperative loop may use it: walk and stop,
/// nothing else. The Foreman adapter implements it over the collection agent's
/// worker motion, commanding him in the name of the order that holds his actor
/// mode; the loop never moves a body itself and never touches an inventory
/// through this port.</summary>
internal interface ICooperationWorker
{
    /// <summary>The body exists, is owned here and is not faulted.</summary>
    bool IsPresent { get; }

    SitePoint Position { get; }

    CooperationWalkStatus WalkStatus { get; }

    /// <summary>Starts or replaces a walk. False when the order does not hold
    /// the worker or the body is not present.</summary>
    bool WalkTo(SitePoint point, float tolerance);

    void Stop();
}

/// <summary>The custody seam the cooperative loop moves material through
/// (agent D's runtime behind the Foreman adapter): the journal-backed view, the
/// transfer executor, the three places a load passes through, and the cart
/// baseline. Every unit that moves goes through <see cref="Executor"/>, intent
/// first; the loop only decides what to ask for.</summary>
internal interface ICooperationCustody
{
    IMaterialCustodyView View { get; }

    ITransferExecutor Executor { get; }

    /// <summary>The journal is loaded, writable and not awaiting a person's
    /// resolution.</summary>
    bool IsWritable { get; }

    /// <summary>Thorstein's persisted inventory as a custody location.</summary>
    CustodyLocation WorkerLocation { get; }

    /// <summary>A leased cart's container as a custody location, by the cart's
    /// in-session key in the provider epoch it was described in.</summary>
    CustodyLocation CartLocation(string cartSessionKey, Guid providerEpoch);

    CustodyLocation DestinationLocation(DeliveryTarget target);

    bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>The cart's container above its recorded baseline, in this
    /// provider epoch only.</summary>
    bool TryResolveCart(string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>The order's own container: owned here, not in use, allowed,
    /// within reach. Never another chest.</summary>
    bool TryResolveContainer(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal);

    /// <summary>Records the cart's pre-existing cargo before the order first
    /// puts anything in it (<c>CartBaselineRecorded</c>).</summary>
    bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch);
}
