using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Tools;

/// <summary>Records a person's decision about an interrupted handover.
///
/// The ledger could already be resolved in memory, and for one commit that was
/// all it could do — the answer was discarded on the next load and the holding
/// went straight back to unknown, while the documentation called it
/// "resolvable". The decision is part of the record now, so it survives.
///
/// Only a person calls this. Nothing works the answer out.</summary>
internal static class ToolResolution
{
    internal static bool TryRecord(
        RequestId transaction,
        bool workerHasIt,
        ToolLedger ledger,
        SettlementJournal journal,
        Func<bool> saveNow,
        out string message)
    {
        if (ledger == null || journal == null || saveNow == null || transaction.IsEmpty
            || !ledger.TryGet(transaction, out ToolHolding holding))
        {
            message = "There is no record of that handover.";
            return false;
        }

        if (journal.IsReadOnly)
        {
            message = "That cannot be settled yet: this settlement's record could not be fully " +
                "read, so nothing new is being written to it.";
            return false;
        }

        if (holding.State != ToolHoldingState.Uncertain)
        {
            message = "That handover is not waiting on an answer.";
            return false;
        }

        int beforeAnswer = journal.Entries.Count;
        journal.Append(
            workerHasIt
                ? JournalEntryKind.ToolResolvedToWorker
                : JournalEntryKind.ToolResolvedToPlayer,
            default,
            transaction,
            worker: holding.Worker,
            tool: holding.Tool);

        if (!TryPersist(saveNow))
        {
            // "Recorded" has to mean recorded. Saying it after a failed write
            // would send somebody away believing a question was settled that
            // will be asked again on the next load.
            //
            // The ledger is settled AFTER this, not before and rolled back. The
            // rollback was asymmetric and silently failed for one of the two
            // answers: "you have it" leaves the holding Returned, and
            // MarkUncertain refuses to move a settled holding -- correctly, but
            // it meant the undo only worked for "he has it". Not touching the
            // ledger until the record is safe removes the need for an undo.
            journal.TryDiscardUnsaved(beforeAnswer);

            message = "That could not be written down, so nothing has been settled. Try again.";
            return false;
        }

        ledger.Resolve(transaction, workerHasIt);

        message = workerHasIt
            ? "Recorded: he has it."
            : "Recorded: you have it.";
        return true;
    }

    /// <summary>Calls a save and turns any answer we did not get into "no".
    ///
    /// A save that throws established exactly as much as one that returned
    /// false, and letting it escape mid-decision would leave the record and the
    /// ledger disagreeing about a real tool.</summary>
    internal static bool TryPersist(Func<bool> saveNow)
    {
        try
        {
            return saveNow();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
