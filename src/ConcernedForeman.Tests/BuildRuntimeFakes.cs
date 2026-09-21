using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace ConcernedForeman.Tests;

/// <summary>Custody, as far as a build order uses it: the writable-record gate,
/// the world-load epoch, the two inventory ports it resolves, and the recovered
/// collection order it can report.
///
/// <b>The ports it hands back are the REAL ones.</b>
/// <see cref="WorkerInventoryPort"/> and <see cref="ContainerInventoryPort"/> over
/// a stubbed body and chest, because the whole question these tests ask is
/// whether material really moves between two vanilla inventories in the right
/// order. A fake port would have made the movement a statement about
/// arithmetic.</summary>
internal sealed class FakeCustody : ICustodyRuntime
{
    internal FakeCustody(IInventoryPort? worker, IInventoryPort? chest)
    {
        WorkerPort = worker;
        ChestPort = chest;
        Journal = new SettlementJournal(new SettlementScope(4242, new SettlementId("build-test")));
        Core = CustodyCore.Open(Journal, () => { Journal.MarkClean(); return true; }, () => 10,
            new WorldLoad(0, WorldLoadEpoch), () => _writable);
    }

    internal IInventoryPort? WorkerPort { get; set; }

    internal IInventoryPort? ChestPort { get; set; }

    internal CollectionAttentionReason WorkerRefusal { get; set; } = CollectionAttentionReason.WorkerBodyLost;

    internal CollectionAttentionReason ChestRefusal { get; set; } =
        CollectionAttentionReason.DestinationUnavailable;

    internal CollectionOrderDefinition? Recovered { get; set; }

    internal CollectionOrderState RecoveredState { get; set; } = CollectionOrderState.Unspecified;

    /// <summary>What the record says is at the worker's place, per item - what a
    /// CANCELLED collection order leaves behind. The build order's refusal reads
    /// this and nothing else of custody's accounting.</summary>
    internal Dictionary<string, int> AtWorker { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public IMaterialCustodyView View => throw new NotSupportedException("a build order reads no custody view");

    public int RecordedAt(CustodyLocation location, MaterialItem item) =>
        location.Place == CustodyPlace.Worker && AtWorker.TryGetValue(item.PrefabName, out int count)
            ? count
            : 0;

    public CustodyLocation WorkerLocation { get; set; } =
        new CustodyLocation(CustodyPlace.Worker, "foreman/thorstein", Guid.Empty);

    public ITransferExecutor Executor =>
        throw new NotSupportedException("a build order does not move collection material");

    private bool _writable = true;
    public bool IsWritable { get => _writable && !Core.BuildMaterials.NeedsRepair; set => _writable = value; }

    internal SettlementJournal Journal { get; }
    internal CustodyCore Core { get; }
    internal DeliveryTarget LastContainer { get; private set; }

    public CustodyOutcome ReserveBuild(Reservation reservation, Func<bool> draw, out string failure) =>
        Core.BuildMaterials.Reserve(reservation, draw, out failure);
    public CustodyOutcome CommitBuild(Reservation reservation, Func<bool> placeAndPay, out string failure) =>
        Core.BuildMaterials.Commit(reservation, placeAndPay, out failure);
    public CustodyOutcome RefundBuild(Reservation reservation, Func<bool> putBack, out string failure) =>
        Core.BuildMaterials.Refund(reservation, putBack, out failure);

    public Guid WorldLoadEpoch { get; set; } = ForemanFixtures.Epoch;

    public bool TryRecoverOrder(
        WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state)
    {
        order = Recovered;
        state = RecoveredState;
        return order != null;
    }

    public bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery) => true;

    public bool TryResolveWorker(
        WorkerKey worker, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = WorkerPort;
        refusal = port == null ? WorkerRefusal : CollectionAttentionReason.Unspecified;
        return port != null;
    }

    public bool TryResolveContainer(
        DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        LastContainer = target;
        port = ChestPort;
        refusal = port == null ? ChestRefusal : CollectionAttentionReason.Unspecified;
        return port != null;
    }

    public bool TryResolveCart(
        string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = null;
        refusal = CollectionAttentionReason.CartLeaseLost;
        return false;
    }

    public bool RecordAccepted(CollectionOrderDefinition order) => true;

    public bool RecordTransition(
        OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason) => true;

    public bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch) => true;

    public RequestId? BeginPickup(OrderId order, SourceKey source) => null;

    public bool FinishPickup(RequestId pickup, PickupResult result) => true;

    public TransferOutcome BeginTransfer(TransferIntent intent) => TransferOutcome.Refused;

    public bool FinishTransfer(TransferReceipt receipt) => true;
}

/// <summary>Walking Thorstein, over the C1 seam, with no body: the position is
/// whatever a test puts it at and a walk arrives.</summary>
internal sealed class FakeMotion : IWorkerMotion
{
    internal FakeMotion(WorkerKey worker)
    {
        Worker = worker;
    }

    public WorkerKey Worker { get; }

    public bool IsPresent { get; set; } = true;

    public Vector3 Position { get; set; }

    public WorkerWalkStatus Status { get; set; } = WorkerWalkStatus.Idle;

    public WorkerDeferralReason LastDeferral { get; set; } = WorkerDeferralReason.None;

    internal int Walks { get; private set; }

    internal string? LastJob { get; private set; }

    public bool WalkTo(Vector3 point, float arrivalTolerance, string jobId)
    {
        LastJob = jobId;
        Walks++;
        Position = point;
        Status = WorkerWalkStatus.Arrived;
        return true;
    }

    public void Stop(string jobId) => Status = WorkerWalkStatus.Idle;
}

/// <summary>An actor-mode hold with no arbiter behind it: the rules the product
/// depends on (one job at a time, every non-grant is a refusal) and nothing
/// else.</summary>
internal sealed class FakeModes : IActorModeHold
{
    internal FakeModes(WorkerKey worker)
    {
        Worker = worker;
    }

    public WorkerKey Worker { get; }

    public bool IsIdentityKnown { get; set; } = true;

    public ActorMode Mode { get; private set; } = ActorMode.Resting;

    public string? JobId { get; private set; }

    public bool MayRelocateHome => JobId == null;

    public bool MayRetireBody => JobId == null;

    /// <summary>What Enter answers. A test sets this to prove the caller treats
    /// every non-grant as a refusal.</summary>
    internal ActorModeOutcome? Refuse { get; set; }

    internal int Entries { get; private set; }

    internal int Releases { get; private set; }

    public bool IsHeldBy(string? jobId) =>
        JobId != null && jobId != null && string.Equals(JobId, jobId, StringComparison.Ordinal);

    public ActorModeOutcome Enter(ActorMode mode, string jobId)
    {
        if (Refuse.HasValue)
        {
            return Refuse.Value;
        }

        if (JobId != null && !IsHeldBy(jobId))
        {
            return ActorModeOutcome.RefusedBusy;
        }

        bool already = JobId != null && Mode == mode;
        JobId = jobId;
        Mode = mode;
        Entries++;
        return already ? ActorModeOutcome.AlreadyInMode : ActorModeOutcome.Entered;
    }

    public ActorModeOutcome Release(string jobId)
    {
        if (!IsHeldBy(jobId))
        {
            return ActorModeOutcome.NotHeld;
        }

        JobId = null;
        Mode = ActorMode.Resting;
        Releases++;
        return ActorModeOutcome.Released;
    }
}
