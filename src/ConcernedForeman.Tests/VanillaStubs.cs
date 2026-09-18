using System;
using System.Collections.Generic;
using UnityEngine;

// The Valheim half of the stubbed game surface; UnityStubs.cs carries the
// engine half and the header explaining what these are and are not.

/// <summary>Vanilla's item stack and the shared data behind it.</summary>
public class ItemDrop : MonoBehaviour
{
    public ItemData m_itemData = new ItemData();

    public class ItemData
    {
        public SharedData m_shared = new SharedData();

        public int m_stack = 1;

        public int m_quality = 1;

        public int m_variant;

        public int m_worldLevel;

        public bool m_cheated;

        public bool m_equipped;

        public float m_durability = 100f;

        public GameObject? m_dropPrefab;

        /// <summary>A memberwise clone, as vanilla's is
        /// (decomp/ItemDrop.cs:458-462): the prefab reference and the shared
        /// data survive, which is what keeps a part-stack move identifiable.
        /// </summary>
        public ItemData Clone() => (ItemData)MemberwiseClone();

        public float GetWeight() => GetWeight(m_stack);

        public float GetWeight(int stack) => m_shared.m_weight * stack;

        public class SharedData
        {
            public string m_name = string.Empty;

            public int m_maxStackSize = 1;

            public float m_weight = 1f;

            public bool m_useDurability;

            public int m_toolTier;

            public PieceTable? m_buildPieces;

            public HitData.DamageTypes m_damages;
        }
    }
}

public static class HitData
{
    public struct DamageTypes
    {
        public float m_damage;
        public float m_blunt;
        public float m_slash;
        public float m_pierce;
        public float m_chop;
        public float m_pickaxe;
    }
}

public class PieceTable : MonoBehaviour
{
    public List<GameObject> m_pieces = new List<GameObject>();
}

public class Piece : MonoBehaviour
{
    public long Creator { get; set; }

    public long GetCreator() => Creator;
}

public class TerrainOp : MonoBehaviour
{
}

/// <summary>A vanilla inventory, modelled on 1.0.14's stacking and move
/// semantics (see the file header). The grid is width × height slots; one slot
/// holds one stack.</summary>
public class Inventory
{
    private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();

    public Inventory(string name, object? background, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
    }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    public Action? m_onChanged;

    /// <summary>Every call that changed something, for ordering assertions.
    /// </summary>
    public List<string> Calls { get; } = new List<string>();

    /// <summary>Set by a test: one ordered log across every inventory, which is
    /// how "added here before removed there" is provable.</summary>
    public static List<string>? Trace { get; set; }

    private void Log(string call)
    {
        Calls.Add(call);
        Trace?.Add(Name + "." + call);
    }

    public List<ItemDrop.ItemData> GetAllItems() => new List<ItemDrop.ItemData>(_items);

    public List<ItemDrop.ItemData> GetAllItemsInGridOrder() => new List<ItemDrop.ItemData>(_items);

    public int NrOfItems() => _items.Count;

    public int GetEmptySlots() => Math.Max(0, (Width * Height) - _items.Count);

    public bool CanAddItem(ItemDrop.ItemData item, int stack = -1)
    {
        int wanted = stack < 0 ? item.m_stack : stack;
        return FreeRoomFor(item) >= wanted;
    }

    public bool ContainsItem(ItemDrop.ItemData item) => _items.Contains(item);

    /// <summary>Adds this exact instance, merging into matching stacks first and
    /// decrementing the incoming stack as it merges. False when it could not all
    /// be added; whatever merged stays merged.</summary>
    public bool AddItem(ItemDrop.ItemData item)
    {
        Log("add:" + item.m_shared.m_name + ":" + item.m_stack);
        foreach (ItemDrop.ItemData existing in _items)
        {
            if (item.m_stack <= 0)
            {
                break;
            }

            if (!Stacks(existing, item))
            {
                continue;
            }

            int room = Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack);
            int moved = Math.Min(room, item.m_stack);
            existing.m_stack += moved;
            item.m_stack -= moved;
        }

        if (item.m_stack <= 0)
        {
            Changed();
            return true;
        }

        if (GetEmptySlots() < 1)
        {
            Changed();
            return false;
        }

        _items.Add(item);
        Changed();
        return true;
    }

    public bool RemoveItem(ItemDrop.ItemData item)
    {
        Log("remove:" + item.m_shared.m_name + ":" + item.m_stack);
        bool removed = _items.Remove(item);
        if (removed)
        {
            Changed();
        }

        return removed;
    }

    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        Log("remove:" + item.m_shared.m_name + ":" + amount);
        if (!_items.Contains(item) || amount < 1 || amount > item.m_stack)
        {
            return false;
        }

        item.m_stack -= amount;
        if (item.m_stack <= 0)
        {
            _items.Remove(item);
        }

        Changed();
        return true;
    }

    /// <summary>Vanilla's own move: add here first, and remove from the source
    /// only when the add took everything (decomp/Inventory.cs:283-288).
    /// </summary>
    public bool MoveItemToThis(Inventory from, ItemDrop.ItemData item)
    {
        Log("move:" + item.m_shared.m_name + ":" + item.m_stack);
        if (AddItem(item))
        {
            from.RemoveItem(item);
            return true;
        }

        from.Changed();
        return false;
    }

    public void Changed() => m_onChanged?.Invoke();

    /// <summary>A stand-in for vanilla's package format: enough to prove that a
    /// body stores and restores what it carries, never a claim about the real
    /// bytes.</summary>
    public void Save(ZPackage package)
    {
        package.Items.Clear();
        foreach (ItemDrop.ItemData item in _items)
        {
            package.Items.Add(item.Clone());
        }
    }

    public void Load(ZPackage package)
    {
        _items.Clear();
        foreach (ItemDrop.ItemData item in package.Items)
        {
            _items.Add(item.Clone());
        }

        Changed();
    }

    private int FreeRoomFor(ItemDrop.ItemData item)
    {
        long room = 0;
        foreach (ItemDrop.ItemData existing in _items)
        {
            if (Stacks(existing, item))
            {
                room += Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack);
            }
        }

        room += (long)GetEmptySlots() * Math.Max(1, item.m_shared.m_maxStackSize);
        return (int)Math.Min(int.MaxValue, room);
    }

    /// <summary>1.0.14's stacking rule, including the cheated flag it gained in
    /// that build.</summary>
    private static bool Stacks(ItemDrop.ItemData existing, ItemDrop.ItemData item) =>
        existing != item
        && existing.m_shared.m_name == item.m_shared.m_name
        && existing.m_quality == item.m_quality
        && existing.m_worldLevel == item.m_worldLevel
        && existing.m_cheated == item.m_cheated;
}

public class ZPackage
{
    public ZPackage()
    {
    }

    public ZPackage(byte[] bytes)
    {
        Bytes = bytes;
        if (Store.TryGetValue(Key(bytes), out List<ItemDrop.ItemData>? items))
        {
            Items = items;
        }
    }

    /// <summary>Packages are identified by their bytes, so a body can store one
    /// in a ZDO and read it back exactly as the game does.</summary>
    internal static Dictionary<string, List<ItemDrop.ItemData>> Store { get; } =
        new Dictionary<string, List<ItemDrop.ItemData>>(StringComparer.Ordinal);

    public byte[] Bytes { get; private set; } = Array.Empty<byte>();

    public List<ItemDrop.ItemData> Items { get; private set; } = new List<ItemDrop.ItemData>();

    public byte[] GetArray()
    {
        Bytes = Guid.NewGuid().ToByteArray();
        Store[Key(Bytes)] = Items;
        return Bytes;
    }

    private static string Key(byte[] bytes) => new Guid(bytes).ToString("N");
}

public struct ZDOID : IEquatable<ZDOID>
{
    public ZDOID(long userId, uint id)
    {
        UserID = userId;
        ID = id;
    }

    public static readonly ZDOID None = new ZDOID(0L, 0u);

    public long UserID { get; }

    public uint ID { get; }

    public bool Equals(ZDOID other) => UserID == other.UserID && ID == other.ID;

    public override bool Equals(object? obj) => obj is ZDOID other && Equals(other);

    public override int GetHashCode() => unchecked((UserID.GetHashCode() * 397) ^ (int)ID);

    public static bool operator ==(ZDOID left, ZDOID right) => left.Equals(right);

    public static bool operator !=(ZDOID left, ZDOID right) => !left.Equals(right);
}

public class ZDO
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

    public ZDOID m_uid = new ZDOID(1L, 1u);

    /// <summary>Every key this object was written with, so a test can prove
    /// nothing was written outside the worker's own three fields.</summary>
    public List<string> Written { get; } = new List<string>();

    /// <summary>Set by a test: the object stops taking writes, the way it does
    /// when this peer is no longer the one that owns it.</summary>
    public bool FailWrites { get; set; }

    public void Set(string key, string value) => Write(key, value);

    public void Set(string key, int value) => Write(key, value);

    public void Set(string key, byte[] value) => Write(key, value);

    public string GetString(string key, string fallback) =>
        _values.TryGetValue(key, out object? value) && value is string text ? text : fallback;

    public int GetInt(string key, int fallback) =>
        _values.TryGetValue(key, out object? value) && value is int number ? number : fallback;

    public byte[]? GetByteArray(string key, byte[]? fallback) =>
        _values.TryGetValue(key, out object? value) && value is byte[] bytes ? bytes : fallback;

    private void Write(string key, object value)
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("this peer no longer owns that object");
        }

        Written.Add(key);
        _values[key] = value;
    }
}

public class ZNetView : MonoBehaviour
{
    public bool m_persistent;

    public bool Valid { get; set; } = true;

    public bool Owner { get; set; } = true;

    public ZDO Zdo { get; set; } = new ZDO();

    public bool Destroyed { get; private set; }

    public bool IsValid() => Valid;

    public bool IsOwner() => Owner;

    public ZDO GetZDO() => Zdo;

    public void Destroy() => Destroyed = true;
}

public class Character : MonoBehaviour
{
    public Action? m_onDeath;

    public bool Dead { get; set; }

    public bool IsDead() => Dead;
}

public class Humanoid : Character
{
    public Inventory Bag { get; set; } = new Inventory("worker", null, 8, 4);

    /// <summary>Set by a test to make a drop fail the way a missing animator
    /// would.</summary>
    public Func<ItemDrop.ItemData, bool>? DropThrows { get; set; }

    public List<ItemDrop.ItemData> Dropped { get; } = new List<ItemDrop.ItemData>();

    public List<ItemDrop.ItemData> Equipped { get; } = new List<ItemDrop.ItemData>();

    public ItemDrop.ItemData? RightItem { get; set; }

    public ItemDrop.ItemData? LeftItem { get; set; }

    public Inventory GetInventory() => Bag;

    public bool EquipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)
    {
        Equipped.Add(item);
        item.m_equipped = true;
        return true;
    }

    public void UnequipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)
    {
        Equipped.Remove(item);
        item.m_equipped = false;
    }

    public bool IsItemEquiped(ItemDrop.ItemData item) => Equipped.Contains(item);

    /// <summary>Vanilla removes the stack from the inventory and spawns it in
    /// the world (decomp/Humanoid.cs:814-874). The failure a test can ask for is
    /// the one review R2's m3 names: a throw part way through the loop.
    /// </summary>
    public bool DropItem(Inventory inventory, ItemDrop.ItemData item, int amount)
    {
        if (DropThrows != null && DropThrows(item))
        {
            throw new NullReferenceException("the cloned creature has no animator");
        }

        if (!inventory.RemoveItem(item, amount))
        {
            return false;
        }

        Dropped.Add(item);
        return true;
    }
}

public class Player : Humanoid
{
    public static Player? m_localPlayer;

    public GameObject? Hovering { get; set; }

    public long PlayerId { get; set; } = 42L;

    public GameObject? GetHoverObject() => Hovering;

    public long GetPlayerID() => PlayerId;
}

public class Container : MonoBehaviour
{
    public enum PrivacySetting
    {
        Public,
        Private,
        Group,
    }

    public PrivacySetting m_privacy = PrivacySetting.Public;

    public bool m_checkGuardStone;

    public Vagon? m_wagon;

    public GameObject? m_rootObjectOverride;

    public Inventory Bag { get; set; } = new Inventory("chest", null, 6, 4);

    public bool Owner { get; set; } = true;

    public bool InUse { get; set; }

    public Inventory? GetInventory() => Bag;

    public bool IsOwner() => Owner;

    public bool IsInUse() => InUse;
}

public class Vagon : MonoBehaviour
{
    public Container? m_container;

    public bool Attached { get; set; }

    public Player? AttachedTo { get; set; }

    /// <summary>Vanilla's <c>InUse()</c> is true while anything is attached or
    /// the container is open.</summary>
    public bool InUse() => Attached || (m_container != null && m_container.IsInUse());

    public bool IsAttached(Player player) => Attached && ReferenceEquals(AttachedTo, player);
}

public static class PrivateArea
{
    /// <summary>The ward answer a test sets. Vanilla flashes and checks the
    /// guard stone; the adapters only ever read the boolean.</summary>
    public static bool Access { get; set; } = true;

    public static bool CheckAccess(Vector3 point, float radius = 0f, bool flash = true, bool wardCheck = false) => Access;
}

public class PlayerProfile
{
    public long PlayerId { get; set; } = 42L;

    public long GetPlayerID() => PlayerId;
}

public class Game
{
    public static Game? instance;

    public static int m_worldLevel;

    public PlayerProfile Profile { get; set; } = new PlayerProfile();

    public PlayerProfile GetPlayerProfile() => Profile;
}

public class ObjectDB
{
    public static ObjectDB? instance;

    public Dictionary<string, GameObject> Prefabs { get; } = new Dictionary<string, GameObject>(StringComparer.Ordinal);

    public GameObject? GetItemPrefab(string name) =>
        Prefabs.TryGetValue(name, out GameObject? prefab) ? prefab : null;
}
