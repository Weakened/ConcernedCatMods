using System;
using Jotunn.Managers;
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
/// <b>The base prefab name is data, not an API.</b> Creature prefab names live
/// in the game's asset bundles, not in the assembly, so no amount of reading
/// <c>assembly_valheim.dll</c> can prove one exists. It is therefore
/// configurable, resolved at runtime, and <b>fails closed</b>: if the named
/// prefab is missing, or is not a humanoid with the components a worker needs,
/// no worker prefab is created and the runtime says exactly what was missing.
/// This is the same rule the companion audit applied to the start-location
/// name.</summary>
internal static class ForemanWorkerPrefab
{
    /// <summary>The name the worker prefab is registered under.</summary>
    internal const string PrefabName = "CF_SettlementWorker";

    private static GameObject? _prefab;

    internal static bool IsReady => _prefab != null;

    /// <summary>Why the last <see cref="TryCreate"/> failed, or null.</summary>
    internal static string? LastFailure { get; private set; }

    /// <summary>Creates the worker prefab from <paramref name="baseCreature"/>.
    /// Idempotent: a second call with the prefab already built is a no-op, which
    /// matters because world load can run initialisation more than once.</summary>
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
            prefabs.DestroyPrefab(PrefabName);
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
        // spawn-point owner is vanilla behaviour this spike does not want.
        foreach (Tameable tameable in clone.GetComponentsInChildren<Tameable>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(tameable);
        }

        clone.AddComponent<ForemanWorkerAI>();

        Character character = clone.GetComponent<Character>();
        // Not an enemy of anyone, and not a boss. Faction behaviour is out of
        // scope for this leaf; what is in scope is not shipping a worker that
        // inherits a hostile creature's faction by accident.
        character.m_faction = Character.Faction.Players;
        character.m_boss = false;

        prefabs.RegisterToZNetScene(clone);
        _prefab = clone;
        log?.Invoke(
            $"Worker prefab '{PrefabName}' built from '{baseCreature}'.");
        return true;
    }

    /// <summary>Spawns one worker. The caller is responsible for having
    /// established authority; this refuses anyway if the ground is not loaded,
    /// because a creature spawned into unloaded ground is a creature nobody
    /// owns.</summary>
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

    /// <summary>Forgets the built prefab. Called on world unload so a second
    /// world in the same session rebuilds against its own scene.</summary>
    internal static void Reset()
    {
        _prefab = null;
        LastFailure = null;
    }

    private static string? DescribeMissingComponents(GameObject clone)
    {
        if (clone.GetComponent<ZNetView>() == null)
        {
            return "it has no ZNetView, so it cannot be a networked entity";
        }

        if (clone.GetComponent<Character>() == null)
        {
            return "it has no Character, so it has no body to move";
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
