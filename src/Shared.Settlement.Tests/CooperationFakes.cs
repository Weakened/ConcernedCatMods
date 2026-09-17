using System;
using System.Collections.Generic;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>Gunnar behind <c>concernedcat.haul/1</c>, simulated at the wire:
/// it parses real requests with the shared reader, answers with the shared
/// reply types, keeps request-id idempotence, bumps its revision on every phase
/// change, holds the cart only while Waiting and still, and travels to a leg's
/// target after <see cref="TravelSeconds"/> of the test clock. Faults are
/// injected explicitly. (The real provider is tested in Teamster's suite and
/// against the consumer in <c>src/Interop.Tests</c>.)</summary>
internal sealed class CooperationFakeGunnar : IHaulEndpointSource
{
    private readonly Dictionary<string, (string Fingerprint, bool Accepted)> _requests =
        new Dictionary<string, (string, bool)>(StringComparer.Ordinal);

    private HaulCancelDisposition? _cancelAfterUnloading;
    private WorkPoint _legStart;
    private float _legStartedAt;

    public CooperationFakeGunnar(Guid epoch, WorkPoint cartPosition)
    {
        Epoch = epoch;
        CartPosition = cartPosition;
        Discovery = HaulDiscovery.Available("1.0.5", Handle);
    }

    public HaulDiscovery Discovery { get; set; }

    public float Now { get; set; }

    public Guid Epoch { get; private set; }

    public WorkAuthorityVerdict Authority { get; set; } = WorkAuthorityVerdict.Granted;

    public bool WorkerAvailable { get; set; } = true;

    public string LeaseId { get; set; } = "lease-1";

    public string CartKey { get; set; } = "7:1";

    public bool CartUpright { get; set; } = true;

    public string HaulId { get; private set; } = string.Empty;

    public HaulWirePhase Phase { get; private set; } = HaulWirePhase.Ready;

    public int Revision { get; private set; } = 1;

    public HaulWireReason Attention { get; private set; }

    public bool Attached { get; private set; }

    public bool CartStill { get; private set; } = true;

    public bool Arrived { get; private set; }

    public WorkPoint CartPosition { get; private set; }

    public WorkPoint? Target { get; private set; }

    public bool LegToDestination { get; private set; }

    public float TravelSeconds { get; set; } = 2f;

    /// <summary>How far short of its target the cart stops (a chest indoors).
    /// </summary>
    public float StopShortMetres { get; set; }

    /// <summary>A route verdict per requested target; null accepts.</summary>
    public Func<WorkPoint, bool, HaulWireReason?>? RouteCheck { get; set; }

    /// <summary>Answers nothing at all while set (a dead provider).</summary>
    public bool Silent { get; set; }

    /// <summary>Applies the next N mutations but loses their replies.</summary>
    public int LoseRepliesAfterApplying { get; set; }

    /// <summary>Runs just before a mutation is judged: a concurrent change.
    /// </summary>
    public Action<HaulMutatingMessage>? BeforeApply { get; set; }

    public int Legs { get; private set; }

    public int Holds { get; private set; }

    public int Releases { get; private set; }

    public List<HaulCancelDisposition> Cancels { get; } = new List<HaulCancelDisposition>();

    public List<string> Log { get; } = new List<string>();

    /// <summary>Anything a well-behaved consumer must never cause.</summary>
    public List<string> Violations { get; } = new List<string>();

    public void EndControl(HaulWireReason reason)
    {
        Attention = reason;
        Attached = false;
        Arrived = false;
        Phase = HaulWirePhase.NeedsAttention;
        Revision++;
    }

    public void Reload(Guid newEpoch)
    {
        Epoch = newEpoch;
        _requests.Clear();
        LeaseId = string.Empty;
        HaulId = string.Empty;
        Attached = false;
        Arrived = false;
        Phase = HaulWirePhase.Unassigned;
        Revision++;
    }

    /// <summary>Something on Gunnar's side changed that is not a phase (a
    /// reason, the lease): the revision moves on.</summary>
    public void BumpRevision() => Revision++;

    public void Advance()
    {
        bool moving = Phase == HaulWirePhase.Approaching || Phase == HaulWirePhase.Pulling;
        if (!moving || Target == null || Now - _legStartedAt < TravelSeconds)
        {
            return;
        }

        WorkPoint target = Target.Value;
        if (StopShortMetres > 0f)
        {
            float dx = _legStart.X - target.X;
            float dz = _legStart.Z - target.Z;
            float length = (float)Math.Sqrt((dx * dx) + (dz * dz));
            if (length > 0.01f)
            {
                float scale = Math.Min(StopShortMetres, length) / length;
                target = new WorkPoint(target.X + (dx * scale), target.Y, target.Z + (dz * scale));
            }
        }

        CartPosition = target;
        Attached = true;
        CartStill = true;
        Arrived = true;
        Phase = HaulWirePhase.Waiting;
        Revision++;
    }

    private IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string> wire)
    {
        if (Silent)
        {
            return null!;
        }

        Advance();
        HaulRequestReading reading = HaulRequestReader.Read(wire);
        if (!reading.IsValid)
        {
            Violations.Add("malformed request: " + reading.Detail);
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, reading.Refusal, reading.Detail);
        }

        HaulRequestMessage message = reading.Message!;
        Log.Add(HaulOps.WireName(message.Op));
        if (message is HelloMessage)
        {
            return new HelloReply(
                HaulReplyHeader.Success(HaulReplyStatus.Accepted), 0, "1.0.5", Epoch, Authority, WorkerAvailable,
                Phase, LeaseId.Length > 0 ? LeaseId : null).ToWire();
        }

        if (((HaulEpochMessage)message).ProviderEpoch != Epoch)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.EpochMismatch, "reloaded");
        }

        switch (message)
        {
            case DescribeLeaseMessage _:
                return LeaseId.Length == 0
                    ? HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.NoLease, null)
                    : new DescribeLeaseReply(
                        HaulReplyHeader.Success(HaulReplyStatus.Accepted), LeaseId, CartKey, CartPosition, CartStill,
                        CartUpright, Attached, Phase, Revision).ToWire();

            case GetHaulMessage get:
                return HaulId.Length == 0 || HaulId != get.HaulId
                    ? HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, null)
                    : State(HaulReplyStatus.Accepted);
        }

        var mutation = (HaulMutatingMessage)message;
        string fingerprint = mutation.Fingerprint();
        if (_requests.TryGetValue(mutation.RequestId, out (string Fingerprint, bool Accepted) seen))
        {
            if (seen.Fingerprint != fingerprint)
            {
                Violations.Add("request id reused with another payload: " + mutation.RequestId);
                return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload, null);
            }

            if (seen.Accepted)
            {
                return PhaseState(mutation, HaulReplyStatus.AlreadySatisfied);
            }
        }
        else
        {
            _requests[mutation.RequestId] = (fingerprint, false);
        }

        BeforeApply?.Invoke(mutation);
        IReadOnlyDictionary<string, string>? refusal = Apply(mutation);
        if (refusal != null)
        {
            return refusal;
        }

        _requests[mutation.RequestId] = (fingerprint, true);
        if (LoseRepliesAfterApplying > 0)
        {
            LoseRepliesAfterApplying--;
            return null!;
        }

        return PhaseState(mutation, HaulReplyStatus.Accepted);
    }

    private IReadOnlyDictionary<string, string>? Apply(HaulMutatingMessage mutation)
    {
        switch (mutation)
        {
            case RequestHaulMessage leg:
                if (Authority != WorkAuthorityVerdict.Granted || !WorkerAvailable)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Unavailable, Authority == WorkAuthorityVerdict.OtherPeersConnected ? HaulWireReason.OtherPeersConnected : HaulWireReason.NoAuthority, null);
                }

                if (LeaseId.Length == 0)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.NoLease, null);
                }

                if (leg.LeaseId != LeaseId)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.LeaseInvalidated, null);
                }

                if (Phase == HaulWirePhase.Unloading)
                {
                    Violations.Add("asked to move the cart while it was held for a transfer");
                }

                if ((HaulId.Length > 0 && (HaulId != leg.HaulId || (Phase != HaulWirePhase.Waiting && Phase != HaulWirePhase.Ready))) ||
                    (HaulId.Length == 0 && Phase != HaulWirePhase.Ready))
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, null);
                }

                if (leg.Revision != Revision)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, null);
                }

                HaulWireReason? route = RouteCheck?.Invoke(leg.Target, leg.Purpose == HaulLegPurpose.ToDestination);
                if (route.HasValue)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, route.Value, null);
                }

                Legs++;
                HaulId = leg.HaulId;
                Target = leg.Target;
                LegToDestination = leg.Purpose == HaulLegPurpose.ToDestination;
                _legStart = CartPosition;
                _legStartedAt = Now;
                CartStill = false;
                Arrived = false;
                Phase = Phase == HaulWirePhase.Ready ? HaulWirePhase.Approaching : HaulWirePhase.Pulling;
                Revision++;
                return null;

            case AcknowledgeWaitMessage acknowledgement:
                if (HaulId.Length == 0 || acknowledgement.HaulId != HaulId)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, null);
                }

                if (acknowledgement.Activity == HaulWaitActivity.Transferring)
                {
                    if (Authority != WorkAuthorityVerdict.Granted)
                    {
                        return HaulRefusal.ToWire(HaulReplyStatus.Unavailable, HaulWireReason.NoAuthority, null);
                    }

                    if (Phase != HaulWirePhase.Waiting || !CartStill)
                    {
                        return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, null);
                    }
                }
                else if (Phase != HaulWirePhase.Unloading)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, null);
                }

                if (acknowledgement.ExpectedRevision != Revision)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, null);
                }

                if (acknowledgement.Activity == HaulWaitActivity.Transferring)
                {
                    Holds++;
                    Phase = HaulWirePhase.Unloading;
                    Revision++;
                }
                else
                {
                    Releases++;
                    Phase = HaulWirePhase.Waiting;
                    Revision++;
                    if (_cancelAfterUnloading.HasValue)
                    {
                        HaulCancelDisposition pending = _cancelAfterUnloading.Value;
                        _cancelAfterUnloading = null;
                        ApplyCancel(pending);
                    }
                }

                return null;

            case CancelHaulMessage cancel:
                if (HaulId.Length == 0 || cancel.HaulId != HaulId)
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, null);
                }

                Cancels.Add(cancel.Disposition);
                if (Phase == HaulWirePhase.Unloading)
                {
                    _cancelAfterUnloading = cancel.Disposition;
                    return null;
                }

                ApplyCancel(cancel.Disposition);
                return null;

            default:
                return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownOp, null);
        }
    }

    private void ApplyCancel(HaulCancelDisposition disposition)
    {
        Arrived = false;
        if (Phase == HaulWirePhase.Approaching)
        {
            HaulId = string.Empty;
            Phase = HaulWirePhase.Ready;
            Revision++;
            return;
        }

        if (Phase == HaulWirePhase.Pulling || Phase == HaulWirePhase.Stopping || Phase == HaulWirePhase.Recovering)
        {
            CartStill = true;
            Phase = HaulWirePhase.Waiting;
            Revision++;
        }

        if (disposition == HaulCancelDisposition.DetachAndPark &&
            (Phase == HaulWirePhase.Waiting || Phase == HaulWirePhase.NeedsAttention || Phase == HaulWirePhase.Paused))
        {
            Attached = false;
            HaulId = string.Empty;
            Phase = HaulWirePhase.Ready;
            Revision++;
        }
    }

    private IReadOnlyDictionary<string, string> State(HaulReplyStatus status) =>
        new GetHaulReply(
            HaulReplyHeader.Success(status), Phase, Attention, null, Revision, Arrived, Attached, CartPosition,
            null, CartStill).ToWire();

    private IReadOnlyDictionary<string, string> PhaseState(HaulMutatingMessage mutation, HaulReplyStatus status) =>
        mutation is RequestHaulMessage leg
            ? new RequestHaulReply(HaulReplyHeader.Success(status), leg.HaulId, Phase, Revision).ToWire()
            : new HaulPhaseReply(HaulReplyHeader.Success(status), Phase, Revision).ToWire();
}

/// <summary>An inventory: counts per item above any pre-existing cargo, a unit
/// capacity that pre-existing cargo also occupies, and switchable faults.
/// </summary>
internal sealed class CooperationFakeInventory : IInventoryPort
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

    public CooperationFakeInventory(string describe, int capacityUnits, int preExistingUnits = 0)
    {
        Describe = describe;
        CapacityUnits = capacityUnits;
        PreExistingUnits = preExistingUnits;
    }

    public string Describe { get; }

    public bool IsAvailable { get; set; } = true;

    public int CapacityUnits { get; set; }

    /// <summary>Cargo that was there before the order: it takes room and is
    /// never counted, moved or credited.</summary>
    public int PreExistingUnits { get; }

    public int Total
    {
        get
        {
            int total = 0;
            foreach (int count in _counts.Values)
            {
                total += count;
            }

            return total;
        }
    }

    public int Count(MaterialItem item) => _counts.TryGetValue(item.PrefabName, out int count) ? count : 0;

    public int CanAccept(MaterialItem item, int count) =>
        Math.Max(0, Math.Min(count, CapacityUnits - PreExistingUnits - Total));

    public int Add(MaterialItem item, int count)
    {
        int added = CanAccept(item, count);
        _counts[item.PrefabName] = Count(item) + added;
        return added;
    }

    public int Remove(MaterialItem item, int count)
    {
        int removed = Math.Min(count, Count(item));
        _counts[item.PrefabName] = Count(item) - removed;
        return removed;
    }

    /// <summary>Someone else takes material out, behind the record's back.
    /// </summary>
    public void TakeBehindTheRecordsBack(CollectedResource resource, int count) =>
        Remove(MaterialItem.Of(resource), count);
}

/// <summary>Custody for one order, in memory: a ledger per place and resource,
/// the executor ordering of §5.2 (check, add, remove, classify from actual
/// deltas) and hooks that record the conditions each transfer ran under.
/// </summary>
internal sealed class CooperationFakeCustody : ICooperationCustody, IMaterialCustodyView, ITransferExecutor
{
    private readonly Dictionary<(CustodyPlace, CollectedResource), int> _ledger = new Dictionary<(CustodyPlace, CollectedResource), int>();
    private readonly CollectionOrderDefinition _order;

    public CooperationFakeCustody(CollectionOrderDefinition order, CooperationFakeInventory pack, CooperationFakeInventory cart, CooperationFakeInventory chest)
    {
        _order = order;
        Pack = pack;
        Cart = cart;
        Chest = chest;
    }

    public CooperationFakeInventory Pack { get; }

    public CooperationFakeInventory Cart { get; }

    public CooperationFakeInventory Chest { get; }

    public int Revision { get; private set; } = 1;

    public bool Writable { get; set; } = true;

    public string ResolvableCartKey { get; set; } = "7:1";

    public bool CartResolvable { get; set; } = true;

    public CollectionAttentionReason ContainerRefusal { get; set; }

    public bool BaselineFails { get; set; }

    public int BaselineRecords { get; private set; }

    /// <summary>Transfers that ran before the baseline was recorded.</summary>
    public int TransfersBeforeBaseline { get; private set; }

    public Queue<TransferOutcome> ForcedOutcomes { get; } = new Queue<TransferOutcome>();

    public List<TransferIntent> Intents { get; } = new List<TransferIntent>();

    public List<TransferReceipt> Receipts { get; } = new List<TransferReceipt>();

    /// <summary>Asked at each transfer touching the cart: is it held still?
    /// </summary>
    public Func<bool>? CartIsHeld { get; set; }

    public int CartTransfersWithoutHold { get; private set; }

    public IMaterialCustodyView View => this;

    public ITransferExecutor Executor => this;

    public bool IsWritable => Writable;

    public CustodyLocation WorkerLocation => new CustodyLocation(CustodyPlace.Worker, "foreman/thorstein", Guid.Empty);

    public CustodyLocation CartLocation(string cartSessionKey, Guid providerEpoch) =>
        new CustodyLocation(CustodyPlace.Cart, cartSessionKey, providerEpoch);

    public CustodyLocation DestinationLocation(DeliveryTarget target) =>
        new CustodyLocation(CustodyPlace.Destination, target.ContainerKey, target.WorldLoadEpoch);

    public bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = Pack.IsAvailable ? Pack : null;
        refusal = Pack.IsAvailable ? CollectionAttentionReason.Unspecified : CollectionAttentionReason.WorkerBodyLost;
        return port != null;
    }

    /// <summary>The provider's current epoch; a cart key from any other epoch
    /// may name another object and must never be asked for.</summary>
    public Func<Guid>? CurrentProviderEpoch { get; set; }

    public int StaleCartResolutions { get; private set; }

    public bool TryResolveCart(string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        if (CurrentProviderEpoch != null && providerEpoch != CurrentProviderEpoch())
        {
            StaleCartResolutions++;
        }

        bool ok = CartResolvable && Cart.IsAvailable && cartSessionKey == ResolvableCartKey;
        port = ok ? Cart : null;
        refusal = ok ? CollectionAttentionReason.Unspecified : CollectionAttentionReason.CartLeaseLost;
        return ok;
    }

    public bool TryResolveContainer(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        bool ok = ContainerRefusal == CollectionAttentionReason.Unspecified && Chest.IsAvailable;
        port = ok ? Chest : null;
        refusal = ok ? CollectionAttentionReason.Unspecified : Known(ContainerRefusal);
        return ok;
    }

    public RequestId NextTransferId(OrderId order) => new RequestId(order.Value + "-t-" + (Intents.Count + 1));

    public bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch)
    {
        if (BaselineFails)
        {
            return false;
        }

        BaselineRecords++;
        return true;
    }

    public int CountAt(OrderId order, CustodyPlace place, CollectedResource resource) =>
        _ledger.TryGetValue((place, resource), out int count) ? count : 0;

    public ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource)
    {
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
        };
    }

    public bool HasUncertainTransfer(OrderId order) =>
        Receipts.Exists(receipt => receipt.Outcome == TransferOutcome.Uncertain);

    /// <summary>The collection loop's pickup, simulated: real units arrive in
    /// Thorstein's pack and in the record together, within his capacity.
    /// </summary>
    public int Collect(CollectedResource resource, int count)
    {
        int added = Pack.Add(MaterialItem.Of(resource), count);
        Move(CustodyPlace.Worker, resource, added);
        if (added > 0)
        {
            Revision++;
        }

        return added;
    }

    public void DropOnGround(CollectedResource resource, int count)
    {
        Move(CustodyPlace.SourceGround, resource, count);
        Revision++;
    }

    public TransferReceipt Execute(TransferIntent intent, IInventoryPort from, IInventoryPort to)
    {
        Intents.Add(intent);
        bool touchesCart = intent.From.Place == CustodyPlace.Cart || intent.To.Place == CustodyPlace.Cart;
        if (touchesCart && CartIsHeld != null && !CartIsHeld())
        {
            CartTransfersWithoutHold++;
        }

        if (intent.To.Place == CustodyPlace.Cart && BaselineRecords == 0)
        {
            TransfersBeforeBaseline++;
        }

        TransferReceipt receipt = ExecuteUnrecorded(intent, from, to);
        Receipts.Add(receipt);
        return receipt;
    }

    private TransferReceipt ExecuteUnrecorded(TransferIntent intent, IInventoryPort from, IInventoryPort to)
    {
        CollectedResources.TryFromItemPrefabName(intent.Item.PrefabName, out CollectedResource resource);
        if (ForcedOutcomes.Count > 0)
        {
            TransferOutcome forced = ForcedOutcomes.Dequeue();
            if (forced == TransferOutcome.Uncertain)
            {
                // The add happened and the remove could not be proven: a
                // duplicate, not a loss, and nothing is credited.
                to.Add(intent.Item, intent.Count);
            }

            return new TransferReceipt(intent.Request, forced, 0, intent.From, "forced " + forced);
        }

        if (!Writable || !from.IsAvailable || !to.IsAvailable || from.Count(intent.Item) < intent.Count ||
            to.CanAccept(intent.Item, intent.Count) <= 0)
        {
            return new TransferReceipt(intent.Request, TransferOutcome.Refused, 0, intent.From, "precondition");
        }

        if (intent.ExpectedCustodyRevision != Revision)
        {
            return new TransferReceipt(intent.Request, TransferOutcome.Stale, 0, intent.From, "revision");
        }

        int accepted = to.Add(intent.Item, Math.Min(intent.Count, to.CanAccept(intent.Item, intent.Count)));
        int removed = from.Remove(intent.Item, accepted);
        if (removed != accepted)
        {
            return new TransferReceipt(intent.Request, TransferOutcome.Uncertain, 0, intent.From, "counts disagree");
        }

        Move(intent.From.Place, resource, -accepted);
        Move(intent.To.Place, resource, accepted);
        Revision++;
        return new TransferReceipt(
            intent.Request, accepted == intent.Count ? TransferOutcome.Completed : TransferOutcome.Partial, accepted,
            intent.From, string.Empty);
    }

    private void Move(CustodyPlace place, CollectedResource resource, int delta)
    {
        _ledger[(place, resource)] = CountAt(_order.Order, place, resource) + delta;
    }

    private static CollectionAttentionReason Known(CollectionAttentionReason reason) =>
        reason == CollectionAttentionReason.Unspecified ? CollectionAttentionReason.DestinationUnavailable : reason;
}

/// <summary>Thorstein's body: walks arrive at once unless told otherwise.
/// </summary>
internal sealed class CooperationFakeWorker : ICooperationWorker
{
    public CooperationFakeWorker(SitePoint position)
    {
        Position = position;
    }

    public bool IsPresent { get; set; } = true;

    public SitePoint Position { get; set; }

    public CooperationWalkStatus WalkStatus { get; private set; } = CooperationWalkStatus.Idle;

    public bool DeferWalks { get; set; }

    public int Walks { get; private set; }

    public int Stops { get; private set; }

    public bool WalkTo(SitePoint point, float tolerance)
    {
        Walks++;
        if (DeferWalks)
        {
            WalkStatus = CooperationWalkStatus.Deferred;
            return true;
        }

        Position = point;
        WalkStatus = CooperationWalkStatus.Arrived;
        return true;
    }

    public void Stop()
    {
        Stops++;
        WalkStatus = CooperationWalkStatus.Idle;
    }
}
