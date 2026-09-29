using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The one file in Concerned Teamster allowed to move material <b>out
/// of Gunnar's own inventory into a vanilla container</b> (#381/#374, under the
/// owner's decision of 2026-09-29, `DECISIONS.md` <b>D15</b>).
///
/// <b>Why this file exists, and why it is only this file.</b> Gunnar could pick
/// a loose stone up and could not put it down. The only ways material left him
/// were a reload, which preserves it, and <c>ct_haul retire force</c>, which
/// destroys it and says so. That was a dead end, and #415 asked the one question
/// that ends it: may he move items from his own inventory into a vanilla
/// <c>Container</c> the player explicitly marked <c>Deposit</c> or <c>Both</c>?
/// The answer was yes, scoped to exactly that. The repository's convention is
/// that each vanilla call which moves a player's material gets its own named
/// allowance, confined to one file and pinned verbatim by the validator - which
/// is the difference between an allowance and an exemption. So: one file, three
/// pinned calls, refused everywhere else in this product.
///
/// <b>What it is allowed to do.</b> Move units of one material out of Gunnar's
/// own inventory into one container the player marked, once, having asked the
/// mark at the moment of the move. That is all. It writes no ownership, no mass,
/// no force, no velocity, no position, and no mod data into a vanilla object; it
/// creates no item from a name; it chooses no chest.
///
/// <b>There is no nearest-chest lookup in this file, and that is structural.</b>
/// The destination arrives as an argument together with the key it was
/// designated under, and a live container whose key does not match is
/// <see cref="NpcContainerRefusal.NotThisContainer"/>. Nothing here searches,
/// scans, sorts by distance or falls back. `DECISIONS.md` D15 says "no chest
/// chosen by proximity"; the way to make that true rather than stated is to give
/// this file no way to find one.
///
/// <b>Permission is minted, never re-derived.</b> The player's mark is read
/// through <see cref="ContainerPermissionRuntime.Allowance"/> - the same
/// derivation that wrote it - and handed to
/// <see cref="NpcContainerPermit.Issue"/>, which re-asks the container's own
/// access in the same breath and returns <c>null</c> for every refusal. A
/// refused container therefore yields no token at all, so there is no path from
/// here to a recorded transfer without one. The permit is spent by the transfer
/// that records it, so a replay of the same leg cannot move a second load.
///
/// <b>Add before remove, measured on both sides.</b> Vanilla's own
/// <c>MoveItemToThis</c> does the add and the remove inside one call for a whole
/// stack; a partial stack clones that stack's data for exactly the units wanted,
/// adds it, and then removes exactly what <b>arrived</b>. Either way the counts
/// are taken on both inventories before and after, and
/// <see cref="ContainerMoveResult.Record"/> classifies from those deltas alone.
/// A disagreement in either direction is <see cref="DepositOutcome.Uncertain"/>:
/// nothing credited, nothing retried, nothing compensated, nothing minted. The
/// asymmetry is deliberate and is the most important thing in this file - a
/// crash between the add and the remove leaves a duplicate that the counts
/// expose, where the reverse order would destroy real items with no evidence
/// left.
///
/// <b>A full chest leaves the remainder in him.</b> Never on the floor, never
/// deleted. That is <see cref="NpcTransferPlan"/>'s whole reason for existing and
/// this file does not second-guess it.</summary>
internal sealed class GunnarDepositPort
{
    /// <summary>Whether the engine and the measurement agreed about the move.
    ///
    /// <b>It exists because "measured on both sides" has a blind spot.</b> If the
    /// destination already holds a stack of the same material that the count does
    /// not recognise as the same thing, an add can succeed while both measured
    /// deltas read zero - and the classification from the deltas alone is then
    /// "nothing moved, and nothing was lost", which is the one wrong answer
    /// available. The engine's own yes is kept so the disagreement can be
    /// reported instead of measured away.</summary>
    private readonly struct MoveTally
    {
        private MoveTally(bool attempted, bool inconsistent)
        {
            WasAttempted = attempted;
            IsInconsistent = inconsistent;
        }

        /// <summary>Whether either inventory was touched at all. <b>Kept apart
        /// from <see cref="IsInconsistent"/> because the two deserve different
        /// sentences</b>: "it may or may not have happened" and "it was never
        /// attempted" are not the same thing to tell a player, and a review
        /// caught the second being reported as the first.</summary>
        public bool WasAttempted { get; }

        public bool IsInconsistent { get; }

        public static MoveTally Consistent => new MoveTally(true, false);

        public static MoveTally Inconsistent => new MoveTally(true, true);

        /// <summary>Nothing was tried, because the inventories could not be read
        /// at the moment of the move. Refuses, and says so honestly.</summary>
        public static MoveTally NothingAttempted => new MoveTally(false, true);
    }

    private readonly List<ItemDrop.ItemData> _matching = new List<ItemDrop.ItemData>();

    /// <summary>What Gunnar is carrying that the caller asked about, by item
    /// prefab name, in a deterministic order.
    ///
    /// <b>Read from the body, never from the ledger.</b> An empty body cannot
    /// deposit whatever the accounting believes, and the accounting is what this
    /// is used to check <i>against</i>.
    ///
    /// <b>The filter is not optional, and it is not a convenience.</b> An earlier
    /// version of this returned every item with a drop prefab. Gunnar happens to
    /// carry only picked material today, so "he deposits what he collected" was
    /// true by accident rather than by construction - and the first tool a worker
    /// body is ever issued would have been deposited into the player's chest
    /// along with the stone. #381's manual haul needs a requested cargo filter
    /// anyway; making it the only way to ask means there is no unfiltered
    /// reading to reach for.
    ///
    /// <b>Equipped items are never offered.</b> Moving one out from under the
    /// body is not something any caller here has asked for, and vanilla's own
    /// clone path clears the flag rather than handling it.</summary>
    /// <param name="wanted">The item prefabs that may move. Empty moves
    /// nothing - a filter nobody filled is not permission to move
    /// everything.</param>
    public IReadOnlyList<KeyValuePair<string, int>> Carrying(
        Humanoid? worker, Func<string, bool> wanted, out bool readable)
    {
        readable = true;
        if (wanted == null)
        {
            return Array.Empty<KeyValuePair<string, int>>();
        }

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        // Three situations come back null here and they are not the same: no
        // body, a dead body, and an accessor that THREW. The first two are
        // genuinely nothing to offer, and the gate has its own clause for an
        // absent Gunnar. The third is an unknown amount, and reporting it as
        // nothing is the falsehood the Unreadable clause exists to stop - reached
        // one function over, which is where a review found it.
        Inventory? inventory = ReadInventoryOf(worker, out bool accessorFailed);
        if (accessorFailed)
        {
            readable = false;
            return Array.Empty<KeyValuePair<string, int>>();
        }

        if (inventory == null)
        {
            return Array.Empty<KeyValuePair<string, int>>();
        }

        try
        {
            foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
            {
                string name = PrefabNameOf(stack);
                if (name.Length == 0 || stack.m_stack <= 0 || stack.m_equipped || !wanted(name))
                {
                    continue;
                }

                totals.TryGetValue(name, out int already);
                totals[name] = already + stack.m_stack;
            }
        }
        catch (Exception)
        {
            // A body whose inventory could not be enumerated is carrying an
            // UNKNOWN amount, which is not the same as nothing - and saying
            // "he is not carrying anything" about it is a falsehood of exactly
            // the kind the record-state refusals exist to stop. The caller is
            // told it could not be read, and the gate answers Unreadable.
            readable = false;
            return Array.Empty<KeyValuePair<string, int>>();
        }

        var list = new List<KeyValuePair<string, int>>(totals.Count);
        foreach (KeyValuePair<string, int> entry in totals)
        {
            list.Add(entry);
        }

        list.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return list;
    }

    /// <summary>Moves up to <paramref name="wanted"/> units of one material out
    /// of Gunnar and into the designated container, or says why it will not.
    ///
    /// One material, one container, one leg, one permit. A deposit of two kinds
    /// is two calls, because the player's permission is re-asked for each and a
    /// chest can close between them.</summary>
    /// <param name="designatedIn">The world load the destination was chosen in.
    /// Compared by the permit against <paramref name="world"/>, so a chest
    /// chosen before a reload is refused rather than resolved to whatever
    /// inherited its id.</param>
    /// <param name="expectedKey">The key the destination was designated under.
    /// A live container whose key differs is refused as
    /// <see cref="NpcContainerRefusal.NotThisContainer"/> - which is what stops
    /// a rebuilt or replaced chest inheriting a decision made about a different
    /// one.</param>
    public DepositResult DepositOne(
        Humanoid? worker,
        Container? destination,
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

        Inventory? from = InventoryOf(worker);
        if (from == null || string.IsNullOrEmpty(itemPrefab) || wanted < 1)
        {
            return new DepositResult(DepositOutcome.Refused, null, "nothing to move, or Gunnar could not be read");
        }

        // The epoch the destination was DESIGNATED in, not the one we are in
        // now. Handing both the same value made the permit's own staleness check
        // compare a value with itself, so it could never refuse - the guarantee
        // was being relied on and was not running.
        var subject = new DesignatedContainer(
            destination, expectedKey, designatedIn, allowance, workerPosition, reachMetres);

        // THE MINT. Re-asks the container's own access now - the player's mark,
        // this client's ownership, whether anybody has it open, the ward, the
        // privacy setting and the reach - and answers null for every refusal. A
        // refused container yields no token, so there is no way from here to a
        // recorded transfer without one.
        NpcContainerPermit? permit = NpcContainerPermit.Issue(
            subject, NpcContainerUse.Deposit, world, out refusal);
        if (permit == null)
        {
            return new DepositResult(DepositOutcome.Refused, null, DepositSentences.Describe(refusal));
        }

        Inventory? to = InventoryOf(destination);
        if (to == null)
        {
            refusal = NpcContainerRefusal.Gone;
            return new DepositResult(DepositOutcome.Refused, null, DepositSentences.Describe(refusal));
        }

        int? availableBefore = SafeCount(from, itemPrefab);
        // The stack that will actually move decides what vanilla would merge
        // into; there is no such thing as the room for an abstract item.
        int? roomFor = SafeRoom(to, itemPrefab, wanted, FirstMatching(from, itemPrefab));
        int? arrivedBefore = SafeCount(to, itemPrefab);
        if (!availableBefore.HasValue || !roomFor.HasValue || !arrivedBefore.HasValue)
        {
            return new DepositResult(
                DepositOutcome.Refused, null,
                "one of the two inventories could not be counted, so nothing was moved");
        }

        NpcTransferPlan plan = NpcTransferPlan.For(wanted, availableBefore.Value, roomFor.Value);
        if (plan.IsEmpty)
        {
            // A full chest is an ordinary Tuesday. He keeps what he is carrying
            // and is told which of the two reasons it was.
            return new DepositResult(
                DepositOutcome.Refused, null,
                availableBefore.Value < 1
                    ? "he is not carrying any " + itemPrefab
                    : "that chest has no room for " + itemPrefab + "; he keeps it");
        }

        // The move takes the two ROLES, not two interchangeable `Inventory`
        // parameters, and derives the inventories from them itself. See its own
        // summary for why: a pair of same-typed parameters can be reversed by
        // renaming them, which no text audit can see.
        bool faulted = false;
        MoveTally tally;
        try
        {
            tally = Move(worker, destination, itemPrefab, plan.Units);
        }
        catch (Exception)
        {
            // A fault halfway is exactly what Uncertain is for. The counts below
            // still run, and if they happen to agree the move is classified from
            // them - but a fault that also broke the counting lands on Uncertain
            // rather than on a guess.
            faulted = true;
            tally = MoveTally.Inconsistent;
        }

        int? availableAfter = SafeCount(from, itemPrefab);
        int? arrivedAfter = SafeCount(to, itemPrefab);
        if (faulted || !availableAfter.HasValue || !arrivedAfter.HasValue)
        {
            return new DepositResult(
                DepositOutcome.Uncertain, null,
                "the move faulted or could not be counted afterwards, so whether it happened is not known; " +
                "nothing is credited, nothing is retried and nothing is put right automatically");
        }

        if (!tally.WasAttempted)
        {
            // Neither inventory was touched. Refused rather than uncertain: an
            // uncertain outcome asserts that something may have happened, and
            // here provably nothing did.
            return new DepositResult(
                DepositOutcome.Refused, null,
                "the two inventories could not be read at the moment of the move, so nothing was " +
                "attempted and nothing changed");
        }

        if (tally.IsInconsistent)
        {
            // The engine said it moved something and the measurement could not
            // see it - which happens when the destination already holds a stack
            // of this material that the count does not recognise as the same
            // thing (a null or instance-named drop prefab, say, built by another
            // mod). Both deltas then read zero, and classifying from them alone
            // would answer "nothing moved, and nothing was lost" while the chest
            // has quietly gained a stack. That is the one wrong answer available
            // here, so the disagreement is reported rather than measured away.
            return new DepositResult(
                DepositOutcome.Uncertain, null,
                "the move reported putting something in and the count could not see it, so what " +
                "happened is not known; nothing is credited, nothing is retried and nothing is put " +
                "right automatically");
        }

        int left = availableBefore.Value - availableAfter.Value;
        int arrived = arrivedAfter.Value - arrivedBefore.Value;

        // Both measured deltas, compared. Fewer arrived than left is material
        // lost; more arrived than left is material minted, which is worse. Either
        // is Uncertain, and the permit is spent here whichever way it goes.
        ContainerMoveResult recorded = ContainerMoveResult.Record(permit, world, plan, left, arrived);

        switch (recorded.Outcome)
        {
            case ContainerMoveOutcome.Completed:
                return new DepositResult(
                    DepositOutcome.Deposited, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Partial:
                return new DepositResult(
                    DepositOutcome.PartlyDeposited, One(itemPrefab, recorded.Moved), recorded.Evidence);
            case ContainerMoveOutcome.Nothing:
                return new DepositResult(DepositOutcome.Refused, null, recorded.Evidence);
            case ContainerMoveOutcome.Refused:
                return new DepositResult(DepositOutcome.Refused, null, recorded.Evidence);
            default:
                return new DepositResult(DepositOutcome.Uncertain, null, recorded.Evidence);
        }
    }

    /// <summary>Vanilla's own move, whole stacks first and then the remainder.
    ///
    /// <b>The three pinned calls live here and nowhere else.</b> A whole stack
    /// goes through <c>MoveItemToThis</c>, which adds to the destination and
    /// removes from the source inside one vanilla call and keeps the moved
    /// instance - so a tool's wear, a crafter's name and a world level survive.
    /// A partial stack has no such call, so its data is cloned for exactly the
    /// units wanted, added, and then exactly what <b>arrived</b> is removed from
    /// the original. Never the other way round, and never the number asked
    /// for.</summary>
    private MoveTally Move(Humanoid? worker, Container? destination, string itemPrefab, int units)
    {
        // DERIVED HERE, FROM THE TWO ROLES, AND THIS IS THE POINT.
        //
        // This method used to take `(Inventory from, Inventory to, ...)`. An
        // independent review showed that reversing the deposit into a withdrawal
        // needed no new call and no changed argument: renaming those two
        // parameters to `(Inventory to, Inventory from, ...)` leaves the call
        // site positional and byte-identical, leaves
        // `to.MoveItemToThis(from, stack)` byte-identical, and empties the
        // player's chest into Gunnar with every validator pin green. The guard
        // that was supposed to stop it lived in the caller, over the caller's own
        // locals, so it passed too - and a comment there claimed the property was
        // closed when it was not.
        //
        // Two same-typed parameters are reversible by renaming. Two ROLES are
        // not: `from` can only be Gunnar because it is assigned from the worker,
        // and `to` can only be the chest because it is assigned from the
        // destination. Reversing it now means editing one of the two lines below,
        // and `#381 deposit-mint audit` pins both verbatim.
        Inventory? from = InventoryOf(worker);
        Inventory? to = InventoryOf(destination);
        if (from == null || to == null || ReferenceEquals(from, to))
        {
            // Nothing is touched on this path, so it must not be reported as a
            // transfer whose outcome is unknown.
            return MoveTally.NothingAttempted;
        }

        int remaining = units;
        bool inconsistent = false;
        Matching(from, itemPrefab, _matching);

        try
        {
            for (int index = 0; index < _matching.Count && remaining > 0; index++)
            {
                ItemDrop.ItemData stack = _matching[index];
                int before = CountIn(to, itemPrefab);

                if (stack.m_stack <= remaining)
                {
                    int whole = stack.m_stack;
                    to.MoveItemToThis(from, stack);
                    int moved = CountIn(to, itemPrefab) - before;
                    remaining -= moved > 0 ? moved : 0;
                    if (moved < whole)
                    {
                        // The destination took less than the whole stack, so it
                        // is full or refusing. Stopping here leaves the rest with
                        // him, which is the only correct place for it.
                        break;
                    }
                }
                else
                {
                    // Part of a stack, which vanilla has no single call for. The
                    // clone is of THIS stack - the one the next line removes from
                    // - so nothing is built from a name and total units are
                    // conserved across the pair.
                    ItemDrop.ItemData part = stack.Clone();
                    part.m_stack = remaining;
                    part.m_equipped = false;
                    int offered = part.m_stack;
                    bool added = to.AddItem(part);

                    // HOW MANY UNITS VANILLA SAYS ARRIVED, DERIVED FROM WHAT
                    // VANILLA ACTUALLY DOES RATHER THAN FROM WHAT IT SOUNDS LIKE
                    // IT DOES.
                    //
                    // Decompiled from the installed `assembly_valheim.dll`,
                    // `Inventory.AddItem(ItemData)` merges unit by unit into
                    // existing stacks and writes `item.m_stack` in ONE place
                    // only - the iteration where merging stops:
                    //
                    //     for (int i = 0; i < item.m_stack; i++) {
                    //         var free = FindFreeStackItem(...);
                    //         if (free != null) { free.m_stack++; continue; }
                    //         item.m_stack = item.m_stack - i;      // here, once
                    //         if (FindEmptySlot(...) found) m_inventory.Add(item);
                    //         else flag = false;
                    //         break;
                    //     }
                    //
                    // Four cases, for an offered stack of N:
                    //   A all N merge into existing stacks  -> m_stack N, flag true,  N arrived
                    //   B no merge, clone placed in a slot  -> m_stack N, flag true,  N arrived
                    //   C i merge, remainder placed         -> m_stack N-i, flag true, N arrived
                    //   D i merge, no slot for the rest     -> m_stack N-i, flag false, i arrived
                    //
                    // So the engine's own number is `offered` when it returned
                    // true, and the merged part - `offered - part.m_stack` - when
                    // it returned false.
                    //
                    // THE PREVIOUS VERSION OF THIS BLOCK GOT IT WRONG AND MINTED.
                    // It asserted that vanilla decrements the clone as it merges,
                    // and computed `taken = offered - part.m_stack` as the amount
                    // that moved. In cases A and B - the two ordinary successful
                    // deposits - that is ZERO, so the removal was skipped while
                    // the chest kept the units. Gunnar lost nothing, the chest
                    // gained everything, and the player was told the outcome was
                    // unknown. The premise was reasoned about; it should have been
                    // decompiled, which is what the comment above now is.
                    int arrived = added ? offered : offered - (part.m_stack > 0 ? part.m_stack : 0);
                    int moved = CountIn(to, itemPrefab) - before;

                    if (arrived > 0 && moved == arrived)
                    {
                        // The engine and the measurement agree. Remove exactly
                        // that, never the count asked for.
                        from.RemoveItem(stack, arrived);
                    }
                    else if (arrived > 0 || moved > 0)
                    {
                        // They disagree. The case this catches that a count alone
                        // cannot: the destination already holds a stack of this
                        // material that `CountIn` does not recognise as the same
                        // thing, so everything merges into it and `moved` reads
                        // zero. Classifying from `moved` alone would answer
                        // "nothing moved, and nothing was lost" while the chest
                        // had gained. Nothing is removed from him on a number in
                        // dispute, and the caller must not read this as nothing
                        // happening.
                        inconsistent = true;
                    }

                    // arrived == 0 && moved == 0: the chest had no room and took
                    // nothing. An ordinary full chest, and the remainder stays
                    // with him, which is where it should be.

                    break;
                }
            }
        }
        finally
        {
            // In a finally so a throw does not leave the list holding references
            // to stacks that are now in a player's chest.
            _matching.Clear();
        }

        return inconsistent ? MoveTally.Inconsistent : MoveTally.Consistent;
    }

    // ------------------------------------------------------------------
    // Reading, all of it wrapped: a game state that cannot be read is a
    // refusal, never a zero.
    // ------------------------------------------------------------------

    private static Inventory? InventoryOf(Humanoid? worker) => ReadInventoryOf(worker, out _);

    /// <summary>The worker's inventory, telling an absent or dead body apart from
    /// one whose accessor threw.
    ///
    /// <b>The distinction is not cosmetic.</b> A null from a throw means the
    /// amount he is carrying is unknown; a null from a dead or missing body means
    /// there is nothing to carry. Collapsing them is how "he is not carrying
    /// anything" gets said about a Gunnar who is holding something.</summary>
    private static Inventory? ReadInventoryOf(Humanoid? worker, out bool accessorFailed)
    {
        accessorFailed = false;
        try
        {
            return worker == null || worker.IsDead() ? null : worker.GetInventory();
        }
        catch (Exception)
        {
            accessorFailed = true;
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

    /// <summary>How many of <paramref name="wanted"/> would fit: room in stacks
    /// vanilla's own add would merge into, plus empty slots.
    ///
    /// An estimate, deliberately - the measured deltas are what decides. What it
    /// must not do is over-count, so a stack the game would refuse to merge into
    /// (a different quality, world level, or a cheated one) contributes
    /// nothing.</summary>
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

            // Vanilla merges on the MOVING item's quality and world level, not on
            // the global level or on the ObjectDB template's. Using those meant
            // the estimate missed stacks vanilla would merge into AND counted
            // stacks it would refuse - in any world past a boss, which is the
            // ordinary case rather than the exotic one.
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

    /// <summary>Whether a stack is one this deposit may move.
    ///
    /// <b>One predicate, used by every side.</b> An earlier version had
    /// <see cref="Carrying"/> exclude equipped stacks while the counting and the
    /// moving did not, so "equipped items are never offered" was true of what was
    /// REPORTED and false of what would actually have been taken - which is the
    /// same shape as the tool defect the filter was added to fix, one layer
    /// down.</summary>
    private static bool Movable(ItemDrop.ItemData? stack, string itemPrefab) =>
        stack != null
        && stack.m_stack > 0
        && !stack.m_equipped
        && string.Equals(PrefabNameOf(stack), itemPrefab, StringComparison.Ordinal);

    /// <summary>The first stack that would move, for asking the destination what
    /// it would merge into.</summary>
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

    /// <summary>Matching stacks in grid order, copied into a list, so which
    /// stack moves first never depends on the live list and the move is free to
    /// change it underneath.</summary>
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
    /// none - which is refused rather than matched, because an item that cannot
    /// name itself must not be moved by a name somebody else assumed.</summary>
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

    /// <summary>The designated destination, as the shared library is allowed to
    /// see it.
    ///
    /// <b><see cref="Access"/> is a property and is re-read on every get</b>,
    /// which is the contract <see cref="INpcContainer"/> states and the reason
    /// the permit mint can be trusted: between the tick that chose this chest and
    /// this one, the player can have walked into it, a ward can have gone up, the
    /// chest can have been destroyed, ownership can have migrated.</summary>
    private sealed class DesignatedContainer : INpcContainer
    {
        private readonly Container? _container;
        private readonly string _expectedKey;
        private readonly NpcWorldEpoch _epoch;
        private readonly Func<Container?, NpcContainerUse> _allowance;
        private readonly Func<Vector3?> _workerPosition;
        private readonly float _reachMetres;

        public DesignatedContainer(
            Container? container,
            string expectedKey,
            NpcWorldEpoch epoch,
            Func<Container?, NpcContainerUse> allowance,
            Func<Vector3?> workerPosition,
            float reachMetres)
        {
            _container = container;
            _expectedKey = expectedKey ?? string.Empty;
            _epoch = epoch;
            _allowance = allowance;
            _workerPosition = workerPosition;
            _reachMetres = reachMetres;
        }

        public string Key => _expectedKey;

        public NpcWorldEpoch Epoch => _epoch;

        public string Describe => "the chest you chose";

        public NpcPoint Position
        {
            get
            {
                try
                {
                    if (_container == null)
                    {
                        return default;
                    }

                    Vector3 at = _container.transform.position;
                    return new NpcPoint(at.x, at.y, at.z);
                }
                catch (Exception)
                {
                    return default;
                }
            }
        }

        /// <summary>What the player allows and what the world allows, as of now.
        /// The order is the order a player would fix them in, and <b>every
        /// unknown refuses</b>.</summary>
        public NpcContainerAccess Access
        {
            get
            {
                try
                {
                    if (_container == null || _container.GetInventory() == null)
                    {
                        return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
                    }

                    if (!string.Equals(LiveKeyOf(_container), _expectedKey, StringComparison.Ordinal)
                        || _expectedKey.Length == 0)
                    {
                        // Something stands there; it is not what was chosen.
                        return new NpcContainerAccess(
                            NpcContainerUse.Off, NpcContainerRefusal.NotThisContainer);
                    }

                    NpcContainerUse allowed = _allowance(_container);

                    if (!_container.IsOwner())
                    {
                        return new NpcContainerAccess(allowed, NpcContainerRefusal.NotOwnedHere);
                    }

                    // A non-owner write is discarded silently, and an open chest
                    // reloads over what was added. A chest riding a cart that is
                    // in use counts as in use too.
                    if (_container.IsInUse()
                        || (_container.m_wagon != null && _container.m_wagon.InUse()))
                    {
                        return new NpcContainerAccess(allowed, NpcContainerRefusal.InUse);
                    }

                    if (!WardAllows(_container))
                    {
                        return new NpcContainerAccess(allowed, NpcContainerRefusal.WardDenied);
                    }

                    if (!PrivacyAllows(_container))
                    {
                        return new NpcContainerAccess(allowed, NpcContainerRefusal.PrivacyDenied);
                    }

                    return WithinReach()
                        ? new NpcContainerAccess(allowed, NpcContainerRefusal.None)
                        : new NpcContainerAccess(allowed, NpcContainerRefusal.OutOfReach);
                }
                catch (Exception)
                {
                    // A container that cannot say what it is has not said yes.
                    return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
                }
            }
        }

        private bool WithinReach()
        {
            Vector3? worker = _workerPosition();
            if (!worker.HasValue || _container == null)
            {
                return false;
            }

            Vector3 flat = worker.Value - _container.transform.position;
            flat.y = 0f;
            return flat.magnitude <= _reachMetres;
        }

        /// <summary>The container's identity within this world load: its own
        /// network record's id. Empty when there is none, which the caller reads
        /// as "not the chest that was chosen".</summary>
        internal static string LiveKeyOf(Container? container)
        {
            try
            {
                ZNetView? view = container == null ? null : container.GetComponent<ZNetView>();
                ZDO? record = view == null || !view.IsValid() ? null : view.GetZDO();
                return record == null ? string.Empty : record.m_uid.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>The container's ward check, exactly where vanilla makes it
        /// before opening one, without the flash.</summary>
        private static bool WardAllows(Container container) =>
            !container.m_checkGuardStone
            || PrivateArea.CheckAccess(container.transform.position, 0f, flash: false, wardCheck: false);

        /// <summary>Vanilla's container privacy, mirrored because the method is
        /// private: public for everyone, private for its builder, and anything
        /// else for nobody here.</summary>
        private static bool PrivacyAllows(Container container)
        {
            switch (container.m_privacy)
            {
                case Container.PrivacySetting.Public:
                    return true;
                case Container.PrivacySetting.Private:
                {
                    Game game = Game.instance;
                    Piece? piece = container.GetComponent<Piece>();
                    return game != null
                        && piece != null
                        && piece.GetCreator() == game.GetPlayerProfile().GetPlayerID();
                }

                default:
                    return false;
            }
        }
    }

    /// <summary>The container's identity within this world load, for a caller
    /// that is designating one rather than using one.</summary>
    public static string KeyOf(Container? container) => DesignatedContainer.LiveKeyOf(container);
}
