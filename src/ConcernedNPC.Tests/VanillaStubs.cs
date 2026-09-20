using System;
using System.Collections.Generic;
using UnityEngine;

// The Valheim half of the game surface the linked library sources compile
// against. See the header of UnityStubs.cs for why this file exists and what it
// deliberately is not.
//
// Everything here is modelled on what the three shipped worker runtimes and
// their own tests record about the installed build. Where the real behaviour is
// surprising, the surprise is reproduced rather than smoothed over - a stub
// without the quirk tests something that is not the game.

/// <summary>A shipped inventory, as far as a body's persistence uses it: a
/// change callback that fires inside the call that changed it, and a save and
/// load pair that round-trips through a package.</summary>
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

    /// <summary>Set by a test: saving throws, the way a damaged package does.
    /// </summary>
    public bool SaveThrows { get; set; }

    /// <summary>Set by a test: loading throws, which is what makes a body
    /// inert.</summary>
    public bool LoadThrows { get; set; }

    public int NrOfItems() => _items.Count;

    public List<ItemDrop.ItemData> GetAllItems() => _items;

    public bool AddItem(ItemDrop.ItemData item)
    {
        _items.Add(item);
        m_onChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        m_onChanged?.Invoke();
        return true;
    }

    public void Save(ZPackage package)
    {
        if (SaveThrows)
        {
            throw new InvalidOperationException("the inventory package could not be written");
        }

        package.Items = new List<ItemDrop.ItemData>(_items);
    }

    public void Load(ZPackage package)
    {
        if (LoadThrows)
        {
            throw new InvalidOperationException("the stored inventory is damaged");
        }

        _items.Clear();
        _items.AddRange(package.Items);
        m_onChanged?.Invoke();
    }
}

public class ItemDrop
{
    public class ItemData
    {
        public SharedData m_shared = new SharedData();

        public int m_stack = 1;

        public int m_quality = 1;

        public int m_variant;

        public bool m_equipped;

        public GameObject? m_dropPrefab;

        public class SharedData
        {
            public string m_name = string.Empty;
        }
    }
}

/// <summary>A byte package. Identified by its bytes, so a body can store one in
/// a network object and read it back exactly as the game does.</summary>
public class ZPackage
{
    private static readonly Dictionary<string, List<ItemDrop.ItemData>> Store =
        new Dictionary<string, List<ItemDrop.ItemData>>(StringComparer.Ordinal);

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

    public byte[] Bytes { get; private set; } = Array.Empty<byte>();

    public List<ItemDrop.ItemData> Items { get; set; } = new List<ItemDrop.ItemData>();

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

    public override string ToString() => UserID + ":" + ID;
}

public class ZDO
{
    private static uint s_next = 1;

    private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

    public ZDO()
    {
        m_uid = new ZDOID(1L, s_next++);
    }

    public ZDOID m_uid;

    /// <summary>Every key this object was written with, so a test can prove
    /// nothing was written outside a body's own three fields.</summary>
    public List<string> Written { get; } = new List<string>();

    /// <summary>Set by a test: the object stops taking writes, the way it does
    /// when this peer no longer owns it.</summary>
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

/// <summary>Who owns a network object right now.</summary>
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

    public bool DestroyCalled { get; private set; }

    public bool IsValid() => Valid;

    public bool IsOwner() => Ownership == StubOwnership.Us;

    public bool HasOwner() => Ownership != StubOwnership.Nobody;

    public ZDO GetZDO() => Zdo;

    public void Destroy()
    {
        DestroyCalled = true;
        UnityEngine.Object.Destroy(gameObject);
    }
}

public class ZNetScene
{
    public static ZNetScene instance = null!;

    public Dictionary<ZDO, ZNetView> m_instances { get; } = new Dictionary<ZDO, ZNetView>();
}

public class ZDOMan
{
    public static ZDOMan instance = null!;

    /// <summary>Saved objects by prefab name, whether or not their ground is
    /// loaded.</summary>
    public Dictionary<string, List<ZDO>> Saved { get; } =
        new Dictionary<string, List<ZDO>>(StringComparer.Ordinal);

    /// <summary>Set by a test: the scan throws, the way a damaged world does.
    /// </summary>
    public bool Throws { get; set; }

    /// <summary>How many calls one full walk takes. One sector per call, so a
    /// test can exercise the incremental path rather than only the terminating
    /// one.</summary>
    public int SectorsPerCall { get; set; } = 1;

    /// <summary>The game's iterative sector walk, including the quirk that its
    /// terminating call adds the first sector a second time. The census
    /// deduplicates because of that, and a stub without it would not be testing
    /// the deduplication.</summary>
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

        if (index < all.Count)
        {
            int taken = 0;
            while (index < all.Count && taken < SectorsPerCall)
            {
                zdos.Add(all[index]);
                index++;
                taken++;
            }

            return false;
        }

        // The terminating call: the first sector again.
        if (all.Count > 0)
        {
            zdos.Add(all[0]);
        }

        return true;
    }
}

public class ZoneSystem
{
    public static ZoneSystem instance = null!;

    public bool Loaded { get; set; } = true;

    public bool IsZoneLoaded(Vector3 point) => Loaded;
}

public class EffectList
{
}

public class Pathfinding
{
    public static Pathfinding instance = null!;

    public enum AgentType
    {
        Humanoid = 0,
        HugeMonster = 1,
    }
}

public class ZSyncAnimation : MonoBehaviour
{
}

public class Tameable : MonoBehaviour
{
}

public class Sadle : MonoBehaviour
{
}

public class NpcTalk : MonoBehaviour
{
}

public class CharacterDrop : MonoBehaviour
{
}

public class Procreation : MonoBehaviour
{
}

public class Growup : MonoBehaviour
{
}

public class CharacterTimedDestruction : MonoBehaviour
{
}

public class VisEquipment : MonoBehaviour
{
    public Transform? m_helmet;

    public Transform? m_rightHand;

    public SkinnedMeshRenderer? m_bodyModel;
}

public class Character : MonoBehaviour
{
    public enum Faction
    {
        Players = 0,
        AnimalsVeg = 1,
        ForestMonsters = 2,
    }

    public Action? m_onDeath;

    public Faction m_faction = Faction.ForestMonsters;

    public bool m_boss;

    public string m_name = string.Empty;

    public float m_originalMass = 70f;

    public bool Running { get; private set; }

    public bool Walking { get; private set; }

    public bool Dead { get; set; }

    public bool IsDead() => Dead;

    public void SetRun(bool run) => Running = run;

    public void SetWalk(bool walk) => Walking = walk;
}

public class Humanoid : Character
{
    public GameObject[] m_defaultItems = Array.Empty<GameObject>();

    public GameObject[] m_randomWeapon = Array.Empty<GameObject>();

    public GameObject[] m_randomArmor = Array.Empty<GameObject>();

    public GameObject[] m_randomShield = Array.Empty<GameObject>();

    public ItemSet[] m_randomSets = Array.Empty<ItemSet>();

    public RandomItem[] m_randomItems = Array.Empty<RandomItem>();

    public Inventory Bag { get; set; } = new Inventory("npc", null, 8, 4);

    public Func<ItemDrop.ItemData, bool>? DropThrows { get; set; }

    public List<ItemDrop.ItemData> Dropped { get; } = new List<ItemDrop.ItemData>();

    public List<ItemDrop.ItemData> Equipped { get; } = new List<ItemDrop.ItemData>();

    public Inventory GetInventory() => Bag;

    public bool EquipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)
    {
        Equipped.Add(item);
        item.m_equipped = true;
        return true;
    }

    /// <summary>The game removes the stack from the inventory and spawns it in
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

    public class ItemSet
    {
    }

    public class RandomItem
    {
    }
}

public class Player : Humanoid
{
    public static Player? m_localPlayer;
}

/// <summary>One of the game's own scripts that live inside a character's visual
/// subtree. The real one dereferences the character in its own Awake and puts
/// itself in a static list the engine walks every physics and late update, so a
/// half-built one does not merely log once - it sits inside the game's loop for
/// the session. That is the whole reason the extraction refuses rather than
/// disables.</summary>
public class CharacterAnimEvent : MonoBehaviour
{
}

/// <summary>The base of every creature mind.
///
/// The four facts the library's own mind is written around are reproduced
/// here: the update is an ownership gate that carries no creature behaviour;
/// the move answers "stopped" rather than "arrived"; the path question that
/// looks like a field read is one, and the one that looks cheap is not; and
/// waking arms a repeating idle sound that no override of the update can
/// reach.</summary>
public class BaseAI : MonoBehaviour
{
    public ZNetView? m_nview;

    public Character m_character = new Character();

    public EffectList m_idleSound = new EffectList();

    public float m_idleSoundChance = 1f;

    public bool m_canBeAlerted = true;

    public string m_spawnMessage = "a creature appears";

    public string m_deathMessage = "a creature dies";

    public string m_alertedMessage = "a creature is alerted";

    public float m_viewRange = 30f;

    public float m_hearRange = 30f;

    public Pathfinding.AgentType m_pathAgentType = Pathfinding.AgentType.HugeMonster;

    public bool Hunting { get; private set; } = true;

    public bool AwakeRan { get; private set; }

    /// <summary>Set by a test: this peer owns the body and the view is valid,
    /// which is the whole of what the real gate decides.</summary>
    public bool Owned { get; set; } = true;

    public bool PathFound { get; set; }

    public bool PathSearchSucceeds { get; set; } = true;

    public int PathSearches { get; private set; }

    public int Moves { get; private set; }

    public int Stops { get; private set; }

    public Vector3 LastMoveTarget { get; private set; }

    public Vector3 LastDirection { get; private set; }

    public Vector3 LastLook { get; private set; }

    public virtual void Awake()
    {
        AwakeRan = true;
        InvokeRepeating("DoIdleSound", 30f, 30f);
    }

    public virtual void OnEnable()
    {
    }

    public virtual void OnDisable()
    {
    }

    /// <summary>The ownership gate, and nothing else. False means this peer
    /// does not own the body.</summary>
    public virtual bool UpdateAI(float dt) => Owned;

    public bool FoundPath() => PathFound;

    public bool FindPath(Vector3 point)
    {
        PathSearches++;
        PathFound = PathSearchSucceeds;
        return PathFound;
    }

    /// <summary>Answers "stopped", not "arrived": true when the point is close,
    /// when the path search failed, and when the path ran out.</summary>
    public bool MoveTo(float dt, Vector3 point, float distance, bool run)
    {
        Moves++;
        LastMoveTarget = point;
        return true;
    }

    public void MoveTowards(Vector3 direction, bool run)
    {
        Moves++;
        LastDirection = direction;
    }

    public void LookTowards(Vector3 direction) => LastLook = direction;

    public void StopMoving() => Stops++;

    public void SetHuntPlayer(bool hunt) => Hunting = hunt;
}
