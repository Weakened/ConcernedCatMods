using System;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>The loop's custody port over agent D's <see cref="ICustodyRuntime"/>.
/// A straight forward: nothing is decided here.</summary>
internal sealed class CustodyBridge : ICollectionCustody
{
    private readonly ICustodyRuntime _custody;
    private readonly WorkerKey _worker;

    public CustodyBridge(ICustodyRuntime custody, WorkerKey worker)
    {
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _worker = worker;
    }

    public IMaterialCustodyView View => _custody.View;

    public ITransferExecutor Executor => _custody.Executor;

    public bool IsWritable => _custody.IsWritable;

    public Guid WorldLoadEpoch => _custody.WorldLoadEpoch;

    public bool TryRecoverOrder(WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state) =>
        _custody.TryRecoverOrder(worker, out order, out state);

    public bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery) =>
        _custody.RecordRebound(order, scope, delivery);

    public bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal) =>
        _custody.TryResolveWorker(_worker, out port, out refusal);

    public bool TryResolveDestination(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal) =>
        _custody.TryResolveContainer(target, out port, out refusal);

    public bool RecordAccepted(CollectionOrderDefinition order) => _custody.RecordAccepted(order);

    public bool RecordTransition(OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason) =>
        _custody.RecordTransition(order, from, to, reason);
}

/// <summary>The loop's cooperation port over agent E's
/// <see cref="ICooperativeDelivery"/>.</summary>
internal sealed class CooperationBridge : ICollectionCooperation
{
    private readonly ICooperativeDelivery _delivery;

    public CooperationBridge(ICooperativeDelivery delivery)
    {
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    }

    public bool IsAvailable(out CollectionAttentionReason reason) => _delivery.IsAvailable(out reason);

    public CollectionHandOff Tick(CollectionOrderDefinition order, float now, out CollectionAttentionReason reason)
    {
        switch (_delivery.Tick(order, now, out reason))
        {
            case CooperativeStep.Working:
                return CollectionHandOff.Working;
            case CooperativeStep.CollectMore:
                return CollectionHandOff.CollectMore;
            case CooperativeStep.Delivered:
                return CollectionHandOff.Delivered;
            case CooperativeStep.Paused:
                return CollectionHandOff.Paused;
            case CooperativeStep.NeedsAttention:
                return CollectionHandOff.NeedsAttention;
            default:
                return CollectionHandOff.Unspecified;
        }
    }

    public void Cancel(CollectionOrderDefinition order, bool detachAndPark) => _delivery.Cancel(order, detachAndPark);
}
