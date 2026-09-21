using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>Foreman's custody runtime (<see cref="ICustodyRuntime"/>, TASKS.md
/// §3): the game-free <see cref="CustodyCore"/> for this world load, wired to
/// the world — the clock, the authority answer, the worker body, chests and
/// carts, and the world-save hook.
///
/// It owns the load order the contract fixes: open the record, re-bind the
/// worker body by its key, apply the world-save marker rule, reconcile against
/// what the body actually carries, and only then report whether new work may
/// be taken.</summary>
internal sealed class ForemanCustodyRuntime : ICustodyRuntime
{
    /// <summary>Documented default of <c>Collection/WorkerCarryWeight</c>: 50
    /// Stone or 50 Wood at 2.0 each. The setting itself belongs to the lead's
    /// settings file; until it is bound this value is used.</summary>
    internal const float DefaultCarryWeight = 100f;

    /// <summary>How close the worker stands to a chest, a cart or the player to
    /// put something in or take something out.</summary>
    internal const float ReachMetres = 3f;

    private static ForemanCustodyRuntime? s_active;
    private static bool s_hooked;

    private readonly SettlementRecords _records;
    private readonly Func<WorkAuthorityVerdict> _authority;
    private readonly Action<string> _log;

    private CustodyCore? _core;
    private Guid _epoch;
    private WorkerBodyCensus? _census;
    private ReconciliationReport? _loadReport;

    internal ForemanCustodyRuntime(SettlementRecords records, Func<WorkAuthorityVerdict> authority, Action<string> log)
    {
        _records = records;
        _authority = authority;
        _log = log;
        WorkerKey = WorkerKey.Thorstein;
    }

    internal WorkerKey WorkerKey { get; }

    /// <summary>Where the record says the worker's own inventory is. Epoch-less
    /// on purpose, matching <c>SoloCollectionLoop.WorkerLocation</c>: a chest key
    /// is renumbered on every load and a worker body is not, so his place is the
    /// same place across loads.</summary>
    public CustodyLocation WorkerLocation => new CustodyLocation(CustodyPlace.Worker, WorkerKey.Value, Guid.Empty);

    internal WorkerId ToolWorker => new WorkerId(WorkerKey.Worker);

    internal Func<float> CarryWeight { get; set; } = () => DefaultCarryWeight;

    internal CustodyCore? Core => _core;

    internal Guid Epoch => _epoch;

    internal WorkerBodyCensus? Census => _census;

    internal ReconciliationReport? LoadReport => _loadReport;

    // ------------------------------------------------------------------
    // Hooks
    // ------------------------------------------------------------------

    /// <summary>Subscribes the world-save hook once, at plugin start. It does
    /// nothing unless custody has rows no save has confirmed yet.</summary>
    internal static void InstallSaveHook()
    {
        if (s_hooked)
        {
            return;
        }

        s_hooked = true;
        ZNet.WorldSaveStarted = (Action)Delegate.Combine(ZNet.WorldSaveStarted, new Action(OnWorldSaveStarted));
    }

    private static void OnWorldSaveStarted()
    {
        // Invoked on the main thread inside ZNet.SaveWorld, before the
        // snapshot. Nothing may escape into the game's save.
        try
        {
            ForemanCustodyRuntime? runtime = s_active;
            ZNet net = ZNet.instance;
            if (runtime?._core == null || net == null)
            {
                return;
            }

            runtime._core.OnWorldSaveStarted(net.GetTimeSeconds());
            if (runtime._core.IsMarkerOwed)
            {
                runtime._log(
                    "The world was saved, but the settlement record could not note it. No new custody work " +
                    "will start until it can; nothing already carried or delivered is affected.");
            }
        }
        catch (Exception exception)
        {
            s_active?._log("Settlement world-save marker failed: " + exception);
        }
    }

    // ------------------------------------------------------------------
    // World lifecycle
    // ------------------------------------------------------------------

    /// <summary>Called as soon as a world is up, before any time can pass:
    /// opens custody for this load and reconciles.</summary>
    internal void OnWorldLoaded()
    {
        _core = null;
        _census = null;
        _loadReport = null;
        s_active = this;

        ZNet net = ZNet.instance;
        if (net == null || !_records.TryOpen(out SettlementRegister _, out SettlementJournal journal))
        {
            return;
        }

        _epoch = _records.LoadEpoch;
        double loaded = net.GetTimeSeconds();

        _core = CustodyCore.Open(
            journal,
            _records.TryPersistJournal,
            () => ZNet.instance == null ? double.NaN : ZNet.instance.GetTimeSeconds(),
            new WorldLoad(loaded, _epoch),
            () => _authority() == WorkAuthorityVerdict.Granted);

        SaveTimelineReport? timeline = _core.LoadReplay.Timeline;
        if (timeline != null && (timeline.VoidedCount > 0 || timeline.AmbiguousCount > 0))
        {
            _log(
                "Settlement record: " + timeline.VoidedCount.ToString(CultureInfo.InvariantCulture) +
                " recorded change(s) were after the world save that was loaded and are void; " +
                timeline.AmbiguousCount.ToString(CultureInfo.InvariantCulture) + " could not be placed.");
        }

        _census = WorldCustodyObjects.Census(WorkerKey.Value);
        if (_census.Unidentified > 0)
        {
            _log(
                _census.Unidentified.ToString(CultureInfo.InvariantCulture) + " worker body(ies) in this world carry " +
                "no identity (spawned by an older build). They are left exactly as they are.");
        }

        _loadReport = CustodyReconciler.Reconcile(_core.Ledger, new StoredObserver(this), ordersWereRunning: false);
        PauseOrdersAfterLoad(_loadReport);

        foreach (ReconciliationFinding finding in _loadReport.Findings)
        {
            if (finding.NeedsAttention)
            {
                _log("Settlement custody: " + finding.Sentence);
            }
        }
    }

    internal void OnWorldUnloaded()
    {
        _core = null;
        _census = null;
        _loadReport = null;
        if (ReferenceEquals(s_active, this))
        {
            s_active = null;
        }
    }

    /// <summary>§8: every order resumes Paused after a reload — or
    /// NeedsAttention, with the reason, when reconciliation found something.
    /// Nothing resumes by itself.</summary>
    private void PauseOrdersAfterLoad(ReconciliationReport report)
    {
        if (_core == null)
        {
            return;
        }

        foreach (CollectionOrderRecord order in _core.Ledger.Orders)
        {
            if (CollectionOrderStates.IsTerminal(order.State))
            {
                continue;
            }

            // The body's own state first (CONTRACTS.md §5.6). Without exactly
            // one body the stored inventory cannot be read, so reconciliation
            // can only say "cannot be checked", which would otherwise read as a
            // generic mismatch and hide the reason a person has to act on.
            CollectionAttentionReason reason = CollectionAttentionReason.Unspecified;
            if (_census != null && _census.IsDuplicated)
            {
                reason = CollectionAttentionReason.WorkerBodyDuplicated;
            }
            else if (_census != null && _census.IsMissing && CarriesAnything(order.Order))
            {
                reason = CollectionAttentionReason.WorkerBodyLost;
            }

            if (reason == CollectionAttentionReason.Unspecified)
            {
                reason = report.ReasonFor(order.Order);
            }

            if (reason == CollectionAttentionReason.Unspecified && _core.Ledger.HasUncertainTransfer(order.Order))
            {
                reason = CollectionAttentionReason.TransferUncertain;
            }

            CollectionOrderState target = reason == CollectionAttentionReason.Unspecified
                ? CollectionOrderState.Paused
                : CollectionOrderState.NeedsAttention;

            // C2: nothing wrong, but the order's chest key and work-area snapshot
            // belong to the previous load; it stays Paused until the player
            // confirms a rebind.
            if (target == CollectionOrderState.Paused)
            {
                reason = order.Definition.Delivery.Kind == DeliveryKind.Container
                    ? CollectionAttentionReason.DestinationStale
                    : CollectionAttentionReason.ScopeChanged;
            }
            // The player's own pause survives a reload; every other reason is
            // re-derived from what is true now, including one the order was
            // already carrying (review R2, m8).
            if (order.State == CollectionOrderState.Paused
                && order.Reason == CollectionAttentionReason.PausedByPlayer)
            {
                continue;
            }

            if (order.State == target && order.Reason == reason)
            {
                continue;
            }

            if (!_core.RecordTransition(order.Order, order.State, target, reason, out string refusal))
            {
                _log("Order \"" + order.Order.Value + "\" could not be paused after the reload: " + refusal);
            }
        }
    }

    private bool CarriesAnything(OrderId order)
    {
        foreach (Holding holding in _core!.Ledger.Holdings)
        {
            if (holding.Order.Equals(order) && holding.Location.Place == CustodyPlace.Worker)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // ICustodyRuntime
    // ------------------------------------------------------------------

    public IMaterialCustodyView View => (IMaterialCustodyView?)_core?.Ledger ?? EmptyView.Instance;

    public ITransferExecutor Executor => (ITransferExecutor?)_core?.Executor ?? RefusingExecutor.Instance;

    public bool IsWritable
    {
        get
        {
            if (_core == null || !_core.IsWritable || _core.BuildMaterials.NeedsRepair || _census == null || _census.IsDuplicated)
            {
                return false;
            }

            foreach (CollectionOrderRecord order in _core.Ledger.Orders)
            {
                if (_core.Ledger.HasUncertainTransfer(order.Order))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>C2: the one epoch of this world load — the settlement records'
    /// identity epoch, which every custody row of the load also carries.
    /// </summary>
    public Guid WorldLoadEpoch => _epoch;

    public CustodyOutcome ReserveBuild(Reservation reservation, Func<bool> draw, out string failure)
    {
        failure = "build custody is unavailable; run cf_settle reconcile";
        return _core == null ? CustodyOutcome.Rejected :
            _core.BuildMaterials.Reserve(reservation, () => IsWritable && draw(), out failure);
    }

    public CustodyOutcome CommitBuild(Reservation reservation, Func<bool> placeAndPay, out string failure)
    {
        failure = "build custody is unavailable; run cf_settle reconcile";
        return _core == null ? CustodyOutcome.Rejected :
            _core.BuildMaterials.Commit(reservation, () => IsWritable && placeAndPay(), out failure);
    }

    public CustodyOutcome RefundBuild(Reservation reservation, Func<bool> putBack, out string failure)
    {
        failure = "build custody is unavailable; run cf_settle reconcile";
        return _core == null ? CustodyOutcome.Rejected :
            _core.BuildMaterials.Refund(reservation, () => IsWritable && putBack(), out failure);
    }

    public bool TryRecoverOrder(WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state)
    {
        if (_core == null)
        {
            order = null;
            state = CollectionOrderState.Unspecified;
            return false;
        }

        return _core.TryRecoverOrder(worker, out order, out state);
    }

    public bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery)
    {
        if (_core == null)
        {
            return false;
        }

        if (!_core.RecordRebound(order, scope, delivery, out string reason))
        {
            _log("Order \"" + order.Value + "\" not rebound: " + reason);
            return false;
        }

        return true;
    }

    /// <summary>What the record says is at one place, per item, for <b>every</b>
    /// order it knows - terminal ones included.
    ///
    /// <b>Why this is here and not on <c>IMaterialCustodyView</c>.</b> The view's
    /// <c>CountAt</c> answers for one order, so a caller has to know which orders
    /// exist, and the recovery seam only hands back non-terminal ones - which
    /// leaves a CANCELLED collection order's carried material unaccountable to
    /// anybody but the ledger. The ledger has always been able to answer
    /// (<c>MaterialCustodyLedger.TotalAt</c>); what was missing was a way to ask
    /// it from a consumer. Adding the method to the shared view would have been
    /// four test fakes in two projects for one question one product asks, so the
    /// question lives on this product's own seam and forwards.</summary>
    /// <returns>Zero when there is no record to read, which refuses to make a
    /// claim about somebody else's material rather than denying one.</returns>
    public int RecordedAt(CustodyLocation location, MaterialItem item)
    {
        try
        {
            return _core == null ? 0 : Math.Max(0, _core.Ledger.TotalAt(location, item));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public bool TryResolveWorker(WorkerKey worker, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = null;
        if (!AuthorityAllows(out refusal))
        {
            return false;
        }

        if (!worker.Equals(WorkerKey))
        {
            refusal = CollectionAttentionReason.WorkerNotRecruited;
            return false;
        }

        if (_census != null && _census.IsDuplicated)
        {
            refusal = CollectionAttentionReason.WorkerBodyDuplicated;
            return false;
        }

        WorkerBody? body = WorkerBody.FindLive(worker.Value);
        if (body == null)
        {
            // A body whose ground is not loaded is not lost: work simply cannot
            // happen out there.
            refusal = _census != null && !_census.IsMissing
                ? CollectionAttentionReason.ScopeUnloaded
                : CollectionAttentionReason.WorkerBodyLost;
            return false;
        }

        var workerPort = new WorkerInventoryPort(body, CarryWeight);
        if (!workerPort.IsAvailable)
        {
            refusal = CollectionAttentionReason.ReconciliationMismatch;
            return false;
        }

        port = workerPort;
        refusal = CollectionAttentionReason.Unspecified;
        return true;
    }

    public bool TryResolveContainer(DeliveryTarget target, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = null;
        if (!AuthorityAllows(out refusal))
        {
            return false;
        }

        if (target.Kind != DeliveryKind.Container)
        {
            refusal = CollectionAttentionReason.DestinationUnavailable;
            return false;
        }

        if (target.WorldLoadEpoch != _epoch || _epoch == Guid.Empty)
        {
            refusal = CollectionAttentionReason.DestinationStale;
            return false;
        }

        Container? container = WorldCustodyObjects.FindContainer(target.ContainerKey);
        if (container == null)
        {
            refusal = CollectionAttentionReason.DestinationUnavailable;
            return false;
        }

        var containerPort = new ContainerInventoryPort(container, target.ContainerKey, WorkerPosition, ReachMetres);
        string? unavailable = containerPort.Unavailable;
        if (unavailable != null && unavailable != "out of reach")
        {
            refusal = unavailable == "access denied"
                ? CollectionAttentionReason.DestinationAccessDenied
                : CollectionAttentionReason.DestinationUnavailable;
            return false;
        }

        // Reach is checked again by the executor at the transfer; resolving a
        // chest from across the camp is how the walk there is planned.
        port = containerPort;
        refusal = CollectionAttentionReason.Unspecified;
        return true;
    }

    public bool TryResolveCart(string cartSessionKey, Guid providerEpoch, out IInventoryPort? port, out CollectionAttentionReason refusal)
    {
        port = null;
        if (!AuthorityAllows(out refusal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(cartSessionKey) || providerEpoch == Guid.Empty || _core == null)
        {
            refusal = CollectionAttentionReason.CartLeaseLost;
            return false;
        }

        Vagon? cart = WorldCustodyObjects.FindCart(cartSessionKey);
        if (cart == null)
        {
            refusal = CollectionAttentionReason.CartLeaseLost;
            return false;
        }

        CustodyCore core = _core;
        var cartPort = new CartInventoryPort(
            cart,
            item => core.Ledger.TryGetCartBaseline(cartSessionKey, providerEpoch, out CartBaseline baseline)
                ? baseline.CountOf(item)
                : (int?)null,
            WorkerPosition,
            ReachMetres);

        string? unavailable = cartPort.Unavailable;
        if (unavailable != null && unavailable != "out of reach")
        {
            refusal = unavailable == "access denied"
                ? CollectionAttentionReason.DestinationAccessDenied
                : CollectionAttentionReason.HaulerNeedsAttention;
            return false;
        }

        port = cartPort;
        refusal = CollectionAttentionReason.Unspecified;
        return true;
    }

    public bool RecordAccepted(CollectionOrderDefinition order)
    {
        if (_core == null)
        {
            return false;
        }

        if (!_core.RecordAccepted(order, out string reason))
        {
            _log("Order not accepted: " + reason);
            return false;
        }

        return true;
    }

    public bool RecordTransition(OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason)
    {
        if (_core == null)
        {
            return false;
        }

        if (!_core.RecordTransition(order, from, to, reason, out string refusal))
        {
            _log("Order \"" + order.Value + "\" not moved to " + to + ": " + refusal);
            return false;
        }

        return true;
    }

    public bool RecordCartBaseline(OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch)
    {
        if (_core == null || !TryResolveCart(cartSessionKey, providerEpoch, out IInventoryPort? _, out _))
        {
            return false;
        }

        Vagon? cart = WorldCustodyObjects.FindCart(cartSessionKey);
        Inventory? inventory = cart == null || cart.m_container == null ? null : cart.m_container.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        // Every item already in the cart, material or not: the cargo that was
        // there before gathered material was, never counted as gathered.
        var counts = new Dictionary<MaterialItem, int>();
        var sequence = new List<MaterialItem>();
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (stack.m_dropPrefab == null)
            {
                continue;
            }

            var item = new MaterialItem(stack.m_dropPrefab.name, stack.m_quality, stack.m_variant);
            if (!counts.ContainsKey(item))
            {
                counts[item] = 0;
                sequence.Add(item);
            }

            counts[item] += stack.m_stack;
        }

        var baseline = new List<ItemCount>(sequence.Count);
        foreach (MaterialItem item in sequence)
        {
            baseline.Add(new ItemCount(item, counts[item]));
        }

        if (!_core.RecordCartBaseline(order, leaseId, cartSessionKey, providerEpoch, baseline, out string reason))
        {
            _log("The cart's existing cargo could not be recorded: " + reason);
            return false;
        }

        return true;
    }

    public RequestId? BeginPickup(OrderId order, SourceKey source)
    {
        if (_core == null)
        {
            return null;
        }

        RequestId? pickup = _core.BeginPickup(order, source, out string reason);
        if (!pickup.HasValue)
        {
            _log("Pickup not started: " + reason);
        }

        return pickup;
    }

    public bool FinishPickup(RequestId pickup, PickupResult result)
    {
        if (_core == null)
        {
            return false;
        }

        if (!_core.FinishPickup(pickup, result, out string reason))
        {
            _log("Pickup result not recorded: " + reason);
            return false;
        }

        return true;
    }

    public TransferOutcome BeginTransfer(TransferIntent intent)
    {
        if (_core == null)
        {
            return TransferOutcome.Refused;
        }

        TransferOutcome outcome = _core.BeginTransfer(intent, out string reason);
        if (outcome != TransferOutcome.Unspecified)
        {
            _log("Transfer " + intent.Request.Value + " not started (" + outcome + "): " + reason);
        }

        return outcome;
    }

    /// <summary>For a transfer involving the worker, the body must have written
    /// its inventory in the same call as the change (D9); if it did not, the
    /// receipt is recorded as uncertain, never as completed.</summary>
    public bool FinishTransfer(TransferReceipt receipt)
    {
        if (_core == null || receipt == null)
        {
            return false;
        }

        TransferReceipt recorded = receipt;
        if ((receipt.Outcome == TransferOutcome.Completed || receipt.Outcome == TransferOutcome.Partial)
            && _core.Ledger.TryGetTransfer(receipt.Request, out TransferRecord transfer)
            && (transfer.Intent.To.Place == CustodyPlace.Worker || transfer.Intent.From.Place == CustodyPlace.Worker))
        {
            WorkerBody? body = WorkerBody.FindLive(WorkerKey.Value);
            if (body == null || !body.LastChangePersisted)
            {
                recorded = new TransferReceipt(
                    receipt.Request, TransferOutcome.Uncertain, 0, receipt.RemainderAt,
                    receipt.Evidence + "; the worker's inventory change could not be written to his body");
            }
        }

        if (!_core.FinishTransfer(recorded, out string reason))
        {
            _log("Transfer result not recorded: " + reason);
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Death, reconciliation in session, helpers
    // ------------------------------------------------------------------

    /// <summary>A body died and dropped what it carried: the order's material
    /// is recorded as moved to Lost at that place, with the place as evidence,
    /// for the player to recover; its tools as returns in flight, for a person
    /// to settle once they have picked them up.</summary>
    internal void OnWorkerDied(WorkerBody body, IReadOnlyList<DroppedItem> dropped, Vector3 where)
    {
        if (_core == null || body == null || !string.Equals(body.Key, WorkerKey.Value, StringComparison.Ordinal))
        {
            return;
        }

        string place = string.Format(CultureInfo.InvariantCulture, "died at ({0:0}, {1:0}, {2:0})", where.x, where.y, where.z);
        var lost = new CustodyLocation(CustodyPlace.Lost, place, _epoch);

        foreach (DroppedItem item in dropped)
        {
            if (string.IsNullOrEmpty(item.PrefabName))
            {
                continue;
            }

            var material = new MaterialItem(item.PrefabName, item.Quality, item.Variant);
            int remaining = item.Count;
            foreach (CollectionOrderRecord order in _core.Ledger.Orders)
            {
                int held = _core.Ledger.HoldingAt(order.Order, WorkerLocation, material);
                int units = Math.Min(held, remaining);
                if (units < 1)
                {
                    continue;
                }

                var intent = new TransferIntent(
                    CustodyIds.Mint(order.Order, "died", _core.Ledger.Revision), order.Order, WorkerLocation, lost,
                    material, units, _core.Ledger.Revision);

                // Written through the ordinary gates. When work is not
                // authorised now, or the record cannot take it, the note is not
                // written; the body is gone either way, so the next
                // reconciliation reports the shortfall and a person records it.
                if (_core.BeginTransfer(intent, out string reason) == TransferOutcome.Unspecified)
                {
                    _core.FinishTransfer(
                        new TransferReceipt(intent.Request, TransferOutcome.Completed, units, WorkerLocation, place), out reason);
                    remaining -= units;
                }
                else
                {
                    _log("A dying worker's " + material + " could not be recorded as dropped: " + reason);
                }
            }
        }

        RecordDroppedTools(dropped, place);

        _log("The worker died " + place + ". Everything he carried is on the ground there; tools he held are " +
            "recorded as unsettled until you pick them up and answer cf_settle resolve <request> mine.");
    }

    /// <summary>A tool he dropped is recorded as a return in flight, with where
    /// it fell: the replay reports it and a person answers "mine" once they
    /// have it. Never settled by the machine — it is on the ground, in nobody's
    /// hands.</summary>
    private void RecordDroppedTools(IReadOnlyList<DroppedItem> dropped, string place)
    {
        if (!_records.TryOpen(out SettlementRegister _, out SettlementJournal journal) || journal.IsReadOnly)
        {
            return;
        }

        TheConcernedCat.Settlement.Tools.ToolLedger tools = journal.Replay().Tools;
        JournalStamp? stamp = Stamp();
        foreach (DroppedItem item in dropped)
        {
            if (!ToolClassifier.TryDescribe(item.Item, out TheConcernedCat.Settlement.Tools.ToolSpecimen specimen))
            {
                continue;
            }

            foreach (TheConcernedCat.Settlement.Tools.ToolHolding holding in tools.HeldBy(ToolWorker))
            {
                if (holding.Tool.Kind != specimen.Kind
                    || !string.Equals(holding.Tool.ItemKey, specimen.ItemKey, StringComparison.Ordinal))
                {
                    continue;
                }

                // The same gate as every world-effect row: a note written while
                // a world-save marker is owed would later sit before that marker
                // and read as part of a save it is not in.
                if (_core == null || !_core.IsWritable)
                {
                    _log("The dropped " + specimen.Kind + " (request " + holding.Transaction.Value + ") was not noted: the " +
                        "record is not taking new work now (" + (_core == null ? "closed" : _core.WriteBlock.ToString()) +
                        "). The record still says he holds it.");
                    break;
                }

                int before = journal.Entries.Count;
                journal.Append(
                    JournalEntryKind.ToolReturned, default, holding.Transaction, worker: holding.Worker, tool: holding.Tool,
                    worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default, returnIntent: true,
                    note: "dropped when he " + place);
                if (!SettlementCustodyJournal.TryPersist(_records.TryPersistJournal) && journal.TryDiscardUnsaved(before))
                {
                    _log("The dropped " + specimen.Kind + " could not be noted in the record.");
                }

                tools.MarkUncertain(holding.Transaction);
                break;
            }
        }
    }

    /// <summary>In-session reconciliation over what can be observed now.
    /// </summary>
    internal ReconciliationReport? Reconcile(bool ordersWereRunning)
    {
        return _core == null
            ? null
            : CustodyReconciler.Reconcile(_core.Ledger, new LiveObserver(this), ordersWereRunning);
    }

    internal Vector3? WorkerPosition()
    {
        WorkerBody? body = WorkerBody.FindLive(WorkerKey.Value);
        return body == null ? (Vector3?)null : body.transform.position;
    }

    internal JournalStamp? Stamp()
    {
        ZNet net = ZNet.instance;
        return net == null || _epoch == Guid.Empty ? (JournalStamp?)null : new JournalStamp(net.GetTimeSeconds(), _epoch);
    }

    private bool AuthorityAllows(out CollectionAttentionReason refusal)
    {
        WorkAuthorityVerdict verdict;
        try
        {
            verdict = _authority();
        }
        catch (Exception)
        {
            verdict = WorkAuthorityVerdict.Unspecified;
        }

        switch (verdict)
        {
            case WorkAuthorityVerdict.Granted:
                refusal = CollectionAttentionReason.Unspecified;
                return true;

            case WorkAuthorityVerdict.OtherPeersConnected:
                refusal = CollectionAttentionReason.OtherPeersConnected;
                return false;

            default:
                refusal = CollectionAttentionReason.NoAuthority;
                return false;
        }
    }

    /// <summary>At load: the worker as stored in his body's object, loaded or
    /// not; carts and chests from before the load are not observable.</summary>
    private sealed class StoredObserver : ICustodyObserver
    {
        private readonly ForemanCustodyRuntime _runtime;

        public StoredObserver(ForemanCustodyRuntime runtime)
        {
            _runtime = runtime;
        }

        public int? Observe(CustodyLocation location, MaterialItem item)
        {
            if (location.Place != CustodyPlace.Worker
                || !string.Equals(location.Key, _runtime.WorkerKey.Value, StringComparison.Ordinal)
                || _runtime._census == null
                || _runtime._census.Bodies.Count != 1)
            {
                return null;
            }

            return WorldCustodyObjects.CountStored(_runtime._census.Bodies[0], item);
        }
    }

    /// <summary>In session: the live worker body, and a cart from this provider
    /// epoch when it is loaded.</summary>
    private sealed class LiveObserver : ICustodyObserver
    {
        private readonly ForemanCustodyRuntime _runtime;

        public LiveObserver(ForemanCustodyRuntime runtime)
        {
            _runtime = runtime;
        }

        public int? Observe(CustodyLocation location, MaterialItem item)
        {
            switch (location.Place)
            {
                case CustodyPlace.Worker:
                {
                    WorkerBody? body = WorkerBody.FindLive(location.Key);
                    return body == null || body.Inventory == null ? (int?)null : EngineInventoryPort.CountIn(body.Inventory, item);
                }

                case CustodyPlace.Cart:
                {
                    Vagon? cart = WorldCustodyObjects.FindCart(location.Key);
                    Inventory? inventory = cart == null || cart.m_container == null ? null : cart.m_container.GetInventory();
                    if (inventory == null || _runtime._core == null
                        || !_runtime._core.Ledger.TryGetCartBaseline(location.Key, location.WorldLoadEpoch, out CartBaseline baseline))
                    {
                        return null;
                    }

                    return Math.Max(0, EngineInventoryPort.CountIn(inventory, item) - baseline.CountOf(item));
                }

                default:
                    return null;
            }
        }
    }

    private sealed class EmptyView : IMaterialCustodyView
    {
        internal static readonly EmptyView Instance = new EmptyView();

        public int Revision => 0;

        public int CountAt(OrderId order, CustodyPlace place, CollectedResource resource) => 0;

        public ResourceProgress ProgressFor(CollectionOrderDefinition order, CollectedResource resource) =>
            new ResourceProgress(resource, 0);

        public bool HasUncertainTransfer(OrderId order) => false;
    }

    /// <summary>Before a world's custody is open, every transfer is refused
    /// without touching anything.</summary>
    private sealed class RefusingExecutor : ITransferExecutor
    {
        internal static readonly RefusingExecutor Instance = new RefusingExecutor();

        public TransferReceipt Execute(TransferIntent intent, IInventoryPort from, IInventoryPort to) =>
            new TransferReceipt(intent.Request, TransferOutcome.Refused, 0, intent.From, "custody is not open for this world");
    }
}
