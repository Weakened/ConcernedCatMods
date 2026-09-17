using System;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Moves one real tool between the player's inventory and a worker's.
///
/// <b>This is the part a ledger cannot prove.</b> The game-free contract records
/// what was handed over and makes the record idempotent; only this class
/// actually moves an item, and until it runs nothing has changed hands.
///
/// <b>What this adapter decides, and what it hands on.</b> It decides what only
/// the game can: whether the item is a tool this build issues
/// (<see cref="ToolClassifier"/>), whether it is still usable, and whether it is
/// a single, non-stacking instance. The move itself — intention persisted, add,
/// remove, result persisted, and every failure between — is
/// <see cref="ToolHandoverProcedure"/>, game-free and fault-injected in the
/// settlement tests. #299's final review found that ordering "defended by
/// reading alone" while it lived here.
///
/// Verified against the installed 1.0.12 build, and the verification is what
/// makes the design work: <c>Inventory.RemoveItem(ItemDrop.ItemData)</c> is
/// <c>m_inventory.Contains(item)</c> then <c>m_inventory.Remove(item)</c> — it
/// operates on the <b>same object reference</b>, as does
/// <c>Inventory.AddItem(ItemDrop.ItemData)</c> — <b>off its stacking branch</b>,
/// which mutates <c>m_stack</c> on the source and can return false after moving
/// part of a stack. A single, non-stacking item is refused before it reaches
/// that branch, so moving a tool is moving one instance between two lists, and
/// its type, quality, durability and every other field travel with it because
/// they <i>are</i> it. <c>ItemData.Clone()</c> is deliberately never called here
/// — a clone is a second axe.
///
/// <b>Where the tool goes.</b> The worker's inventory is his body's persisted
/// inventory (D9): the body writes it to its own network object in the same call
/// as the add, so an issued axe survives a relog, a zone unload and a world
/// reload with the world save that holds it.</summary>
internal static class ToolHandover
{
    /// <summary>Calls a save and turns any answer we did not get into "no".
    /// </summary>
    internal static bool TryPersist(Func<bool> saveNow) => ToolResolution.TryPersist(saveNow);

    /// <summary>Hands one specific item from a player to a worker.
    ///
    /// <paramref name="selected"/> must be an item the player actually chose.
    /// Nothing here searches an inventory, picks a "best" axe, or touches a
    /// container: the owner's rule is that the player's specifically selected
    /// instance moves, and a method that could find its own candidate would
    /// eventually be asked to.</summary>
    /// <param name="saveNow">Writes the journal and returns whether it reached
    /// disk. It may be called more than once and is allowed to throw — a throw
    /// is treated exactly as <c>false</c>.</param>
    /// <param name="stamp">The world time and load for the rows, so the
    /// world-save marker rule can place them; null only outside a world.</param>
    internal static HandoverOutcome TryGive(
        Humanoid? from,
        Humanoid? to,
        ItemDrop.ItemData? selected,
        WorkerId worker,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        Func<bool> saveNow,
        JournalStamp? stamp,
        out ToolSpecimen given,
        out string message)
    {
        given = default;

        if (ledger == null || journal == null || saveNow == null || worker.IsEmpty || transaction.IsEmpty)
        {
            message = "That handover was not set up properly. Nothing was taken.";
            return HandoverOutcome.Refused;
        }

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

        if (selected.m_shared.m_maxStackSize > 1 || selected.m_stack != 1)
        {
            // Read from the 1.0.12 binary: Inventory.AddItem takes a stacking
            // branch that mutates m_stack on the SOURCE and can still return
            // false after moving some units. Every real axe and hammer has a
            // maximum stack of one, so this refuses something that does not
            // exist rather than restricting anything.
            message = "He takes one tool at a time, not a stack.";
            return HandoverOutcome.Refused;
        }

        // Equipped items stay referenced by the player's equipment slots after
        // they leave the inventory; vanilla's own drop unequips first.
        bool wasEquipped = from.IsItemEquiped(selected);
        if (wasEquipped)
        {
            from.UnequipItem(selected, triggerEquipEffects: false);
        }

        HandoverOutcome outcome = ToolHandoverProcedure.Give(
            new InventoryTools(source), new InventoryTools(destination), selected, specimen, worker, transaction,
            ledger, journal, saveNow, stamp, out message);

        if (outcome == HandoverOutcome.Refused && wasEquipped && source.ContainsItem(selected))
        {
            from.EquipItem(selected, triggerEquipEffects: false);
        }

        if (outcome == HandoverOutcome.Given || outcome == HandoverOutcome.Uncertain)
        {
            given = specimen;
        }

        if (outcome == HandoverOutcome.Given)
        {
            // Equipping is presentation, and its failure is not the handover's
            // failure: he owns the tool either way, and the record already says so.
            try
            {
                to.EquipItem(selected);
            }
            catch (Exception)
            {
                // Presentation.
            }
        }

        return outcome;
    }

    /// <summary>Gives a tool back, once. The same ordering in reverse: the
    /// intention written first (#300), the player's inventory added to before
    /// his is removed from.</summary>
    internal static HandoverOutcome TryReturn(
        Humanoid? worker,
        Humanoid? player,
        ItemDrop.ItemData? held,
        RequestId transaction,
        ToolLedger ledger,
        SettlementJournal journal,
        Func<bool> saveNow,
        JournalStamp? stamp,
        out string message)
    {
        if (worker == null || player == null || held == null)
        {
            if (ledger != null && ledger.TryGet(transaction, out ToolHolding holding) && holding.State == ToolHoldingState.Returned)
            {
                message = "You already have that one back.";
                return HandoverOutcome.AlreadyGiven;
            }

            message = "Nothing was returned: one of the two of you is not here.";
            return HandoverOutcome.Refused;
        }

        Inventory source = worker.GetInventory();
        Inventory destination = player.GetInventory();
        if (source == null || destination == null)
        {
            message = "Nothing was returned: an inventory could not be reached.";
            return HandoverOutcome.Refused;
        }

        if (worker.IsItemEquiped(held))
        {
            worker.UnequipItem(held, triggerEquipEffects: false);
        }

        return ToolHandoverProcedure.Return(
            new InventoryTools(source), new InventoryTools(destination), held, transaction, ledger!, journal, saveNow,
            stamp, out message);
    }

    /// <summary>A vanilla inventory as a tool inventory: exact instances only.
    /// </summary>
    private sealed class InventoryTools : IToolInventory<ItemDrop.ItemData>
    {
        private readonly Inventory _inventory;

        public InventoryTools(Inventory inventory)
        {
            _inventory = inventory;
        }

        public bool Contains(ItemDrop.ItemData item) => _inventory.ContainsItem(item);

        public bool CanAdd(ItemDrop.ItemData item) => _inventory.CanAddItem(item);

        public bool Add(ItemDrop.ItemData item) => _inventory.AddItem(item);

        public bool Remove(ItemDrop.ItemData item) => _inventory.RemoveItem(item);
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
                || !string.Equals(live.ItemKey, holding.Tool.ItemKey, StringComparison.Ordinal))
            {
                continue;
            }

            return ToolClassifier.IsStillUsable(item);
        }

        return false;
    }
}
