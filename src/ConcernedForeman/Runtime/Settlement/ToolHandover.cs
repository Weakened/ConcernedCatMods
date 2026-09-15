using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>What happened when the player tried to hand a tool over.</summary>
internal enum HandoverOutcome
{
    /// <summary>Nothing moved. Zero, so an unfilled result never reads as
    /// success.</summary>
    Refused = 0,

    /// <summary>The item is now the worker's. Exactly one of it exists.</summary>
    Given = 1,

    /// <summary>This transaction had already been carried out. Nothing moved a
    /// second time.</summary>
    AlreadyGiven = 2,

    /// <summary>The item reached the worker but could not be taken from the
    /// player, so where it is now is <b>not recorded</b>. Nothing is guessed
    /// and nothing further is done.</summary>
    Uncertain = 3,
}

/// <summary>Moves one real tool from the player's inventory into a worker's.
///
/// <b>This is the part a ledger cannot prove.</b> The game-free contract records
/// what was handed over and makes the record idempotent; only this class
/// actually moves an item, and until it runs nothing has changed hands. A
/// dictionary changing state is not a handover.
///
/// Verified against the installed 1.0.12 build, and the verification is what
/// makes the design work: <c>Inventory.RemoveItem(ItemDrop.ItemData)</c> is
/// <c>m_inventory.Contains(item)</c> then <c>m_inventory.Remove(item)</c> — it
/// operates on the <b>same object reference</b>, as does
/// <c>Inventory.AddItem(ItemDrop.ItemData)</c>. So moving a tool is moving one
/// instance between two lists, and its type, quality, durability and every other
/// field travel with it because they <i>are</i> it. Nothing needs copying, and
/// <c>ItemData.Clone()</c> is deliberately never called here — a clone is a
/// second axe.
///
/// <b>The order is add-then-remove, and that is not arbitrary.</b> Neither order
/// is atomic against a game that can be killed between two statements, so the
/// question is which failure is survivable:
///
/// <list type="bullet">
/// <item><i>Remove first, then add.</i> If the add fails — a full worker
/// inventory — the item has already left the player and there is nothing left
/// holding it. That loses a real tool.</item>
/// <item><i>Add first, then remove.</i> If the remove fails, the item is
/// momentarily referenced by two inventories, and we are holding both ends. That
/// is a duplication we can see.</item>
/// </list>
///
/// A visible duplication beats a silent loss, so add goes first — and the
/// failure is then reported as <see cref="HandoverOutcome.Uncertain"/> rather
/// than repaired by guessing, because "the player's inventory no longer holds
/// the item we took from it" means something else moved it, and removing our
/// copy could destroy the last reference to it.</summary>
internal static class ToolHandover
{
    /// <summary>Hands one specific item from a player to a worker.
    ///
    /// <paramref name="selected"/> must be an item the player actually chose.
    /// Nothing here searches an inventory, picks a "best" axe, or touches a
    /// container: the owner's rule is that the player's specifically selected
    /// instance moves, and a method that could find its own candidate would
    /// eventually be asked to.</summary>
    internal static HandoverOutcome TryGive(
        Humanoid? from,
        Humanoid? to,
        ItemDrop.ItemData? selected,
        WorkerId worker,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        out ToolSpecimen given,
        out string message)
    {
        given = default;

        if (ledger == null || journal == null || worker.IsEmpty || transaction.IsEmpty)
        {
            message = "That handover was not set up properly. Nothing was taken.";
            return HandoverOutcome.Refused;
        }

        if (journal.IsReadOnly)
        {
            // The record of the handover cannot be written, so the handover must
            // not happen -- the same rule that stops a designation being cleared
            // against a journal this build may not write. Taking somebody's axe
            // on a promise we cannot keep is worse than refusing.
            message = "He cannot take it: this settlement's record could not be fully read, so " +
                "nothing new is being written to it. Nothing has been taken.";
            return HandoverOutcome.Refused;
        }

        // Idempotent before anything is touched. The caller may not know whether
        // its previous attempt reached disk, and must not have to.
        if (ledger.TryGet(transaction, out ToolHolding existing))
        {
            given = existing.Tool;
            message = existing.Tool.Kind + " already handed over. Nothing changed.";
            return HandoverOutcome.AlreadyGiven;
        }

        if (from == null || to == null || selected == null)
        {
            message = "Nothing was taken: one of the two of you is not here.";
            return HandoverOutcome.Refused;
        }

        Inventory source = from.GetInventory();
        Inventory destination = to.GetInventory();
        if (source == null || destination == null)
        {
            message = "Nothing was taken: an inventory could not be reached.";
            return HandoverOutcome.Refused;
        }

        if (!source.ContainsItem(selected))
        {
            message = "Nothing was taken: that item is not yours to give.";
            return HandoverOutcome.Refused;
        }

        if (!ToolClassifier.TryDescribe(selected, out ToolSpecimen specimen))
        {
            message = "He has no use for that. He needs an axe to cut and a hammer to build.";
            return HandoverOutcome.Refused;
        }

        if (!ToolClassifier.IsStillUsable(selected))
        {
            message = "That one is worn out. Repair it first and he will take it.";
            return HandoverOutcome.Refused;
        }

        // Asked before anything moves, so a full worker is an ordinary refusal
        // rather than a half-completed transfer.
        if (!destination.CanAddItem(selected))
        {
            message = "He has no room for that right now.";
            return HandoverOutcome.Refused;
        }

        if (!destination.AddItem(selected))
        {
            // Nothing moved: the item is still the player's, exactly as before.
            message = "He could not take it. Nothing was taken from you.";
            return HandoverOutcome.Refused;
        }

        // The intention, written BEFORE the item moves. A replay that finds
        // this with no matching finish knows a handover was in flight, which is
        // a completely different situation from never having tried.
        journal.Append(
            JournalEntryKind.ToolHandoverStarted, default, transaction,
            worker: worker, tool: specimen);

        if (!source.RemoveItem(selected))
        {
            // The item reached him but did not leave us -- something else moved
            // it in between. Removing our side could destroy the last reference,
            // so this stops and says so instead. The started entry above is left
            // standing with no finish, which is exactly how a replay learns that
            // this one is unresolved.
            ledger.Issue(new ToolHolding(transaction, worker, specimen));
            ledger.MarkUncertain(transaction);

            message = "Something went wrong mid-handover and it is not recorded whether that " +
                "item changed hands. Nothing further has been done. Check both inventories " +
                "before continuing — this build will not guess.";
            return HandoverOutcome.Uncertain;
        }

        ledger.Issue(new ToolHolding(transaction, worker, specimen));
        journal.Append(
            JournalEntryKind.ToolHandoverFinished, default, transaction,
            worker: worker, tool: specimen);

        // Equipping is presentation, and its failure is not the handover's
        // failure: he owns the tool either way, and the record already says so.
        to.EquipItem(selected);

        given = specimen;
        message = specimen.Kind == ToolKind.Axe
            ? "Good edge. He takes the axe."
            : "Sound handle. He takes the hammer.";
        return HandoverOutcome.Given;
    }

    /// <summary>Gives a tool back, once.
    ///
    /// The same ordering argument in reverse, for the same reason: the worker is
    /// the one holding it, so the player's inventory is added to first.</summary>
    internal static HandoverOutcome TryReturn(
        Humanoid? worker,
        Humanoid? player,
        ItemDrop.ItemData? held,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        out string message)
    {
        if (ledger == null || journal == null || transaction.IsEmpty
            || !ledger.TryGet(transaction, out ToolHolding holding))
        {
            message = "There is no record of that tool.";
            return HandoverOutcome.Refused;
        }

        if (journal.IsReadOnly)
        {
            message = "He cannot give it back yet: this settlement's record could not be fully " +
                "read, so nothing new is being written to it.";
            return HandoverOutcome.Refused;
        }

        if (holding.State == ToolHoldingState.Returned)
        {
            message = "You already have that one back.";
            return HandoverOutcome.AlreadyGiven;
        }

        if (holding.State == ToolHoldingState.Uncertain)
        {
            message = "That handover was interrupted and has not been resolved. Nothing will be " +
                "moved until it is.";
            return HandoverOutcome.Uncertain;
        }

        if (worker == null || player == null || held == null)
        {
            message = "Nothing was returned: one of the two of you is not here.";
            return HandoverOutcome.Refused;
        }

        Inventory source = worker.GetInventory();
        Inventory destination = player.GetInventory();
        if (source == null || destination == null || !source.ContainsItem(held))
        {
            message = "Nothing was returned: that item is not his to give back.";
            return HandoverOutcome.Refused;
        }

        if (!destination.CanAddItem(held) || !destination.AddItem(held))
        {
            message = "You have no room for it. He is still holding it.";
            return HandoverOutcome.Refused;
        }

        if (!source.RemoveItem(held))
        {
            ledger.MarkUncertain(transaction);
            message = "Something went wrong mid-return and it is not recorded whether that item " +
                "changed hands. Check both inventories — this build will not guess.";
            return HandoverOutcome.Uncertain;
        }

        ledger.Return(transaction);
        journal.Append(
            JournalEntryKind.ToolReturned, default, transaction,
            worker: holding.Worker, tool: holding.Tool);

        message = "He hands it back.";
        return HandoverOutcome.Given;
    }
}

/// <summary>Answers "is this issued tool still usable" by looking at the actual
/// item in the worker's inventory.
///
/// The game-free layer asks this question through
/// <see cref="IToolCondition"/> precisely so that it never has to answer it from
/// a stored number. Anything this cannot establish — no worker, no inventory, no
/// matching item — answers <b>false</b>, because a worker standing still because
/// we were not sure is visible and harmless, and one swinging a tool we could not
/// check is neither.</summary>
internal sealed class WorldToolCondition : IToolCondition
{
    private readonly Humanoid? _worker;

    internal WorldToolCondition(Humanoid? worker)
    {
        _worker = worker;
    }

    public bool IsUsable(ToolHolding holding)
    {
        if (holding == null || _worker == null)
        {
            return false;
        }

        Inventory inventory = _worker.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (!ToolClassifier.TryDescribe(item, out ToolSpecimen live))
            {
                continue;
            }

            // Matched on kind and identity, not on durability: durability is
            // exactly what has changed since it was handed over.
            if (live.Kind != holding.Tool.Kind
                || !string.Equals(live.ItemKey, holding.Tool.ItemKey, System.StringComparison.Ordinal))
            {
                continue;
            }

            return ToolClassifier.IsStillUsable(item);
        }

        return false;
    }
}
