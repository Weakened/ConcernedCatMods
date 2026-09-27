using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>Approval and withdrawal use the settlement journal's atomic writer,
/// scope, order IDs and save lineage. Progress is always an observation. The NPC
/// library's plan journal requires a role codec and a separate file; it supplies
/// no settlement marker or source binding, so it is not a second ledger here.</summary>
internal sealed class BuildOrderJournal
{
    private readonly CustodyCore _core;
    private readonly WorkerId _worker;
    private readonly Func<bool> _bindingValid;
    private string _failure = string.Empty;
    private long _checkedTransitions = -1;

    internal BuildOrderJournal(CustodyCore core, WorkerId worker, Func<bool> bindingValid)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _worker = worker;
        _bindingValid = bindingValid ?? throw new ArgumentNullException(nameof(bindingValid));
        Read();
    }

    internal Guid Epoch => _core.Load.LoadEpoch;
    internal BuildOrderRecordedRow? Current { get; private set; }
    internal bool IsCancelled => Current?.Transition == OrderTransition.Cancel;
    internal IEnumerable<Reservation> Reservations => _core.BuildMaterials.Ledger.Reservations;

    internal string Refusal
    {
        get
        {
            RefreshTransitions();
            if (_failure.Length != 0) return _failure;
            if (_core.Journal.Journal.IsReadOnly) return "RecordUnreadable: the settlement journal needs repair.";
            if (!_bindingValid()) return "WorkerOrSettlementUnavailable: the recorded settlement and recruited worker must be available.";
            if (_core.BuildMaterials.Ledger.HasUncertainCustody || _core.LoadReplay.MaterialRepairs.Count != 0)
                return "CustodyNeedsReconciliation: an interrupted or rolled-back material operation remains uncertain; run cf_settle reconcile.";
            foreach (Reservation held in Reservations)
            {
                if (held.State == ReservationState.Held && held.ContainerEpoch != Epoch.ToString("N"))
                    return "SourceIdentityStale: request " + held.Request.Value + " retains its material from " +
                        held.Container + " (load " + held.ContainerEpoch + "). The exact source cannot be proved in this load; " +
                        "nothing is placed or refunded to a newly marked chest. Run cf_settle reconcile.";
            }
            return string.Empty;
        }
    }

    internal bool HasHeld
    {
        get
        {
            foreach (Reservation held in Reservations)
                if (held.State == ReservationState.Held || held.State == ReservationState.Uncertain) return true;
            return false;
        }
    }

    internal string DescribeCustody()
    {
        var spent = new MaterialTally();
        var returned = new MaterialTally();
        var held = new MaterialTally();
        var uncertain = new MaterialTally();
        foreach (Reservation reservation in Reservations)
        {
            if (Current == null || !reservation.Order.Equals(Current.Order)) continue;
            MaterialTally total = reservation.State == ReservationState.Committed ? spent :
                reservation.State == ReservationState.Refunded ? returned :
                reservation.State == ReservationState.Held ? held : uncertain;
            foreach (MaterialStack stack in reservation.Stacks) total.Add(stack.Item, stack.Count);
        }
        return "Recorded build material: spent at placement: " + spent.Describe() +
            "; returned to recorded sources: " + returned.Describe() +
            "; held: " + held.Describe() + "; uncertain: " + uncertain.Describe() + ".";
    }

    internal bool TryApprove(ShelterPlan plan, out string refusal)
    {
        refusal = Refusal;
        if (refusal.Length != 0) return false;
        if (Current != null && (!IsCancelled || HasHeld))
        {
            refusal = "PreviousOrderUnsettled: withdraw the old order and reconcile its retained custody first.";
            return false;
        }
        if (!plan.IsPlanned || !_core.HasAuthority() || !_core.IsWritable)
        {
            refusal = "ApprovalRefused: the plan, authority and writable settlement record are required.";
            return false;
        }
        var row = new BuildOrderRecordedRow(new OrderId("build-" + Guid.NewGuid().ToString("N")),
            _worker, OrderTransition.Approve, Encode(plan));
        if (!_core.Journal.TryRecord(row))
        {
            refusal = "ApprovalNotDurable: the approval could not be recorded; nothing is authorised.";
            return false;
        }
        Current = row;
        return true;
    }

    internal bool TryCancel(out string refusal)
    {
        RefreshTransitions();
        refusal = string.Empty;
        if (Current == null || IsCancelled) return true;
        // Withdrawing authority is safe even when material cannot be reconciled.
        // Persist it before the desk changes, so reopening never restores consent.
        if (!_core.HasAuthority() || !_core.IsWritable || _failure.Length != 0)
        {
            refusal = "CancellationNotDurable: authority or the record is unavailable; custody is retained. " + _failure;
            return false;
        }
        var row = new BuildOrderRecordedRow(Current.Order, Current.Worker, OrderTransition.Cancel, Current.Payload);
        if (!_core.Journal.TryRecord(row))
        {
            refusal = "CancellationNotDurable: withdrawal was not saved; custody is retained.";
            return false;
        }
        Current = row;
        return true;
    }

    internal bool Validate(ShelterPlan plan, WorkerId worker, IPieceSight sight, out string refusal)
    {
        refusal = Refusal;
        if (refusal.Length != 0) return false;
        if (!plan.IsPlanned)
        {
            refusal = "BlueprintUnavailable: the approved plan cannot be priced in this world yet. " + plan.Refusal;
            return false;
        }
        if (Current == null || !Current.Worker.Equals(worker) || Encode(plan) != Current.Payload)
            return Fail("ApprovedPayloadChanged: worker, footprint, blueprint order or real recipe differs from the approved record.", out refusal);

        foreach (Reservation held in Reservations)
        {
            if (!held.Order.Equals(Current.Order)) continue;
            CostedPiece? match = null;
            foreach (CostedPiece piece in plan.Pieces)
                if (RequestFor(piece).Equals(held.Request)) { match = piece; break; }
            if (!match.HasValue || !SameCost(held, match.Value))
                return Fail("ReservationPayloadChanged: a build request has no exact approved piece and cost.", out refusal);
            // A cancelled order never places again. Damage to a paid piece must
            // not prevent returning unrelated, proven unspent custody. Rollback
            // and incomplete material operations still refuse above.
            if (IsCancelled || held.State != ReservationState.Committed) continue;
            PiecePlacement placement = match.Value.Placement;
            PieceSighting seen = sight.Look(in placement);
            if (seen == PieceSighting.Unknown)
            {
                refusal = "StandingWorldUnavailable: paid pieces must be reread in loaded ground before resuming.";
                return false;
            }
            if (seen != PieceSighting.Standing)
            {
                refusal = "SavedPieceMissing: a paid piece is absent or ambiguous in the loaded world; " +
                    "no automatic rebuild. Cancel to return proven unspent custody, or inspect the site.";
                return false;
            }
        }
        return true;
    }

    internal RequestId RequestFor(CostedPiece piece) =>
        CustodyIds.Mint(Current!.Order, "p-" + piece.Key, 0);

    internal static bool SameCost(Reservation reservation, CostedPiece piece)
    {
        var expected = new MaterialTally().Add(piece.Recipe);
        var actual = new MaterialTally();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (MaterialStack stack in reservation.Stacks)
        {
            if (!keys.Add(stack.Item)) return false;
            actual.Add(stack.Item, stack.Count);
        }
        return expected.Missing(actual).IsEmpty && actual.Missing(expected).IsEmpty;
    }

    private void Read()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long previous = -1;
        foreach (JournalEntry entry in _core.Journal.Journal.Entries)
        {
            if (entry.Sequence <= previous) _failure = "RecordAmbiguous: duplicate or reordered journal sequence.";
            previous = entry.Sequence;
            if (!(entry.Custody is BuildOrderRecordedRow row))
            {
                ObserveWithdrawal(entry);
                continue;
            }
            if (!row.Worker.Equals(_worker) || !TryMarker(row.Payload, out _))
                _failure = "LegacyOrCorruptOrder: the approved worker or versioned marker cannot be read.";
            if (row.Transition == OrderTransition.Approve)
            {
                if (!seen.Add(row.Order.Value) || (Current != null && !IsCancelled))
                    _failure = "RecordAmbiguous: duplicate approval or overlapping build orders.";
            }
            else if (Current == null || IsCancelled || !row.Order.Equals(Current.Order) ||
                !row.Worker.Equals(Current.Worker) || row.Payload != Current.Payload)
                _failure = "RecordAmbiguous: withdrawal does not match its approved payload.";
            Current = row;
            if (entry.LoadEpoch != Epoch && entry.WorldTime > _core.Load.LoadedWorldTime)
                _failure = "WorldSaveRollback: the build order is newer than the loaded world; nothing is resumed or refunded.";
        }
        foreach (Reservation held in Reservations)
        {
            if (!seen.Contains(held.Order.Value))
                _failure = "LegacyOrCorruptOrder: material exists without a durable approved marker; run cf_settle reconcile.";
            else if (Current != null && !held.Order.Equals(Current.Order) &&
                (held.State == ReservationState.Held || held.State == ReservationState.Uncertain))
                _failure = "PreviousOrderUnsettled: an older build still owns material; run cf_settle reconcile.";
        }
    }

    internal void RefreshTransitions()
    {
        SettlementJournal record = _core.Journal.Journal;
        if (_checkedTransitions == record.NextSequence) return;
        _checkedTransitions = record.NextSequence;
        foreach (JournalEntry entry in record.Entries) ObserveWithdrawal(entry);
    }

    private void ObserveWithdrawal(JournalEntry entry)
    {
        // The existing register cascade can withdraw an order when its area or
        // source is cleared. Its order-only row is consent withdrawal too; a
        // material refund intent (which carries a request) is not.
        if (Current == null || entry.Kind != JournalEntryKind.OrderTransition ||
            !entry.Request.IsEmpty || !entry.Order.Equals(Current.Order)) return;
        if (entry.Transition == OrderTransition.Cancel)
            Current = new BuildOrderRecordedRow(Current.Order, Current.Worker, OrderTransition.Cancel, Current.Payload);
        if (entry.Transition == OrderTransition.FlagForRepair)
            _failure = "OrderNeedsReconciliation: the settlement record flagged this build for repair.";
    }

    private bool Fail(string reason, out string refusal)
    {
        _failure = refusal = reason;
        return false;
    }

    // Exact ordered payload, including round-trip marker coordinates, stable
    // piece indices and actual costs. Reconstruct only the marker; regenerate
    // the approved plan and compare ALL bytes before accepting it. Reordered,
    // duplicated or changed piece payloads cannot acquire authority by parsing.
    internal static string Encode(ShelterPlan plan)
    {
        var text = new StringBuilder("shelter/1|");
        text.Append(Point(plan.Marker.At)).Append('|').Append(Number(plan.Marker.Yaw));
        foreach (CostedPiece piece in plan.Pieces)
        {
            text.Append('\n').Append(piece.Key).Append('|').Append(Uri.EscapeDataString(piece.Placement.Piece.Prefab))
                .Append('|').Append((int)piece.Phase).Append('|').Append(Point(piece.Placement.At))
                .Append('|').Append(Number(piece.Placement.Yaw));
            var costs = new List<PieceCost>(new MaterialTally().Add(piece.Recipe).Lines);
            costs.Sort((a, b) => string.CompareOrdinal(a.Item, b.Item));
            foreach (PieceCost cost in costs)
                text.Append('|').Append(Uri.EscapeDataString(cost.Item)).Append('=').Append(cost.Amount.ToString(CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    internal static bool TryMarker(string payload, out BuildOrderMarker marker)
    {
        marker = default;
        string[] first = payload.Split('\n')[0].Split('|');
        if (first.Length != 5 || first[0] != "shelter/1" ||
            !Parse(first[1], out float x) || !Parse(first[2], out float y) ||
            !Parse(first[3], out float z) || !Parse(first[4], out float yaw) || yaw < 0 || yaw >= 360) return false;
        marker = BuildOrderMarker.Proposed(BuildOrderKind.Shelter, new SitePoint(x, y, z), yaw).Confirm();
        return marker.IsConfirmed;
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Point(SitePoint at) => Number(at.X) + "|" + Number(at.Y) + "|" + Number(at.Z);
    private static bool Parse(string text, out float value) => float.TryParse(text, NumberStyles.Float,
        CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
}
