using System;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;

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
            Fail("building it threw " + exception.GetType().Name + ": " + exception.Message);
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

        // D9: the identity lives in the worker body's own network object, the
        // only network-object field this product writes.
        view.GetZDO().Set("tcc.worker.key", WorkerKey.Gunnar.Value);
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
