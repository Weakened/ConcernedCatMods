using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>The pickup seam of CONTRACTS.md §5.3 and §6 over the installed game:
/// revalidate, journal the intent, perform the game's own pick once, trace
/// exactly the drops that pick spawned, and take them into the worker's
/// inventory through the game's own pickup, each take journaled.
///
/// <b>One synchronous call.</b> The loop calls <see cref="TryPick"/> and then
/// <see cref="TryTakeDrop"/> for every drop inside the same worker tick. For a
/// pickable this process owns, <c>Pickable.Interact</c> runs <c>RPC_Pick</c>
/// synchronously (<c>ZRoutedRpc</c> dispatches locally), the drops'
/// <c>ItemDrop.Awake</c> runs inside <c>Instantiate</c>, and
/// <c>Humanoid.Pickup</c> adds before it destroys. No player
/// <c>FixedUpdate</c> can run in between, and the player's auto-pickup ignores
/// items younger than 0.5 s anyway (PICKUP_SEAM_AUDIT.md §2.4, §7.4). A traced
/// drop is guarded with <c>m_autoPickup = false</c> the moment it is found;
/// any drop not taken gets it back and stays an ordinary world item, logged.
/// A trace is never carried over to a later frame: drops left in the world may
/// merge with others and stop being traceable.
///
/// <b>What it never does.</b> It never claims ownership, never picks through
/// <c>ItemDrop.Pickup</c>/<c>Interact</c> (the non-player trap), never adds an
/// item it did not see arrive, never destroys a source itself, and never
/// grants an estimate.</summary>
internal sealed class WorldSourcePickupPort : ISourcePickupPort
{
    private readonly CollectionParameters _parameters;
    private readonly ICustodyRuntime _custody;
    private readonly SourceDirectory _directory;
    private readonly SourceReservationBook _reservations;
    private readonly Func<OrderId, CollectionOrderDefinition?> _orders;
    private readonly Func<ForemanWorkerAI?> _body;
    private readonly IDesignationSite _site;
    private readonly CollectionRequestIds _requestIds;
    private readonly Func<WorkAuthorityVerdict> _authority;
    private readonly Func<CollectedResource, float> _unitWeight;
    private readonly Action<string> _log;
    private readonly HashSet<ItemDrop> _before = new HashSet<ItemDrop>();
    private readonly Dictionary<string, TracedDrop> _traced = new Dictionary<string, TracedDrop>(StringComparer.Ordinal);
    private readonly HashSet<SourceKey> _poisoned = new HashSet<SourceKey>();
    private int _traceFrame = -1;

    public WorldSourcePickupPort(
        CollectionParameters parameters,
        ICustodyRuntime custody,
        SourceDirectory directory,
        SourceReservationBook reservations,
        Func<OrderId, CollectionOrderDefinition?> orders,
        Func<ForemanWorkerAI?> body,
        IDesignationSite site,
        CollectionRequestIds requestIds,
        Func<WorkAuthorityVerdict> authority,
        Func<CollectedResource, float> unitWeight,
        Action<string> log)
    {
        _parameters = parameters;
        _custody = custody;
        _directory = directory;
        _reservations = reservations;
        _orders = orders;
        _body = body;
        _site = site;
        _requestIds = requestIds;
        _authority = authority;
        _unitWeight = unitWeight;
        _log = log;
    }

    public PickupResult TryPick(SourceKey source, OrderId order)
    {
        // A trace never outlives the call that made it.
        ReleaseUntakenDrops();

        // --- 1. Revalidate, in the same tick as the pick -------------------------------------------
        if (_authority() != WorkAuthorityVerdict.Granted)
        {
            return Refused("work authority is not granted");
        }

        CollectionOrderDefinition? definition = _orders(order);
        if (definition == null)
        {
            return Refused("no such active order");
        }

        if (!_reservations.IsHeldBy(source, order))
        {
            return Refused("the order does not hold this source's reservation");
        }

        if (_poisoned.Contains(source))
        {
            return Refused("this source misbehaved earlier this session and is left alone");
        }

        if (!_directory.TryResolve(source, out ZNetView view, out Pickable pickable, out string missing))
        {
            return Refused(missing);
        }

        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(
            NaturalSourceClassifier.ReadFacts(view, source.PrefabName, out _));
        if (!verdict.IsEligible || NaturalSourceAllowlist.YieldOf(verdict.Kind) != YieldOf(source))
        {
            return Refused("not an eligible natural source now: " + verdict);
        }

        ForemanWorkerAI? body = _body();
        Humanoid? humanoid = body != null ? body.GetComponent<Humanoid>() : null;
        if (body == null || humanoid == null || body.IsFaulted || !body.IsOwnedAndValid)
        {
            // Humanoid.Pickup destroys only the local object for a body without
            // a network object, which would duplicate the item: a valid, owned
            // body is required.
            return Refused("Thorstein's body is not here, not owned here, or faulted");
        }

        if (!_custody.TryResolveWorker(WorkerKey.Thorstein, out IInventoryPort? workerPort, out CollectionAttentionReason workerRefusal) ||
            workerPort == null)
        {
            return Refused("his inventory is not available (" + workerRefusal + ")");
        }

        int expected = NaturalSourceClassifier.ExpectedYield(pickable);
        CollectedResource resource = verdict.Yields;
        var item = MaterialItem.Of(resource);
        Vector3 position = view.transform.position;

        var site = new SourceSiteFacts(
            ownedHere: view.IsOwner(),
            inScope: definition.Scope.Contains(NaturalSourceClassifier.ToSitePoint(position)),
            inInterior: Character.InInterior(position),
            insideLocation: Location.IsInsideLocation(position, 0f),
            ward: _site.CheckAccess(NaturalSourceClassifier.ToSitePoint(position), 0f),
            workerDistanceMetres: Utils.DistanceXZ(body.transform.position, position),
            capacityFits: expected >= 1 && CarryFits(humanoid, workerPort, item, resource, expected),
            localPlayerPresent: Player.m_localPlayer != null);
        SiteRefusal siteRefusal = NaturalSourcePredicate.CheckSite(site, _parameters.PickupReachMetres);
        if (siteRefusal != SiteRefusal.Unspecified)
        {
            return Refused(NaturalSourcePredicate.Describe(siteRefusal));
        }

        // --- 2. The intent reaches disk before anything is touched ---------------------------------
        RequestId? pickup = _custody.BeginPickup(order, source);
        if (pickup == null)
        {
            return Refused("the pickup could not be written to the settlement record");
        }

        // --- 3. The game's own pick, once --------------------------------------------------------------
        _before.Clear();
        foreach (ItemDrop existing in ItemDrop.s_instances)
        {
            if (existing != null)
            {
                _before.Add(existing);
            }
        }

        Exception? thrown = null;
        try
        {
            pickable.Interact(humanoid, repeat: false, alt: false);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        // --- 4. Trace exactly what that pick spawned ----------------------------------------------------
        string yieldPrefab = CollectedResources.ItemPrefabName(resource);
        var drops = new List<SpawnedDrop>();
        int untraced = 0;
        _traceFrame = Time.frameCount;
        foreach (ItemDrop candidate in ItemDrop.s_instances)
        {
            if (candidate == null || _before.Contains(candidate))
            {
                continue;
            }

            ZNetView dropView = candidate.GetComponent<ZNetView>();
            bool ours = dropView != null && dropView.IsValid() && dropView.IsOwner() &&
                string.Equals(Utils.GetPrefabName(candidate.gameObject), yieldPrefab, StringComparison.Ordinal) &&
                Utils.DistanceXZ(candidate.transform.position, position) <= 3f;
            if (!ours)
            {
                // Something else spawned during the call (another mod's hook):
                // it is not this order's and stays in the world.
                untraced++;
                continue;
            }

            candidate.m_autoPickup = false;
            int stack = Math.Max(1, candidate.m_itemData.m_stack);
            string id = NaturalSourceClassifier.FormatId(dropView!.GetZDO().m_uid);
            var dropItem = new MaterialItem(yieldPrefab, candidate.m_itemData.m_quality, candidate.m_itemData.m_variant);
            drops.Add(new SpawnedDrop(id, dropItem, stack));
            _traced[id] = new TracedDrop(candidate, order, source, dropItem, stack);
        }

        bool sourceChanged = !view.IsValid() || pickable == null || pickable.GetPicked();
        PickupResult result;
        if (drops.Count > 0)
        {
            string note = "picked " + drops.Count + " traced drop(s), expected " + expected;
            if (thrown != null || !sourceChanged)
            {
                // Real items exist and are taken, but this source behaved
                // unlike vanilla: it is never picked again this session.
                _poisoned.Add(source);
                note += thrown != null ? "; the pick threw " + thrown.GetType().Name : "; the source did not change";
                _log("Collection: " + source + " " + note + ". It will not be picked again this session.");
            }

            result = new PickupResult(PickupOutcome.Picked, drops, note);
        }
        else if (!sourceChanged)
        {
            // Nothing happened: nothing to grant, nothing to trace. A throw
            // here is the missing-local-player path, before any mutation.
            result = new PickupResult(
                PickupOutcome.Refused, drops,
                thrown != null ? "the pick threw before changing anything: " + thrown.GetType().Name : "the pick produced nothing");
        }
        else
        {
            result = new PickupResult(PickupOutcome.Uncertain, drops, "the source changed but no drop could be identified");
        }

        if (untraced > 0)
        {
            _log("Collection: " + untraced + " untraced item(s) appeared during a pick at " + source + " and were left alone.");
        }

        // --- 5. The traced result reaches the record ----------------------------------------------------
        if (!_custody.FinishPickup(pickup.Value, result))
        {
            // The record does not know these drops, so nothing may be taken
            // on their account: they become ordinary world items.
            ReleaseUntakenDrops();
            return new PickupResult(PickupOutcome.Uncertain, drops, "the pickup's result could not be written to the settlement record");
        }

        return result;
    }

    public int TryTakeDrop(SpawnedDrop drop, IInventoryPort worker)
    {
        if (!_traced.TryGetValue(drop.SessionId, out TracedDrop traced))
        {
            return 0;
        }

        _traced.Remove(drop.SessionId);
        ItemDrop itemDrop = traced.Drop;
        if (itemDrop == null)
        {
            return 0;
        }

        ZNetView dropView = itemDrop.GetComponent<ZNetView>();
        ForemanWorkerAI? body = _body();
        Humanoid? humanoid = body != null ? body.GetComponent<Humanoid>() : null;
        bool valid = Time.frameCount == _traceFrame &&
            dropView != null && dropView.IsValid() && dropView.IsOwner() &&
            itemDrop.m_itemData.m_stack == drop.Count && traced.Count == drop.Count &&
            _authority() == WorkAuthorityVerdict.Granted &&
            body != null && humanoid != null && body.IsOwnedAndValid && !body.IsFaulted &&
            worker != null && worker.CanAccept(traced.Item, drop.Count) >= drop.Count;
        if (!valid)
        {
            Restore(itemDrop, "it could not be taken safely in the same call");
            return 0;
        }

        var intent = new TransferIntent(
            _requestIds.Next(traced.Order, "take"),
            traced.Order,
            new CustodyLocation(CustodyPlace.SourceGround, traced.Source.ToString(), traced.Source.WorldLoadEpoch),
            new CustodyLocation(CustodyPlace.Worker, WorkerKey.Thorstein.Value, Guid.Empty),
            traced.Item,
            drop.Count,
            _custody.View.Revision);

        if (_custody.BeginTransfer(intent) != TransferOutcome.Unspecified)
        {
            Restore(itemDrop, "the take could not be written to the settlement record");
            return 0;
        }

        Inventory inventory = humanoid!.GetInventory();
        string sharedName = itemDrop.m_itemData.m_shared.m_name;
        int before = inventory.CountItems(sharedName, -1, matchWorldLevel: false);

        bool added;
        Exception? thrown = null;
        try
        {
            // The game's own pickup: add first, destroy the drop second, and
            // no autopickup delay because the drop is this call's own.
            added = humanoid.Pickup(itemDrop.gameObject, autoequip: false, autoPickupDelay: false);
        }
        catch (Exception exception)
        {
            added = false;
            thrown = exception;
        }

        int delta = inventory.CountItems(sharedName, -1, matchWorldLevel: false) - before;
        bool dropGone = itemDrop == null || dropView == null || !dropView.IsValid();
        string evidence = "inventory " + before + " -> " + (before + delta) + ", drop " + (dropGone ? "gone" : "still in the world") +
            (thrown != null ? ", threw " + thrown.GetType().Name : string.Empty);

        TransferReceipt receipt;
        if (thrown == null && added && delta == drop.Count && dropGone)
        {
            receipt = new TransferReceipt(intent.Request, TransferOutcome.Completed, delta, intent.From, evidence);
        }
        else if (thrown == null && !added && delta == 0 && !dropGone)
        {
            Restore(itemDrop, "the game refused the pickup");
            receipt = new TransferReceipt(intent.Request, TransferOutcome.Refused, 0, intent.From, evidence);
        }
        else
        {
            // Counts that do not add up are never guessed at.
            if (!dropGone)
            {
                Restore(itemDrop, "the take ended uncertain");
            }

            receipt = new TransferReceipt(intent.Request, TransferOutcome.Uncertain, Math.Max(0, delta), intent.From, evidence);
            _log("Collection: an uncertain take at " + traced.Source + ": " + evidence + ".");
        }

        if (!_custody.FinishTransfer(receipt))
        {
            _log("Collection: the receipt of " + intent.Request + " could not be written; reconciliation decides from the inventories.");
        }

        return receipt.Accepted;
    }

    /// <summary>Gives every traced but untaken drop back to the world as an
    /// ordinary item. Called after every loop tick, and before every pick.
    /// </summary>
    public void ReleaseUntakenDrops()
    {
        if (_traced.Count == 0)
        {
            return;
        }

        foreach (TracedDrop traced in _traced.Values)
        {
            Restore(traced.Drop, "it was not taken in the call that traced it");
        }

        _traced.Clear();
    }

    private bool CarryFits(Humanoid humanoid, IInventoryPort workerPort, MaterialItem item, CollectedResource resource, int count)
    {
        if (workerPort.CanAccept(item, count) < count)
        {
            return false;
        }

        float carried = 0f;
        Inventory inventory = humanoid.GetInventory();
        foreach (CollectedResource kind in new[] { CollectedResource.Stone, CollectedResource.Wood })
        {
            float unit = _unitWeight(kind);
            ItemDrop? prefab = ObjectDB.instance != null
                ? ObjectDB.instance.GetItemPrefab(CollectedResources.ItemPrefabName(kind))?.GetComponent<ItemDrop>()
                : null;
            if (unit > 0f && prefab != null)
            {
                carried += inventory.CountItems(prefab.m_itemData.m_shared.m_name, -1, matchWorldLevel: false) * unit;
            }
        }

        return CarryPlanner.CarryRoomUnits(_parameters.WorkerCarryWeight, carried, _unitWeight(resource), int.MaxValue) >= count;
    }

    private void Restore(ItemDrop? drop, string why)
    {
        if (drop != null)
        {
            drop.m_autoPickup = true;
            _log("Collection: a traced drop was left as an ordinary item because " + why + ".");
        }
    }

    private static CollectedResource YieldOf(SourceKey source) =>
        NaturalSourceAllowlist.TryKindOfPrefab(source.PrefabName, out NaturalSourceKind kind)
            ? NaturalSourceAllowlist.YieldOf(kind)
            : CollectedResource.Unspecified;

    private static PickupResult Refused(string why) =>
        new PickupResult(PickupOutcome.Refused, Array.Empty<SpawnedDrop>(), why);

    private readonly struct TracedDrop
    {
        public TracedDrop(ItemDrop drop, OrderId order, SourceKey source, MaterialItem item, int count)
        {
            Drop = drop;
            Order = order;
            Source = source;
            Item = item;
            Count = count;
        }

        public ItemDrop Drop { get; }

        public OrderId Order { get; }

        public SourceKey Source { get; }

        public MaterialItem Item { get; }

        public int Count { get; }
    }
}
