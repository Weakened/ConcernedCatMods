using System;
using System.Collections.Generic;
using UnityEngine;

// The Valheim half of the stubbed game surface; UnityStubs.cs carries the
// engine half and the header explaining what these are and are not.
//
// The Fireplace stub below is the important one, and it is a deliberate
// line-by-line transcription of the decompiled 1.0.14 UseItem and RPC_AddFuel
// rather than a convenient approximation. Every claim the Steward's evidence
// makes about vanilla's behaviour -- that UseItem answers true for a refusal,
// that "full" is a CEILING comparison, that an unowned object takes the item
// and throws the fuel away -- is a claim about THIS code, so this code has to
// be the real shape or the evidence is about nothing.

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

        public GameObject? m_dropPrefab;

        /// <summary>A memberwise clone, as vanilla's is: the prefab reference
        /// and the shared data survive, which is what keeps a part-stack move
        /// identifiable.</summary>
        public ItemData Clone() => (ItemData)MemberwiseClone();

        public class SharedData
        {
            public string m_name = string.Empty;

            public int m_maxStackSize = 1;

            public float m_weight = 1f;
        }
    }
}

public class Piece : MonoBehaviour
{
    /// <summary>Vanilla keeps every placed piece in a static list and filters
    /// it by distance; a test builds the list directly.</summary>
    public static readonly List<Piece> s_allPieces = new List<Piece>();

    public long Creator { get; set; }

    public long GetCreator() => Creator;

    /// <summary>Vanilla's own signature, including its three-dimensional
    /// distance test -- which is exactly why WorldFuelTargets widens the query
    /// radius and lets the domain apply the real horizontal containment.
    /// </summary>
    public static void GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)
    {
        foreach (Piece piece in s_allPieces)
        {
            if (piece != null && Vector3.Distance(p, piece.transform.position) < radius)
            {
                pieces.Add(piece);
            }
        }
    }
}

/// <summary>A vanilla inventory, modelled on 1.0.14's stacking and move
/// semantics. The grid is width by height slots; one slot holds one
/// stack.</summary>
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

    /// <summary>Set by a test: one ordered log across every inventory, which is
    /// how "added here before removed there" is provable.</summary>
    public static List<string>? Trace { get; set; }

    public List<ItemDrop.ItemData> GetAllItems() => new List<ItemDrop.ItemData>(_items);

    public List<ItemDrop.ItemData> GetAllItemsInGridOrder() => new List<ItemDrop.ItemData>(_items);

    public int NrOfItems() => _items.Count;

    public int GetEmptySlots() => Math.Max(0, (Width * Height) - _items.Count);

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

    /// <summary>Vanilla's part-stack removal: clamps to the stack, drops the
    /// stack when it empties.</summary>
    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        amount = Mathf.Min(item.m_stack, amount);
        if (!_items.Contains(item) || amount < 1)
        {
            return false;
        }

        Log("remove:" + item.m_shared.m_name + ":" + amount);
        item.m_stack -= amount;
        if (item.m_stack <= 0)
        {
            _items.Remove(item);
        }

        Changed();
        return true;
    }

    /// <summary>Vanilla's own move: add here first, and remove from the source
    /// only when the add took everything.</summary>
    public void MoveItemToThis(Inventory from, ItemDrop.ItemData item)
    {
        Log("move:" + item.m_shared.m_name + ":" + item.m_stack);
        if (AddItem(item))
        {
            from.RemoveItem(item);
        }

        Changed();
        from.Changed();
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

    private void Log(string call) => Trace?.Add(Name + "." + call);

    /// <summary>1.0.14's stacking rule, including the cheated flag.</summary>
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

/// <summary>The hashed field names vanilla stores object data under. Only the
/// ones the Steward reads.</summary>
public static class ZDOVars
{
    public static readonly int s_fuel = "fuel".GetHashCode();

    public static readonly int s_state = "state".GetHashCode();
}

public class ZDO
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

    public ZDOID m_uid = new ZDOID(1L, 1u);

    /// <summary>Every key this object was written with, so a test can prove
    /// nothing was written outside the Steward's own three fields.</summary>
    public List<string> Written { get; } = new List<string>();

    /// <summary>Set by a test: the object stops taking writes, the way it does
    /// when this peer is no longer the one that owns it.</summary>
    public bool FailWrites { get; set; }

    public void Set(string key, string value) => Write(key, value);

    public void Set(string key, int value) => Write(key, value);

    public void Set(string key, byte[] value) => Write(key, value);

    public void Set(int hash, float value) => Write(hash.ToString(), value);

    public void Set(int hash, int value, bool okForNotOwner = false) => Write(hash.ToString(), value);

    public string GetString(string key, string fallback) =>
        _values.TryGetValue(key, out object? value) && value is string text ? text : fallback;

    public int GetInt(string key, int fallback) =>
        _values.TryGetValue(key, out object? value) && value is int number ? number : fallback;

    public int GetInt(int hash, int fallback = 0) =>
        _values.TryGetValue(hash.ToString(), out object? value) && value is int number ? number : fallback;

    public float GetFloat(int hash, float fallback = 0f) =>
        _values.TryGetValue(hash.ToString(), out object? value) && value is float number ? number : fallback;

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

/// <summary>Who owns a network object right now.
///
/// Three values and not a boolean, because the audit's section 3 turns on the
/// difference between "somebody else owns it" and "nobody owns it": vanilla
/// runs the fuel RPC locally in the second case and then discards the fuel on
/// its own owner check, destroying the item.</summary>
public enum StubOwnership
{
    Us,
    Other,
    Nobody,
}

public class ZNetView : MonoBehaviour
{
    public bool m_persistent;

    public bool Valid { get; set; } = true;

    public StubOwnership Ownership { get; set; } = StubOwnership.Us;

    public ZDO Zdo { get; set; } = new ZDO();

    /// <summary>Every RPC this view was asked to invoke, so a test can prove
    /// which vanilla path the adapter took.</summary>
    public List<string> Invoked { get; } = new List<string>();

    private readonly Dictionary<string, Action> _handlers = new Dictionary<string, Action>(StringComparer.Ordinal);

    public bool IsValid() => Valid;

    public bool IsOwner() => Ownership == StubOwnership.Us;

    public bool HasOwner() => Ownership != StubOwnership.Nobody;

    public ZDO GetZDO() => Zdo;

    public void ClaimOwnership() => Ownership = StubOwnership.Us;

    internal void Register(string name, Action handler) => _handlers[name] = handler;

    /// <summary>Vanilla's routed dispatch, as decompiled: run locally and
    /// inline when this peer is the target or when there is no target at all
    /// (owner zero); route away, unobservable here, when another peer owns it.
    /// </summary>
    public void InvokeRPC(string method, params object[] parameters)
    {
        Invoked.Add(method);
        if (Ownership == StubOwnership.Other)
        {
            return;
        }

        if (_handlers.TryGetValue(method, out Action? handler))
        {
            handler();
        }
    }
}

public class ZNetScene
{
    public static ZNetScene? instance;

    public Dictionary<ZDO, ZNetView> m_instances { get; } = new Dictionary<ZDO, ZNetView>();

    public void Register(ZNetView view) => m_instances[view.GetZDO()] = view;
}

public class ZDOMan
{
    public static ZDOMan? instance;

    /// <summary>Saved objects by prefab name, whether or not their ground is
    /// loaded.</summary>
    public Dictionary<string, List<ZDO>> Saved { get; } =
        new Dictionary<string, List<ZDO>>(StringComparer.Ordinal);

    /// <summary>Set by a test: the scan throws, the way a damaged world does.
    /// </summary>
    public bool Throws { get; set; }

    /// <summary>Vanilla's iterative sector walk, including the quirk that its
    /// terminating call adds sector 0 a second time -- verified present in
    /// 1.0.14. The census deduplicates because of this, and a test that did not
    /// reproduce it would not be testing that.</summary>
    public bool GetAllZDOsWithPrefabIterative(string prefab, List<ZDO> zdos, ref int index)
    {
        if (Throws)
        {
            throw new InvalidOperationException("the world's object index is damaged");
        }

        if (!Saved.TryGetValue(prefab, out List<ZDO>? all))
        {
            all = new List<ZDO>();
        }

        if (index == 0)
        {
            zdos.AddRange(all);
            index = 1;
            return false;
        }

        // The terminating call: sector 0 again.
        if (all.Count > 0)
        {
            zdos.Add(all[0]);
        }

        return true;
    }
}

public class Character : MonoBehaviour
{
    public Action? m_onDeath;

    public bool Dead { get; set; }

    public bool IsDead() => Dead;

    /// <summary>Vanilla's Character.Message is an EMPTY virtual, and Humanoid
    /// does not override it -- so every user.Message call on the fuel path is
    /// inert for a worker body. Recording the calls is what lets a test prove
    /// that, rather than assume it.</summary>
    public List<string> Messages { get; } = new List<string>();

    public virtual void Message(MessageHud.MessageType type, string msg, int amount = 0, object? icon = null, bool log = false)
    {
    }
}

public static class MessageHud
{
    public enum MessageType
    {
        TopLeft = 1,
        Center = 2,
    }
}

public class Humanoid : Character
{
    public Inventory Bag { get; set; } = new Inventory("steward", null, 8, 4);

    public Func<ItemDrop.ItemData, bool>? DropThrows { get; set; }

    public List<ItemDrop.ItemData> Dropped { get; } = new List<ItemDrop.ItemData>();

    public Inventory GetInventory() => Bag;

    /// <summary>Vanilla removes the stack from the inventory and spawns it in
    /// the world. The failure a test can ask for is a throw part way through
    /// the loop, which is what a cloned creature with no animator does.
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

    public GameObject? GetHoverObject() => Hovering;

    /// <summary>Player DOES override Message, and gates it on owning its own
    /// view. Present so the difference from Humanoid is visible in the stub as
    /// it is in the game.</summary>
    public override void Message(MessageHud.MessageType type, string msg, int amount = 0, object? icon = null, bool log = false)
    {
        Messages.Add(msg);
    }
}

/// <summary>A player-built fire.
///
/// A transcription of the decompiled 1.0.14 Fireplace, limited to the fuel
/// path. The three behaviours the Steward's evidence is about:
///
///   1. UseItem returns TRUE for "cannot add more" exactly as for a unit taken.
///   2. Full is Mathf.CeilToInt(fuel) >= m_maxFuel -- a ceiling comparison.
///   3. RPC_AddFuel is owner-gated, and UseItem removes the item BEFORE
///      invoking it, so on an unowned object the item is destroyed.
/// </summary>
public class Fireplace : MonoBehaviour
{
    public string m_name = "Fire";

    public float m_maxFuel = 10f;

    public float m_secPerFuel = 3f;

    public bool m_infiniteFuel;

    public bool m_canRefill = true;

    public bool m_canTurnOff;

    public ItemDrop? m_fuelItem;

    private ZNetView? _view;

    /// <summary>Wires the fire to its network object and registers the one RPC
    /// the Steward's path reaches, as Fireplace.Awake does.</summary>
    public void Bind(ZNetView view, float startingFuel)
    {
        _view = view;
        view.Register("RPC_AddFuel", RPC_AddFuel);
        view.GetZDO().Set(ZDOVars.s_fuel, startingFuel);
    }

    public float Fuel => _view == null ? 0f : _view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);

    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        if (!m_canRefill)
        {
            return false;
        }

        if (m_fuelItem != null
            && item.m_shared.m_name == m_fuelItem.m_itemData.m_shared.m_name
            && !m_infiniteFuel)
        {
            if ((float)Mathf.CeilToInt(_view!.GetZDO().GetFloat(ZDOVars.s_fuel, 0f)) >= m_maxFuel)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_cantaddmore");
                return true;
            }

            Inventory inventory = user.GetInventory();
            user.Message(MessageHud.MessageType.Center, "$msg_fireadding");
            inventory.RemoveItem(item, 1);
            _view.InvokeRPC("RPC_AddFuel");
            return true;
        }

        return false;
    }

    /// <summary>The paths the Steward must never take. Present so a test can
    /// assert they exist and are still not called.</summary>
    public void AddFuel(float fuel) => throw new InvalidOperationException(
        "AddFuel conjures fuel with no item consumed; the Steward must never call it");

    public void SetFuel(float fuel) => throw new InvalidOperationException(
        "SetFuel conjures fuel with no item consumed; the Steward must never call it");

    private void RPC_AddFuel()
    {
        if (_view == null || !_view.IsOwner())
        {
            // The destructive case: the item is already gone from the user's
            // inventory and this gate throws the fuel away.
            return;
        }

        float fuel = _view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
        if ((float)Mathf.CeilToInt(fuel) >= m_maxFuel)
        {
            return;
        }

        fuel = Mathf.Clamp(fuel, 0f, m_maxFuel);
        fuel++;
        fuel = Mathf.Clamp(fuel, 0f, m_maxFuel);
        _view.GetZDO().Set(ZDOVars.s_fuel, fuel);
    }
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

    public GameObject? m_rootObjectOverride;

    public Inventory Bag { get; set; } = new Inventory("chest", null, 6, 4);

    public bool Owner { get; set; } = true;

    public bool InUse { get; set; }

    public Inventory? GetInventory() => Bag;

    public bool IsOwner() => Owner;

    public bool IsInUse() => InUse;
}

public static class PrivateArea
{
    /// <summary>The ward answer a test sets. Vanilla flashes and checks the
    /// guard stone; the adapters only ever read the boolean.</summary>
    public static bool Access { get; set; } = true;

    public static bool CheckAccess(Vector3 point, float radius = 0f, bool flash = true, bool wardCheck = false) => Access;
}

public class ZoneSystem
{
    public static ZoneSystem? instance;

    public bool Loaded { get; set; } = true;

    public bool IsZoneLoaded(Vector3 point) => Loaded;
}

public class ZNetPeer
{
}

public class ZNet
{
    public static ZNet? instance;

    public bool Server { get; set; } = true;

    public bool Dedicated { get; set; }

    public long WorldUid { get; set; } = 1234L;

    public List<ZNetPeer> Peers { get; } = new List<ZNetPeer>();

    /// <summary>Set by a test: asking who is connected throws, which must read
    /// as "could not tell" and refuse, never as "nobody".</summary>
    public bool PeersThrow { get; set; }

    public bool IsServer() => Server;

    public bool IsDedicated() => Dedicated;

    public long GetWorldUID() => WorldUid;

    public List<ZNetPeer> GetPeers() =>
        PeersThrow ? throw new InvalidOperationException("the peer list is not available") : Peers;
}

public class Game
{
    public static Game? instance;

    public static int m_worldLevel;
}
