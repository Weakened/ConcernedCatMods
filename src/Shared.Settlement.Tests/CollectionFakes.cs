using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>Facts for the audited prefabs (PICKUP_SEAM_AUDIT.md §1.9, §4.1,
/// §4.2): the two allowlisted ones and their look-alikes, as the adapter would
/// read them from the installed game.</summary>
internal static class SourceFacts
{
    public static NaturalSourceFacts VanillaStone() => Make("Pickable_Stone", "Stone", "$item_stone", respawn: 0f, hide: false);

    public static NaturalSourceFacts VanillaBranch() => Make("Pickable_Branch", "Wood", "$item_wood", respawn: 240f, hide: true);

    public static NaturalSourceFacts Make(
        string prefab,
        string? yieldPrefab,
        string? yieldShared,
        float respawn,
        bool hide,
        bool valid = true,
        int pickables = 1,
        string[]? forbidden = null,
        string? tag = "Untagged",
        bool yieldHasItemDrop = true,
        int amount = 1,
        int minAmountScaled = 1,
        bool dontScale = false,
        bool extraDropsEmpty = true,
        float aggravate = 0f,
        long creator = 0L,
        bool picked = false,
        int enabled = 1,
        bool canBePicked = true) =>
        new NaturalSourceFacts(
            hasValidNetworkObject: valid,
            networkPrefabName: prefab,
            pickableCount: pickables,
            forbiddenComponents: forbidden ?? Array.Empty<string>(),
            rootTag: tag,
            yieldHasItemDrop: yieldHasItemDrop,
            yieldPrefabName: yieldPrefab,
            yieldSharedName: yieldShared,
            amount: amount,
            minAmountScaled: minAmountScaled,
            dontScale: dontScale,
            extraDropsEmpty: extraDropsEmpty,
            aggravateRange: aggravate,
            respawnTimeMinutes: respawn,
            hasHideWhenPicked: hide,
            creator: creator,
            picked: picked,
            enabled: enabled,
            canBePicked: canBePicked);
}

internal sealed class CollectionFakeInventory : IInventoryPort
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _capacity = new Dictionary<string, int>(StringComparer.Ordinal);

    public CollectionFakeInventory(string describe, int defaultCapacity = 1000)
    {
        Describe = describe;
        DefaultCapacity = defaultCapacity;
    }

    public string Describe { get; }

    public bool IsAvailable { get; set; } = true;

    public int DefaultCapacity { get; set; }

    public int Count(MaterialItem item) => _counts.TryGetValue(item.PrefabName, out int count) ? count : 0;

    public void Set(CollectedResource resource, int count) => _counts[CollectedResources.ItemPrefabName(resource)] = count;

    public void Limit(CollectedResource resource, int maxUnits) => _capacity[CollectedResources.ItemPrefabName(resource)] = maxUnits;

    public int CanAccept(MaterialItem item, int count)
    {
        int max = _capacity.TryGetValue(item.PrefabName, out int limit) ? limit : DefaultCapacity;
        return Math.Max(0, Math.Min(count, max - Count(item)));
    }

    public int Add(MaterialItem item, int count)
    {
        int accepted = CanAccept(item, count);
        _counts[item.PrefabName] = Count(item) + accepted;
        return accepted;
    }

    public int Remove(MaterialItem item, int count)
    {
        int removed = Math.Min(count, Count(item));
        _counts[item.PrefabName] = Count(item) - removed;
        return removed;
    }
}

/// <summary>A minimal custody runtime: progress buckets per order, a journal
/// of transitions that asserts the contract table, and an executor that adds
/// before it removes. Faults are injected by setting the flags.</summary>
internal sealed class FakeCustody : ICollectionCustody, IMaterialCustodyView, ITransferExecutor
{
    private readonly Dictionary<(string Order, CollectedResource Resource), ResourceProgress> _progress =
        new Dictionary<(string, CollectedResource), ResourceProgress>();

    public FakeCustody(CollectionFakeInventory worker, CollectionFakeInventory destination)
    {
        Worker = worker;
        Destination = destination;
    }

    public CollectionFakeInventory Worker { get; }

    public CollectionFakeInventory Destination { get; }

    public bool Writable { get; set; } = true;

    public bool FailRecordTransitions { get; set; }

    public bool FailRecordAccepted { get; set; }

    public bool Uncertain { get; set; }

    /// <summary>What the record holds across a reload, for adoption (C2).
    /// </summary>
    public CollectionOrderDefinition? RecoverableOrder { get; set; }

    public CollectionOrderState RecoveredState { get; set; } = CollectionOrderState.Paused;

    public Guid Epoch { get; set; } = CollectionRig.Epoch;

    public bool AllowRebind { get; set; } = true;

    /// <summary>Deliberately wrong: credit the count the intent asked for
    /// instead of the count the inventories actually moved. Only one test turns
    /// it on, to prove the conservation check can fail.</summary>
    public bool CreditWithoutMeasuring { get; set; }

    public List<(OrderId Order, WorkScope Scope, DeliveryTarget Delivery)> Rebinds { get; } =
        new List<(OrderId, WorkScope, DeliveryTarget)>();

    public bool WorkerResolvable { get; set; } = true;

    public CollectionAttentionReason DestinationRefusal { get; set; } = CollectionAttentionReason.Unspecified;

    /// <summary>When set, the next Execute answers this instead of doing it.
    /// </summary>
    public Queue<TransferOutcome> ForcedOutcomes { get; } = new Queue<TransferOutcome>();

    public List<(CollectionOrderState From, CollectionOrderState To, CollectionAttentionReason Reason)> Transitions { get; } =
        new List<(CollectionOrderState, CollectionOrderState, CollectionAttentionReason)>();

    public List<CollectionOrderDefinition> Accepted { get; } = new List<CollectionOrderDefinition>();

    public List<TransferIntent> Executed { get; } = new List<TransferIntent>();

    public int Revision { get; private set; }

    public IMaterialCustodyView View => this;

    public ITransferExecutor Executor => this;

    public bool IsWritable => Writable;

    public Guid WorldLoadEpoch => Epoch;

    public bool TryRecoverOrder(WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state)
    {
        order = RecoverableOrder != null && RecoverableOrder.Worker.Equals(worker) ? RecoverableOrder : null;
        state = order != null ? RecoveredState : CollectionOrderState.Unspecified;
        return order != null;
    }

    public bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery)
    {
        if (!AllowRebind || !Writable)
        {
            return false;
        }

        Assert.Equal(Epoch, scope.WorldLoadEpoch);
        if (delivery.Kind == DeliveryKind.Container)
        {
            Assert.Equal(Epoch, delivery.WorldLoadEpoch);
        }

        Rebinds.Add((order, scope, delivery));
        if (RecoverableOrder != null && RecoverableOrder.Order.Equals(order))
        {
            RecoverableOrder = new CollectionOrderDefinition(
                RecoverableOrder.Order, RecoverableOrder.Worker, RecoverableOrder.Quotas, scope, delivery,
                RecoverableOrder.Participation, RecoverableOrder.IssuedByCharacter);
        }

        Revision++;
        return true;
    }

    public ResourceProgress Bucket(CollectionOrderDefinition order, CollectedResource resource)
    {
        if (!_progress.TryGetValue((order.Order.Value, resource), out ResourceProgress? progress))
        {
            int requested = 0;
            foreach (ResourceQuota quota in order.Quotas)
            {
                if (quota.Resource == resource)
                {
                    requested = quota.Requested;
                }
            }

            progress = new ResourceProgress(resource, requested);
            _progress[(order.Order.Value, resource)] = progress;
        }

        return progress;
    }

    public bool TryResolveWorker(out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = WorkerResolvable ? Worker : null;
        refusal = WorkerResolvable ? CollectionAttentionReason.Unspecified : CollectionAttentionReason.WorkerBodyLost;
        return WorkerResolvable;
    }

    public bool TryResolveDestination(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        bool ok = DestinationRefusal == CollectionAttentionReason.Unspecified && Destination.IsAvailable;
        port = ok ? Destination : null;
        refusal = ok ? CollectionAttentionReason.Unspecified
            : DestinationRefusal == CollectionAttentionReason.Unspecified ? CollectionAttentionReason.DestinationUnavailable
            : DestinationRefusal;
        return ok;
    }

    public bool RecordAccepted(CollectionOrderDefinition order)
    {
        if (FailRecordAccepted || !Writable)
        {
            return false;
        }

        Accepted.Add(order);
        Revision++;
        return true;
    }

    public bool RecordTransition(OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason)
    {
        Assert.True(CollectionOrderStates.CanTransition(from, to), "illegal transition recorded: " + from + " -> " + to);
        if (to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention)
        {
            // Every stop carries one actionable reason, the player's own pause
            // included (C4's PausedByPlayer).
            Assert.NotEqual(CollectionAttentionReason.Unspecified, reason);
        }

        if (FailRecordTransitions || !Writable)
        {
            return false;
        }

        Transitions.Add((from, to, reason));
        Revision++;
        return true;
    }

    public int CountAt(OrderId order, CustodyPlace place, CollectedResource resource)
    {
        if (!_progress.TryGetValue((order.Value, resource), out ResourceProgress? progress))
        {
            return 0;
        }

        switch (place)
        {
            case CustodyPlace.Worker:
                return progress.Carried;
            case CustodyPlace.Destination:
                return progress.Delivered;
            case CustodyPlace.SourceGround:
                return progress.OnGround;
            default:
                return 0;
        }
    }

    public ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource) => Bucket(order, resource);

    public bool HasUncertainTransfer(OrderId order) => Uncertain;

    public TransferReceipt Execute(TransferIntent intent, IInventoryPort from, IInventoryPort to)
    {
        Executed.Add(intent);
        Assert.Equal(CustodyPlace.Worker, intent.From.Place);
        Assert.Equal(CustodyPlace.Destination, intent.To.Place);

        if (ForcedOutcomes.Count > 0)
        {
            TransferOutcome forced = ForcedOutcomes.Dequeue();
            if (forced == TransferOutcome.Uncertain)
            {
                Uncertain = true;
            }

            return new TransferReceipt(intent.Request, forced, 0, intent.From, "forced");
        }

        if (intent.ExpectedCustodyRevision != Revision)
        {
            return new TransferReceipt(intent.Request, TransferOutcome.Stale, 0, intent.From, "stale");
        }

        // Measured, never claimed: the counts before and after decide, exactly
        // as the shipped executor's rule does. A port that reports one thing
        // and stores another therefore shows up as a mismatch rather than
        // being copied into the record.
        int destinationBefore = to.Count(intent.Item);
        int sourceBefore = from.Count(intent.Item);
        to.Add(intent.Item, Math.Min(intent.Count, to.CanAccept(intent.Item, intent.Count)));
        int gained = to.Count(intent.Item) - destinationBefore;
        from.Remove(intent.Item, gained);
        int lost = sourceBefore - from.Count(intent.Item);
        int accepted = CreditWithoutMeasuring ? intent.Count : gained;
        if (!CreditWithoutMeasuring && gained != lost)
        {
            return new TransferReceipt(
                intent.Request, TransferOutcome.Uncertain, 0, intent.From,
                "gained " + gained + " but lost " + lost);
        }

        CollectionOrderDefinition? order = null;
        foreach (CollectionOrderDefinition candidate in Accepted)
        {
            if (candidate.Order.Equals(intent.Order))
            {
                order = candidate;
            }
        }

        CollectedResources.TryFromItemPrefabName(intent.Item.PrefabName, out CollectedResource resource);
        ResourceProgress progress = Bucket(order!, resource);
        progress.Carried -= accepted;
        progress.Delivered += accepted;
        Revision++;

        TransferOutcome outcome = accepted == intent.Count ? TransferOutcome.Completed
            : accepted > 0 ? TransferOutcome.Partial
            : TransferOutcome.Refused;
        return new TransferReceipt(intent.Request, outcome, accepted, intent.From, "fake");
    }
}

internal sealed class FakeMotion : ICollectionMotion
{
    private readonly IActorModeHold _modes;
    private SitePoint? _goal;
    private float _tolerance;

    public FakeMotion(IActorModeHold modes, SitePoint start)
    {
        _modes = modes;
        Position = start;
    }

    public bool IsPresent { get; set; } = true;

    public SitePoint Position { get; set; }

    /// <summary>Metres per tick.</summary>
    public float Speed { get; set; } = 1000f;

    /// <summary>Goals within this set defer with the given reason.</summary>
    public Func<SitePoint, WorkerDeferralReason> DeferralFor { get; set; } = _ => WorkerDeferralReason.None;

    public WorkerDeferralReason LastDeferral { get; private set; }

    public int WalksIssued { get; private set; }

    public int RefusedCommands { get; private set; }

    public CollectionWalkStatus Status
    {
        get
        {
            if (_goal == null)
            {
                return CollectionWalkStatus.Idle;
            }

            if (LastDeferral != WorkerDeferralReason.None)
            {
                return CollectionWalkStatus.Deferred;
            }

            return Position.HorizontalDistanceTo(_goal.Value) <= _tolerance
                ? CollectionWalkStatus.Arrived
                : CollectionWalkStatus.Walking;
        }
    }

    public bool WalkTo(SitePoint point, float arrivalTolerance, string jobId)
    {
        if (!_modes.IsHeldBy(jobId) || !IsPresent)
        {
            RefusedCommands++;
            return false;
        }

        _goal = point;
        _tolerance = arrivalTolerance;
        LastDeferral = DeferralFor(point);
        WalksIssued++;
        return true;
    }

    public void Stop(string jobId)
    {
        if (!_modes.IsHeldBy(jobId))
        {
            RefusedCommands++;
            return;
        }

        _goal = null;
        LastDeferral = WorkerDeferralReason.None;
    }

    /// <summary>One tick of walking.</summary>
    public void Advance()
    {
        if (_goal == null || LastDeferral != WorkerDeferralReason.None || !IsPresent)
        {
            return;
        }

        SitePoint goal = _goal.Value;
        float distance = Position.HorizontalDistanceTo(goal);
        if (distance <= _tolerance * 0.5f)
        {
            return;
        }

        float step = Math.Min(Speed, distance - (_tolerance * 0.5f));
        float fraction = step / distance;
        Position = new SitePoint(
            Position.X + ((goal.X - Position.X) * fraction), goal.Y, Position.Z + ((goal.Z - Position.Z) * fraction));
    }
}

/// <summary>A world of sources behind the pickup port. Enforces the port's own
/// preconditions the way the adapter does: the reservation must be held and the
/// worker must be within reach.</summary>
internal sealed class FakePickup : ISourcePickupPort
{
    private readonly Dictionary<SourceKey, (CollectedResource Resource, int Yield, bool Consumable)> _sources =
        new Dictionary<SourceKey, (CollectedResource, int, bool)>();
    private readonly Dictionary<string, (CollectionOrderDefinition Order, CollectedResource Resource)> _drops =
        new Dictionary<string, (CollectionOrderDefinition, CollectedResource)>(StringComparer.Ordinal);
    private readonly SourceReservationBook _book;
    private readonly FakeCustody _custody;
    private readonly FakeMotion _motion;
    private int _nextDrop;

    public FakePickup(SourceReservationBook book, FakeCustody custody, FakeMotion motion)
    {
        _book = book;
        _custody = custody;
        _motion = motion;
    }

    public CollectionOrderDefinition? Order { get; set; }

    public Queue<PickupOutcome> ForcedOutcomes { get; } = new Queue<PickupOutcome>();

    /// <summary>Units of each take that fail to reach the worker.</summary>
    public int TakeShortfall { get; set; }

    public int Picks { get; private set; }

    public List<SourceKey> Picked { get; } = new List<SourceKey>();

    /// <summary>Lets the world change after a pick: a stone is gone, a branch
    /// is exhausted.</summary>
    public Action<SourceKey>? OnPicked { get; set; }

    public void Add(SourceKey key, CollectedResource resource, int yield = 1) => _sources[key] = (resource, yield, true);

    public void Remove(SourceKey key) => _sources.Remove(key);

    public bool Exists(SourceKey key) => _sources.ContainsKey(key);

    public PickupResult TryPick(SourceKey source, OrderId order)
    {
        Assert.True(_book.IsHeldBy(source, order), "picked without holding the reservation");
        Assert.True(_motion.Position.HorizontalDistanceTo(source.Position) <= 2f, "picked out of reach");

        if (ForcedOutcomes.Count > 0)
        {
            PickupOutcome forced = ForcedOutcomes.Dequeue();
            return new PickupResult(forced, Array.Empty<SpawnedDrop>(), "forced " + forced);
        }

        if (!_sources.TryGetValue(source, out var entry))
        {
            return new PickupResult(PickupOutcome.Refused, Array.Empty<SpawnedDrop>(), "gone");
        }

        _sources.Remove(source);
        Picks++;
        Picked.Add(source);
        OnPicked?.Invoke(source);
        var drops = new List<SpawnedDrop>();
        ResourceProgress progress = _custody.Bucket(Order!, entry.Resource);
        for (int unit = 0; unit < entry.Yield; unit++)
        {
            string id = "drop-" + (++_nextDrop);
            drops.Add(new SpawnedDrop(id, MaterialItem.Of(entry.Resource), 1));
            _drops[id] = (Order!, entry.Resource);
            progress.OnGround++;
        }

        return new PickupResult(PickupOutcome.Picked, drops, "picked");
    }

    public int TryTakeDrop(SpawnedDrop drop, IInventoryPort worker)
    {
        Assert.True(_drops.ContainsKey(drop.SessionId), "took a drop that was not traced to a pick");
        (CollectionOrderDefinition order, CollectedResource resource) = _drops[drop.SessionId];
        _drops.Remove(drop.SessionId);
        if (TakeShortfall > 0)
        {
            TakeShortfall--;
            return 0;
        }

        int added = worker.Add(drop.Item, drop.Count);
        ResourceProgress progress = _custody.Bucket(order, resource);
        progress.OnGround -= added;
        progress.Carried += added;
        return added;
    }
}

internal sealed class FakeProbe : ISurveyProbe
{
    private int _cursor;

    public List<SurveyCandidate> Candidates { get; } = new List<SurveyCandidate>();

    public Func<SitePoint, bool> Loaded { get; set; } = _ => true;

    public int BeginCount { get; private set; }

    public int MaxReturnedPerCall { get; private set; }

    public int OverDeliver { get; set; }

    /// <summary>The world gained or lost objects while the pass ran.</summary>
    public bool SceneMoved { get; set; }

    public bool IsLoaded(SitePoint point) => Loaded(point);

    public void Begin(WorkScope scope)
    {
        BeginCount++;
        _cursor = 0;
    }

    public DiscoveryStep Discover(int maxEntries, int maxCandidates, List<SurveyCandidate> into)
    {
        int allowed = maxCandidates + OverDeliver;
        int examined = 0;
        int before = into.Count;
        while (_cursor < Candidates.Count && examined < maxEntries && into.Count - before < allowed)
        {
            into.Add(Candidates[_cursor++]);
            examined++;
        }

        MaxReturnedPerCall = Math.Max(MaxReturnedPerCall, into.Count - before);
        return new DiscoveryStep(examined, _cursor >= Candidates.Count, SceneMoved);
    }
}

internal sealed class CollectionFakeWorld : ICollectionWorld
{
    public WorkAuthorityVerdict Authority { get; set; } = WorkAuthorityVerdict.Granted;

    public ReadinessVerdict Readiness { get; set; } = ReadyVerdict();

    public bool ScopePresent { get; set; } = true;

    public int ScopeRevision { get; set; }

    public Guid Epoch { get; set; }

    public Func<SitePoint, bool> Loaded { get; set; } = _ => true;

    public float StoneWeight { get; set; } = 2f;

    public float WoodWeight { get; set; } = 2f;

    public int ReadinessAsked { get; private set; }

    public WorkAuthorityVerdict EvaluateAuthority() => Authority;

    public ReadinessVerdict AssessReadiness(WorkerId worker)
    {
        ReadinessAsked++;
        return Readiness;
    }

    public ScopeObservation ObserveScope(WorkScope scope) => new ScopeObservation(ScopePresent, ScopeRevision, Epoch);

    public bool IsLoaded(SitePoint point) => Loaded(point);

    public float UnitWeight(CollectedResource resource) =>
        resource == CollectedResource.Stone ? StoneWeight : resource == CollectedResource.Wood ? WoodWeight : 0f;

    /// <summary>A ready verdict, built the way the game-free readiness rule
    /// builds one: recruited, with a usable axe and hammer.</summary>
    public static ReadinessVerdict ReadyVerdict()
    {
        var worker = new WorkerId("thorstein");
        var ledger = new ToolLedger();
        ledger.Issue(new ToolHolding(new RequestId("give-axe"), worker, new ToolSpecimen(ToolKind.Axe, "$item_axe_stone", 1, 100f, 1)));
        ledger.Issue(new ToolHolding(new RequestId("give-hammer"), worker, new ToolSpecimen(ToolKind.Hammer, "$item_hammer", 1, 100f, 1)));
        return WorkerReadiness.Assess(worker, true, ledger, WorkerReadiness.ForBuilding, new AllUsable());
    }

    public static ReadinessVerdict NotReady(ReadinessRefusal refusal)
    {
        var worker = new WorkerId("thorstein");
        var ledger = new ToolLedger();
        switch (refusal)
        {
            case ReadinessRefusal.NotRecruited:
                return WorkerReadiness.Assess(worker, false, ledger, WorkerReadiness.ForBuilding, new AllUsable());
            case ReadinessRefusal.ToolUnusable:
                ledger.Issue(new ToolHolding(new RequestId("give-axe"), worker, new ToolSpecimen(ToolKind.Axe, "$item_axe_stone", 1, 0f, 1)));
                ledger.Issue(new ToolHolding(new RequestId("give-hammer"), worker, new ToolSpecimen(ToolKind.Hammer, "$item_hammer", 1, 100f, 1)));
                return WorkerReadiness.Assess(worker, true, ledger, WorkerReadiness.ForBuilding, UnknownToolCondition.Instance);
            default:
                ledger.Issue(new ToolHolding(new RequestId("give-hammer"), worker, new ToolSpecimen(ToolKind.Hammer, "$item_hammer", 1, 100f, 1)));
                return WorkerReadiness.Assess(worker, true, ledger, WorkerReadiness.ForBuilding, new AllUsable());
        }
    }

    private sealed class AllUsable : IToolCondition
    {
        public bool IsUsable(ToolHolding holding) => true;
    }
}

internal sealed class FakeCooperation : ICollectionCooperation
{
    public bool Available { get; set; } = true;

    public Queue<(CollectionHandOff Step, CollectionAttentionReason Reason)> Script { get; } =
        new Queue<(CollectionHandOff, CollectionAttentionReason)>();

    public int Ticks { get; private set; }

    public List<bool> Cancels { get; } = new List<bool>();

    public bool IsAvailable(out CollectionAttentionReason reason)
    {
        reason = Available ? CollectionAttentionReason.Unspecified : CollectionAttentionReason.HaulerUnavailable;
        return Available;
    }

    public CollectionHandOff Tick(CollectionOrderDefinition order, float now, out CollectionAttentionReason reason)
    {
        Ticks++;
        if (Script.Count == 0)
        {
            reason = CollectionAttentionReason.Unspecified;
            return CollectionHandOff.Working;
        }

        (CollectionHandOff step, CollectionAttentionReason why) = Script.Dequeue();
        reason = why;
        return step;
    }

    public void Cancel(CollectionOrderDefinition order, bool detachAndPark) => Cancels.Add(detachAndPark);
}

/// <summary>Everything a loop test needs, wired the way the Foreman runtime
/// wires the real ports.</summary>
internal sealed class CollectionRig
{
    public static readonly Guid Epoch = new Guid("0f0e0d0c-0b0a-0908-0706-050403020100");
    public static readonly SitePoint Anchor = new SitePoint(0f, 30f, 0f);
    public static readonly SitePoint ChestAt = new SitePoint(3f, 30f, 0f);

    private int _nextSource;

    /// <param name="modes">The mode hold the loop is driven through. Defaults to
    /// the pre-adoption <see cref="ActorModeOwner"/> this rig has always built, so
    /// every existing test is unchanged. A test passes one in to stand in for the
    /// arbiter-backed hold Concerned Foreman now uses — most importantly one that
    /// refuses with <c>Unspecified</c>, which is what the library answers for an
    /// identity it does not track and which must not read as permission.</param>
    public CollectionRig(
        CollectionParameters? parameters = null, bool withHauler = false, IActorModeHold? modes = null)
    {
        Parameters = parameters ?? CollectionParameters.Default;
        Modes = new ActorModeOwner(WorkerKey.Thorstein);
        Hold = modes ?? Modes;
        Book = new SourceReservationBook(Epoch);
        Motion = new FakeMotion(Hold, Anchor);
        WorkerInventory = new CollectionFakeInventory("Thorstein", defaultCapacity: 32 * 50);
        Chest = new CollectionFakeInventory("chest", defaultCapacity: 1000);
        Custody = new FakeCustody(WorkerInventory, Chest);
        Pickup = new FakePickup(Book, Custody, Motion);
        Probe = new FakeProbe();
        World = new CollectionFakeWorld { Epoch = Epoch, ScopeRevision = ScopeRevision.ForAnchor("bed", Anchor) };
        Cooperation = withHauler ? new FakeCooperation() : null;
        Pickup.OnPicked = key =>
        {
            int index = Probe.Candidates.FindIndex(candidate => candidate.Key.Equals(key));
            if (index < 0)
            {
                return;
            }

            SurveyCandidate picked = Probe.Candidates[index];
            if (key.PrefabName == "Pickable_Stone")
            {
                // A picked stone is destroyed.
                Probe.Candidates.RemoveAt(index);
            }
            else
            {
                // A picked branch hides its model and waits to respawn.
                Probe.Candidates[index] = new SurveyCandidate(
                    key,
                    SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, picked: true, canBePicked: false),
                    picked.OwnedHere, picked.InInterior, picked.Location, picked.Ward, picked.EstimatedYield);
            }
        };
        Loop = new SoloCollectionLoop(
            Parameters, WorkerKey.Thorstein, new WorkerId("thorstein"), Hold, Book, Motion, Custody, Pickup, Probe,
            World, Cooperation, new CollectionRequestIds(Epoch));
    }

    public CollectionParameters Parameters { get; }

    /// <summary>The rig's own mode owner. Still here, and still what
    /// <c>rig.Modes.Mode</c> means in every test that reads it, because those
    /// tests drive the default hold — which IS this owner.</summary>
    public ActorModeOwner Modes { get; }

    /// <summary>The hold the loop and the motion port were actually given: this
    /// rig's <see cref="Modes"/> unless a test substituted one.</summary>
    public IActorModeHold Hold { get; }

    public SourceReservationBook Book { get; }

    public FakeMotion Motion { get; }

    public CollectionFakeInventory WorkerInventory { get; }

    public CollectionFakeInventory Chest { get; }

    public FakeCustody Custody { get; }

    public FakePickup Pickup { get; }

    public FakeProbe Probe { get; }

    public CollectionFakeWorld World { get; }

    public FakeCooperation? Cooperation { get; }

    public SoloCollectionLoop Loop { get; }

    public float Now { get; private set; }

    public WorkScope Scope() =>
        new WorkScope(WorkScopeSource.DefaultCampCircle, Anchor, 30f, "your bed", ScopeRevision.ForAnchor("bed", Anchor), Epoch);

    public CollectionOrderDefinition Order(
        int stone, int wood, bool hold = false, ParticipationMode mode = ParticipationMode.Solo, string id = "collect-1")
    {
        var quotas = new List<ResourceQuota>();
        if (stone > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Stone, stone));
        }

        if (wood > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Wood, wood));
        }

        DeliveryTarget delivery = hold ? DeliveryTarget.HoldForPlayer() : DeliveryTarget.ToContainer("00000000cafe0000:00000042", Epoch, ChestAt);
        return new CollectionOrderDefinition(
            new OrderId(id), new WorkerId("thorstein"), quotas, Scope(), delivery, mode, "TESTER");
    }

    public static IReadOnlyDictionary<CollectedResource, int> OnePerPick() =>
        new Dictionary<CollectedResource, int> { [CollectedResource.Stone] = 1, [CollectedResource.Wood] = 1 };

    public CollectionIntakeRefusal Accept(CollectionOrderDefinition order)
    {
        CollectionIntakeRefusal refusal = Loop.Accept(order, defaultCirclePreviewed: true, OnePerPick(), Now);
        if (refusal == CollectionIntakeRefusal.Unspecified)
        {
            // Only an accepted order owns the drops its picks spawn.
            Pickup.Order = order;
        }

        return refusal;
    }

    /// <summary>Places a natural source in the world and in the probe.</summary>
    public SourceKey AddSource(CollectedResource resource, float x, float z, int yield = 1,
        SourceAvailability availability = SourceAvailability.Available)
    {
        bool stone = resource == CollectedResource.Stone;
        string prefab = stone ? "Pickable_Stone" : "Pickable_Branch";
        var key = new SourceKey(prefab, "00000000cafe0000:" + (++_nextSource).ToString("x8"), Epoch, new SitePoint(x, 30f, z));
        NaturalSourceFacts facts = stone ? SourceFacts.VanillaStone() : SourceFacts.VanillaBranch();
        if (availability == SourceAvailability.Exhausted)
        {
            facts = stone
                ? SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, picked: true, canBePicked: false)
                : SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, picked: true, canBePicked: false);
        }

        AreaAccess ward = availability == SourceAvailability.Inaccessible ? AreaAccess.Denied
            : availability == SourceAvailability.Unknown ? AreaAccess.Unavailable
            : AreaAccess.Granted;
        Probe.Candidates.Add(new SurveyCandidate(key, facts, true, false, LocationStanding.Outside, ward, yield));
        if (availability == SourceAvailability.Available)
        {
            Pickup.Add(key, resource, yield);
        }

        return key;
    }

    public void Tick(int count = 1)
    {
        for (int index = 0; index < count; index++)
        {
            Now += 0.05f;
            Loop.Tick(Now);
            Motion.Advance();
        }
    }

    /// <summary>Ticks until the state is reached or the tick limit passes.
    /// </summary>
    public bool RunUntil(Func<bool> condition, int maxTicks = 20000)
    {
        for (int index = 0; index < maxTicks; index++)
        {
            if (condition())
            {
                return true;
            }

            Tick();
        }

        return condition();
    }

    public void Wait(float seconds)
    {
        Now += seconds;
    }

    public ResourceProgress Progress(CollectedResource resource) => Custody.ProgressFor(Loop.Order!, resource);
}
