using System;
using System.Globalization;
using System.Text;
using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>An inventory that can take units straight out of another engine
/// inventory in one engine call, keeping the moved item instances.
///
/// <b>Why this exists beside <see cref="IInventoryPort.Add"/>.</b> A port's
/// <c>Add</c> has no item to add: an engine adapter implementing it could only
/// create new items from the prefab, and a created stack loses what the moved
/// one carried (world level, crafter, custom data — DATA-03 asks that metadata
/// survive). CONTRACTS.md §5.2 lets a deposit adapter perform steps 3 and 4 as
/// vanilla's own <c>MoveItemToThis</c>, which is add-then-remove inside one
/// call; this is that seam, for every engine-to-engine transfer. The executor
/// still measures both inventories before and after and classifies from those
/// deltas only, exactly as for the two-step path.</summary>
internal interface IInventoryMoveTarget
{
    /// <summary>True when <paramref name="source"/> is an engine inventory this
    /// target can move items out of.</summary>
    bool CanMoveFrom(IInventoryPort source);

    /// <summary>Moves up to <paramref name="count"/> units of
    /// <paramref name="item"/> from <paramref name="source"/>, adding to this
    /// inventory before removing from the source for every stack. Returns
    /// nothing the executor trusts: it counts.</summary>
    void MoveFrom(IInventoryPort source, MaterialItem item, int count);
}

/// <summary>Performs one transfer, in the order CONTRACTS.md §5.2 fixes, and
/// never out of it:
/// <list type="number">
/// <item><b>Check</b> — idempotence, authority, a writable journal, both ports
/// available, the record (order known, revision current, the source holding the
/// units for this order, no unresolved transfer), the source actually holding
/// them, and the destination accepting at least one. Nothing is mutated.</item>
/// <item><b>Persist the intent.</b> On failure: refused, nothing mutated, the
/// unsaved row discarded.</item>
/// <item><b>Add</b> to the destination: <c>min(count, canAccept)</c>.</item>
/// <item><b>Remove</b> from the source exactly what the destination actually
/// gained.</item>
/// <item><b>Classify</b> from the measured deltas of both inventories: equal
/// deltas are Completed or Partial (or Refused when both are zero); anything
/// else, or any fault during 3–4, is Uncertain with the evidence, and nothing is
/// compensated.</item>
/// <item><b>Persist the receipt.</b> On failure the row is discarded and the
/// transfer is uncertain in memory; the next load decides from the record and
/// the actual inventories.</item>
/// </list>
///
/// <b>Why add before remove.</b> A crash between 3 and 4 leaves a duplicate,
/// which the persisted intent and the counts expose, and a person resolves. The
/// reverse order would lose real items with no evidence left.</summary>
internal sealed class TransferExecutor : ITransferExecutor
{
    private readonly MaterialCustodyLedger _ledger;
    private readonly ICustodyJournal _journal;
    private readonly Func<bool> _authority;
    private readonly Func<bool> _mayWrite;

    public TransferExecutor(MaterialCustodyLedger ledger, ICustodyJournal journal, Func<bool> authority)
        : this(ledger, journal, authority, () => true)
    {
    }

    /// <param name="mayWrite">The runtime's own gate on new custody writes (a
    /// world-save marker owed, a load restatement not yet recorded).</param>
    public TransferExecutor(
        MaterialCustodyLedger ledger, ICustodyJournal journal, Func<bool> authority, Func<bool> mayWrite)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _mayWrite = mayWrite ?? throw new ArgumentNullException(nameof(mayWrite));
    }

    public TransferReceipt Execute(TransferIntent intent, IInventoryPort from, IInventoryPort to)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        // ------------------------------------------------------------------
        // 1. Check. Idempotence first: a transfer that already happened has
        // already happened, whatever authority says now.
        // ------------------------------------------------------------------
        if (_ledger.TryGetTransfer(intent.Request, out TransferRecord existing))
        {
            TransferOutcome answer = _ledger.Check(intent, out string recorded);
            int applied = answer == TransferOutcome.AlreadySatisfied ? existing.Applied : 0;
            return Receipt(intent, answer, applied, recorded);
        }

        if (!Safe(_authority))
        {
            return Refused(intent, "work is not authorised here now");
        }

        if (!_journal.IsWritable || !Safe(_mayWrite))
        {
            return Refused(intent, "the settlement's record cannot be written now");
        }

        if (from == null || to == null || !Available(from) || !Available(to))
        {
            return Refused(intent, "one of the two inventories is not available");
        }

        TransferOutcome record = _ledger.Check(intent, out string reason);
        if (record != TransferOutcome.Unspecified)
        {
            return Receipt(intent, record, 0, reason);
        }

        int? fromBefore = SafeCount(from, intent.Item);
        int? toBefore = SafeCount(to, intent.Item);
        if (!fromBefore.HasValue || !toBefore.HasValue)
        {
            return Refused(intent, "an inventory could not be counted");
        }

        if (fromBefore.Value < intent.Count)
        {
            return Refused(
                intent,
                from.Describe + " actually holds " + fromBefore.Value.ToString(CultureInfo.InvariantCulture) +
                " " + intent.Item + ", not " + intent.Count.ToString(CultureInfo.InvariantCulture));
        }

        int canAccept;
        try
        {
            canAccept = to.CanAccept(intent.Item, intent.Count);
        }
        catch (Exception exception)
        {
            return Refused(intent, "could not ask " + to.Describe + " for room: " + Brief(exception));
        }

        if (canAccept < 1)
        {
            return Refused(intent, to.Describe + " has no room for " + intent.Item);
        }

        // ------------------------------------------------------------------
        // 2. Persist the intent, before anything moves.
        // ------------------------------------------------------------------
        if (!_journal.TryRecord(new TransferStartedRow(intent)))
        {
            return Refused(intent, "the intention could not be written down, so nothing was moved");
        }

        _ledger.CountRecord();
        _ledger.Begin(intent);

        // ------------------------------------------------------------------
        // 3-4. Add to the destination, then remove from the source.
        // ------------------------------------------------------------------
        int requested = Math.Min(intent.Count, canAccept);
        int? addReturned = null;
        int? removeReturned = null;
        bool moved = false;
        Exception? fault = null;

        try
        {
            if (to is IInventoryMoveTarget mover && mover.CanMoveFrom(from))
            {
                moved = true;
                mover.MoveFrom(from, intent.Item, requested);
            }
            else
            {
                addReturned = to.Add(intent.Item, requested);

                // Remove exactly what the destination actually gained, measured,
                // never what Add said or what was asked for.
                int? afterAdd = SafeCount(to, intent.Item);
                if (!afterAdd.HasValue)
                {
                    throw new InvalidOperationException("the destination could not be counted after the add");
                }

                int gained = afterAdd.Value - toBefore.Value;
                if (gained > 0)
                {
                    removeReturned = from.Remove(intent.Item, gained);
                }
            }
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        // ------------------------------------------------------------------
        // 5. Classify from both inventories' actual deltas.
        // ------------------------------------------------------------------
        int? fromAfter = SafeCount(from, intent.Item);
        int? toAfter = SafeCount(to, intent.Item);

        string evidence = Evidence(
            intent, from, to, requested, fromBefore.Value, fromAfter, toBefore.Value, toAfter,
            moved, addReturned, removeReturned, fault);

        TransferOutcome outcome;
        int accepted = 0;

        if (fault == null && fromAfter.HasValue && toAfter.HasValue)
        {
            int gained = toAfter.Value - toBefore.Value;
            int lost = fromBefore.Value - fromAfter.Value;

            if (gained == lost && gained >= 0 && gained <= requested)
            {
                accepted = gained;
                outcome = gained == 0
                    ? TransferOutcome.Refused
                    : gained == intent.Count ? TransferOutcome.Completed : TransferOutcome.Partial;
            }
            else
            {
                outcome = TransferOutcome.Uncertain;
            }
        }
        else
        {
            outcome = TransferOutcome.Uncertain;
        }

        var receipt = new TransferReceipt(intent.Request, outcome, accepted, intent.From, evidence);

        // ------------------------------------------------------------------
        // 6. Persist the receipt.
        // ------------------------------------------------------------------
        if (!_journal.TryRecord(new TransferFinishedRow(intent.Order, receipt)))
        {
            string unrecorded = evidence + "; the result could not be written down";
            _ledger.MarkUncertain(intent.Request, unrecorded);
            return new TransferReceipt(intent.Request, TransferOutcome.Uncertain, 0, intent.From, unrecorded);
        }

        _ledger.CountRecord();
        _ledger.Finish(receipt);
        return receipt;
    }

    private static TransferReceipt Refused(TransferIntent intent, string reason) =>
        new TransferReceipt(intent.Request, TransferOutcome.Refused, 0, intent.From, reason);

    private static TransferReceipt Receipt(TransferIntent intent, TransferOutcome outcome, int accepted, string reason) =>
        new TransferReceipt(intent.Request, outcome, accepted, intent.From, reason);

    private static bool Safe(Func<bool> gate)
    {
        try
        {
            return gate();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Available(IInventoryPort port)
    {
        try
        {
            return port.IsAvailable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int? SafeCount(IInventoryPort port, MaterialItem item)
    {
        try
        {
            int count = port.Count(item);
            return count < 0 ? (int?)null : count;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Evidence(
        TransferIntent intent, IInventoryPort from, IInventoryPort to, int requested,
        int fromBefore, int? fromAfter, int toBefore, int? toAfter,
        bool moved, int? addReturned, int? removeReturned, Exception? fault)
    {
        var text = new StringBuilder();
        text.Append(SafeDescribe(from)).Append(' ').Append(intent.Item).Append(": ")
            .Append(fromBefore.ToString(CultureInfo.InvariantCulture)).Append(" -> ")
            .Append(fromAfter.HasValue ? fromAfter.Value.ToString(CultureInfo.InvariantCulture) : "?");
        text.Append("; ").Append(SafeDescribe(to)).Append(": ")
            .Append(toBefore.ToString(CultureInfo.InvariantCulture)).Append(" -> ")
            .Append(toAfter.HasValue ? toAfter.Value.ToString(CultureInfo.InvariantCulture) : "?");
        text.Append("; asked ").Append(requested.ToString(CultureInfo.InvariantCulture));

        if (moved)
        {
            text.Append(" (one engine move)");
        }

        if (addReturned.HasValue)
        {
            text.Append("; add said ").Append(addReturned.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (removeReturned.HasValue)
        {
            text.Append("; remove said ").Append(removeReturned.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (fault != null)
        {
            text.Append("; fault: ").Append(Brief(fault));
        }

        return text.ToString();
    }

    private static string SafeDescribe(IInventoryPort port)
    {
        try
        {
            return string.IsNullOrEmpty(port.Describe) ? "an inventory" : port.Describe;
        }
        catch (Exception)
        {
            return "an inventory";
        }
    }

    private static string Brief(Exception exception) => exception.GetType().Name + ": " + exception.Message;
}
