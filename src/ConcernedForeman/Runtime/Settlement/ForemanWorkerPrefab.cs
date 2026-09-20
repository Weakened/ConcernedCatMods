using System;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Builds the worker prefab: a vanilla humanoid with its
/// <c>MonsterAI</c> replaced by <see cref="ForemanWorkerAI"/>.
///
/// <b>Why the swap happens on a prefab and never on a live creature.</b>
/// <c>BaseAI.Awake</c> registers three RPCs through <c>ZNetView.Register</c>,
/// which is <c>Dictionary.Add</c> on the name's stable hash — so registering the
/// same name twice on one view throws <c>ArgumentException</c>. Spawning a
/// creature and then swapping its AI would run <c>Awake</c> twice on one view
/// and throw, inside a driver that does not catch. Jotunn's
/// <c>CreateClonedPrefab</c> clones into its own inactive container, so the
/// clone's <c>Awake</c> has not run when the components are swapped, and runs
/// exactly once when a real instance is spawned.
///
/// <b>Registered at plugin start, and kept (D9, CONTRACTS.md §5.6).</b> The body
/// is persistent now: its identity and what it carries live in its own network
/// object and are saved with the world. The host destroys any saved object whose
/// prefab is not registered when the world's objects are created ("Destroyed
/// invalid prefab ZDO"), so a prefab built lazily by a console command — as it
/// was — would let the next load delete the body and everything in it. The
/// prefab is built as soon as vanilla prefabs exist (Jotunn's
/// <c>OnVanillaPrefabsAvailable</c>, at the main menu) and added to Jotunn's
/// custom prefabs, which registers it into every scene before any object in it
/// is created. It is never unregistered on world unload.
///
/// <b>Nothing is minted.</b> The base creature's loot table
/// (<c>CharacterDrop</c>: the Dverger drops coins, marble and a trophy) is
/// removed, so a worker death drops only what he really carried; its default
/// and random gear is cleared, so no item is granted on spawn and the stored
/// inventory is the only thing in his hands.
///
/// <b>The base prefab name is data, not an API.</b> Creature prefab names live
/// in the game's asset bundles, not in the assembly, so no amount of reading
/// <c>assembly_valheim.dll</c> can prove one exists. It is therefore
/// configurable, resolved at runtime, and <b>fails closed</b>: if the named
/// prefab is missing, or is not a humanoid with the components a worker needs,
/// no worker prefab is created and the runtime says exactly what was missing.
/// </summary>
internal static class ForemanWorkerPrefab
{
    /// <summary>The name the worker prefab is registered under. Saved bodies
    /// are found by this name; it never changes.
    ///
    /// It is <see cref="ForemanRole.BodyPrefabName"/> rather than a second copy
    /// of the same literal, so the name registered with the game and the name
    /// declared to Concerned NPC as <c>NpcBodyContract.PrefabName</c> cannot
    /// drift apart - the compiler proves they are one string. A mismatch would
    /// not be a cosmetic bug: the host destroys any saved object whose prefab is
    /// not registered.</summary>
    internal const string PrefabName = Domain.Npc.ForemanRole.BodyPrefabName;

    private static GameObject? _prefab;
    private static bool _installed;
    private static string _baseCreature = string.Empty;
    private static Action<string>? _log;

    internal static bool IsReady => _prefab != null;

    /// <summary>Why the last <see cref="TryCreate"/> failed, or null.</summary>
    internal static string? LastFailure { get; private set; }

    /// <summary>Builds the prefab as soon as vanilla prefabs are available, at
    /// plugin start, so it is registered before any world's objects are
    /// created. Idempotent.</summary>
    internal static void Install(string baseCreature, Action<string>? log)
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        _baseCreature = baseCreature ?? string.Empty;
        _log = log;
        PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
    }

    private static void OnVanillaPrefabsAvailable()
    {
        try
        {
            if (_prefab != null || TryCreate(_baseCreature, _log))
            {
                PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            }
        }
        catch (Exception exception)
        {
            LastFailure = "building the worker prefab threw " + exception.GetType().Name;
            _log?.Invoke("Worker prefab not created: " + exception);
        }
    }

    /// <summary>Creates the worker prefab from <paramref name="baseCreature"/>.
    /// Idempotent: a second call with the prefab already built is a no-op.
    /// </summary>
    internal static bool TryCreate(string baseCreature, Action<string>? log = null)
    {
        if (_prefab != null)
        {
            return true;
        }

        LastFailure = null;

        if (string.IsNullOrWhiteSpace(baseCreature))
        {
            return Fail("No base creature prefab is configured.", log);
        }

        PrefabManager prefabs = PrefabManager.Instance;
        if (prefabs == null)
        {
            return Fail("Jotunn's prefab manager is not available yet.", log);
        }

        GameObject? existing = prefabs.GetPrefab(PrefabName);
        if (existing != null && existing.GetComponent<WorkerBody>() != null)
        {
            // Built earlier in this process (for instance by a lazy fallback)
            // and still registered with Jotunn.
            _prefab = existing;
            return true;
        }

        GameObject? basePrefab = prefabs.GetPrefab(baseCreature);
        if (basePrefab == null)
        {
            return Fail(
                $"Base creature prefab '{baseCreature}' does not exist in this game build. " +
                "Set Settlement/WorkerBaseCreature to a humanoid prefab that does.",
                log);
        }

        GameObject? clone = prefabs.CreateClonedPrefab(PrefabName, basePrefab);
        if (clone == null)
        {
            return Fail($"Could not clone '{baseCreature}'.", log);
        }

        // Everything below runs while the clone is inactive inside Jotunn's
        // prefab container, so no Awake has executed on any of it.
        string? missing = DescribeMissingComponents(clone);
        if (missing != null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return Fail(
                $"'{baseCreature}' cannot be a worker: {missing}. " +
                "A worker needs a networked humanoid body.",
                log);
        }

        foreach (BaseAI vanillaAi in clone.GetComponentsInChildren<BaseAI>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(vanillaAi);
        }

        // Anything that would make the worker tameable, saddleable or a
        // spawn-point owner is vanilla behaviour this runtime does not want.
        foreach (Tameable tameable in clone.GetComponentsInChildren<Tameable>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(tameable);
        }

        // No loot is minted when a worker dies: only what he really carried is
        // dropped, by his body's own death handler.
        foreach (CharacterDrop drop in clone.GetComponentsInChildren<CharacterDrop>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(drop);
        }

        Humanoid humanoid = clone.GetComponent<Humanoid>();
        // Humanoid.Start gives non-players their default and random gear on
        // every instantiation. Empty arrays, not null: GiveDefaultItems reads
        // each array's length.
        humanoid.m_defaultItems = new GameObject[0];
        humanoid.m_randomWeapon = new GameObject[0];
        humanoid.m_randomArmor = new GameObject[0];
        humanoid.m_randomShield = new GameObject[0];
        humanoid.m_randomSets = new Humanoid.ItemSet[0];
        humanoid.m_randomItems = new Humanoid.RandomItem[0];

        clone.GetComponent<ZNetView>().m_persistent = true;

        clone.AddComponent<ForemanWorkerAI>();
        clone.AddComponent<WorkerBody>();

        // Not an enemy of anyone, and not a boss. Faction behaviour is out of
        // scope; what is in scope is not shipping a worker that inherits a
        // hostile creature's faction by accident.
        Character character = clone.GetComponent<Character>();
        character.m_faction = Character.Faction.Players;
        character.m_boss = false;

        // Kept by Jotunn and registered into every scene from now on; and into
        // the current one, when a world is already up.
        prefabs.AddPrefab(new CustomPrefab(clone, fixReference: false));
        prefabs.RegisterToZNetScene(clone);
        _prefab = clone;
        log?.Invoke($"Worker prefab '{PrefabName}' built from '{baseCreature}' and registered for every world.");
        return true;
    }

    /// <summary>Spawns one worker. The caller is responsible for having
    /// established authority and stamping its identity; this refuses anyway if
    /// the ground is not loaded, because a creature spawned into unloaded ground
    /// is a creature nobody owns.</summary>
    internal static ForemanWorkerAI? Spawn(Vector3 position, Quaternion rotation)
    {
        if (_prefab == null)
        {
            return null;
        }

        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null || !zones.IsZoneLoaded(position))
        {
            return null;
        }

        GameObject instance = UnityEngine.Object.Instantiate(_prefab, position, rotation);
        return instance.GetComponent<ForemanWorkerAI>();
    }

    private static string? DescribeMissingComponents(GameObject clone)
    {
        if (clone.GetComponent<ZNetView>() == null)
        {
            return "it has no ZNetView, so it cannot be a networked entity";
        }

        if (clone.GetComponent<Humanoid>() == null)
        {
            return "it has no Humanoid, so it has no body to move or inventory to carry with";
        }

        if (clone.GetComponent<ZSyncAnimation>() == null)
        {
            return "it has no ZSyncAnimation, which BaseAI.Awake requires";
        }

        if (clone.GetComponent<Rigidbody>() == null)
        {
            return "it has no Rigidbody, so it cannot walk";
        }

        if (clone.GetComponent<BaseAI>() == null)
        {
            return "it has no BaseAI, so it is not a creature prefab";
        }

        return null;
    }

    private static bool Fail(string reason, Action<string>? log)
    {
        LastFailure = reason;
        log?.Invoke("Worker prefab not created: " + reason);
        return false;
    }
}
