using System;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedSteward.Domain;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Builds the Steward's prefab: a vanilla humanoid with its
/// <c>MonsterAI</c> replaced by <see cref="StewardWorkerAI"/>.
///
/// <b>Why the swap happens on a prefab and never on a live creature.</b>
/// <c>BaseAI.Awake</c> registers three RPCs through <c>ZNetView.Register</c>,
/// which is <c>Dictionary.Add</c> on the name's stable hash — registering the
/// same name twice on one view throws. Spawning a creature and then swapping
/// its AI runs <c>Awake</c> twice on one view and throws, inside a driver that
/// does not catch. Jötunn's <c>CreateClonedPrefab</c> clones into its own
/// inactive container, so the clone's <c>Awake</c> has not run when the
/// components are swapped and runs exactly once when a real instance is
/// spawned. `CLAUDE.md` says the same thing as a rule: never instantiate a live
/// body and strip components afterwards.
///
/// <b>Registered at plugin start, and kept.</b> The body is persistent: its
/// identity and what it carries live in its own network object and are saved
/// with the world. The host destroys any saved object whose prefab is not
/// registered when the world's objects are created, so a prefab built lazily by
/// a console command would let the next load delete the Steward and everything
/// in his pack. It is built as soon as vanilla prefabs exist — at the main
/// menu — and never unregistered.
///
/// <b>Nothing is minted.</b> The base creature's loot table is removed, so a
/// death drops only what he really carried; its default and random gear is
/// cleared, so no item is granted on spawn and his stored inventory is the only
/// thing in his hands.
///
/// <b>The base prefab name is data, not an API.</b> Creature prefab names live
/// in the game's asset bundles, so no amount of reading the assembly can prove
/// one exists. It is configurable, resolved at runtime, and <b>fails closed</b>:
/// a missing or unsuitable prefab produces no Steward and an explanation.
/// </summary>
/// <remarks>The construction sequence is Concerned Foreman's, arrived at from
/// the same decompile and the same in-game failures. Copied rather than shared:
/// shared source may not contain game types, and products never reference each
/// other.</remarks>
internal static class StewardWorkerPrefab
{
    /// <summary>The name the prefab is registered under. Saved bodies are found
    /// by it, so it never changes — including when the Steward is finally
    /// named. It is declared with the rest of his permanent identity rather
    /// than here, because that is the kind of thing it is.</summary>
    internal const string PrefabName = StewardRole.BodyPrefabName;

    private static GameObject? _prefab;
    private static bool _installed;
    private static string _baseCreature = string.Empty;
    private static Action<string>? _log;

    internal static bool IsReady => _prefab != null;

    /// <summary>Why the last attempt failed, or null.</summary>
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
            LastFailure = "building the Steward's prefab threw " + exception.GetType().Name;
            _log?.Invoke("The Steward's prefab was not created: " + exception);
        }
    }

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
            return Fail("Jötunn's prefab manager is not available yet.", log);
        }

        GameObject? existing = prefabs.GetPrefab(PrefabName);
        if (existing != null && existing.GetComponent<StewardBody>() != null)
        {
            _prefab = existing;
            return true;
        }

        GameObject? basePrefab = prefabs.GetPrefab(baseCreature);
        if (basePrefab == null)
        {
            return Fail(
                "The base creature prefab '" + baseCreature + "' does not exist in this game " +
                "build. Set Steward/BaseCreature to a humanoid prefab that does.",
                log);
        }

        GameObject? clone = prefabs.CreateClonedPrefab(PrefabName, basePrefab);
        if (clone == null)
        {
            return Fail("Could not clone '" + baseCreature + "'.", log);
        }

        // Everything below runs while the clone is inactive inside Jötunn's
        // prefab container, so no Awake has executed on any of it.
        string? missing = DescribeMissingComponents(clone);
        if (missing != null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return Fail(
                "'" + baseCreature + "' cannot be the Steward: " + missing +
                ". He needs a networked humanoid body.",
                log);
        }

        foreach (BaseAI vanillaAi in clone.GetComponentsInChildren<BaseAI>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(vanillaAi);
        }

        // Tameable would make him a pet with a spawn point and a saddle. None
        // of that is what this is.
        foreach (Tameable tameable in clone.GetComponentsInChildren<Tameable>(includeInactive: true))
        {
            UnityEngine.Object.DestroyImmediate(tameable);
        }

        // No loot is minted when he dies: only what he really carried is
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

        clone.AddComponent<StewardWorkerAI>();
        clone.AddComponent<StewardBody>();

        // Not an enemy of anyone, and not a boss. Faction behaviour is out of
        // scope; what is in scope is not shipping a Steward who inherits a
        // hostile creature's faction by accident.
        Character character = clone.GetComponent<Character>();
        character.m_faction = Character.Faction.Players;
        character.m_boss = false;

        prefabs.AddPrefab(new CustomPrefab(clone, fixReference: false));
        prefabs.RegisterToZNetScene(clone);
        _prefab = clone;
        log?.Invoke(
            "The Steward's prefab '" + PrefabName + "' was built from '" + baseCreature +
            "' and registered for every world.");
        return true;
    }

    /// <summary>Spawns the Steward. The caller establishes authority and stamps
    /// his identity; this refuses anyway when the ground is not loaded, because
    /// a body spawned into unloaded ground is a body nobody owns.</summary>
    internal static StewardWorkerAI? Spawn(Vector3 position, Quaternion rotation)
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
        return instance.GetComponent<StewardWorkerAI>();
    }

    /// <summary>The prefab stays registered across worlds; only the reference
    /// to a scene-bound instance is dropped elsewhere. Present so a test of the
    /// plugin lifecycle has something to call.</summary>
    internal static void OnWorldUnloaded() => StewardBody.ForgetAll();

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
        log?.Invoke("The Steward's prefab was not created: " + reason);
        return false;
    }
}
