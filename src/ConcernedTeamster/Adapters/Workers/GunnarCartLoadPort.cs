using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The one file in Concerned Teamster allowed to move material <b>out
/// of Gunnar's own inventory into the container of the cart the player assigned
/// him</b> (#381, under the owner's decision of 2026-10-31, `DECISIONS.md`
/// <b>D22</b>).
///
/// <b>The deposit, into a different place, and nothing more.</b> The owner
/// allowed his collection tour to load what it picks into his explicitly
/// assigned cart's container, measured. So this file is
/// <see cref="GunnarDepositPort"/> with a different destination: the same three
/// vanilla calls, the same permit mint, the same add-before-remove order, the
/// same measurement. The destination is not a chest the player marked; it is
/// the container of the one cart the hauling runtime's lease names, derived
/// inside the mover from that cart so a rename cannot point it anywhere else.
///
/// <b>What it is allowed to do.</b> Move units of one collected material out of
/// Gunnar into the assigned cart, once, having asked the assignment, the cart's
/// ownership, whether it is open or hitched, the ward, the privacy setting, the
/// reach and whether the cart is still where it stood, at the moment of the
/// move. It writes no ownership, no mass, no force, no velocity, no position,
/// and no mod data into a vanilla object; it creates no item from a name; it
/// chooses no cart; it takes nothing out of the cart.
///
/// <b>There is no nearest-cart lookup in this file.</b> The cart arrives as an
/// argument together with the key the lease names it by, and a live cart whose
/// key does not match is <see cref="NpcContainerRefusal.NotThisContainer"/>.
///
/// <b>Add before remove, measured on both sides.</b> A partial stack clones that
/// stack's data for exactly the units wanted, adds the clone to the cart, and
/// then removes from him exactly what <b>arrived</b>. A disagreement in either
/// direction is <see cref="DepositOutcome.Uncertain"/>: nothing credited,
/// nothing retried, nothing compensated, nothing minted.
///
/// <b>What does not fit stays in him.</b> Never on the floor, never deleted.
/// </summary>
internal sealed class GunnarCartLoadPort
{
    /// <summary>Whether the engine and the measurement agreed about the move.
    /// The same blind spot as the deposit's: a stack in the cart that the count
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

    /// <summary>Moves up to <paramref name="wanted"/> units of one material out
    /// of Gunnar and into the assigned cart, or says why it will not.
    ///
    /// One material, one cart, one leg, one permit.</summary>
    /// <param name="designatedIn">The world load the tour began in, compared by
    /// the permit against <paramref name="world"/>.</param>
    /// <param name="expectedKey">The cart's own network id as the lease names
    /// it. A live cart whose id differs is refused.</param>
    /// <param name="parkedAt">Where the cart stood when the tour began.</param>
    public DepositResult LoadOne(
        Humanoid? worker,
        Vagon? assigned,
        string expectedKey,
        string itemPrefab,
        int wanted,
        NpcWorldEpoch designatedIn,
        NpcWorldEpoch world,
        Func<Vagon?, NpcContainerUse> allowance,
        Func<Vector3?> workerPosition,
        Vector3 parkedAt,
        float reachMetres,
        float mostDriftMetres,
        out NpcContainerRefusal refusal)
    {
        refusal = NpcContainerRefusal.Gone;

        Inventory? gunnar = InventoryOf(worker);
        if (gunnar == null || string.IsNullOrEmpty(itemPrefab) || wanted < 1)
        {
            return new DepositResult(DepositOutcome.Refused, null, "nothing to load, or Gunnar could not be read");
        }

        var subject = new AssignedCartContainer(
            assigned, expectedKey, designatedIn, allowance, workerPosition, parkedAt, reachMetres, mostDriftMetres);

        // THE MINT, for DEPOSIT and for nothing else. Re-asks the cart's own
        // access now - the assignment, this client's ownership, whether anybody
        // has it open or hitched, the ward, the privacy setting, the reach and
        // whether it is still where it stood - and answers null for every
        // refusal. The allowance never answers Take, so nothing here can come
        // OUT of the cart.
        NpcContainerPermit? permit = NpcContainerPermit.Issue(
            subject, NpcContainerUse.Deposit, world, out refusal);
        if (permit == null)
        {
            return new DepositResult(DepositOutcome.Refused, null, CartLoadSentences.Describe(refusal));
        }

        Inventory? cart = InventoryOf(AssignedCartContainer.BedOf(assigned));
        if (cart == null)
        {
            refusal = NpcContainerRefusal.Gone;
            return new DepositResult(DepositOutcome.Refused, null, CartLoadSentences.Describe(refusal));
        }

        int? availableBefore = SafeCount(gunnar, itemPrefab);
        int? roomFor = SafeRoom(cart, itemPrefab, wanted, FirstMatching(gunnar, itemPrefab));
        int? arrivedBefore = SafeCount(cart, itemPrefab);
        if (!availableBefore.HasValue || !roomFor.HasValue || !arrivedBefore.HasValue)
        {
            return new DepositResult(
                DepositOutcome.Refused, null,
                "one of the two inventories could not be counted, so nothing was moved");
        }

        NpcTransferPlan plan = NpcTransferPlan.For(wanted, availableBefore.Value, roomFor.Value);
        if (plan.IsEmpty)
        {
            return new DepositResult(
                DepositOutcome.Refused, null,
                availableBefore.Value < 1
                    ? "he is not carrying any " + itemPrefab
                    : "the cart has no room for " + itemPrefab + "; he keeps it");
        }

        bool faulted = false;
        MoveTally tally;
        try
        {
            tally = Move(worker, assigned, itemPrefab, plan.Units);
        }
        catch (Exception)
        {
            faulted = true;
            tally = MoveTally.Inconsistent;
        }

        int? availableAfter = SafeCount(gunnar, itemPrefab);
        int? arrivedAfter = SafeCount(cart, itemPrefab);
        if (faulted || !availableAfter.HasValue || !arrivedAfter.HasValue)
        {
            return new DepositResult(
                DepositOutcome.Uncertain, null,
                "the move faulted or could not be counted afterwards, so whether it happened is not known; " +
                "nothing is credited, nothing is retried and nothing is put right automatically");
        }

        if (!tally.WasAttempted)
        {
            return new DepositResult(
                DepositOutcome.Refused, null,
                "the two inventories could not be read at the moment of the move, so nothing was " +
                "attempted and nothing changed");
        }

        if (tally.IsInconsistent)
        {
            return new DepositResult(
                DepositOutcome.Uncertain, null,
                "the move reported putting something in the cart and the count could not see it, so what " +
                "happened is not known; nothing is credited, nothing is retried and nothing is put " +
                "right automatically");
        }

        int left = availableBefore.Value - availableAfter.Value;
        int arrived = arrivedAfter.Value - arrivedBefore.Value;

        // Both measured deltas, compared, and the permit spent whichever way it
        // goes. More arriving in the cart than left him is minting; fewer is
        // loss. Either is Uncertain.
        ContainerMoveResult recorded = ContainerMoveResult.Record(permit, world, plan, left, arrived);

        switch (recorded.Outcome)
        {
            case ContainerMoveOutcome.Completed:
                return new DepositResult(DepositOutcome.Deposited, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Partial:
                return new DepositResult(
                    DepositOutcome.PartlyDeposited, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Nothing:
            case ContainerMoveOutcome.Refused:
                return new DepositResult(DepositOutcome.Refused, null, recorded.Evidence);
            default:
                return new DepositResult(DepositOutcome.Uncertain, null, recorded.Evidence);
        }
    }

    /// <summary>Vanilla's own move, whole stacks first and then the remainder.
    ///
    /// <b>The three pinned calls live here and nowhere else.</b> A whole stack
    /// goes through <c>MoveItemToThis</c>, which adds to the cart and removes
    /// from him inside one vanilla call and keeps the moved instance. A partial
    /// stack is cloned for exactly the units wanted, added to the cart, and then
    /// exactly what <b>arrived</b> is removed from his stack.
    ///
    /// <b>It takes the worker and the CART, and derives both inventories
    /// itself</b>, for the reason the deposit's mover gives: two same-typed
    /// inventory parameters can be reversed by renaming them, which turns a load
    /// into the cart into an unload out of it with every pinned call
    /// byte-identical. The two derivations below are pinned by
    /// `#381 cart-load audit`.</summary>
    private MoveTally Move(Humanoid? worker, Vagon? assigned, string itemPrefab, int units)
    {
        Inventory? gunnar = InventoryOf(worker);
        Inventory? cart = InventoryOf(AssignedCartContainer.BedOf(assigned));
        if (gunnar == null || cart == null || ReferenceEquals(gunnar, cart))
        {
            return MoveTally.NothingAttempted;
        }

        int remaining = units;
        bool inconsistent = false;
        Matching(gunnar, itemPrefab, _matching);

        try
        {
            for (int index = 0; index < _matching.Count && remaining > 0; index++)
            {
                ItemDrop.ItemData stack = _matching[index];
                int before = CountIn(cart, itemPrefab);

                if (stack.m_stack <= remaining)
                {
                    int whole = stack.m_stack;
                    cart.MoveItemToThis(gunnar, stack);
                    int moved = CountIn(cart, itemPrefab) - before;
                    remaining -= moved > 0 ? moved : 0;
                    if (moved < whole)
                    {
                        // The cart took less than the whole stack, so it is full.
                        // The rest stays in him, which is the only correct place
                        // for it.
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
                    bool added = cart.AddItem(part);

                    // Vanilla's own number for what arrived, from the decompiled
                    // AddItem the deposit port documents case by case: all of it
                    // when the add answered true, and the merged part when it
                    // answered false.
                    int arrived = added ? offered : offered - (part.m_stack > 0 ? part.m_stack : 0);
                    int moved = CountIn(cart, itemPrefab) - before;

                    if (arrived > 0 && moved == arrived)
                    {
                        // The engine and the measurement agree. Take exactly that
                        // off his stack, never the count asked for.
                        gunnar.RemoveItem(stack, arrived);
                    }
                    else if (arrived > 0 || moved > 0)
                    {
                        // They disagree. Nothing is removed from him on a number
                        // in dispute.
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

    /// <summary>How many of <paramref name="wanted"/> would fit in the cart:
    /// room in stacks vanilla's own add would merge into, plus empty slots. An
    /// estimate that must not over-count; the measured deltas decide.</summary>
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

    /// <summary>Whether a stack is one this load may move, or count. One
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
