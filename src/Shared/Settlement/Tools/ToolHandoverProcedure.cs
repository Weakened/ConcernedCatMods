using System;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Tools;

/// <summary>What happened when a tool was handed over or given back.</summary>
internal enum HandoverOutcome
{
    /// <summary>Nothing moved. Zero, so an unfilled result never reads as
    /// success.</summary>
    Refused = 0,

    /// <summary>The item is now where it was going. Exactly one of it exists.
    /// </summary>
    Given = 1,

    /// <summary>This transaction had already been carried out. Nothing moved a
    /// second time.</summary>
    AlreadyGiven = 2,

    /// <summary>Where the item is, or what the record says about it, is not
    /// certain. Nothing is guessed and nothing further is done; a person
    /// answers.</summary>
    Uncertain = 3,
}

/// <summary>One inventory a tool can move between, behind a seam so the
/// ordering of a handover can be fault-injected without the game. The item is
/// opaque: the adapter's item instance, never a copy.</summary>
internal interface IToolInventory<TItem>
{
    bool Contains(TItem item);

    bool CanAdd(TItem item);

    /// <summary>Adds this exact instance. False when it did not.</summary>
    bool Add(TItem item);

    /// <summary>Removes this exact instance. False when it did not.</summary>
    bool Remove(TItem item);
}

/// <summary>The world time and load that stamp a journal row, so the
/// world-save marker rule can place it (schema v3).</summary>
internal readonly struct JournalStamp
{
    public JournalStamp(double worldTime, Guid loadEpoch)
    {
        if (double.IsNaN(worldTime) || double.IsInfinity(worldTime) || loadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A stamp is a real world time and a world load.");
        }

        WorldTime = worldTime;
        LoadEpoch = loadEpoch;
    }

    public double WorldTime { get; }

    public Guid LoadEpoch { get; }
}

/// <summary>Moving one real tool between a player and a worker, in the one
/// order whose failures are all recoverable.
///
/// <b>Moved out of the Foreman adapter so it can be tested.</b> #299's final
/// review found the handover's ordering "defended by reading alone": it lived
/// beside <c>Inventory</c>, where no game-free test could reach it. The adapter
/// still does what only it can — classify the item, check durability and stack
/// size — and hands the move itself to this.
///
/// <b>The order, give and return alike:</b>
/// <list type="number">
/// <item>Write the intention and persist it. If that fails, nothing has
/// moved.</item>
/// <item>Add the item to the receiving inventory. If that fails, nothing has
/// moved; the handover closes its own attempt in the record, marked as written
/// by the handover rather than by a person (#300 item 5).</item>
/// <item>Remove it from the giving inventory. If that fails, the item is
/// referenced twice, which is visible and recoverable; the intention is on
/// disk, so a replay reports it and a person answers.</item>
/// <item>Write the result and persist it, and only then settle the ledger. If
/// the write fails, the result is discarded and the ledger says uncertain —
/// exactly what the next load will conclude — and the player is told (#300
/// item 2).</item>
/// </list>
///
/// A return writes its intention first now, as a give always did: an
/// interrupted return is in the record, and the same command that settles an
/// interrupted give settles it (#300 item 1).
///
/// <b>Whatever either method answers, the ledger it was handed then says what
/// a replay of the journal says</b> about that transaction — held, returned,
/// no holding, or unsettled — including when a closing row could not be
/// written. The tests hold every path to that.
///
/// What a player is told about a failed write is true in this session, where
/// the record is what <c>cf_settle resolve</c> reads. It is not promised to
/// outlive a reload: when the world was not saved after the attempt, the load
/// rolls the attempt back with the world and there is nothing to answer.</summary>
internal static class ToolHandoverProcedure
{
    public static HandoverOutcome Give<TItem>(
        IToolInventory<TItem> from,
        IToolInventory<TItem> to,
        TItem item,
        ToolSpecimen specimen,
        WorkerId worker,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        Func<bool> saveNow,
        JournalStamp? stamp,
        out string message)
    {
        if (ledger == null || journal == null || saveNow == null || worker.IsEmpty || transaction.IsEmpty || specimen.IsEmpty)
        {
            message = "That handover was not set up properly. Nothing was taken.";
            return HandoverOutcome.Refused;
        }

        // Idempotence first, and before the read-only check. A handover that has
        // already happened has already happened -- reporting "nothing has been
        // taken" about a tool that was is a false statement about the player's
        // own inventory, and an earlier ordering made it.
        if (ledger.TryGet(transaction, out ToolHolding existing))
        {
            message = existing.Tool.Kind + " already handed over. Nothing changed.";
            return HandoverOutcome.AlreadyGiven;
        }

        if (journal.IsReadOnly)
        {
            message = "He cannot take it: this settlement's record could not be fully read, so " +
                "nothing new is being written to it. Nothing has been taken.";
            return HandoverOutcome.Refused;
        }

        if (from == null || to == null || !from.Contains(item))
        {
            message = "Nothing was taken: that item is not yours to give.";
            return HandoverOutcome.Refused;
        }

        if (!to.CanAdd(item))
        {
            message = "He has no room for that right now.";
            return HandoverOutcome.Refused;
        }

        // 1. The intention, written and PERSISTED before anything is touched.
        if (!Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolHandoverStarted, default, transaction, worker: worker, tool: specimen,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default)))
        {
            message = "He cannot take it: this settlement's record could not be written, so " +
                "nothing has been taken.";
            return HandoverOutcome.Refused;
        }

        // 2. Add first.
        if (!to.Add(item))
        {
            bool closed = Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolResolvedToPlayer, default, transaction, worker: worker, tool: specimen,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default, writtenByHandover: true));

            if (closed)
            {
                message = "He could not take it. Nothing was taken from you.";
                return HandoverOutcome.Refused;
            }

            // Nothing moved, but the record still holds an intention with no
            // result, which is an unsettled handover. The ledger says so too.
            ledger.Issue(new ToolHolding(transaction, worker, specimen));
            ledger.MarkUncertain(transaction);
            message = "He could not take it, and nothing was taken from you — but that could not be written " +
                "down, so the record shows the handover as unsettled. Answer: cf_settle resolve " +
                transaction.Value + " mine";
            return HandoverOutcome.Refused;
        }

        // 3. Then remove.
        if (!from.Remove(item))
        {
            // The item reached him but did not leave you -- something else moved
            // it in between. Removing his side could destroy the last reference,
            // so this stops. The intention on disk with no result is how a replay
            // learns this one is unresolved.
            ledger.Issue(new ToolHolding(transaction, worker, specimen));
            ledger.MarkUncertain(transaction);
            message = "Something went wrong mid-handover and it is not recorded whether that " +
                "item changed hands. Nothing further has been done. Check both inventories, then " +
                "run: cf_settle resolve " + transaction.Value + " mine|his";
            return HandoverOutcome.Uncertain;
        }

        // 4. The result, persisted before the ledger is settled.
        ledger.Issue(new ToolHolding(transaction, worker, specimen));
        if (!Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolHandoverFinished, default, transaction, worker: worker, tool: specimen,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default)))
        {
            ledger.MarkUncertain(transaction);
            message = "He has it — but that could not be written down, so it is recorded as unsettled. " +
                "Confirm it with: cf_settle resolve " + transaction.Value + " his";
            return HandoverOutcome.Uncertain;
        }

        message = specimen.Kind == ToolKind.Axe
            ? "Good edge. He takes the axe."
            : "Sound handle. He takes the hammer.";
        return HandoverOutcome.Given;
    }

    public static HandoverOutcome Return<TItem>(
        IToolInventory<TItem> worker,
        IToolInventory<TItem> player,
        TItem item,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        Func<bool> saveNow,
        JournalStamp? stamp,
        out string message)
    {
        if (ledger == null || journal == null || saveNow == null || transaction.IsEmpty
            || !ledger.TryGet(transaction, out ToolHolding holding))
        {
            message = "There is no record of that tool.";
            return HandoverOutcome.Refused;
        }

        if (holding.State == ToolHoldingState.Returned)
        {
            message = "You already have that one back.";
            return HandoverOutcome.AlreadyGiven;
        }

        if (journal.IsReadOnly)
        {
            message = "He cannot give it back yet: this settlement's record could not be fully " +
                "read, so nothing new is being written to it.";
            return HandoverOutcome.Refused;
        }

        if (holding.State == ToolHoldingState.Uncertain)
        {
            message = "That handover was interrupted and has not been resolved. Nothing will be " +
                "moved until it is: cf_settle resolve " + transaction.Value + " mine|his";
            return HandoverOutcome.Uncertain;
        }

        if (worker == null || player == null || !worker.Contains(item))
        {
            message = "Nothing was returned: that item is not his to give back.";
            return HandoverOutcome.Refused;
        }

        if (!player.CanAdd(item))
        {
            message = "You have no room for it. He is still holding it.";
            return HandoverOutcome.Refused;
        }

        // 1. The intention to return, before anything moves (#300 item 1).
        if (!Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolReturned, default, transaction, worker: holding.Worker, tool: holding.Tool,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default, returnIntent: true)))
        {
            message = "He cannot give it back: this settlement's record could not be written. He is " +
                "still holding it.";
            return HandoverOutcome.Refused;
        }

        // 2. Add to the player first.
        if (!player.Add(item))
        {
            bool closed = Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolResolvedToWorker, default, transaction, worker: holding.Worker, tool: holding.Tool,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default, writtenByHandover: true));

            if (closed)
            {
                message = "You have no room for it. He is still holding it.";
                return HandoverOutcome.Refused;
            }

            ledger.MarkUncertain(transaction);
            message = "You have no room for it and he is still holding it — but that could not be written down, " +
                "so the record shows the return as unsettled. Answer: cf_settle resolve " + transaction.Value + " his";
            return HandoverOutcome.Refused;
        }

        // 3. Then remove from him.
        if (!worker.Remove(item))
        {
            ledger.MarkUncertain(transaction);
            message = "Something went wrong mid-return and it is not recorded whether that item " +
                "changed hands. Check both inventories, then run: cf_settle resolve " +
                transaction.Value + " mine|his";
            return HandoverOutcome.Uncertain;
        }

        // 4. The result, persisted before the ledger is settled.
        if (!Record(journal, saveNow, () => journal.Append(
                JournalEntryKind.ToolReturned, default, transaction, worker: holding.Worker, tool: holding.Tool,
                worldTime: stamp?.WorldTime, loadEpoch: stamp?.LoadEpoch ?? default)))
        {
            ledger.MarkUncertain(transaction);
            message = "You have it back — but that could not be written down, so it is recorded as unsettled. " +
                "Confirm it with: cf_settle resolve " + transaction.Value + " mine";
            return HandoverOutcome.Uncertain;
        }

        ledger.Return(transaction);
        message = "He hands it back.";
        return HandoverOutcome.Given;
    }

    /// <summary>Appends a row and persists it. True only when the row is on
    /// disk; a row that is not is removed again, unless the journal refuses
    /// because it reached disk after all.</summary>
    private static bool Record(SettlementJournal journal, Func<bool> saveNow, Action append)
    {
        int before = journal.Entries.Count;
        append();

        if (SettlementCustodyJournal.TryPersist(saveNow))
        {
            return true;
        }

        return !journal.TryDiscardUnsaved(before);
    }
}
