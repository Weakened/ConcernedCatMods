using System;
using System.Reflection;
using Jotunn.Managers;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The literal durable facts of the three shipped worker bodies, and
/// the scaffolding for standing one up against the stubbed game.
///
/// <b>These literals belong here and nowhere else.</b> The library is forbidden
/// to contain any of them - that rule has its own audit - so this file is the
/// only place in the repository outside the three products themselves where the
/// exact prefab names and key prefixes appear. That is the point: a golden test
/// has to name the bytes it is pinning, or it pins whatever the code currently
/// does.</summary>
internal static class BodyFixtures
{
    // ---------------------------------------------------------------------
    // The shipped facts. Changing any line in this block is a data migration,
    // not a test edit.
    // ---------------------------------------------------------------------

    /// <summary>Concerned Foreman's settlement worker.</summary>
    internal const string ForemanPrefab = "CF_SettlementWorker";

    /// <summary>Concerned Teamster's cart puller. A different prefab from the
    /// Foreman worker and the SAME key prefix, which is exactly why the census
    /// filters by prefab before it reads a key.</summary>
    internal const string TeamsterPrefab = "CT_TeamsterWorker";

    /// <summary>Concerned Steward's body.</summary>
    internal const string StewardPrefab = "CS_Steward";

    internal const string WorkerKeyPrefix = "tcc.worker.";

    internal const string StewardKeyPrefix = "tcc.steward.";

    /// <summary>The three field names Concerned Foreman's and Concerned
    /// Teamster's bodies write, as string literals copied from the shipped
    /// sources.</summary>
    internal const string WorkerKeyField = "tcc.worker.key";

    internal const string WorkerInventoryField = "tcc.worker.inventory";

    internal const string WorkerRevisionField = "tcc.worker.revision";

    /// <summary>Concerned Steward's three.</summary>
    internal const string StewardKeyField = "tcc.steward.key";

    internal const string StewardInventoryField = "tcc.steward.inventory";

    internal const string StewardRevisionField = "tcc.steward.revision";

    internal static NpcBodyContract ForemanContract() =>
        NpcBodyContract.ForWorker(ForemanPrefab, WorkerKeyPrefix);

    internal static NpcBodyContract TeamsterContract() =>
        NpcBodyContract.ForWorker(TeamsterPrefab, WorkerKeyPrefix);

    internal static NpcBodyContract StewardContract() =>
        NpcBodyContract.ForWorker(StewardPrefab, StewardKeyPrefix);

    internal static NpcIdentity Thorstein => new NpcIdentity("foreman", "thorstein");

    internal static NpcIdentity Gunnar => new NpcIdentity("teamster", "gunnar");

    internal static NpcIdentity Sunniva => new NpcIdentity("steward", "sunniva");

    // ---------------------------------------------------------------------
    // Scaffolding.
    // ---------------------------------------------------------------------

    /// <summary>Puts the process back to how it starts: no registered prefabs,
    /// no live bodies, no world, no subscribers.</summary>
    internal static void ResetWorld()
    {
        ClearRegisteredPrefabs();
        NpcBody.ForgetAll();
        NpcBody.Loaded = null;
        NpcBody.Died = null;
        NpcBody.ErrorLog = null;
        NpcBodyMind.ErrorLog = null;
        PrefabManager.ResetSubscribersForTests();
        PrefabManager.Instance = null!;
        ZDOMan.instance = null!;
        ZNetScene.instance = null!;
        ZoneSystem.instance = new ZoneSystem();
        Pathfinding.instance = new Pathfinding();
        ClearLiveMinds();
    }

    /// <summary>Empties the prefab-to-setup table by reflection rather than
    /// through a method on the shipped type.
    ///
    /// Deliberate. Prefab registration is permanent and one-way for a reason -
    /// a prefab missing when a world's objects are created is how saved bodies
    /// are destroyed - so a public "forget everything" would be a method whose
    /// only correct number of production callers is zero. A test reaches in
    /// instead, exactly as the settlement product's own body tests do.</summary>
    private static void ClearRegisteredPrefabs()
    {
        object table = typeof(NpcBodyContracts)
            .GetField("Known", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        table.GetType().GetMethod("Clear")!.Invoke(table, Array.Empty<object>());
    }

    private static void ClearLiveMinds()
    {
        var list = (System.Collections.Generic.List<NpcBodyMind>)typeof(NpcBodyMind)
            .GetField("LiveMinds", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        list.Clear();
    }

    /// <summary>Registers a role's body the way its prefab factory would, so a
    /// body of that prefab can find its own keys.</summary>
    internal static void Register(
        NpcBodyContract contract, NpcBodyKeeps keeps = NpcBodyKeeps.IdentityAndInventory)
    {
        if (!NpcBodyContracts.TryRemember(contract, keeps, out string reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>A base creature good enough to become a worker body: a
    /// networked humanoid with animation sync, a rigid body and a mind to
    /// replace.</summary>
    internal static GameObject BaseCreature(string name = "Dverger")
    {
        var creature = new GameObject(name);
        creature.Add(new ZNetView());
        creature.Add(new Humanoid());
        creature.Add(new ZSyncAnimation());
        creature.Add(new Rigidbody());
        creature.Add(new BaseAI());
        return creature;
    }

    /// <summary>A prefab manager holding one base creature under
    /// <paramref name="baseName"/>.</summary>
    internal static PrefabManager ManagerWith(GameObject baseCreature, string baseName = "Dverger")
    {
        var manager = new PrefabManager();
        manager.Preload(baseName, baseCreature);
        PrefabManager.Instance = manager;
        return manager;
    }

    /// <summary>Runs a component's <c>Start</c>, which the stubbed engine does
    /// not.</summary>
    internal static void Start(Component component) =>
        component.GetType()
            .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, Array.Empty<object>());

    /// <summary>A stack of one named item.</summary>
    internal static ItemDrop.ItemData Stack(string name, int count)
    {
        var item = new ItemDrop.ItemData { m_stack = count };
        item.m_shared.m_name = name;
        return item;
    }

    /// <summary>A body standing in a world, as the host would have created it
    /// from a save: the registered prefab's name plus the suffix the engine
    /// appends, an owned network object, and whatever the save held.</summary>
    internal static NpcBody SavedBody(string prefabName, Action<ZDO>? stored = null)
    {
        var instance = new GameObject(prefabName + "(Clone)");
        instance.Add(new ZNetView());
        instance.Add(new Humanoid());
        var body = instance.Add(new NpcBody());
        stored?.Invoke(instance.GetComponent<ZNetView>()!.GetZDO());
        Start(body);
        return body;
    }
}
