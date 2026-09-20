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

public partial class Piece : MonoBehaviour
{
    /// <summary>Vanilla keeps every placed piece in a static list and filters it
    /// by distance; a test builds the list directly.</summary>
    public static readonly List<Piece> s_allPieces = new List<Piece>();

    public long Creator { get; set; }

    public long GetCreator() => Creator;

    /// <summary>Vanilla's own signature and behaviour, including the two details
    /// the housing survey depends on: a <b>three-dimensional</b> distance test
    /// (which is why the caller widens the radius to the diagonal and lets the
    /// designation apply the real horizontal containment), and the <b>ghost
    /// layer exclusion</b> that keeps the build menu's placement preview out of
    /// the result. It appends without clearing.</summary>
    public static void GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)
    {
        foreach (Piece piece in s_allPieces)
        {
            if (piece != null &&
                piece.gameObject.layer != GhostLayer &&
                Vector3.Distance(p, piece.transform.position) < radius)
            {
                pieces.Add(piece);
            }
        }
    }

    /// <summary>Whatever `LayerMask.NameToLayer("ghost")` resolves to. The value
    /// does not matter; that the comparison happens does.</summary>
    public const int GhostLayer = 11;
}

/// <summary>`ZDOVars.s_owner` is the only key the housing survey reads.</summary>
public static class ZDOVars
{
    public static readonly int s_owner = "owner".GetHashCode();
}

/// <summary>A vanilla bed. `Bed` and `Piece` sit on one GameObject in every
/// vanilla bed prefab, which is the arrangement the survey relies on.</summary>
public class Bed : MonoBehaviour
{
    public Vector3 SpawnPoint { get; set; }

    /// <summary>Vanilla's `IsCurrent()` is `IsMine() && Distance(spawn,
    /// customSpawn) < 1f`, so a test sets the answer rather than the geometry.
    /// </summary>
    public bool Current { get; set; }

    public Vector3 GetSpawnPoint() => SpawnPoint;

    public bool IsCurrent() => Current;
}

/// <summary>`Cover.GetCoverForPoint` is public and static in assembly_utils and
/// takes a point, which is what makes a bed's shelter measurable rather than
/// extrapolated from the player's.</summary>
public static class Cover
{
    /// <summary>Set by a test, keyed by the point asked about; anything not set
    /// answers "no roof, no cover".</summary>
    public static readonly Dictionary<string, (float Cover, bool UnderRoof)> Answers =
        new Dictionary<string, (float, bool)>(StringComparer.Ordinal);

    public static void GetCoverForPoint(
        Vector3 point, out float coverPercentage, out bool underRoof, float minDistance = 0.5f)
    {
        if (Answers.TryGetValue(Key(point), out (float Cover, bool UnderRoof) answer))
        {
            coverPercentage = answer.Cover;
            underRoof = answer.UnderRoof;
            return;
        }

        coverPercentage = 0f;
        underRoof = false;
    }

    public static void Set(Vector3 point, float cover, bool underRoof) =>
        Answers[Key(point)] = (cover, underRoof);

    public static string Key(Vector3 point) =>
        point.x.ToString("0.##") + "/" + point.y.ToString("0.##") + "/" + point.z.ToString("0.##");
}

/// <summary>`EffectArea.IsPointInsideArea` returns the area, or null when the
/// point is outside every one of them.</summary>
public class EffectArea : MonoBehaviour
{
    public enum Type
    {
        Heat = 1,
        Burning = 2,
    }

    /// <summary>Points a test has declared warm.</summary>
    public static readonly HashSet<string> Warm = new HashSet<string>(StringComparer.Ordinal);

    public static EffectArea? IsPointInsideArea(Vector3 point, Type type, float radius = 0f) =>
        type == Type.Heat && Warm.Contains(Cover.Key(point)) ? Shared : null;

    private static readonly EffectArea Shared = new EffectArea();
}

public class TerrainModifier : MonoBehaviour { }

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

    public void Set(int key, long value) => Write(key.ToString(), value);

    public long GetLong(int key, long fallback) =>
        _values.TryGetValue(key.ToString(), out object? value) && value is long number ? number : fallback;

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

/// <summary>#380: the networked animation component, with only the two members
/// this product uses on a worker body - <c>GetHash</c> and <c>SetFloat</c>, the
/// same two <c>ClimbPose</c> uses on the local player and the ones this
/// repository has verified against the installed build.
///
/// <c>SetTrigger</c> is deliberately NOT here. It sends an RPC
/// (docs/mods/concerned-cartographer/COMPANION_COMPATIBILITY.md section 3) and a
/// cosmetic hammer trigger was explicitly not authorised, so a stub for it would
/// make the forbidden call compile in the one project that could have caught
/// it.</summary>
public class ZSyncAnimation : MonoBehaviour
{
    /// <summary>Every float written, by parameter hash, so a test can read what
    /// the pose actually set rather than that it did not throw.</summary>
    public Dictionary<int, float> Floats { get; } = new Dictionary<int, float>();

    public static int GetHash(string name) => name == null ? 0 : name.GetHashCode();

    public void SetFloat(int hash, float value) => Floats[hash] = value;
}

public partial class Character : MonoBehaviour
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

    /// <summary>#380: the real 1.0.x signature, read out of the installed
    /// assembly's own metadata - <c>PlacePiece(piece, pos, rot, doAttack,
    /// cheated)</c>, returning VOID. The position and rotation are PARAMETERS,
    /// which is the fact the whole build order rests on: an NPC can aim a
    /// placement, and nothing has to drive the local player's placement ghost.
    /// There is no success to read back, which is why progress is read from the
    /// world rather than from what was asked for.</summary>
    public List<string> Placed { get; } = new List<string>();

    public Exception? PlaceThrows { get; set; }

    public void PlacePiece(Piece piece, Vector3 pos, Quaternion rot, bool doAttack, bool cheated)
    {
        if (PlaceThrows != null)
        {
            throw PlaceThrows;
        }

        Placed.Add(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}@{1:0.##}/{2:0.##}/{3:0.##} yaw {4:0.##} attack={5} cheated={6}",
            piece.gameObject.name, pos.x, pos.y, pos.z, rot.eulerAngles.y, doAttack, cheated));
    }

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

/// <summary>#380: the three vanilla surfaces the construction adapters read,
/// and nothing more of them than the adapters touch.
///
/// <c>Piece.Requirement</c> and <c>Piece.m_resources</c> are the <b>real build
/// cost</b> - the one source #380 allows, and the reason this product carries no
/// cost table of its own. <c>CraftingStation.HaveBuildStationInRange</c> and the
/// two <c>ZoneSystem</c> queries are two of the gates CF-SET-003 names.</summary>
public partial class Piece
{
    public class Requirement
    {
        public ItemDrop? m_resItem;

        public int m_amount;

        public int m_recover;
    }

    public Requirement[]? m_resources;

    public CraftingStation? m_craftingStation;

    // Every placement constraint the real Piece declares, with the real names
    // and the real types, read out of the installed assembly's own metadata.
    // The restrictive value is `true` for all but the two marked.
    public bool m_enabled = true;                 // restrictive when FALSE

    public bool m_allowedInDeepSnow = true;       // restrictive when FALSE

    public bool m_isUpgrade;

    public bool m_repairPiece;

    public bool m_removePiece;

    public bool m_groundPiece;

    public bool m_groundOnly;

    public bool m_cultivatedGroundOnly;

    public bool m_vegetationGroundOnly;

    public bool m_waterPiece;

    public bool m_noInWater;

    public bool m_notOnWood;

    public bool m_notOnTiltingSurface;

    public bool m_inCeilingOnly;

    public bool m_notOnFloor;

    public bool m_onlyInTeleportArea;

    public bool m_requireDeepSnow;

    public bool m_allowedInDungeons;

    public float m_spaceRequirement;

    public Piece? m_mustConnectTo;

    public List<Piece>? m_blockingPieces;

    public Heightmap.Biome m_onlyInBiome = Heightmap.Biome.None;
}

/// <summary>Only the biome enum and the point lookup the constraint reader
/// uses. The real one is a flags enum, which is why a piece can say "meadows or
/// plains" and why the mask test is a bitwise and.</summary>
public static class Heightmap
{
    [Flags]
    public enum Biome
    {
        None = 0,
        Meadows = 1,
        Swamp = 2,
        Mountain = 4,
        BlackForest = 8,
        Plains = 16,
    }

    /// <summary>What a test says is where. Meadows unless it says otherwise.
    /// </summary>
    public static Biome Here { get; set; } = Biome.Meadows;

    public static Biome FindBiome(Vector3 point) => Here;
}

/// <summary>`Character.InInterior(point)` is how the game asks whether a place
/// is inside a dungeon, and it is one of the two constraints this runtime
/// judges rather than refuses.</summary>
public partial class Character
{
    public static HashSet<string> Interiors { get; } = new HashSet<string>(StringComparer.Ordinal);

    public static bool InInterior(Vector3 position) => Interiors.Contains(ZoneSystem.Key(position));
}

/// <summary>`Location.IsInsideNoBuildLocation` is vanilla's own no-build zone,
/// and a gate in its own right.</summary>
public static class Location
{
    public static HashSet<string> NoBuild { get; } = new HashSet<string>(StringComparer.Ordinal);

    public static bool IsInsideNoBuildLocation(Vector3 point) => NoBuild.Contains(ZoneSystem.Key(point));
}

public class CraftingStation : MonoBehaviour
{
    /// <summary>Which station names a test says are in range. Vanilla walks a
    /// static list of live stations and measures; the adapter only ever reads
    /// the boolean.</summary>
    public static readonly HashSet<string> InRange = new HashSet<string>(StringComparer.Ordinal);

    public string m_name = string.Empty;

    public static bool HaveBuildStationInRange(string name, Vector3 point) => InRange.Contains(name);
}

public class ZoneSystem
{
    public static ZoneSystem? instance;

    /// <summary>Points a test says are NOT on loaded ground. Empty means all of
    /// it is loaded, because most tests are not about streaming.</summary>
    public HashSet<string> Unloaded { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Points the ground height cannot be measured at.</summary>
    public HashSet<string> NoGround { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Exception? Throws { get; set; }

    public static string Key(Vector3 point) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "{0:0.##}/{1:0.##}/{2:0.##}", point.x, point.y, point.z);

    public bool IsZoneLoaded(Vector3 point)
    {
        if (Throws != null)
        {
            throw Throws;
        }

        return !Unloaded.Contains(Key(point));
    }

    public bool GetSolidHeight(Vector3 point, out float height)
    {
        if (Throws != null)
        {
            throw Throws;
        }

        height = 0f;
        return !NoGround.Contains(Key(point));
    }
}

public class ZNetScene
{
    public static ZNetScene? instance;

    public Dictionary<string, GameObject> Prefabs { get; } =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);

    public GameObject? GetPrefab(string name) =>
        Prefabs.TryGetValue(name, out GameObject? prefab) ? prefab : null;
}
