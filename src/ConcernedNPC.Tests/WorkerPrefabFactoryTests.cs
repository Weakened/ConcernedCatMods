using System;
using System.Collections.Generic;
using Jotunn.Managers;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The one place a worker body is built, and every way it refuses.
///
/// The build half pins each of the eight ways the three shipped copies had
/// drifted, so a later edit that quietly drops one is a failing test rather
/// than a reviewer's memory. The spawn half is the acceptance criterion this
/// leaf carries from the contract re-review: a lease is required, and it is
/// re-checked immediately before the body is created.</summary>
public sealed class WorkerPrefabFactoryTests : IDisposable
{
    private readonly List<string> _log = new List<string>();

    public WorkerPrefabFactoryTests() => BodyFixtures.ResetWorld();

    public void Dispose() => BodyFixtures.ResetWorld();

    // -----------------------------------------------------------------
    // Building the prefab.
    // -----------------------------------------------------------------

    [Fact]
    public void A_role_that_cannot_name_its_prefab_gets_no_factory_at_all()
    {
        Assert.False(NpcWorkerPrefabFactory.TryFor(
            NpcBodyContract.ForPresentation(),
            NpcWorkerPrefabOptions.Keeping(NpcBodyKeeps.IdentityAndInventory),
            out NpcWorkerPrefabFactory? factory,
            out string reason));

        Assert.Null(factory);
        Assert.NotEqual(string.Empty, reason);
    }

    [Fact]
    public void A_role_that_has_not_said_what_its_body_stores_gets_no_factory()
    {
        Assert.False(NpcWorkerPrefabFactory.TryFor(
            BodyFixtures.ForemanContract(),
            NpcWorkerPrefabOptions.Keeping(NpcBodyKeeps.Unspecified),
            out _,
            out string reason));

        Assert.Contains("must say what its body stores", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_removes_every_one_of_the_eight_component_families()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        creature.Add(new Tameable());
        creature.Add(new Sadle());
        creature.Add(new NpcTalk());
        creature.Add(new CharacterDrop());
        creature.Add(new Procreation());
        creature.Add(new Growup());
        creature.Add(new CharacterTimedDestruction());
        BodyFixtures.ManagerWith(creature);

        GameObject prefab = Build(creature).Prefab!;

        // Two of the three shipped builds removed only the first, the second
        // and the fifth. The other five are the cart runtime's, found in game.
        //
        // The base creature's own mind is gone and the only one left is this
        // library's, which is itself a mind: asking whether any remains would
        // answer yes for the right reason and prove nothing.
        BaseAI[] minds = prefab.GetComponentsInChildren<BaseAI>(includeInactive: true);
        Assert.Single(minds);
        Assert.IsType<NpcBodyMind>(minds[0]);

        Assert.True(prefab.GetComponent<Tameable>() == null);
        Assert.True(prefab.GetComponent<Sadle>() == null);
        Assert.True(prefab.GetComponent<NpcTalk>() == null);
        Assert.True(prefab.GetComponent<CharacterDrop>() == null);
        Assert.True(prefab.GetComponent<Procreation>() == null);
        Assert.True(prefab.GetComponent<Growup>() == null);
        Assert.True(prefab.GetComponent<CharacterTimedDestruction>() == null);
    }

    [Fact]
    public void The_build_empties_all_six_gear_arrays_rather_than_nulling_them()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        Humanoid source = creature.GetComponent<Humanoid>();
        source.m_defaultItems = new[] { new GameObject("axe") };
        BodyFixtures.ManagerWith(creature);

        Humanoid built = Build(creature).Prefab!.GetComponent<Humanoid>();

        // Empty, never null: the code that grants gear reads each array's
        // length.
        Assert.Empty(built.m_defaultItems);
        Assert.Empty(built.m_randomWeapon);
        Assert.Empty(built.m_randomArmor);
        Assert.Empty(built.m_randomShield);
        Assert.Empty(built.m_randomSets);
        Assert.Empty(built.m_randomItems);
    }

    [Fact]
    public void The_built_prefab_is_persistent_a_player_faction_and_not_a_boss()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(creature);

        GameObject prefab = Build(creature).Prefab!;

        Assert.True(prefab.GetComponent<ZNetView>().m_persistent);
        Assert.Equal(Character.Faction.Players, prefab.GetComponent<Character>().m_faction);
        Assert.False(prefab.GetComponent<Character>().m_boss);
        Assert.NotNull(prefab.GetComponent<NpcBodyMind>());
        Assert.NotNull(prefab.GetComponent<NpcBody>());
    }

    [Fact]
    public void A_display_name_is_set_only_when_the_role_asked_for_one()
    {
        GameObject anonymous = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(anonymous);
        anonymous.GetComponent<Character>().m_name = "Dverger";
        Assert.Equal("Dverger", Build(anonymous).Prefab!.GetComponent<Character>().m_name);

        BodyFixtures.ResetWorld();
        GameObject named = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(named);
        NpcWorkerPrefabFactory.TryFor(
            BodyFixtures.TeamsterContract(),
            NpcWorkerPrefabOptions.Keeping(NpcBodyKeeps.IdentityOnly).Named("Gunnar"),
            out NpcWorkerPrefabFactory? factory,
            out _);
        factory!.TryCreate("Dverger", _log.Add);

        Assert.Equal("Gunnar", factory.Prefab!.GetComponent<Character>().m_name);
    }

    [Fact]
    public void Registration_is_both_calls_not_one()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        PrefabManager manager = BodyFixtures.ManagerWith(creature);

        Build(creature);

        // One of the three shipped copies made only the first call. Being known
        // to the framework by name is not the same as being in the network
        // scene's table when a saved object is recreated, and the difference is
        // the difference between a saved body coming back and being destroyed.
        Assert.Contains(BodyFixtures.ForemanPrefab, manager.Added);
        Assert.Contains(BodyFixtures.ForemanPrefab, manager.RegisteredToScene);
    }

    [Theory]
    [InlineData("ZNetView", "network view")]
    [InlineData("Humanoid", "humanoid")]
    [InlineData("ZSyncAnimation", "animation sync")]
    [InlineData("Rigidbody", "rigid body")]
    [InlineData("BaseAI", "AI")]
    public void A_base_creature_missing_anything_a_body_needs_is_refused_by_name(string missing, string words)
    {
        GameObject creature = WithoutComponent(missing);
        BodyFixtures.ManagerWith(creature);
        NpcWorkerPrefabFactory factory = Factory();

        Assert.False(factory.TryCreate("Dverger", _log.Add));
        Assert.Contains(words, factory.LastFailure, StringComparison.Ordinal);
        Assert.False(factory.IsReady);
    }

    [Fact]
    public void A_missing_base_creature_fails_closed_and_says_which_setting_to_change()
    {
        BodyFixtures.ManagerWith(BodyFixtures.BaseCreature(), "SomethingElse");
        NpcWorkerPrefabFactory factory = Factory();

        Assert.False(factory.TryCreate("Dverger", _log.Add));
        Assert.Contains("does not exist in this game build", factory.LastFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_failure_is_logged_once_rather_than_on_every_return_to_the_menu()
    {
        BodyFixtures.ManagerWith(BodyFixtures.BaseCreature(), "SomethingElse");
        NpcWorkerPrefabFactory factory = Factory();

        factory.TryCreate("Dverger", _log.Add);
        factory.TryCreate("Dverger", _log.Add);
        factory.TryCreate("Dverger", _log.Add);

        Assert.Single(_log);
    }

    [Fact]
    public void An_existing_prefab_of_ours_is_adopted_and_somebody_elses_is_refused()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        PrefabManager manager = BodyFixtures.ManagerWith(creature);
        NpcWorkerPrefabFactory first = Factory();
        first.TryCreate("Dverger", _log.Add);

        NpcWorkerPrefabFactory second = Factory();
        Assert.True(second.TryCreate("Dverger", _log.Add));
        Assert.Same(first.Prefab, second.Prefab);

        // Now a stranger under the same name.
        BodyFixtures.ResetWorld();
        GameObject other = BodyFixtures.BaseCreature();
        manager = BodyFixtures.ManagerWith(other);
        manager.Preload(BodyFixtures.ForemanPrefab, new GameObject(BodyFixtures.ForemanPrefab));
        NpcWorkerPrefabFactory third = Factory();

        Assert.False(third.TryCreate("Dverger", _log.Add));
        Assert.Contains("already registered", third.LastFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void Installing_arms_the_build_for_the_moment_prefabs_exist_and_then_lets_go()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(creature);
        NpcWorkerPrefabFactory factory = Factory();

        factory.Install("Dverger", _log.Add);
        Assert.Equal(1, PrefabManager.SubscriberCount);

        PrefabManager.RaiseVanillaPrefabsAvailable();

        Assert.True(factory.IsReady);
        Assert.Equal(0, PrefabManager.SubscriberCount);
    }

    [Fact]
    public void A_build_that_fails_keeps_listening_because_staying_unregistered_is_the_expensive_outcome()
    {
        BodyFixtures.ManagerWith(BodyFixtures.BaseCreature(), "SomethingElse");
        NpcWorkerPrefabFactory factory = Factory();

        factory.Install("Dverger", _log.Add);
        PrefabManager.RaiseVanillaPrefabsAvailable();

        Assert.False(factory.IsReady);
        Assert.Equal(1, PrefabManager.SubscriberCount);
    }

    // -----------------------------------------------------------------
    // Spawning. The acceptance criteria.
    // -----------------------------------------------------------------

    [Fact]
    public void Nothing_is_built_without_a_lease()
    {
        NpcWorkerPrefabFactory factory = Ready();

        Assert.Null(factory.TrySpawn(null, Missing(), Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("no lease", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lease_disposed_between_the_claim_and_the_build_refuses_the_build()
    {
        // THE acceptance criterion. Taking a lease proves permission existed at
        // the claim; this needs permission to exist now. A runtime claims, then
        // waits for a census or for ground to load, and in that gap the world
        // can unload or the holder's own finally can let go.
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        lease.Dispose();

        Assert.Null(factory.TrySpawn(lease, Missing(), Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("no longer held", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_world_that_unloaded_after_the_claim_refuses_the_build()
    {
        // The other half of the same race, and the one nobody disposes
        // explicitly: the runtime still holds its lease object and believes it
        // has permission, but the body it named went with the scene.
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        registry.EndWorldLoad();
        registry.BeginWorldLoad(out _);

        Assert.Null(factory.TrySpawn(lease, Missing(), Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("no longer held", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_live_lease_builds_a_body_and_stamps_it_in_the_same_call()
    {
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        NpcBodyMind? mind = factory.TrySpawn(
            lease, Missing(), Vector3.zero, Quaternion.identity, out string failure);

        Assert.NotNull(mind);
        Assert.Equal(string.Empty, failure);
        Assert.Equal(BodyFixtures.Thorstein, mind!.Identity);

        ZDO zdo = mind.GetComponent<ZNetView>().GetZDO();
        Assert.Equal("foreman/thorstein", zdo.GetString(BodyFixtures.WorkerKeyField, string.Empty));
    }

    // The presence arrives as its number because the enum is internal and a
    // public test method may not name one in its signature. These numbers are
    // this library's own and nothing on disk records them.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_census_that_is_anything_but_finished_and_empty_refuses_the_build(int presenceValue)
    {
        var presence = (NpcBodyPresence)presenceValue;
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        NpcBodyTally tally = TallyReading(presence);
        Assert.Equal(presence, tally.Presence);

        Assert.Null(factory.TrySpawn(lease, tally, Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains(presence.ToString(), failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_census_taken_for_somebody_else_refuses_the_build()
    {
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        NpcBodyTally somebodyElse = NpcBodyTally.Of(BodyFixtures.Gunnar, true, 0, 0, 0, false);

        Assert.Null(factory.TrySpawn(lease, somebodyElse, Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("the lease is for", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Unloaded_ground_refuses_the_build()
    {
        NpcWorkerPrefabFactory factory = Ready();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);
        ZoneSystem.instance.Loaded = false;

        Assert.Null(factory.TrySpawn(lease, Missing(), Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("not loaded", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unbuilt_prefab_refuses_the_build_and_says_why_it_is_not_there()
    {
        BodyFixtures.ManagerWith(BodyFixtures.BaseCreature(), "SomethingElse");
        NpcWorkerPrefabFactory factory = Factory();
        factory.TryCreate("Dverger", _log.Add);
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry, factory);

        Assert.Null(factory.TrySpawn(lease, Missing(), Vector3.zero, Quaternion.identity, out string failure));
        Assert.Contains("not registered", failure, StringComparison.Ordinal);
        Assert.Contains("does not exist in this game build", failure, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------

    private static NpcBodyTally Missing() =>
        NpcBodyTally.Of(BodyFixtures.Thorstein, true, 0, 0, 0, false);

    private static NpcBodyTally TallyReading(NpcBodyPresence presence)
    {
        switch (presence)
        {
            case NpcBodyPresence.Searching:
                return NpcBodyTally.NotYetRun(BodyFixtures.Thorstein);
            case NpcBodyPresence.Present:
                return NpcBodyTally.Of(BodyFixtures.Thorstein, true, 1, 1, 0, false);
            case NpcBodyPresence.NotLoaded:
                return NpcBodyTally.Of(BodyFixtures.Thorstein, true, 1, 0, 0, false);
            case NpcBodyPresence.Duplicated:
                return NpcBodyTally.Of(BodyFixtures.Thorstein, true, 2, 0, 0, false);
            case NpcBodyPresence.Faulted:
                return NpcBodyTally.Of(BodyFixtures.Thorstein, true, 1, 1, 0, true);
            default:
                return Missing();
        }
    }

    private static BodyLease Claim(NpcRoleRegistry registry, NpcWorkerPrefabFactory factory)
    {
        registry.Register(new FakeRole(BodyFixtures.Thorstein, factory.Contract));
        registry.BeginWorldLoad(out _);
        BodyClaim claim = registry.TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Worker, "test");
        Assert.True(claim.IsGranted, claim.Reason);
        return claim.Lease!;
    }

    private NpcWorkerPrefabFactory Factory()
    {
        Assert.True(NpcWorkerPrefabFactory.TryFor(
            BodyFixtures.ForemanContract(),
            NpcWorkerPrefabOptions.Keeping(NpcBodyKeeps.IdentityAndInventory),
            out NpcWorkerPrefabFactory? factory,
            out string reason), reason);
        return factory!;
    }

    private NpcWorkerPrefabFactory Ready()
    {
        GameObject creature = BodyFixtures.BaseCreature();
        BodyFixtures.ManagerWith(creature);
        return Build(creature);
    }

    private NpcWorkerPrefabFactory Build(GameObject creature)
    {
        NpcWorkerPrefabFactory factory = Factory();
        Assert.True(factory.TryCreate("Dverger", _log.Add), factory.LastFailure);
        return factory;
    }

    private static GameObject WithoutComponent(string missing)
    {
        var creature = new GameObject("Dverger");
        if (missing != "ZNetView")
        {
            creature.Add(new ZNetView());
        }

        if (missing != "Humanoid")
        {
            creature.Add(new Humanoid());
        }

        if (missing != "ZSyncAnimation")
        {
            creature.Add(new ZSyncAnimation());
        }

        if (missing != "Rigidbody")
        {
            creature.Add(new Rigidbody());
        }

        if (missing != "BaseAI")
        {
            creature.Add(new BaseAI());
        }

        return creature;
    }
}
