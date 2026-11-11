using System;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The two excavation terrain operations (#551): native square TerrainOps of radius 0, each of which changes
/// exactly one lattice vertex (TerrainComp.RaiseTerrain covers 2*ceil(r)+1 vertices on a side). One lowers only, to the
/// op's height minus <see cref="Step"/>; the other raises only, to the op's height plus <see cref="Step"/>, by at most that.
/// Only the position varies per vertex, so a dig of any rectangle is a batch of lowerings and its undo a batch of raises,
/// each through the pickaxe's own path (EXCAVATION.md section 10). They replace #487's two fixed square ops.
///
/// <b>Registered on every peer, whatever the switch says.</b> The operation's settings travel as the prefab-name hash
/// and the terrain tile's OWNER looks them up in its own ObjectDB (TerrainOp.Settings.Deserialize). A tile owner that
/// had not registered them would log an error and drop the dig. Registering is inert: these prefabs do nothing until
/// something instantiates one, and only the opted-in commit gate does.
///
/// <b>When, and why not through Jötunn.</b> Read from the decompiled installed game and Jötunn 2.30.2:
/// <list type="bullet">
/// <item>ObjectDB.Awake sets <c>ObjectDB.instance</c> to itself and then rebuilds <c>m_terrainOpsByHash</c> from its own
/// <c>m_terrainOps</c> list. Nothing else in the game rebuilds it except CopyOtherDB, which only FejdStartup (the main
/// menu) calls.</item>
/// <item>Jötunn's own path adds a prefab's TerrainOp from its ZNetScene.Awake postfix, through <c>ObjectDB.instance</c>.
/// ZNetScene and ObjectDB are both objects of the game scene and the order of their Awakes is Unity's, not code that
/// can be read: if ZNetScene woke first, Jötunn would see the menu's (destroyed) instance and skip, or the game's
/// ObjectDB.Awake would then rebuild the table without the op.</item>
/// <item>So this registers from <c>ItemManager.OnItemsRegistered</c>, which Jötunn raises in an ObjectDB.Awake POSTFIX in
/// the game scene: <c>ObjectDB.instance</c> is provably the instance that just woke, and its table has just been
/// rebuilt, so nothing clears the entry afterwards. The prefabs are not given to Jötunn's PrefabManager at all
/// (they need no ZNetScene entry: the op is instantiated locally and is not networked).</item>
/// </list>
/// Whether the registration held is re-checked at every commit (<see cref="IsRegistered"/>), never assumed.</summary>
internal sealed class ExcavationTerrainOps : IDisposable
{
    internal const string LowerPrefabName = "tcc_excavate_vertex_lower";
    internal const string RaisePrefabName = "tcc_excavate_vertex_raise";

    /// <summary>The ops' fixed delta: the engine's own per-edit clamp (<see cref="DigUndo.RaiseStep"/>).</summary>
    internal const float Step = DigUndo.RaiseStep;

    private readonly Action<string> _log;
    private GameObject? _container;
    private TerrainOp? _lower;
    private TerrainOp? _raise;
    private bool _subscribed;
    private bool _failed;

    internal ExcavationTerrainOps(Action<string> log) => _log = log;

    internal void Install()
    {
        if (_subscribed) return;
        ItemManager.OnItemsRegistered += Register;
        _subscribed = true;
    }

    /// <summary>The op for a batch, or null before the first game scene or after a build failure.</summary>
    internal TerrainOp? For(DigFlightKind kind) => kind == DigFlightKind.Dig ? _lower : _raise;

    /// <summary>The op's height for a vertex's target: lowering reaches the op's height minus the step, raising its height
    /// plus it (TerrainComp.RaiseTerrain, square branch, read from the installed game).</summary>
    internal static float OperationY(DigFlightKind kind, float target) => kind == DigFlightKind.Dig ? target + Step : target - Step;

    /// <summary>True when this game's live ObjectDB maps both ops' prefab hashes to this mod's own ops: the lookup each
    /// tile owner will make.</summary>
    internal bool IsRegistered() => Registered(_lower) && Registered(_raise);

    private static bool Registered(TerrainOp? op)
    {
        ObjectDB? db = ObjectDB.instance;
        if (op == null || db == null) return false;
        return db.m_terrainOpsByHash.TryGetValue(db.GetPrefabHash(op.gameObject), out TerrainOp registered) &&
               ReferenceEquals(registered, op);
    }

    private void Register()
    {
        if (_failed) return;
        try
        {
            ObjectDB? db = ObjectDB.instance;
            if (db == null)
            {
                _log("Excavation terrain operations not registered: no ObjectDB in this scene.");
                return;
            }
            if (_container == null) Build();
            bool lower = Add(db, _lower!);
            bool raise = Add(db, _raise!);
            _log("Excavation terrain operations " + (lower && raise ? "registered" : "NOT fully registered") +
                 " (" + LowerPrefabName + " " + (lower ? "ok" : "refused") + ", " + RaisePrefabName + " " + (raise ? "ok" : "refused") + ").");
        }
        catch (Exception exception)
        {
            // A fault here must never take the ladders down with it. Without the ops this peer cannot apply an
            // excavation as a tile owner, and the commit gate refuses NotRegistered locally.
            _failed = true;
            _log("Excavation terrain operations unavailable: " + SafeFailure.Brief(exception) + ".");
        }
    }

    /// <summary>Builds both prefabs once, under an inactive container that survives scene changes. A child of an
    /// inactive parent never wakes, so TerrainOp.Awake (which applies the op and destroys itself) runs only on a
    /// clone instantiated as a root.</summary>
    private void Build()
    {
        var container = new GameObject("tcc_excavation_ops");
        container.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(container);
        _lower = Make(container.transform, LowerPrefabName, -Step);
        _raise = Make(container.transform, RaisePrefabName, Step);
        _container = container;
        // Attack.SpawnOnHitTerrain finds the op with GetComponentInChildren, which in Unity skips objects that are not
        // active in the hierarchy. If it misses, the game's own ward and no-build re-check is skipped. The commit gate's
        // own checks refuse everything that re-check refuses (the ward at the wider radius in its strict mode, every
        // no-build location at the wider radius), so nothing is lost; the log says which happens.
        bool vanillaSees = _lower.gameObject.GetComponentInChildren<TerrainOp>() != null;
        _log("Excavation terrain operations built; the game's own terrain-tool re-check " +
             (vanillaSees ? "applies" : "is skipped (the mod's stricter ward and no-build checks still run)") + ".");
    }

    private static TerrainOp Make(Transform parent, string name, float delta)
    {
        var prefab = new GameObject(name);
        prefab.transform.SetParent(parent, false);
        TerrainOp op = prefab.AddComponent<TerrainOp>();
        TerrainOp.Settings settings = op.m_settings;
        // One vertex: a square raise of radius 0 covers 2*ceil(0)+1 = 1 vertex (TerrainComp.RaiseTerrain). With a negative
        // delta it skips a vertex already at or below the target and otherwise sets it to the target; with a positive one
        // it skips a vertex already at or above the target and raises by at most the delta.
        settings.m_raise = true;
        settings.m_square = true;
        settings.m_raiseRadius = 0f;
        settings.m_raiseDelta = delta;
        settings.m_raisePower = 0f;
        // Off: level would also raise deeper ground, smooth would break the flat floor, and paint (on by default,
        // radius 2) would widen GetRadius(), and with it the tiles touched, beyond the one vertex. At radius 0 the tile
        // search (Heightmap.IsPointInside, inclusive on both sides) still finds both tiles of a vertex on a tile edge.
        settings.m_level = false;
        settings.m_smooth = false;
        settings.m_paintCleared = false;
        settings.m_rotation = false;
        settings.m_levelOffset = 0f;
        // No item is spawned and no effect is played.
        op.m_spawnOnPlaced = null;
        if (op.GetRadius() != 0f) throw new InvalidOperationException("Terrain operation radius is not zero.");
        return op;
    }

    /// <summary>Adds one op to the live registry, refusing a hash already taken by a different op.</summary>
    private bool Add(ObjectDB db, TerrainOp op)
    {
        int hash = db.GetPrefabHash(op.gameObject);
        if (db.m_terrainOpsByHash.TryGetValue(hash, out TerrainOp existing) && existing != null)
        {
            if (ReferenceEquals(existing, op)) return true;
            _log("Excavation terrain operation " + op.gameObject.name + " not registered: its name hash is already used by " +
                 existing.gameObject.name + ".");
            return false;
        }
        if (!db.m_terrainOps.Contains(op)) db.m_terrainOps.Add(op);
        db.m_terrainOpsByHash[hash] = op;
        return true;
    }

    /// <summary>Takes this mod's own op back out of the registry; an entry that is not ours is left alone.</summary>
    private static void Remove(ObjectDB db, TerrainOp? op)
    {
        if (op == null) return;
        int hash = db.GetPrefabHash(op.gameObject);
        if (db.m_terrainOpsByHash.TryGetValue(hash, out TerrainOp registered) && ReferenceEquals(registered, op))
        {
            db.m_terrainOpsByHash.Remove(hash);
        }
        db.m_terrainOps.Remove(op);
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            try { ItemManager.OnItemsRegistered -= Register; } catch { /* Teardown only. */ }
            _subscribed = false;
        }
        try
        {
            ObjectDB? db = ObjectDB.instance;
            if (db != null)
            {
                Remove(db, _lower);
                Remove(db, _raise);
            }
        }
        catch { /* Teardown only. */ }
        if (_container != null)
        {
            try { UnityEngine.Object.Destroy(_container); } catch { /* Teardown only. */ }
            _container = null;
        }
        _lower = null;
        _raise = null;
    }
}
