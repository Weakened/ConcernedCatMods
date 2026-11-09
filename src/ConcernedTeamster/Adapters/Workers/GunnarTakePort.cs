using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The one file in Concerned Teamster allowed to move material <b>out
/// of a vanilla container into Gunnar's own inventory</b> (#381, under the
/// owner's decision of 2026-10-31, `DECISIONS.md` <b>D20</b>).
///
/// <b>The deposit, the other way round, and nothing more.</b> The owner allowed
/// Gunnar to take from a chest the player explicitly marked <c>Take</c> or
/// <c>Both</c>, measured on both sides, with the remainder left in the chest,
/// off by default and pinned like the D15 deposit. So this file is
/// <see cref="GunnarDepositPort"/> with its two roles exchanged: the same three
/// vanilla calls, the same permit mint, the same add-before-remove order, the
/// same measurement - and the source is the chest and the destination is him,
/// derived from those two roles inside the mover so a rename cannot swap them.
///
/// <b>What it is allowed to do.</b> Move units of one named item out of one
/// container the player chose and marked, into Gunnar, once, having asked the
/// mark at the moment of the move. It writes no ownership, no mass, no force, no
/// velocity, no position, and no mod data into a vanilla object; it creates no
/// item from a name; it chooses no chest; it takes nothing it was not asked for.
///
/// <b>There is no nearest-chest lookup in this file.</b> The source arrives as
/// an argument together with the key it was designated under, and a live
/// container whose key does not match is
/// <see cref="NpcContainerRefusal.NotThisContainer"/>.
///
/// <b>Add before remove, measured on both sides.</b> A partial stack clones that
/// stack's data for exactly the units wanted, adds the clone to Gunnar, and then
/// removes from the chest exactly what <b>arrived</b>. A crash between the two
/// leaves a duplicate the counts expose; the reverse order would destroy the
/// player's items with no evidence left. A disagreement in either direction is
/// <see cref="TakeOutcome.Uncertain"/>: nothing credited, nothing retried,
/// nothing compensated, nothing minted.
///
/// <b>What does not fit stays in the chest.</b> Never on the floor, never
/// deleted.</summary>
internal sealed class GunnarTakePort
{
    /// <summary>Whether the engine and the measurement agreed about the move.
    /// The same blind spot as the deposit's: a stack in Gunnar that the count
    /// does not recognise as the same thing can absorb an add while both deltas
    /// read zero.</summary>
    private readonly struct MoveTally
    {
        private MoveTally(bool attempted, bool inconsistent)
        {
            WasAttempted = attempted;
            IsInconsistent = inconsistent;
        }

        public bool WasAttempted { get; }

        public bool IsInconsistent { get; }

        public static MoveTally Consistent => new MoveTally(true, false);

        public static MoveTally Inconsistent => new MoveTally(true, true);

        public static MoveTally NothingAttempted => new MoveTally(false, true);
    }

    private readonly List<ItemDrop.ItemData> _matching = new List<ItemDrop.ItemData>();

    /// <summary>Moves up to <paramref name="wanted"/> units of one item out of
    /// the designated chest and into Gunnar, or says why it will not.
    ///
    /// One item, one container, one leg, one permit.</summary>
    /// <param name="designatedIn">The world load the source was chosen in,
    /// compared by the permit against <paramref name="world"/>.</param>
    /// <param name="expectedKey">The key the source was designated under. A live
    /// container whose key differs is refused.</param>
    public TakeResult TakeOne(
        Container? source,
        Humanoid? worker,
        string expectedKey,
        string itemPrefab,
        int wanted,
        NpcWorldEpoch designatedIn,
        NpcWorldEpoch world,
        Func<Container?, NpcContainerUse> allowance,
        Func<Vector3?> workerPosition,
        float reachMetres,
        out NpcContainerRefusal refusal)
    {
        refusal = NpcContainerRefusal.Gone;

        Inventory? gunnar = InventoryOf(worker);
        if (gunnar == null || string.IsNullOrEmpty(itemPrefab) || wanted < 1)
        {
            return new TakeResult(TakeOutcome.Refused, null, "nothing to take, or Gunnar could not be read");
        }

        var subject = new DesignatedChest(
            source, expectedKey, designatedIn, allowance, workerPosition, reachMetres);

        // THE MINT, for TAKE and for nothing else. Re-asks the container's own
        // access now - the player's mark, this client's ownership, whether
        // anybody has it open, the ward, the privacy setting and the reach - and
        // answers null for every refusal. A chest marked Deposit only answers
        // UseNotAllowed here.
        NpcContainerPermit? permit = NpcContainerPermit.Issue(
            subject, NpcContainerUse.Take, world, out refusal);
        if (permit == null)
        {
            return new TakeResult(TakeOutcome.Refused, null, TakeSentences.Describe(refusal));
        }

        Inventory? chest = InventoryOf(source);
        if (chest == null)
        {
            refusal = NpcContainerRefusal.Gone;
            return new TakeResult(TakeOutcome.Refused, null, TakeSentences.Describe(refusal));
        }

        int? availableBefore = SafeCount(chest, itemPrefab);
        int? roomFor = SafeRoom(gunnar, itemPrefab, wanted, FirstMatching(chest, itemPrefab));
        int? arrivedBefore = SafeCount(gunnar, itemPrefab);
        if (!availableBefore.HasValue || !roomFor.HasValue || !arrivedBefore.HasValue)
        {
            return new TakeResult(
                TakeOutcome.Refused, null,
                "one of the two inventories could not be counted, so nothing was moved");
        }

        NpcTransferPlan plan = NpcTransferPlan.For(wanted, availableBefore.Value, roomFor.Value);
        if (plan.IsEmpty)
        {
            return new TakeResult(
                TakeOutcome.Refused, null,
                availableBefore.Value < 1
                    ? "that chest has no " + itemPrefab
                    : "he has no room for " + itemPrefab + "; it stays in the chest");
        }

        bool faulted = false;
        MoveTally tally;
        try
        {
            tally = Move(source, worker, itemPrefab, plan.Units);
        }
        catch (Exception)
        {
            faulted = true;
            tally = MoveTally.Inconsistent;
        }

        int? availableAfter = SafeCount(chest, itemPrefab);
        int? arrivedAfter = SafeCount(gunnar, itemPrefab);
        if (faulted || !availableAfter.HasValue || !arrivedAfter.HasValue)
        {
            return new TakeResult(
                TakeOutcome.Uncertain, null,
                "the move faulted or could not be counted afterwards, so whether it happened is not known; " +
                "nothing is credited, nothing is retried and nothing is put right automatically");
        }

        if (!tally.WasAttempted)
        {
            return new TakeResult(
                TakeOutcome.Refused, null,
                "the two inventories could not be read at the moment of the move, so nothing was " +
                "attempted and nothing changed");
        }

        if (tally.IsInconsistent)
        {
            return new TakeResult(
                TakeOutcome.Uncertain, null,
                "the move reported putting something in him and the count could not see it, so what " +
                "happened is not known; nothing is credited, nothing is retried and nothing is put " +
                "right automatically");
        }

        int left = availableBefore.Value - availableAfter.Value;
        int arrived = arrivedAfter.Value - arrivedBefore.Value;

        // Both measured deltas, compared, and the permit spent whichever way it
        // goes. More arriving in him than left the chest is minting; fewer is
        // loss. Either is Uncertain.
        ContainerMoveResult recorded = ContainerMoveResult.Record(permit, world, plan, left, arrived);

        switch (recorded.Outcome)
        {
            case ContainerMoveOutcome.Completed:
                return new TakeResult(TakeOutcome.Taken, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Partial:
                return new TakeResult(TakeOutcome.PartlyTaken, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Nothing:
            case ContainerMoveOutcome.Refused:
                return new TakeResult(TakeOutcome.Refused, null, recorded.Evidence);
            default:
                return new TakeResult(TakeOutcome.Uncertain, null, recorded.Evidence);
        }
    }

    /// <summary>Vanilla's own move, whole stacks first and then the remainder.
    ///
    /// <b>The three pinned calls live here and nowhere else.</b> A whole stack
    /// goes through <c>MoveItemToThis</c>, which adds to Gunnar and removes from
    /// the chest inside one vanilla call and keeps the moved instance. A partial
    /// stack is cloned for exactly the units wanted, added to Gunnar, and then
    /// exactly what <b>arrived</b> is removed from the chest's stack.
    ///
    /// <b>It takes the two ROLES and derives the inventories itself</b>, for the
    /// reason the deposit's mover gives: two same-typed inventory parameters can
    /// be reversed by renaming them, which turns a take into a deposit (or the
    /// deposit into a take) with every pinned call byte-identical. The two
    /// derivations below are pinned by `#381 chest-take audit`.</summary>
    private MoveTally Move(Container? source, Humanoid? worker, string itemPrefab, int units)
    {
        Inventory? chest = InventoryOf(source);
        Inventory? gunnar = InventoryOf(worker);
        if (chest == null || gunnar == null || ReferenceEquals(chest, gunnar))
        {
            return MoveTally.NothingAttempted;
        }

        int remaining = units;
        bool inconsistent = false;
        Matching(chest, itemPrefab, _matching);

        try
        {
            for (int index = 0; index < _matching.Count && remaining > 0; index++)
            {
                ItemDrop.ItemData stack = _matching[index];
                int before = CountIn(gunnar, itemPrefab);

                if (stack.m_stack <= remaining)
                {
                    int whole = stack.m_stack;
                    gunnar.MoveItemToThis(chest, stack);
                    int moved = CountIn(gunnar, itemPrefab) - before;
                    remaining -= moved > 0 ? moved : 0;
                    if (moved < whole)
                    {
                        // He took less than the whole stack, so he is full. The
                        // rest stays in the chest, which is the only correct
                        // place for it.
                        break;
                    }
                }
                else
                {
                    // Part of a stack. The clone is of THIS stack - the one the
                    // remove below takes from - so nothing is built from a name.
                    ItemDrop.ItemData part = stack.Clone();
                    part.m_stack = remaining;
                    part.m_equipped = false;
                    int offered = part.m_stack;
                    bool added = gunnar.AddItem(part);

                    // Vanilla's own number for what arrived, from the decompiled
                    // AddItem the deposit port documents case by case: all of it
                    // when the add answered true, and the merged part when it
                    // answered false.
                    int arrived = added ? offered : offered - (part.m_stack > 0 ? part.m_stack : 0);
                    int moved = CountIn(gunnar, itemPrefab) - before;

                    if (arrived > 0 && moved == arrived)
                    {
                        // The engine and the measurement agree. Take exactly that
                        // off the chest's stack, never the count asked for.
                        chest.RemoveItem(stack, arrived);
                    }
                    else if (arrived > 0 || moved > 0)
                    {
                        // They disagree. Nothing is removed from the chest on a
                        // number in dispute.
                        inconsistent = true;
                    }

                    break;
                }
            }
        }
        finally
        {
            _matching.Clear();
        }

        return inconsistent ? MoveTally.Inconsistent : MoveTally.Consistent;
    }

    // ------------------------------------------------------------------
    // Reading, all of it wrapped: a game state that cannot be read is a
    // refusal, never a zero.
    // ------------------------------------------------------------------

    private static Inventory? InventoryOf(Humanoid? worker)
    {
        try
        {
            return worker == null || worker.IsDead() ? null : worker.GetInventory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Inventory? InventoryOf(Container? container)
    {
        try
        {
            return container == null ? null : container.GetInventory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? SafeCount(Inventory inventory, string itemPrefab)
    {
        try
        {
            return CountIn(inventory, itemPrefab);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>How many of <paramref name="wanted"/> would fit in him: room in
    /// stacks vanilla's own add would merge into, plus empty slots. An estimate
    /// that must not over-count; the measured deltas decide.</summary>
    private static int? SafeRoom(
        Inventory inventory, string itemPrefab, int wanted, ItemDrop.ItemData? moving)
    {
        try
        {
            ObjectDB database = ObjectDB.instance;
            GameObject? prefab = database == null ? null : database.GetItemPrefab(itemPrefab);
            ItemDrop? drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                return null;
            }

            ItemDrop.ItemData template = drop.m_itemData;
            int maxStack = template.m_shared.m_maxStackSize > 0 ? template.m_shared.m_maxStackSize : 1;
            int quality = moving != null ? moving.m_quality : template.m_quality;
            int worldLevel = moving != null ? moving.m_worldLevel : Game.m_worldLevel;

            long room = 0;
            foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
            {
                if (existing.m_shared != null
                    && existing.m_shared.m_name == template.m_shared.m_name
                    && existing.m_quality == quality
                    && existing.m_worldLevel == worldLevel
                    && !existing.m_cheated)
                {
                    int headroom = existing.m_shared.m_maxStackSize - existing.m_stack;
                    room += headroom > 0 ? headroom : 0;
                }
            }

            room += (long)inventory.GetEmptySlots() * maxStack;
            long capped = room < wanted ? room : wanted;
            return (int)(capped < 0 ? 0 : capped);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int CountIn(Inventory inventory, string itemPrefab)
    {
        int total = 0;
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (Movable(stack, itemPrefab))
            {
                total += stack.m_stack;
            }
        }

        return total;
    }

    /// <summary>Whether a stack is one this take may move, or count. One
    /// predicate for every side, so what is counted and what is moved cannot
    /// disagree.</summary>
    private static bool Movable(ItemDrop.ItemData? stack, string itemPrefab) =>
        stack != null
        && stack.m_stack > 0
        && !stack.m_equipped
        && string.Equals(PrefabNameOf(stack), itemPrefab, StringComparison.Ordinal);

    private static ItemDrop.ItemData? FirstMatching(Inventory inventory, string itemPrefab)
    {
        try
        {
            foreach (ItemDrop.ItemData stack in inventory.GetAllItemsInGridOrder())
            {
                if (Movable(stack, itemPrefab))
                {
                    return stack;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable, so the caller falls back to the template's own values.
        }

        return null;
    }

    private static void Matching(Inventory inventory, string itemPrefab, List<ItemDrop.ItemData> into)
    {
        into.Clear();
        foreach (ItemDrop.ItemData stack in inventory.GetAllItemsInGridOrder())
        {
            if (Movable(stack, itemPrefab))
            {
                into.Add(stack);
            }
        }
    }

    /// <summary>An item's identity, by the prefab it drops as. Empty when it has
    /// none, which is refused rather than matched.</summary>
    private static string PrefabNameOf(ItemDrop.ItemData? stack)
    {
        try
        {
            return stack == null || stack.m_dropPrefab == null ? string.Empty : stack.m_dropPrefab.name;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<KeyValuePair<string, int>> One(string itemPrefab, int units) =>
        units > 0
            ? new[] { new KeyValuePair<string, int>(itemPrefab, units) }
            : Array.Empty<KeyValuePair<string, int>>();
}
