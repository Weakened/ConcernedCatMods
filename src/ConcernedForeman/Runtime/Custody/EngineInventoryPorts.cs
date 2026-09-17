using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Tools;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>A vanilla <c>Inventory</c> behind the custody port (CONTRACTS.md
/// §5.2), verified against the installed 1.0.12 <c>Inventory</c>.
///
/// <b>Counting.</b> A material unit matches by the item's own drop prefab name,
/// quality and variant, across every world level (the conservation deltas must
/// not depend on <c>Game.m_worldLevel</c>). A tool never matches a material,
/// so a worker's issued axe is excluded from every count by construction.
///
/// <b>Adding.</b> <see cref="Add"/> refuses to create anything: a port has no
/// item to add, and making one from the prefab would mint a stack that never
/// existed and drop what the real one carried. Every transfer between two
/// engine inventories goes through <see cref="MoveFrom"/> — vanilla
/// <c>MoveItemToThis</c> per whole stack (add, then remove, inside one call),
/// or for part of a stack a clone of that stack's data added first and exactly
/// the added count removed after. The executor classifies from both
/// inventories' counts, never from what this says.</summary>
internal abstract class EngineInventoryPort : IInventoryPort, IInventoryMoveTarget
{
    public abstract string Describe { get; }

    public abstract bool IsAvailable { get; }

    /// <summary>The live vanilla inventory, or null when there is none now.
    /// </summary>
    internal abstract Inventory? Engine { get; }

    /// <summary>Units of an item that belong to nobody's order here: a cart's
    /// pre-existing cargo. Zero elsewhere.</summary>
    protected virtual int Baseline(MaterialItem item) => 0;

    /// <summary>Called after an engine change touching this inventory; throws
    /// when the change could not be made durable, so the executor records the
    /// transfer as uncertain rather than completed.</summary>
    internal virtual void VerifyPersisted()
    {
    }

    public int Count(MaterialItem item)
    {
        Inventory? inventory = Engine;
        if (inventory == null)
        {
            throw new InvalidOperationException(Describe + " has no inventory now");
        }

        return Math.Max(0, CountIn(inventory, item) - Baseline(item));
    }

    public virtual int CanAccept(MaterialItem item, int count)
    {
        Inventory? inventory = Engine;
        GameObject? prefab = ItemPrefab(item);
        if (inventory == null || prefab == null || count < 1)
        {
            return 0;
        }

        ItemDrop.ItemData template = prefab.GetComponent<ItemDrop>().m_itemData;
        int maxStack = Math.Max(1, template.m_shared.m_maxStackSize);

        // Room in existing stacks that vanilla's AddItem would merge into, plus
        // empty slots. 1.0.14 merges only into a stack with the same shared
        // name, quality, world level AND cheated flag; gathered material is
        // never cheated, so a cheated stack's room is not counted. An estimate
        // either way: the executor classifies from the counts.
        long room = 0;
        foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
        {
            if (existing.m_shared.m_name == template.m_shared.m_name
                && existing.m_quality == item.Quality
                && existing.m_worldLevel == Game.m_worldLevel
                && !existing.m_cheated)
            {
                room += Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack);
            }
        }

        room += (long)inventory.GetEmptySlots() * maxStack;
        return (int)Math.Max(0, Math.Min(count, room));
    }

    /// <summary>Refuses: see the class summary.</summary>
    public int Add(MaterialItem item, int count) => 0;

    public int Remove(MaterialItem item, int count)
    {
        Inventory? inventory = Engine;
        if (inventory == null || count < 1)
        {
            return 0;
        }

        int remaining = Math.Min(count, Count(item));
        int removed = 0;
        foreach (ItemDrop.ItemData stack in Matching(inventory, item))
        {
            if (remaining <= 0)
            {
                break;
            }

            int take = Math.Min(remaining, stack.m_stack);
            if (inventory.RemoveItem(stack, take))
            {
                removed += take;
                remaining -= take;
            }
        }

        VerifyPersisted();
        return removed;
    }

    public bool CanMoveFrom(IInventoryPort source) =>
        source is EngineInventoryPort other && other.Engine != null && Engine != null && !ReferenceEquals(other.Engine, Engine);

    public void MoveFrom(IInventoryPort source, MaterialItem item, int count)
    {
        var from = (EngineInventoryPort)source;
        Inventory? sourceInventory = from.Engine;
        Inventory? destination = Engine;
        if (sourceInventory == null || destination == null)
        {
            throw new InvalidOperationException("an inventory went away before the move");
        }

        int remaining = count;
        foreach (ItemDrop.ItemData stack in Matching(sourceInventory, item))
        {
            if (remaining <= 0)
            {
                break;
            }

            int before = CountIn(destination, item);
            if (stack.m_stack <= remaining)
            {
                // Vanilla's own add-then-remove, keeping the instance.
                int whole = stack.m_stack;
                destination.MoveItemToThis(sourceInventory, stack);
                int moved = CountIn(destination, item) - before;
                remaining -= moved;
                if (moved < whole)
                {
                    break;
                }
            }
            else
            {
                // Part of a stack: its data cloned for exactly the units moved,
                // added first; then exactly what arrived is taken off the stack.
                ItemDrop.ItemData part = stack.Clone();
                part.m_stack = remaining;
                part.m_equipped = false;
                destination.AddItem(part);
                int moved = CountIn(destination, item) - before;
                if (moved > 0)
                {
                    sourceInventory.RemoveItem(stack, moved);
                }

                remaining -= moved;
                break;
            }
        }

        from.VerifyPersisted();
        VerifyPersisted();
    }

    internal static int CountIn(Inventory inventory, MaterialItem item)
    {
        int total = 0;
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (Matches(stack, item))
            {
                total += stack.m_stack;
            }
        }

        return total;
    }

    /// <summary>Matching stacks in grid order, copied, so which stack moves
    /// first never depends on list order and the move can change the list.
    /// </summary>
    private static List<ItemDrop.ItemData> Matching(Inventory inventory, MaterialItem item)
    {
        var list = new List<ItemDrop.ItemData>();
        foreach (ItemDrop.ItemData stack in inventory.GetAllItemsInGridOrder())
        {
            if (Matches(stack, item))
            {
                list.Add(stack);
            }
        }

        return list;
    }

    internal static bool Matches(ItemDrop.ItemData stack, MaterialItem item)
    {
        return stack != null
            && stack.m_dropPrefab != null
            && string.Equals(stack.m_dropPrefab.name, item.PrefabName, StringComparison.Ordinal)
            && stack.m_quality == item.Quality
            && stack.m_variant == item.Variant
            && ToolClassifier.Classify(stack) == ToolKind.None;
    }

    internal static GameObject? ItemPrefab(MaterialItem item)
    {
        ObjectDB db = ObjectDB.instance;
        return db == null ? null : db.GetItemPrefab(item.PrefabName);
    }

    /// <summary>Vanilla's container privacy, mirrored because the method is
    /// private: public for everyone, private for its builder, group for nobody
    /// here.</summary>
    internal static bool PrivacyAllows(Container container)
    {
        switch (container.m_privacy)
        {
            case Container.PrivacySetting.Public:
                return true;

            case Container.PrivacySetting.Private:
            {
                Game game = Game.instance;
                Piece? piece = container.GetComponent<Piece>();
                return game != null && piece != null && piece.GetCreator() == game.GetPlayerProfile().GetPlayerID();
            }

            default:
                return false;
        }
    }

    /// <summary>A container's ward check exactly where vanilla makes it before
    /// opening one, without the flash.</summary>
    internal static bool WardAllows(Container container) =>
        !container.m_checkGuardStone || PrivateArea.CheckAccess(container.transform.position, 0f, flash: false, wardCheck: false);

    internal static bool WithinReach(Func<Vector3?>? workerPosition, Vector3 target, float reach)
    {
        if (workerPosition == null)
        {
            return true;
        }

        Vector3? worker = workerPosition();
        if (!worker.HasValue)
        {
            return false;
        }

        Vector3 flat = worker.Value - target;
        flat.y = 0f;
        return flat.magnitude <= reach;
    }
}

/// <summary>The worker body's persisted inventory (D9).</summary>
internal sealed class WorkerInventoryPort : EngineInventoryPort
{
    private readonly WorkerBody _body;
    private readonly Func<float> _carryWeight;

    public WorkerInventoryPort(WorkerBody body, Func<float> carryWeight)
    {
        _body = body;
        _carryWeight = carryWeight;
    }

    internal WorkerBody Body => _body;

    public override string Describe => _body == null ? "the worker" : "the worker (" + _body.Key + ")";

    public override bool IsAvailable =>
        _body != null
        && _body.IsLoaded
        && _body.Fault == null
        && _body.IsOwned
        && _body.LastChangePersisted
        && _body.Humanoid != null
        && !_body.Humanoid.IsDead();

    internal override Inventory? Engine => _body == null ? null : _body.Inventory;

    /// <summary>The real fit, and the documented worker carry budget
    /// (<c>Collection/WorkerCarryWeight</c>, default 100: 50 Stone or 50 Wood).
    /// Tools he was issued do not count against it.</summary>
    public override int CanAccept(MaterialItem item, int count)
    {
        int fit = base.CanAccept(item, count);
        Inventory? inventory = Engine;
        GameObject? prefab = ItemPrefab(item);
        if (inventory == null || prefab == null)
        {
            return 0;
        }

        float unit = prefab.GetComponent<ItemDrop>().m_itemData.GetWeight(1);
        if (!(unit > 0f))
        {
            return fit;
        }

        float carried = 0f;
        foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
        {
            if (ToolClassifier.Classify(stack) == ToolKind.None)
            {
                carried += stack.GetWeight();
            }
        }

        float budget = _carryWeight();
        int byWeight = (int)Math.Floor(Math.Max(0f, budget - carried) / unit);
        return Math.Max(0, Math.Min(fit, byWeight));
    }

    internal override void VerifyPersisted()
    {
        if (_body == null || !_body.LastChangePersisted)
        {
            throw new InvalidOperationException("the worker's inventory change could not be written to his body");
        }
    }
}

/// <summary>A player-selected container (a delivery chest).</summary>
internal sealed class ContainerInventoryPort : EngineInventoryPort
{
    private readonly Container _container;
    private readonly string _key;
    private readonly Func<Vector3?>? _workerPosition;
    private readonly float _reach;

    public ContainerInventoryPort(Container container, string key, Func<Vector3?>? workerPosition, float reach)
    {
        _container = container;
        _key = key;
        _workerPosition = workerPosition;
        _reach = reach;
    }

    public override string Describe => "the chest";

    internal Container Container => _container;

    /// <summary>Why it is not available, for the attention reason; null when it
    /// is.</summary>
    internal string? Unavailable
    {
        get
        {
            if (_container == null || _container.GetInventory() == null)
            {
                return "gone";
            }

            if (!string.Equals(SettlementTargets.TryIdentify(_container), _key, StringComparison.Ordinal))
            {
                return "not the same chest";
            }

            if (!_container.IsOwner())
            {
                return "not owned here";
            }

            // A non-owner write is silently discarded by the game, and an open
            // chest reloads over what was added; a chest on a cart that is being
            // pulled or opened is in use too.
            if (_container.IsInUse() || (_container.m_wagon != null && _container.m_wagon.InUse()))
            {
                return "in use";
            }

            if (!WardAllows(_container) || !PrivacyAllows(_container))
            {
                return "access denied";
            }

            return WithinReach(_workerPosition, _container.transform.position, _reach) ? null : "out of reach";
        }
    }

    public override bool IsAvailable => Unavailable == null;

    internal override Inventory? Engine => _container == null ? null : _container.GetInventory();
}

/// <summary>A leased cart's container, above its recorded pre-existing cargo
/// (D8). Loading and unloading happen while the hauler holds the cart still, so
/// being attached to a worker is not "in use"; a player pulling it, or anybody
/// having it open, is.</summary>
internal sealed class CartInventoryPort : EngineInventoryPort
{
    private readonly Vagon _cart;
    private readonly Func<MaterialItem, int?> _baseline;
    private readonly Func<Vector3?>? _workerPosition;
    private readonly float _reach;

    public CartInventoryPort(Vagon cart, Func<MaterialItem, int?> baseline, Func<Vector3?>? workerPosition, float reach)
    {
        _cart = cart;
        _baseline = baseline;
        _workerPosition = workerPosition;
        _reach = reach;
    }

    public override string Describe => "the cart";

    internal string? Unavailable
    {
        get
        {
            if (_cart == null || _cart.m_container == null || _cart.m_container.GetInventory() == null)
            {
                return "gone";
            }

            Container container = _cart.m_container;
            if (!container.IsOwner())
            {
                return "not owned here";
            }

            if (container.IsInUse())
            {
                return "open";
            }

            try
            {
                Player player = Player.m_localPlayer;
                if (player != null && _cart.IsAttached(player))
                {
                    return "pulled by the player";
                }
            }
            catch (Exception)
            {
                return "its attachment could not be checked";
            }

            if (!WardAllows(container) || !PrivacyAllows(container))
            {
                return "access denied";
            }

            return WithinReach(_workerPosition, _cart.transform.position, _reach) ? null : "out of reach";
        }
    }

    public override bool IsAvailable => Unavailable == null;

    internal override Inventory? Engine =>
        _cart == null || _cart.m_container == null ? null : _cart.m_container.GetInventory();

    /// <summary>With no baseline recorded, everything in the cart is treated as
    /// pre-existing: nothing can be counted, taken or credited until the order
    /// records one.</summary>
    protected override int Baseline(MaterialItem item)
    {
        int? recorded = _baseline(item);
        if (recorded.HasValue)
        {
            return recorded.Value;
        }

        Inventory? inventory = Engine;
        return inventory == null ? 0 : CountIn(inventory, item);
    }
}

/// <summary>The local player, for hold-for-player handovers.</summary>
internal sealed class PlayerInventoryPort : EngineInventoryPort
{
    private readonly Player _player;
    private readonly Func<Vector3?>? _workerPosition;
    private readonly float _reach;

    public PlayerInventoryPort(Player player, Func<Vector3?>? workerPosition, float reach)
    {
        _player = player;
        _workerPosition = workerPosition;
        _reach = reach;
    }

    public override string Describe => "you";

    public override bool IsAvailable =>
        _player != null
        && !_player.IsDead()
        && _player.GetInventory() != null
        && WithinReach(_workerPosition, _player.transform.position, _reach);

    internal override Inventory? Engine => _player == null ? null : _player.GetInventory();
}
