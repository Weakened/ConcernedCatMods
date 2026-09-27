using System;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's worker prefab, <c>CT_TeamsterWorker</c>: a vanilla creature
/// cloned while inactive, its own mind and loot removed before any
/// <c>Awake</c> runs, registered with the game from the start of every session.
///
/// <b>Why at plugin start.</b> The body is persistent: a world saved with Gunnar
/// in it holds his network record, and the game only instantiates a saved record
/// whose prefab is registered when the world's scene wakes. So the clone is
/// built as soon as the game's own prefabs can be cloned
/// (<c>PrefabManager.OnVanillaPrefabsAvailable</c>) and added as a custom prefab,
/// which Jötunn registers into every scene before any saved object is created.
///
/// <b>Why the swap happens on the inactive clone.</b> <c>BaseAI.Awake</c>
/// registers RPCs with <c>Dictionary.Add</c>: a live creature whose AI is
/// replaced runs it twice and throws. Nothing live is woken and then stripped.
///
/// <b>What is removed</b>, each for a reason read from the installed game: every
/// <c>BaseAI</c> (its mind), <c>Tameable</c> and <c>Sadle</c> (taming and riding
/// need <c>MonsterAI</c>), <c>NpcTalk</c> (dereferences <c>MonsterAI</c> every
/// frame), <c>CharacterDrop</c> (no loot is minted when he dies),
/// <c>Procreation</c> and <c>Growup</c> (no breeding, no replacement body) and
/// <c>CharacterTimedDestruction</c> (no despawn timer). Default and random gear
/// is cleared so no item is ever granted on spawn.
///
/// <b>The base creature name is data</b>, not an API: resolved at run time and
/// failing closed with the exact reason.</summary>
internal static class TeamsterWorkerPrefab
{
    internal const string PrefabName = GunnarHaulingDefaults.WorkerPrefabName;

    private static string _baseCreature = GunnarHaulingDefaults.WorkerBaseCreature;
    private static Action<string>? _log;
    private static bool _subscribed;
    private static string? _lastLoggedFailure;

    internal static GameObject? Prefab { get; private set; }

    internal static bool IsReady => Prefab != null;

    internal static string LastFailure { get; private set; } = "waiting for the game's prefabs to load";

    internal static string BaseCreature => _baseCreature;

    /// <summary>Arms the build for when the game's prefabs can be cloned.
    /// Idempotent.</summary>
    internal static void Install(string baseCreature, Action<string> log)
    {
        _baseCreature = string.IsNullOrWhiteSpace(baseCreature) ? GunnarHaulingDefaults.WorkerBaseCreature : baseCreature.Trim();
        _log = log;
        if (Prefab != null || _subscribed)
        {
            return;
        }

        PrefabManager.OnVanillaPrefabsAvailable += Build;
        _subscribed = true;
    }

    /// <summary>Unregisters the prefab. Only for plugin teardown: a world that
    /// loads without it would not create Gunnar's saved body.</summary>
    internal static void Uninstall()
    {
        if (_subscribed)
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Build;
            _subscribed = false;
        }

        if (Prefab != null)
        {
            try
            {
                PrefabManager.Instance.DestroyPrefab(PrefabName);
            }
            catch
            {
                // Shutting down; nothing else depends on it.
            }

            Prefab = null;
        }
    }

    private static void Build()
    {
        try
        {
            if (TryBuild())
            {
                PrefabManager.OnVanillaPrefabsAvailable -= Build;
                _subscribed = false;
            }
        }
        catch (Exception exception)
        {
            Fail("building it threw " + SafeFailure.Brief(exception));
        }
    }

    private static bool TryBuild()
    {
        PrefabManager prefabs = PrefabManager.Instance;
        GameObject? existing = prefabs.GetPrefab(PrefabName);
        if (existing != null)
        {
            Prefab = existing;
            return true;
        }

        GameObject? basePrefab = prefabs.GetPrefab(_baseCreature);
        if (basePrefab == null)
        {
            return Fail("base creature prefab '" + _baseCreature + "' does not exist in this game build; set Workers/WorkerBaseCreature to one that does");
        }

        GameObject? clone = prefabs.CreateClonedPrefab(PrefabName, basePrefab);
        if (clone == null)
        {
            return Fail("could not clone '" + _baseCreature + "'");
        }

        // Everything below runs on the inactive clone: no Awake has executed.
        string? missing = DescribeMissingComponents(clone);
        if (missing != null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return Fail("'" + _baseCreature + "' cannot be a worker: " + missing);
        }

        foreach (BaseAI vanillaAi in clone.GetComponentsInChildren<BaseAI>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(vanillaAi);
        }

        StripAll<Tameable>(clone);
        StripAll<Sadle>(clone);
        StripAll<NpcTalk>(clone);
        StripAll<CharacterDrop>(clone);
        StripAll<Procreation>(clone);
        StripAll<Growup>(clone);
        StripAll<CharacterTimedDestruction>(clone);

        Humanoid? humanoid = clone.GetComponent<Humanoid>();
        if (humanoid != null)
        {
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];
        }

        clone.AddComponent<TeamsterWorkerAI>();

        // #381: the body's own inventory persistence. Added here, on the
        // inactive clone, so it comes up with every saved body too - a body the
        // game instantiates from a world save gets it without anybody
        // remembering to attach it.
        clone.AddComponent<TeamsterWorkerRecord>();

        Character character = clone.GetComponent<Character>();
        character.m_faction = Character.Faction.Players;
        character.m_boss = false;
        character.m_name = GunnarHaulingDefaults.WorkerDisplayName;

        clone.GetComponent<ZNetView>().m_persistent = true;

        prefabs.AddPrefab(clone);
        Prefab = clone;
        LastFailure = string.Empty;
        _log?.Invoke("Gunnar's worker prefab '" + PrefabName + "' is registered, built from '" + _baseCreature + "'.");
        return true;
    }

    /// <summary>Creates Gunnar's one body and writes his identity into its own
    /// network object. The caller has checked authority and that no other body
    /// exists anywhere in the world.</summary>
    internal static TeamsterWorkerAI? Spawn(Vector3 position, Quaternion rotation, out string failure)
    {
        failure = string.Empty;
        if (Prefab == null)
        {
            failure = "the worker prefab is not registered (" + LastFailure + ")";
            return null;
        }

        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null || !zones.IsZoneLoaded(position))
        {
            failure = "that ground is not loaded";
            return null;
        }

        GameObject instance = UnityEngine.Object.Instantiate(Prefab, position, rotation);
        ZNetView view = instance.GetComponent<ZNetView>();
        TeamsterWorkerAI ai = instance.GetComponent<TeamsterWorkerAI>();
        if (view == null || !view.IsValid() || !view.IsOwner() || ai == null)
        {
            failure = "the new body did not come up as a valid, owned network object";
            if (view != null && view.IsValid() && view.IsOwner())
            {
                view.Destroy();
            }
            else
            {
                UnityEngine.Object.Destroy(instance);
            }

            return null;
        }

        // D9: the identity lives in the worker body's own network object, and
        // (since #381) so does what that body is carrying. Both are fields of a
        // MOD-CREATED object; no vanilla object is written to. The keys are
        // spelled as literals rather than through their constants because the
        // validator's scoped allowance matches the literal - that is the
        // boundary working as intended, and GunnarHaulingDefaults carries the
        // same strings for everything else to read.
        view.GetZDO().Set("tcc.worker.key", WorkerKey.Gunnar.Value);
        view.GetZDO().Set("tcc.worker.revision", 0);
        ai.RememberIdentity(WorkerKey.Gunnar.Value);
        return ai;
    }

    private static void StripAll<T>(GameObject clone)
        where T : Component
    {
        foreach (T component in clone.GetComponentsInChildren<T>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }

    private static string? DescribeMissingComponents(GameObject clone)
    {
        if (clone.GetComponent<ZNetView>() == null)
        {
            return "it has no ZNetView, so it cannot be a persistent networked body";
        }

        if (clone.GetComponent<Character>() == null)
        {
            return "it has no Character, so it has no motor";
        }

        if (clone.GetComponent<ZSyncAnimation>() == null)
        {
            return "it has no ZSyncAnimation, which BaseAI.Awake requires";
        }

        if (clone.GetComponent<Rigidbody>() == null)
        {
            return "it has no Rigidbody on its root, so a cart's joint could not hold it";
        }

        if (clone.GetComponent<BaseAI>() == null)
        {
            return "it has no BaseAI, so it is not a creature prefab";
        }

        return null;
    }

    private static bool Fail(string reason)
    {
        LastFailure = reason;
        if (!string.Equals(_lastLoggedFailure, reason, StringComparison.Ordinal))
        {
            _lastLoggedFailure = reason;
            _log?.Invoke("Gunnar's worker prefab is not available: " + reason + ". Hauling stays off; everything else works.");
        }

        return false;
    }
}

/// <summary>A worker body's own inventory, stored in its own network object
/// (#381, DECISIONS.md D9, mirroring Concerned Foreman's
/// <c>SETTLEMENT_AUTHORITY.md</c> §5a).
///
/// <b>Why this exists.</b> Vanilla never saves a non-player humanoid's
/// inventory: <c>Humanoid.m_inventory</c> is a plain readonly field with no save
/// and no load, rebuilt on every instantiation. The body itself is persistent -
/// Gunnar stays in a world until he is retired, and the census reads saved bodies
/// - so before this, a stone he picked up was destroyed by a zone unload, a relog
/// or a world reload while the body came back empty. Silently: no refusal, no
/// record, no drop. That is the same loss <c>WorkerRetirement</c> refuses on a
/// deliberate retire, through a door nobody has to open.
///
/// <b>Why it lives in this file.</b> The validator allows a network-object write
/// only here, and only with a literal <c>"tcc.worker."</c> key. That rule is the
/// reason the inventory is stored in a mod-created object and nowhere near a
/// vanilla one, so the writer belongs inside the rule rather than beside it.
///
/// <b>The same synchronous call as a chest.</b> The inventory is written from
/// vanilla's own <c>Inventory.m_onChanged</c> callback - the exact mechanism
/// <c>Container</c> uses - so an add or a removal is persisted inside the call
/// that made it.
///
/// <b>Nothing is written before a successful load.</b> A body that could not read
/// what it carries is <b>inert</b>: it never saves, and the runtime never binds
/// it, because saving an empty inventory over a carried one is the very loss this
/// type exists to stop. An <i>absent</i> record is not that case - it is an
/// ordinary empty inventory, which is how every body saved before this field
/// existed loads (<see cref="WorkerInventoryRecord"/>).</summary>
internal sealed class TeamsterWorkerRecord : MonoBehaviour
{
    private ZNetView? _view;
    private Humanoid? _humanoid;
    private Inventory? _inventory;
    private Action? _onChanged;
    private bool _loading;

    /// <summary>Whether this body has read its stored inventory and may now both
    /// work and save. False is inert.</summary>
    internal bool IsLoaded { get; private set; }

    /// <summary>Why this body is inert, or empty.</summary>
    internal string Fault { get; private set; } = string.Empty;

    /// <summary>False when the most recent change could not be written. What the
    /// body holds is then uncertain, never assumed.</summary>
    internal bool LastChangePersisted { get; private set; } = true;

    /// <summary>How many writes this body has made. Zero means it never has.
    /// </summary>
    internal int Revision { get; private set; }

    /// <summary>How many items it holds, tools included. Meaningful only while
    /// <see cref="IsLoaded"/>.</summary>
    internal int ItemCount => _inventory == null ? 0 : _inventory.NrOfItems();

    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>The record on a body, or null when it has none - which is itself
    /// a reason to treat the body as unreadable rather than empty.</summary>
    internal static TeamsterWorkerRecord? On(Component? body) =>
        body == null ? null : body.GetComponent<TeamsterWorkerRecord>();

    private void Start()
    {
        try
        {
            _view = GetComponent<ZNetView>();
            _humanoid = GetComponent<Humanoid>();
            if (_view == null || !_view.IsValid() || _humanoid == null)
            {
                Fault = "it has no valid network object or no humanoid";
                return;
            }

            ZDO record = _view.GetZDO();
            Revision = record.GetInt("tcc.worker.revision", 0);
            _inventory = _humanoid.GetInventory();
            if (_inventory == null)
            {
                Fault = "its inventory could not be read";
                return;
            }

            byte[] stored = record.GetByteArray("tcc.worker.inventory", null);
            _loading = true;
            try
            {
                if (WorkerInventoryRecord.Decide(stored) == WorkerRecordLoad.LoadStored)
                {
                    _inventory.Load(new ZPackage(stored));
                }
            }
            finally
            {
                _loading = false;
            }

            _onChanged = OnInventoryChanged;
            _inventory.m_onChanged = (Action)Delegate.Combine(_inventory.m_onChanged, _onChanged);
            IsLoaded = true;
        }
        catch (Exception exception)
        {
            // Inert, never half-loaded. A throw here must not reach the game's
            // own instantiation either.
            IsLoaded = false;
            Fault = "its stored inventory could not be loaded (" + exception.GetType().Name + ")";
            try
            {
                ErrorLog?.Invoke("Gunnar's body is inert: " + Fault +
                    ". He will not work and will not overwrite what he is holding. " + SafeFailure.Describe(exception));
            }
            catch
            {
                // Already failing; nothing more may escape.
            }
        }
    }

    private void OnEnable()
    {
        if (IsLoaded && _inventory != null && _onChanged == null)
        {
            _onChanged = OnInventoryChanged;
            _inventory.m_onChanged = (Action)Delegate.Combine(_inventory.m_onChanged, _onChanged);
        }
    }

    private void OnDisable()
    {
        // Unhooked here rather than in OnDestroy, and re-hooked in OnEnable: a
        // hook symmetric with this component's own enable and disable is simpler
        // to reason about than Unity's message resolution against BaseAI, which
        // declares a private OnDestroy of its own on the same object.
        //
        // A STATED ASSUMPTION OF THE CONTAINMENT, recorded rather than proved or
        // dismissed. While this component is disabled the hook is gone, so a
        // change made in that window is neither persisted NOR flagged:
        // `LastChangePersisted` stays true and `WorkerInventoryRecord.Trust`
        // keeps answering `Trusted`, because it is never asked whether the hook
        // is live. Everything downstream therefore assumes the hook is attached
        // whenever the inventory can change, and nothing here asserts it. The
        // review that found this explicitly did NOT claim it is reachable -
        // ZNetScene destroys distant objects rather than deactivating them - and
        // neither does this comment. It is written down because an unasserted
        // assumption a reader cannot see is worse than one they can.
        if (_inventory != null && _onChanged != null)
        {
            _inventory.m_onChanged = (Action)Delegate.Remove(_inventory.m_onChanged, _onChanged);
            _onChanged = null;
        }
    }

    /// <summary>Writes what the body holds into its own network object now.
    /// Answers whether it got there.</summary>
    internal bool TryPersist()
    {
        try
        {
            if (!IsLoaded || _inventory == null || _view == null || !_view.IsValid() || !_view.IsOwner())
            {
                return false;
            }

            var package = new ZPackage();
            _inventory.Save(package);
            ZDO record = _view.GetZDO();
            record.Set("tcc.worker.inventory", package.GetArray());
            Revision = WorkerInventoryRecord.Next(Revision);
            record.Set("tcc.worker.revision", Revision);
            return true;
        }
        catch (Exception exception)
        {
            try
            {
                ErrorLog?.Invoke("Gunnar's body could not write what it is holding, so what it holds is " +
                    "uncertain until the next change: " + SafeFailure.Brief(exception));
            }
            catch
            {
                // Nothing may escape a change callback.
            }

            return false;
        }
    }

    private void OnInventoryChanged()
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        LastChangePersisted = TryPersist();
    }
}
