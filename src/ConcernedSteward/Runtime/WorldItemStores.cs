using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>An <see cref="IItemStorePort"/> over one real Valheim inventory.
///
/// <b>Counting the way vanilla matches.</b> Every count here enumerates
/// <c>GetAllItems</c> and compares <c>m_shared.m_name</c>, because that is
/// exactly the predicate <c>Fireplace.UseItem</c> uses when it decides whether
/// an item is this fire's fuel. <c>Inventory.CountItems</c> and
/// <c>HaveItem</c> additionally filter on <c>m_worldLevel</c>, so on a world
/// whose level has been raised they answer zero for stacks the fire would burn.
/// A conservation argument measured with one predicate and mutated with another
/// is not a conservation argument.
///
/// <b>Moving through vanilla's own move.</b> A whole stack goes through
/// <c>Inventory.MoveItemToThis</c>, which adds to the destination and removes
/// from the source inside one call; part of a stack is cloned for exactly the
/// units moved, added first, and only then taken off the source. Add before
/// remove, always: a failure between the two leaves a duplicate that the
/// measurements expose and a person can resolve, where the other order destroys
/// real items and leaves no evidence that it did.
///
/// Nothing here returns a success flag the loop is asked to believe. The loop
/// counts both sides before and after and decides from the deltas.</summary>
internal abstract class WorldItemStore : IItemStorePort
{
    public abstract string Describe { get; }

    public abstract bool IsAvailable { get; }

    /// <summary>The engine inventory, or null when this store is not usable.
    /// </summary>
    internal abstract Inventory? Engine { get; }

    /// <summary>Called after a change to this inventory. Throws when the change
    /// could not be made durable, so the loop records the step as uncertain
    /// instead of completed.</summary>
    internal virtual void VerifyPersisted()
    {
    }

    public IReadOnlyCollection<string> ItemNames
    {
        get
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            Inventory? inventory = Engine;
            if (inventory == null)
            {
                // Unreadable means "stocks nothing", which makes every fire
                // ineligible. That is the fail-closed direction.
                return names;
            }

            foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
            {
                if (stack != null && stack.m_shared != null && stack.m_stack > 0
                    && !string.IsNullOrEmpty(stack.m_shared.m_name))
                {
                    names.Add(stack.m_shared.m_name);
                }
            }

            return names;
        }
    }

    public int Count(string fuelItemName)
    {
        Inventory? inventory = Engine;
        if (inventory == null || string.IsNullOrEmpty(fuelItemName))
        {
            return -1;
        }

        return CountIn(inventory, fuelItemName);
    }

    public virtual int RoomFor(string fuelItemName, int count)
    {
        Inventory? inventory = Engine;
        if (inventory == null || count < 1)
        {
            return 0;
        }

        ItemDrop.ItemData? sample = FirstMatching(inventory, fuelItemName);
        if (sample == null)
        {
            // Nothing of this kind is here to measure a stack size from. An
            // empty slot takes at least one, which is all the loop needs to
            // decide whether to try; the measured delta decides the rest.
            return inventory.GetEmptySlots() > 0 ? count : 0;
        }

        int maxStack = Math.Max(1, sample.m_shared.m_maxStackSize);
        long room = 0;
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (Matches(stack, fuelItemName) && stack.m_stack < stack.m_shared.m_maxStackSize)
            {
                room += stack.m_shared.m_maxStackSize - stack.m_stack;
            }
        }

        room += (long)inventory.GetEmptySlots() * maxStack;
        return (int)Math.Min(count, Math.Max(0L, room));
    }

    public void MoveTo(IItemStorePort destination, string fuelItemName, int count)
    {
        if (destination is not WorldItemStore target)
        {
            throw new InvalidOperationException(
                "the Steward can only move items between two real inventories");
        }

        Inventory? source = Engine;
        Inventory? into = target.Engine;
        if (source == null || into == null)
        {
            throw new InvalidOperationException("an inventory went away before the move");
        }

        if (ReferenceEquals(source, into))
        {
            throw new InvalidOperationException("a move needs two different inventories");
        }

        int remaining = count;
        foreach (ItemDrop.ItemData stack in MatchingInGridOrder(source, fuelItemName))
        {
            if (remaining <= 0)
            {
                break;
            }

            int before = CountIn(into, fuelItemName);
            if (stack.m_stack <= remaining)
            {
                int whole = stack.m_stack;
                into.MoveItemToThis(source, stack);
                int moved = CountIn(into, fuelItemName) - before;
                remaining -= moved;
                if (moved < whole)
                {
                    // The destination filled up part way through. Stop rather
                    // than shuffle the rest around; the loop measures what
                    // arrived and records a partial move.
                    break;
                }
            }
            else
            {
                // Part of a stack. Cloned so the moved units keep the metadata
                // the original carried, added to the destination first, and
                // only exactly what arrived taken off the source.
                ItemDrop.ItemData part = stack.Clone();
                part.m_stack = remaining;
                part.m_equipped = false;
                into.AddItem(part);
                int moved = CountIn(into, fuelItemName) - before;
                if (moved > 0)
                {
                    source.RemoveItem(stack, moved);
                }

                remaining -= moved;
                break;
            }
        }

        VerifyPersisted();
        target.VerifyPersisted();
    }

    internal static int CountIn(Inventory inventory, string fuelItemName)
    {
        int total = 0;
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (Matches(stack, fuelItemName))
            {
                total += stack.m_stack;
            }
        }

        return total;
    }

    /// <summary>Vanilla's own predicate at the point of mutation: the shared
    /// name, and nothing else.</summary>
    private static bool Matches(ItemDrop.ItemData? stack, string fuelItemName) =>
        stack != null && stack.m_shared != null && stack.m_stack > 0
        && string.Equals(stack.m_shared.m_name, fuelItemName, StringComparison.Ordinal);

    private static ItemDrop.ItemData? FirstMatching(Inventory inventory, string fuelItemName)
    {
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (Matches(stack, fuelItemName))
            {
                return stack;
            }
        }

        return null;
    }

    /// <summary>Matching stacks in grid order, copied into a list first, so
    /// which stack moves is never a function of list order and the move is free
    /// to change the list underneath.</summary>
    private static List<ItemDrop.ItemData> MatchingInGridOrder(Inventory inventory, string fuelItemName)
    {
        var list = new List<ItemDrop.ItemData>();
        foreach (ItemDrop.ItemData stack in inventory.GetAllItemsInGridOrder())
        {
            if (Matches(stack, fuelItemName))
            {
                list.Add(stack);
            }
        }

        return list;
    }
}

/// <summary>The Steward's own pack.
///
/// Unavailable the moment anything about his body is wrong — not loaded, not
/// owned here, faulted, dead, or holding a change that could not be written to
/// his own network object. The last one matters most: an inventory change that
/// did not persist is a change a reload will undo, and treating it as done is
/// how a worker's hands and his record stop agreeing.</summary>
internal sealed class StewardPackStore : WorldItemStore
{
    private readonly Func<StewardBody?> _body;

    internal StewardPackStore(Func<StewardBody?> body)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
    }

    private StewardBody? Body
    {
        get
        {
            try
            {
                return _body();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public override string Describe => "the Steward's pack";

    public override bool IsAvailable
    {
        get
        {
            StewardBody? body = Body;
            return body != null
                && body.IsLoaded
                && body.Fault == null
                && body.IsOwned
                && body.LastChangePersisted
                && body.Humanoid != null
                && !body.Humanoid.IsDead();
        }
    }

    internal override Inventory? Engine => Body?.Inventory;

    internal override void VerifyPersisted()
    {
        StewardBody? body = Body;
        if (body == null || !body.LastChangePersisted)
        {
            throw new InvalidOperationException(
                "the Steward's inventory change could not be written to her own body");
        }
    }
}

/// <summary>The one chest the player marked as the supply depot.
///
/// <b>Resolved by identity, never by position and never by "the nearest".</b>
/// The key is checked against the container the key found, so a key that has
/// come to name a different chest resolves to nothing rather than to that
/// chest.
///
/// Refuses while a player has it open: racing an open inventory window would
/// mean the player and the Steward each measuring a chest the other is
/// changing.</summary>
internal sealed class DepotStore : WorldItemStore
{
    private readonly Func<string> _depotKey;

    internal DepotStore(Func<string> depotKey)
    {
        _depotKey = depotKey ?? throw new ArgumentNullException(nameof(depotKey));
    }

    private Container? Chest
    {
        get
        {
            try
            {
                return StewardIdentity.FindContainer(_depotKey());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public override string Describe => "the supply chest";

    public override bool IsAvailable
    {
        get
        {
            Container? chest = Chest;
            return chest != null && chest.IsOwner() && !chest.IsInUse() && chest.GetInventory() != null;
        }
    }

    internal override Inventory? Engine
    {
        get
        {
            Container? chest = Chest;
            if (chest == null || !chest.IsOwner() || chest.IsInUse())
            {
                return null;
            }

            return chest.GetInventory();
        }
    }
}
