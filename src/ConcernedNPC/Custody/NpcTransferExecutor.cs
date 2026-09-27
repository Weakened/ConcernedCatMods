using System;
using System.Globalization;
using System.Text;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Performs one transfer, in the order that works, and never out of
/// it:
/// <list type="number">
/// <item><b>Check.</b> Idempotence, the role's authority, a writable record,
/// both ports available, the ledger's own answer, the source actually holding
/// the units, and the destination accepting at least one. Nothing is
/// mutated.</item>
/// <item><b>Persist the intent</b>, before anything moves. On failure: refused,
/// nothing mutated.</item>
/// <item><b>Add</b> to the destination: at most what it said it could take.
/// </item>
/// <item><b>Remove</b> from the source exactly what the destination actually
/// gained - <b>measured</b>, never what the engine's add returned and never
/// what was asked for.</item>
/// <item><b>Classify</b> from the measured deltas of both inventories. Equal
/// deltas are completed or partial, or refused when both are zero. Anything
/// else, or any fault during steps 3 and 4, is uncertain with the evidence, and
/// nothing is compensated.</item>
/// <item><b>Persist the receipt.</b> On failure the transfer is uncertain in
/// memory and the next load decides from the record and the actual
/// inventories.</item>
/// </list>
///
/// <b>Why add before remove.</b> A crash between 3 and 4 leaves a duplicate,
/// which the persisted intent and the counts expose and a person resolves. The
/// reverse order would lose real items with no evidence left. This asymmetry is
/// the single most important thing in this file: the two failures are not
/// equally bad, and the order chooses which one is possible.
///
/// <b>This is moved, not invented.</b> It is the shipped settlement executor
/// with its order, its measurement discipline and its refusal to compensate
/// intact; what changed is that the journal is a seam rather than a file
/// format, the identity types are this library's, and the shipped cart-shaped
/// concepts are gone.</summary>
internal sealed class NpcTransferExecutor
{
    private readonly NpcCustodyLedger _ledger;
    private readonly INpcCustodyJournal _journal;
    private readonly Func<bool> _authority;
    private readonly Func<bool> _mayWrite;

    internal NpcTransferExecutor(NpcCustodyLedger ledger, INpcCustodyJournal journal, Func<bool> authority)
        : this(ledger, journal, authority, AlwaysTrue)
    {
    }

    /// <param name="mayWrite">The role's own gate on new custody writes - a
    /// world-save marker owed, a restatement after a load not yet recorded.
    /// Separate from <paramref name="authority"/> because they refuse for
    /// different reasons and a player is owed the right one.</param>
    internal NpcTransferExecutor(
        NpcCustodyLedger ledger, INpcCustodyJournal journal, Func<bool> authority, Func<bool> mayWrite)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _mayWrite = mayWrite ?? throw new ArgumentNullException(nameof(mayWrite));
    }

    internal NpcTransferReceipt Execute(NpcTransferIntent intent, INpcInventoryPort from, INpcInventoryPort to)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        // 1. Check. Idempotence first: a transfer that already happened has
        // already happened, whatever authority says now. Refusing a recorded
        // transfer because work is no longer authorised would leave the record
        // and the world disagreeing for a reason unrelated to either.
        if (_ledger.TryGetTransfer(intent.Request, out NpcTransferRecord existing))
        {
            NpcTransferOutcome answer = _ledger.Check(intent, out string recorded);
            int applied = answer == NpcTransferOutcome.AlreadySatisfied ? existing.Applied : 0;
            return new NpcTransferReceipt(intent.Request, answer, applied, intent.From, recorded);
        }

        if (!Safe(_authority))
        {
            return Refused(intent, "work is not authorised here now");
        }

        if (!Writable() || !Safe(_mayWrite))
        {
            return Refused(intent, "the record cannot be written now");
        }

        if (from == null || to == null || !Available(from) || !Available(to))
        {
            return Refused(intent, "one of the two inventories is not available");
        }

        NpcTransferOutcome record = _ledger.Check(intent, out string reason);
        if (record != NpcTransferOutcome.Unspecified)
        {
            return new NpcTransferReceipt(intent.Request, record, 0, intent.From, reason);
        }

        int? fromBefore = SafeCount(from, intent.Material);
        int? toBefore = SafeCount(to, intent.Material);
        if (!fromBefore.HasValue || !toBefore.HasValue)
        {
            return Refused(intent, "an inventory could not be counted");
        }

        if (fromBefore.Value < intent.Count)
        {
            return Refused(
                intent,
                Describe(from) + " actually holds " + fromBefore.Value.ToString(CultureInfo.InvariantCulture) +
                " " + intent.Material + ", not " + intent.Count.ToString(CultureInfo.InvariantCulture));
        }

        int canAccept;
        try
        {
            canAccept = to.CanAccept(intent.Material, intent.Count);
        }
        catch (Exception exception)
        {
            return Refused(intent, "could not ask " + Describe(to) + " for room: " + Brief(exception));
        }

        if (canAccept < 1)
        {
            return Refused(intent, "there is no room for " + intent.Material + " in " + Describe(to));
        }

        // 2. Persist the intent, before anything moves.
        if (!Record(intent))
        {
            return Refused(intent, "the intention could not be written down, so nothing was moved");
        }

        _ledger.CountRecord();
        _ledger.Begin(intent);

        // 3-4. Add to the destination, then remove from the source.
        int requested = Math.Min(intent.Count, canAccept);
        int? addReturned = null;
        int? removeReturned = null;
        bool moved = false;
        Exception? fault = null;

        try
        {
            if (to is INpcInventoryMoveTarget mover && mover.CanMoveFrom(from))
            {
                moved = true;
                mover.MoveFrom(from, intent.Material, requested);
            }
            else
            {
                addReturned = to.Add(intent.Material, requested);

                int? afterAdd = SafeCount(to, intent.Material);
                if (!afterAdd.HasValue)
                {
                    throw new InvalidOperationException("the destination could not be counted after the add");
                }

                int gained = afterAdd.Value - toBefore.Value;
                if (gained > 0)
                {
                    removeReturned = from.Remove(intent.Material, gained);
                }
            }
        }
        catch (Exception exception)
        {
            fault = exception;
        }

        // 5. Classify from both inventories' actual deltas.
        int? fromAfter = SafeCount(from, intent.Material);
        int? toAfter = SafeCount(to, intent.Material);

        string evidence = Evidence(
            intent, from, to, requested, fromBefore.Value, fromAfter, toBefore.Value, toAfter,
            moved, addReturned, removeReturned, fault);

        NpcTransferOutcome outcome;
        int accepted = 0;

        if (fault == null && fromAfter.HasValue && toAfter.HasValue)
        {
            int gained = toAfter.Value - toBefore.Value;
            int lost = fromBefore.Value - fromAfter.Value;

            if (gained == lost && gained >= 0 && gained <= requested)
            {
                accepted = gained;
                outcome = gained == 0
                    ? NpcTransferOutcome.Refused
                    : gained == intent.Count ? NpcTransferOutcome.Completed : NpcTransferOutcome.Partial;
            }
            else
            {
                outcome = NpcTransferOutcome.Uncertain;
            }
        }
        else
        {
            outcome = NpcTransferOutcome.Uncertain;
        }

        var receipt = new NpcTransferReceipt(intent.Request, outcome, accepted, intent.From, evidence);

        // 6. Persist the receipt.
        if (!Record(intent, receipt))
        {
            string unrecorded = evidence + "; the result could not be written down";
            _ledger.MarkUncertain(intent.Request, unrecorded);
            return new NpcTransferReceipt(
                intent.Request, NpcTransferOutcome.Uncertain, 0, intent.From, unrecorded);
        }

        _ledger.CountRecord();
        _ledger.Finish(receipt);
        return receipt;
    }

    private static bool AlwaysTrue() => true;

    private static NpcTransferReceipt Refused(NpcTransferIntent intent, string reason) =>
        new NpcTransferReceipt(intent.Request, NpcTransferOutcome.Refused, 0, intent.From, reason);

    private bool Writable()
    {
        try
        {
            return _journal.IsWritable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool Record(NpcTransferIntent intent)
    {
        try
        {
            return _journal.TryRecordIntent(intent);
        }
        catch (Exception)
        {
            // A journal that throws has not written. Treated as a refusal,
            // which is the safe half: nothing has moved yet.
            return false;
        }
    }

    private bool Record(NpcTransferIntent intent, NpcTransferReceipt receipt)
    {
        try
        {
            return _journal.TryRecordReceipt(intent, receipt);
        }
        catch (Exception)
        {
            return false;
        }
    }

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

    private static bool Available(INpcInventoryPort port)
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

    private static int? SafeCount(INpcInventoryPort port, NpcMaterial material)
    {
        try
        {
            int count = port.Count(material);
            return count < 0 ? (int?)null : count;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Evidence(
        NpcTransferIntent intent, INpcInventoryPort from, INpcInventoryPort to, int requested,
        int fromBefore, int? fromAfter, int toBefore, int? toAfter,
        bool moved, int? addReturned, int? removeReturned, Exception? fault)
    {
        var text = new StringBuilder();
        text.Append(Describe(from)).Append(' ').Append(intent.Material).Append(": ")
            .Append(fromBefore.ToString(CultureInfo.InvariantCulture)).Append(" -> ")
            .Append(fromAfter.HasValue ? fromAfter.Value.ToString(CultureInfo.InvariantCulture) : "?");
        text.Append("; ").Append(Describe(to)).Append(": ")
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

    private static string Describe(INpcInventoryPort port)
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

    /// <summary>#416: the one scrubber. This used to compose
    /// `GetType().Name + ": " + Message` itself, which put the path a
    /// filesystem exception failed on - and the machine's user name - into a
    /// refusal a player reads and a status line they can upload.</summary>
    private static string Brief(Exception exception) => SafeFailure.Brief(exception);
}
