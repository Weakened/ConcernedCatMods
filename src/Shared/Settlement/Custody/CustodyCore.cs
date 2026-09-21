using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>Why the custody runtime will not take a new custody write now.
/// </summary>
internal enum CustodyWriteBlock
{
    Unspecified = 0,

    /// <summary>Nothing blocks it.</summary>
    None = 1,

    /// <summary>The record could not be fully read, or belongs to a newer
    /// build.</summary>
    RecordReadOnly = 2,

    /// <summary>The load's restatement of which world save was loaded has not
    /// reached disk. Writing anything before it would let a later replay read
    /// rolled-back rows as confirmed.</summary>
    RestatementOwed = 3,

    /// <summary>A world save happened and its marker has not reached disk. A
    /// row written now would sit between the save and its marker.</summary>
    MarkerOwed = 4,
}

/// <summary>The game-free heart of Foreman's custody runtime: one world load's
/// ledger, executor and journal, and the world-save marker hook.
///
/// The Foreman adapter wraps this with the engine: it supplies the journal's
/// persistence, the world clock, the authority answer and the inventory ports.
/// Everything that decides — ordering, idempotence, what a crash leaves, what a
/// save confirms — is here, where a test can reach it.
///
/// <b>The in-memory state never claims more than the disk.</b> Every change is
/// applied to the ledger only after its row is persisted; a row that could not
/// be persisted is removed, and a receipt that could not be persisted leaves
/// its transfer uncertain in memory, which is exactly what the next load will
/// conclude from the record.</summary>
internal sealed class CustodyCore
{
    private readonly Func<bool> _authority;
    private double? _owedMarkerTime;

    private CustodyCore(
        SettlementCustodyJournal journal, Func<bool> authority, ReplayResult loadReplay, WorldLoad load)
    {
        Journal = journal;
        _authority = authority;
        LoadReplay = loadReplay;
        Load = load;
        Ledger = loadReplay.Custody;
        BuildMaterials = new BuildMaterialCustody(journal, loadReplay.Ledger,
            () => IsWritable, HasAuthority, loadReplay.MaterialRepairs);
        TopGeneration = loadReplay.Timeline?.TopGeneration ?? 0;
        Executor = new TransferExecutor(Ledger, journal, authority, () => WriteBlock == CustodyWriteBlock.None);
    }

    /// <summary>Opens custody for one world load: replays the record with the
    /// world-save marker rule applied for this load, and writes the load's
    /// restatement when the rule changed what the record means.</summary>
    public static CustodyCore Open(
        SettlementJournal journal, Func<bool> persist, Func<double> worldTime, WorldLoad load, Func<bool> authority)
    {
        if (journal == null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        var writer = new SettlementCustodyJournal(journal, persist, worldTime, load.LoadEpoch);
        ReplayResult replay = journal.Replay(load);
        var core = new CustodyCore(writer, authority ?? (() => false), replay, load);

        if (replay.Timeline?.RestatementGeneration != null)
        {
            core.RestatementGeneration = replay.Timeline.RestatementGeneration;
            core.RecordRestatement();
        }

        return core;
    }

    public SettlementCustodyJournal Journal { get; }

    public WorldLoad Load { get; }

    /// <summary>The replay made at load, with this load's marker-rule decision:
    /// its repairs, its voided and ambiguous counts.</summary>
    public ReplayResult LoadReplay { get; }

    public MaterialCustodyLedger Ledger { get; }

    public BuildMaterialCustody BuildMaterials { get; }

    public TransferExecutor Executor { get; }

    /// <summary>The live lineage's last save generation.</summary>
    public int TopGeneration { get; private set; }

    /// <summary>Set while the load's restatement has not reached disk.</summary>
    public int? RestatementGeneration { get; private set; }

    public bool IsMarkerOwed => _owedMarkerTime.HasValue;

    public CustodyWriteBlock WriteBlock
    {
        get
        {
            if (Journal.Journal.IsReadOnly)
            {
                return CustodyWriteBlock.RecordReadOnly;
            }

            if (RestatementGeneration.HasValue)
            {
                return CustodyWriteBlock.RestatementOwed;
            }

            return _owedMarkerTime.HasValue ? CustodyWriteBlock.MarkerOwed : CustodyWriteBlock.None;
        }
    }

    /// <summary>A new custody write may be taken: the record is writable and
    /// nothing is owed to it. A person's answer is still taken while a transfer
    /// awaits one; that is not blocked here.</summary>
    public bool IsWritable
    {
        get
        {
            TryRecover();
            return WriteBlock == CustodyWriteBlock.None;
        }
    }

    public bool HasAuthority()
    {
        try
        {
            return _authority();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Retries whatever is owed to the record: the load restatement
    /// first, then a world-save marker. Cheap when nothing is owed.</summary>
    public void TryRecover()
    {
        if (Journal.Journal.IsReadOnly)
        {
            return;
        }

        if (RestatementGeneration.HasValue && !RecordRestatement())
        {
            return;
        }

        if (_owedMarkerTime.HasValue)
        {
            RecordMarker(_owedMarkerTime.Value);
        }
    }

    // ------------------------------------------------------------------
    // The world-save hook
    // ------------------------------------------------------------------

    /// <summary>Called at <c>ZNet.WorldSaveStarted</c>, on the main thread,
    /// before the snapshot. Writes a marker when rows exist that no save has
    /// confirmed yet; does nothing otherwise, so an idle settlement does not
    /// grow its record with every autosave.
    ///
    /// A marker that cannot be written is owed: new custody writes stop until it
    /// is, and it is written later with this save's time — the earliest save
    /// that holds the rows before it.</summary>
    public void OnWorldSaveStarted(double worldTime)
    {
        if (Journal.Journal.IsReadOnly)
        {
            return;
        }

        if (RestatementGeneration.HasValue && !RecordRestatement())
        {
            // Never a marker before the restatement: it would confirm rows the
            // load already decided the world does not hold. The next load
            // re-derives the same decision from the same record.
            return;
        }

        if (_owedMarkerTime.HasValue)
        {
            RecordMarker(_owedMarkerTime.Value);
            return;
        }

        SaveTimelineReport timeline = SaveTimeline.Classify(Journal.Journal.Entries);
        if (!timeline.HasUnsaved)
        {
            return;
        }

        TopGeneration = timeline.TopGeneration;
        if (!RecordMarker(worldTime))
        {
            _owedMarkerTime = worldTime;
        }
    }

    private bool RecordMarker(double worldTime)
    {
        var marker = new WorldSaveMarkerRow(TopGeneration + 1);
        if (!Journal.TryRecordAt(marker, worldTime))
        {
            return false;
        }

        Ledger.CountRecord();
        TopGeneration = marker.Generation;
        _owedMarkerTime = null;
        return true;
    }

    private bool RecordRestatement()
    {
        if (!RestatementGeneration.HasValue)
        {
            return true;
        }

        var restatement = new WorldSaveMarkerRow(RestatementGeneration.Value);
        if (!Journal.TryRecordAt(restatement, Load.LoadedWorldTime))
        {
            return false;
        }

        Ledger.CountRecord();
        TopGeneration = restatement.Generation;
        RestatementGeneration = null;
        return true;
    }

    // ------------------------------------------------------------------
    // Orders
    // ------------------------------------------------------------------

    /// <summary>Journals an accepted order (<c>CollectionAccepted</c>) before
    /// any work. Refuses a definition that fails its own shape check, one whose
    /// id is already used for something else, and a second active order for the
    /// same worker.</summary>
    public bool RecordAccepted(CollectionOrderDefinition order, out string reason)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        if (!Gate(out reason))
        {
            return false;
        }

        CollectionOrderRefusal shape = order.CheckShape();
        if (shape != CollectionOrderRefusal.Unspecified)
        {
            reason = "the order is not complete: " + shape;
            return false;
        }

        if (Ledger.TryGetOrder(order.Order, out CollectionOrderRecord existing))
        {
            bool same = CollectionOrders.SameDefinition(existing.Definition, order);
            reason = same ? string.Empty : "that order id is already used for a different order";
            return same;
        }

        foreach (CollectionOrderRecord other in Ledger.Orders)
        {
            if (other.Definition.Worker.Equals(order.Worker) && !CollectionOrderStates.IsTerminal(other.State))
            {
                reason = "order \"" + other.Order.Value + "\" is still active for this worker";
                return false;
            }
        }

        if (!Journal.TryRecord(new CollectionAcceptedRow(order)))
        {
            reason = "the order could not be written down";
            return false;
        }

        Ledger.CountRecord();
        Ledger.Accept(order);
        return true;
    }

    /// <summary>C2: the worker's non-terminal order after replay, to adopt
    /// Paused after a reload. Its scope and delivery still carry the previous
    /// load's epoch until the player confirms a rebind.</summary>
    public bool TryRecoverOrder(WorkerId worker, out CollectionOrderDefinition? order, out CollectionOrderState state)
    {
        if (!worker.IsEmpty && Ledger.TryGetActiveOrder(worker, out CollectionOrderRecord record))
        {
            order = record.Definition;
            state = record.State;
            return true;
        }

        order = null;
        state = CollectionOrderState.Unspecified;
        return false;
    }

    /// <summary>C2: journals a player-confirmed rebind (<c>CollectionRebound</c>):
    /// the scope re-snapshotted from the same source and the delivery chosen
    /// again, both in this world load. A person's act, so it needs authority;
    /// it changes no custody, so an owed marker does not block it.</summary>
    public bool RecordRebound(OrderId order, WorkScope scope, DeliveryTarget delivery, out string reason)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (!GateRecordOnly(out reason))
        {
            return false;
        }

        if (!HasAuthority())
        {
            reason = "work is not authorised here now";
            return false;
        }

        if (!Ledger.TryGetOrder(order, out CollectionOrderRecord record) || CollectionOrderStates.IsTerminal(record.State))
        {
            reason = "no active order " + order.Value;
            return false;
        }

        if (scope.WorldLoadEpoch != Load.LoadEpoch)
        {
            reason = "the work area must be snapshotted again in this world load";
            return false;
        }

        if (delivery.Kind == DeliveryKind.Container && delivery.WorldLoadEpoch != Load.LoadEpoch)
        {
            reason = "the chest must be selected again in this world load";
            return false;
        }

        if (scope.Source != record.Definition.Scope.Source)
        {
            reason = "a rebind re-snapshots the same kind of work area (" + record.Definition.Scope.Source + ")";
            return false;
        }

        if (delivery.Kind != record.Definition.Delivery.Kind)
        {
            reason = "a rebind keeps the same kind of delivery (" + record.Definition.Delivery.Kind + ")";
            return false;
        }

        var rebound = new CollectionOrderDefinition(
            order, record.Definition.Worker, record.Definition.Quotas, scope, delivery,
            record.Definition.Participation, record.Definition.IssuedByCharacter);
        if (CollectionOrders.SameDefinition(record.Definition, rebound))
        {
            reason = string.Empty;
            return true;
        }

        if (!Journal.TryRecord(new CollectionReboundRow(order, scope, delivery)))
        {
            reason = "the rebind could not be written down";
            return false;
        }

        Ledger.CountRecord();
        Ledger.Rebind(order, scope, delivery);
        reason = string.Empty;
        return true;
    }

    public bool RecordTransition(
        OrderId order, CollectionOrderState from, CollectionOrderState to, CollectionAttentionReason reason,
        out string refusal)
    {
        if (!Ledger.TryGetOrder(order, out CollectionOrderRecord record))
        {
            refusal = "no accepted order " + order.Value;
            return false;
        }

        bool refreshingReason = record.State == to
            && from == to
            && reason != CollectionAttentionReason.Unspecified
            && record.Reason != reason
            && (to == CollectionOrderState.Paused || to == CollectionOrderState.NeedsAttention);

        if (record.State == to && from == to && !refreshingReason)
        {
            refusal = string.Empty;
            return true;
        }

        if (record.State != from)
        {
            refusal = "order " + order.Value + " is " + record.State + ", not " + from;
            return false;
        }

        if (!refreshingReason && !CollectionOrderStates.CanTransition(from, to))
        {
            refusal = "an order cannot go from " + from + " to " + to;
            return false;
        }

        // A transition into Paused, NeedsAttention or Cancelled is how work
        // stops; it must be recordable even while authority is gone or a marker
        // is owed — but not before the load's restatement, and never over a
        // read-only record.
        bool stopping = to == CollectionOrderState.Paused
            || to == CollectionOrderState.NeedsAttention
            || to == CollectionOrderState.Cancelled;
        if (stopping ? !GateRecordOnly(out refusal) : !Gate(out refusal))
        {
            return false;
        }

        if (!Journal.TryRecord(new CollectionTransitionRow(order, from, to, reason)))
        {
            refusal = "the change could not be written down";
            return false;
        }

        Ledger.CountRecord();
        Ledger.Transition(order, from, to, reason);
        return true;
    }

    // ------------------------------------------------------------------
    // Carts
    // ------------------------------------------------------------------

    public bool RecordCartBaseline(
        OrderId order, string leaseId, string cartSessionKey, Guid providerEpoch, IReadOnlyList<ItemCount> counts,
        out string reason)
    {
        if (!Gate(out reason))
        {
            return false;
        }

        if (!Ledger.TryGetOrder(order, out CollectionOrderRecord _))
        {
            reason = "no accepted order " + order.Value;
            return false;
        }

        if (Ledger.TryGetBaseline(order, cartSessionKey, providerEpoch, out CartBaseline existing))
        {
            bool same = string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal);
            reason = same ? string.Empty : "a baseline for that cart is already recorded under another lease";
            return same;
        }

        if (!Journal.TryRecord(new CartBaselineRow(order, leaseId, cartSessionKey, providerEpoch, counts)))
        {
            reason = "the cart's existing cargo could not be written down";
            return false;
        }

        Ledger.CountRecord();
        Ledger.RecordBaseline(order, leaseId, cartSessionKey, providerEpoch, counts);
        return true;
    }

    // ------------------------------------------------------------------
    // Pickups
    // ------------------------------------------------------------------

    /// <summary>Journals and persists a pickup intent before the pick. Null when
    /// it could not be persisted or is not allowed now.</summary>
    public RequestId? BeginPickup(OrderId order, SourceKey source, out string reason)
    {
        if (!Gate(out reason))
        {
            return null;
        }

        if (!Ledger.TryGetOrder(order, out CollectionOrderRecord record)
            || CollectionOrderStates.IsTerminal(record.State))
        {
            reason = "no active order " + order.Value;
            return null;
        }

        RequestId pickup = CustodyIds.Mint(order, "pick", Ledger.Revision);
        if (!Journal.TryRecord(new PickupStartedRow(pickup, order, source)))
        {
            reason = "the pickup could not be written down, so nothing was picked";
            return null;
        }

        Ledger.CountRecord();
        Ledger.BeginPickup(pickup, order, source);
        return pickup;
    }

    /// <summary>Journals the pick's traced drops. Taken whenever the record is
    /// writable at all: the pick has happened, and its result must not be lost
    /// to a gate that closed in between.</summary>
    public bool FinishPickup(RequestId pickup, PickupResult result, out string reason)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (!Ledger.TryGetPickup(pickup, out PickupRecord record))
        {
            reason = "no recorded pickup " + pickup.Value;
            return false;
        }

        if (record.Result != null)
        {
            CustodyLedgerOutcome again = Ledger.FinishPickup(pickup, result);
            reason = again == CustodyLedgerOutcome.AlreadySatisfied ? string.Empty : "that pickup already has a different result";
            return again == CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (Journal.Journal.IsReadOnly)
        {
            reason = "the settlement's record cannot be written";
            Ledger.MarkPickupUncertain(pickup);
            return false;
        }

        if (!Journal.TryRecord(new PickupFinishedRow(pickup, record.Order, result)))
        {
            reason = "the pickup's result could not be written down";
            Ledger.MarkPickupUncertain(pickup);
            return false;
        }

        Ledger.CountRecord();
        Ledger.FinishPickup(pickup, result);
        reason = string.Empty;
        return true;
    }

    // ------------------------------------------------------------------
    // Transfers done in one engine call outside the executor
    // ------------------------------------------------------------------

    /// <summary>Step 1 and 2 of CONTRACTS.md §5.2 for a transfer whose engine
    /// half is one call the caller makes itself (taking a traced drop).
    /// <see cref="TransferOutcome.Unspecified"/> means "go ahead"; anything else
    /// means do not mutate.</summary>
    public TransferOutcome BeginTransfer(TransferIntent intent, out string reason)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (Ledger.TryGetTransfer(intent.Request, out TransferRecord _))
        {
            return Ledger.Check(intent, out reason);
        }

        if (!Gate(out reason))
        {
            return TransferOutcome.Refused;
        }

        TransferOutcome record = Ledger.Check(intent, out reason);
        if (record != TransferOutcome.Unspecified)
        {
            return record;
        }

        if (!Journal.TryRecord(new TransferStartedRow(intent)))
        {
            reason = "the intention could not be written down, so nothing may move";
            return TransferOutcome.Refused;
        }

        Ledger.CountRecord();
        Ledger.Begin(intent);
        reason = string.Empty;
        return TransferOutcome.Unspecified;
    }

    /// <summary>Step 6 for a transfer begun with <see cref="BeginTransfer"/>.
    /// A receipt that could not be persisted leaves the transfer uncertain in
    /// memory.</summary>
    public bool FinishTransfer(TransferReceipt receipt, out string reason)
    {
        if (receipt == null)
        {
            throw new ArgumentNullException(nameof(receipt));
        }

        if (!Ledger.TryGetTransfer(receipt.Request, out TransferRecord record))
        {
            reason = "no recorded intent " + receipt.Request.Value;
            return false;
        }

        if (record.Status != TransferStatus.Open)
        {
            CustodyLedgerOutcome again = Ledger.Finish(receipt);
            reason = again == CustodyLedgerOutcome.AlreadySatisfied ? string.Empty : "that transfer is already " + record.Status;
            return again == CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (!Journal.TryRecord(new TransferFinishedRow(record.Order, receipt)))
        {
            reason = "the result could not be written down";
            Ledger.MarkUncertain(receipt.Request, receipt.Evidence + "; the result could not be written down");
            return false;
        }

        Ledger.CountRecord();
        Ledger.Finish(receipt);
        reason = string.Empty;
        return true;
    }

    // ------------------------------------------------------------------
    // A person's answers
    // ------------------------------------------------------------------

    /// <summary>Records a person's answer to an uncertain or ambiguous transfer,
    /// or their acknowledgement of an uncertain pickup, and only then settles
    /// the ledger. Never taken without authority: #299's review found the
    /// resolve command writing the record with the runtime switched off.
    /// </summary>
    public CustodyLedgerOutcome Resolve(RequestId request, TransferSide side, int units, string note, out string message)
    {
        if (!GateRecordOnly(out message))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (!HasAuthority())
        {
            message = "work is not authorised here now, so no answer is recorded";
            return CustodyLedgerOutcome.Rejected;
        }

        OrderId order;
        if (Ledger.TryGetTransfer(request, out TransferRecord transfer))
        {
            if (transfer.Status == TransferStatus.Resolved)
            {
                message = "That transfer is already settled.";
                return CustodyLedgerOutcome.AlreadySatisfied;
            }

            if (!transfer.AwaitsResolution)
            {
                message = "That transfer is not waiting on an answer (it is " + transfer.Status + ").";
                return CustodyLedgerOutcome.Rejected;
            }

            if (side == TransferSide.Destination && (units < 1 || units > transfer.Intent.Count))
            {
                message = "Say how many arrived, from 1 to " + transfer.Intent.Count.ToString(CultureInfo.InvariantCulture) + ".";
                return CustodyLedgerOutcome.Rejected;
            }

            if (side == TransferSide.Destination
                && Ledger.HoldingAt(transfer.Order, transfer.Intent.From, transfer.Intent.Item) < units)
            {
                message = "The record does not hold that many at " + transfer.Intent.From + " to have moved.";
                return CustodyLedgerOutcome.Rejected;
            }

            order = transfer.Order;
        }
        else if (Ledger.TryGetPickup(request, out PickupRecord pickup))
        {
            if (!pickup.IsUncertain)
            {
                message = pickup.Acknowledged ? "That pickup is already settled." : "That pickup is not waiting on an answer.";
                return pickup.Acknowledged ? CustodyLedgerOutcome.AlreadySatisfied : CustodyLedgerOutcome.Rejected;
            }

            if (side != TransferSide.Source)
            {
                message = "A pickup is only ever settled as \"source\": nothing was granted from it.";
                return CustodyLedgerOutcome.Rejected;
            }

            order = pickup.Order;
        }
        else
        {
            message = "There is no record of that request.";
            return CustodyLedgerOutcome.Rejected;
        }

        int recordedUnits = side == TransferSide.Destination ? units : 0;
        if (!Journal.TryRecord(new TransferResolvedRow(request, order, side, recordedUnits, note)))
        {
            message = "That could not be written down, so nothing has been settled. Try again.";
            return CustodyLedgerOutcome.Rejected;
        }

        Ledger.CountRecord();
        CustodyLedgerOutcome applied = transfer != null
            ? Ledger.Resolve(request, side, recordedUnits)
            : Ledger.ResolvePickup(request);

        message = side == TransferSide.Source
            ? "Recorded: it did not move."
            : "Recorded: " + recordedUnits.ToString(CultureInfo.InvariantCulture) + " arrived.";
        return applied;
    }

    /// <summary>A person accepting observed loss (<c>LossRecorded</c>).</summary>
    public CustodyLedgerOutcome RecordLoss(
        OrderId order, CustodyLocation at, MaterialItem item, int count, string reason, out string message)
    {
        if (!GateRecordOnly(out message))
        {
            return CustodyLedgerOutcome.Rejected;
        }

        if (!HasAuthority())
        {
            message = "work is not authorised here now, so no loss is recorded";
            return CustodyLedgerOutcome.Rejected;
        }

        if (!Ledger.TryGetOrder(order, out CollectionOrderRecord _))
        {
            message = "There is no order " + order.Value + ".";
            return CustodyLedgerOutcome.Rejected;
        }

        int held = Ledger.HoldingAt(order, at, item);
        if (count < 1 || held < count)
        {
            message = "The record holds " + held.ToString(CultureInfo.InvariantCulture) + " " + item + " at " + at +
                " for that order, so " + count.ToString(CultureInfo.InvariantCulture) + " cannot be recorded as lost.";
            return CustodyLedgerOutcome.Rejected;
        }

        RequestId request = CustodyIds.Mint(order, "lost", Ledger.Revision);
        if (!Journal.TryRecord(new LossRecordedRow(request, order, at, item, count, reason)))
        {
            message = "That could not be written down, so nothing has been recorded as lost. Try again.";
            return CustodyLedgerOutcome.Rejected;
        }

        Ledger.CountRecord();
        CustodyLedgerOutcome applied = Ledger.RecordLoss(request, order, at, item, count);
        message = "Recorded: " + count.ToString(CultureInfo.InvariantCulture) + " " + item + " lost from " + at + ".";
        return applied;
    }

    /// <summary>Hold-for-player acknowledgement after a Worker → Player transfer.
    /// </summary>
    public bool RecordHandover(RequestId transfer, OrderId order, MaterialItem item, int count, out string reason)
    {
        if (!GateRecordOnly(out reason))
        {
            return false;
        }

        CustodyLedgerOutcome check = CheckHandover(transfer, order, item, count);
        if (check != CustodyLedgerOutcome.Applied)
        {
            reason = check == CustodyLedgerOutcome.AlreadySatisfied ? string.Empty : "no handover transfer matches that";
            return check == CustodyLedgerOutcome.AlreadySatisfied;
        }

        if (!Journal.TryRecord(new HandoverFinishedRow(transfer, order, item, count)))
        {
            reason = "the handover could not be written down";
            return false;
        }

        Ledger.CountRecord();
        Ledger.RecordHandover(transfer, order, item, count);
        return true;
    }

    private CustodyLedgerOutcome CheckHandover(RequestId transfer, OrderId order, MaterialItem item, int count)
    {
        if (!Ledger.TryGetTransfer(transfer, out TransferRecord record)
            || !record.Order.Equals(order)
            || record.Intent.To.Place != CustodyPlace.Player
            || !record.Intent.Item.Equals(item)
            || record.Applied < count)
        {
            return CustodyLedgerOutcome.Rejected;
        }

        return CustodyLedgerOutcome.Applied;
    }

    // ------------------------------------------------------------------
    // Gates
    // ------------------------------------------------------------------

    /// <summary>For new work: authority, a writable record, nothing owed.
    /// </summary>
    private bool Gate(out string reason)
    {
        TryRecover();

        switch (WriteBlock)
        {
            case CustodyWriteBlock.None:
                break;
            case CustodyWriteBlock.RecordReadOnly:
                reason = "the settlement's record could not be fully read, so nothing new is written to it";
                return false;
            case CustodyWriteBlock.RestatementOwed:
                reason = "the record has not yet noted which world save was loaded";
                return false;
            case CustodyWriteBlock.MarkerOwed:
                reason = "the last world save has not been noted in the record yet";
                return false;
            default:
                reason = "the record's state is unknown";
                return false;
        }

        if (!HasAuthority())
        {
            reason = "work is not authorised here now";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>For stopping work and a person's answers: a writable record and
    /// the load's restatement recorded. An owed marker does not block these —
    /// they are record-only rows the marker rule never voids.</summary>
    private bool GateRecordOnly(out string reason)
    {
        TryRecover();

        if (Journal.Journal.IsReadOnly)
        {
            reason = "the settlement's record could not be fully read, so nothing new is written to it";
            return false;
        }

        if (RestatementGeneration.HasValue)
        {
            reason = "the record has not yet noted which world save was loaded";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
