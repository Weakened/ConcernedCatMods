using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>Production writer for the reservation lane. Each callback is one
/// synchronous, measured world operation. Intention reaches disk first; a false
/// answer, exception or missing receipt leaves a named repair, never a retry of
/// a possibly completed movement. The store and all rows remain schema v3.</summary>
internal sealed class BuildMaterialCustody
{
    private readonly SettlementCustodyJournal _journal;
    private readonly Func<bool> _mayWrite;
    private readonly Func<bool> _authority;
    private readonly List<string> _faults = new List<string>();
    private readonly CustodyLedger _ledger;
    private int _checkedLegacyCount;
    private bool _busy;

    public BuildMaterialCustody(SettlementCustodyJournal journal, CustodyLedger ledger,
        Func<bool> mayWrite, Func<bool> authority, IEnumerable<string> repairs)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _checkedLegacyCount = journal.Journal.Entries.Count;
        _mayWrite = mayWrite ?? throw new ArgumentNullException(nameof(mayWrite));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _faults.AddRange(repairs);
    }

    public CustodyLedger Ledger
    {
        get { RefreshLegacyRefunds(); return _ledger; }
    }

    /// <summary>The legacy designation cascade writes refunds outside this
    /// writer. Refresh those holdings after persistence so the live gate and
    /// reconcile view agree with replay without a reload. Production holdings
    /// and uncertainty are never settled by this record-only path.</summary>
    private void RefreshLegacyRefunds()
    {
        SettlementJournal journal = _journal.Journal;
        if (_busy || journal.IsDirty || journal.Entries.Count == _checkedLegacyCount) return;
        _checkedLegacyCount = journal.Entries.Count;
        ReplayResult? replay = null;
        foreach (Reservation held in _ledger.Reservations)
        {
            if (held.State != ReservationState.Held || _ledger.RequiresInventoryReceipt(held.Request)) continue;
            replay ??= journal.Replay();
            if (!replay.Ledger.RequiresInventoryReceipt(held.Request) &&
                replay.Ledger.TryGet(held.Request, out Reservation recorded) &&
                held.SamePayloadAs(recorded) && recorded.State == ReservationState.Refunded)
                _ledger.Refund(held.Request);
        }
    }

    public bool NeedsRepair
    {
        get
        {
            if (_faults.Count != 0 || Ledger.HasUncertainCustody) return true;
            foreach (Reservation reservation in Ledger.Reservations)
            {
                if (reservation.State == ReservationState.Held && !Current(reservation)) return true;
            }

            return false;
        }
    }

    public IReadOnlyList<string> Reconcile()
    {
        var lines = new List<string>(_faults);
        foreach (Reservation reservation in Ledger.Reservations)
        {
            if (reservation.State != ReservationState.Held && reservation.State != ReservationState.Uncertain) continue;
            lines.Add("Build order \"" + reservation.Order.Value + "\", request \"" + reservation.Request.Value +
                "\": " + reservation.State + " " + string.Join(", ", reservation.Stacks) +
                " from container \"" + reservation.Container + "\" (load " + (reservation.ContainerEpoch ?? "unknown") +
                "). " + (reservation.State == ReservationState.Uncertain
                    ? "Repair required: compare the source, Thorstein's inventory and the piece; nothing is replayed or refunded on a guess."
                    : Current(reservation)
                        ? "Held for this build; only this recorded source may receive a refund."
                        : "Repair required: the build marker and source binding have not been recovered in this load. Nothing is moved or rebound automatically."));
        }

        return lines;
    }

    public CustodyOutcome Reserve(Reservation reservation, Func<bool> draw, out string failure) =>
        Execute(reservation, JournalEntryKind.Reserved, draw, out failure);

    public CustodyOutcome Commit(Reservation reservation, Func<bool> placeAndPay, out string failure) =>
        Execute(reservation, JournalEntryKind.CommitFinished, placeAndPay, out failure);

    public CustodyOutcome Refund(Reservation reservation, Func<bool> putBack, out string failure) =>
        Execute(reservation, JournalEntryKind.Refunded, putBack, out failure);

    private CustodyOutcome Execute(Reservation reservation, JournalEntryKind receipt, Func<bool> effect, out string failure)
    {
        if (reservation == null) throw new ArgumentNullException(nameof(reservation));
        if (effect == null) throw new ArgumentNullException(nameof(effect));
        failure = string.Empty;
        if (_busy) return Reject("a build material operation is already in flight", out failure);
        bool exists = Ledger.TryGet(reservation.Request, out Reservation recorded);
        if (exists && !recorded.SamePayloadAs(reservation))
            return Reject("that request already names a different order, source or cost", out failure);
        if (exists && recorded.State == ReservationState.Uncertain)
            return Reject("that build request is uncertain; run cf_settle reconcile", out failure);

        ReservationState target = receipt == JournalEntryKind.CommitFinished ? ReservationState.Committed : ReservationState.Refunded;
        if (exists && (receipt == JournalEntryKind.Reserved || recorded.State == target))
            return CustodyOutcome.AlreadySatisfied;
        if (receipt != JournalEntryKind.Reserved && (!exists || recorded.State != ReservationState.Held))
            return Reject("that request has no held build reservation", out failure);

        try
        {
            if (NeedsRepair || !_mayWrite() || !_authority() || !Current(reservation))
                return Reject("build custody is not writable or authorised, or needs repair; run cf_settle reconcile", out failure);
        }
        catch (Exception)
        {
            return Reject("build custody authority could not be established", out failure);
        }

        _busy = true;
        try
        {
            JournalEntryKind intent = receipt == JournalEntryKind.CommitFinished
                ? JournalEntryKind.CommitStarted : JournalEntryKind.OrderTransition;
            OrderTransition transition = receipt == JournalEntryKind.Reserved ? OrderTransition.Reserve : OrderTransition.Cancel;
            if (!_journal.TryRecordMaterial(intent, reservation, transition))
                return Reject("the build material intention could not be persisted; nothing was moved", out failure);

            if (!exists)
            {
                // Copy the payload: callers cannot settle the live ledger through
                // the Reservation object they submitted.
                Ledger.Reserve(new Reservation(reservation.Request, reservation.Order, reservation.Container,
                    reservation.Stacks, reservation.ContainerEpoch));
                Ledger.RequireInventoryReceipt(reservation.Request);
            }

            try
            {
                if (!_mayWrite() || !_authority())
                    return Uncertain(reservation, "authority or journal availability changed after the intent", out failure);
                if (!effect()) return Uncertain(reservation, "the measured operation did not complete", out failure);
            }
            catch (Exception exception)
            {
                return Uncertain(reservation, SafeFailure.Brief(exception), out failure);
            }

            if (!_journal.TryRecordMaterial(receipt, reservation))
                return Uncertain(reservation, "the outcome could not be persisted", out failure);

            if (receipt == JournalEntryKind.CommitFinished) Ledger.Commit(reservation.Request);
            if (receipt == JournalEntryKind.Refunded) Ledger.Refund(reservation.Request);
            return CustodyOutcome.Applied;
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Current(Reservation reservation) =>
        string.Equals(reservation.ContainerEpoch, _journal.LoadEpoch.ToString("N"), StringComparison.Ordinal);

    private CustodyOutcome Uncertain(Reservation reservation, string reason, out string failure)
    {
        Ledger.MarkUncertain(reservation.Request);
        failure = "Build order " + reservation.Order.Value + ", request " + reservation.Request.Value +
            " needs repair: " + reason + ". Nothing more is moved; run cf_settle reconcile.";
        _faults.Add(failure);
        return CustodyOutcome.Rejected;
    }

    private static CustodyOutcome Reject(string reason, out string failure)
    {
        failure = reason;
        return CustodyOutcome.Rejected;
    }
}
