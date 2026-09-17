using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Planning;

// The loop's view of the world, as game-free ports. The Foreman adapters bridge
// each one to the contract seams in src/ConcernedForeman/Runtime/Work/WorkSeams.cs
// (IWorkerMotion, ICustodyRuntime, ICooperativeDelivery), which cannot be compiled
// here because that file is engine-bound. The shapes mirror those seams member
// for member, so the bridge is a straight forward and nothing is decided in it.

/// <summary>Mirror of <c>WorkerWalkStatus</c>.</summary>
internal enum CollectionWalkStatus
{
    Unspecified = 0,
    Idle = 1,
    Walking = 2,

    /// <summary>Within tolerance of the goal, by distance.</summary>
    Arrived = 3,

    Deferred = 4,
}

/// <summary>Walking Thorstein, in <see cref="SitePoint"/> terms. Only the job
/// holding his actor mode may command him.</summary>
internal interface ICollectionMotion
{
    /// <summary>The body exists, is owned here and is not faulted.</summary>
    bool IsPresent { get; }

    SitePoint Position { get; }

    CollectionWalkStatus Status { get; }

    WorkerDeferralReason LastDeferral { get; }

    /// <summary>Starts or replaces a walk. False when the job does not hold the
    /// worker or the body is not present.</summary>
    bool WalkTo(SitePoint point, float arrivalTolerance, string jobId);

    void Stop(string jobId);
}

/// <summary>Mirror of <c>ICustodyRuntime</c>, for the parts the loop uses.
/// Agent D implements custody; every material movement and every order record
/// goes through it, journal first.</summary>
internal interface ICollectionCustody
{
    IMaterialCustodyView View { get; }

    ITransferExecutor Executor { get; }

    bool IsWritable { get; }

    bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal);

    bool TryResolveDestination(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal);

    bool RecordAccepted(CollectionOrderDefinition order);

    bool RecordTransition(OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason);
}

/// <summary>Mirror of <c>CooperativeStep</c>.</summary>
internal enum CollectionHandOff
{
    Unspecified = 0,
    Working = 1,
    CollectMore = 2,
    Delivered = 3,
    Paused = 4,
    NeedsAttention = 5,
}

/// <summary>Mirror of <c>ICooperativeDelivery</c> (agent E). The loop hands an
/// order to it while its participation is WithHauler and takes it back on
/// CollectMore.</summary>
internal interface ICollectionCooperation
{
    bool IsAvailable(out CollectionAttentionReason reason);

    CollectionHandOff Tick(CollectionOrderDefinition order, float now, out CollectionAttentionReason reason);

    void Cancel(CollectionOrderDefinition order, bool detachAndPark);
}

/// <summary>The facts the loop asks the world for at checkpoints. Every answer
/// that could not be established is a refusal, never a grant.</summary>
internal interface ICollectionWorld
{
    /// <summary><see cref="WorkAuthorityPolicy"/> over the current facts. Asked
    /// before every mutation.</summary>
    WorkAuthorityVerdict EvaluateAuthority();

    /// <summary><see cref="WorkerReadiness.Assess"/> against the building
    /// tools (D12).</summary>
    ReadinessVerdict AssessReadiness(WorkerId worker);

    ScopeObservation ObserveScope(WorkScope scope);

    bool IsLoaded(SitePoint point);

    /// <summary>The game's weight of one unit; not positive when unknown.
    /// </summary>
    float UnitWeight(CollectedResource resource);
}
