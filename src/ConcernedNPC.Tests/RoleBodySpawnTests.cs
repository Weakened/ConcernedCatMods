using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>#372: a role whose worker prefab predates the factory hands that
/// prefab in rather than instantiating it, and gets exactly the factory's gate.
///
/// Each test is the refusal the shipped products did not have when each of them
/// called the engine's own instantiation directly: no lease, a lease that has
/// gone, a census that is not finished and empty, a prefab the network scene
/// cannot name, and an identity that never made it onto the new body.</summary>
public sealed class RoleBodySpawnTests : IDisposable
{
    private readonly List<GameObject> _stamped = new List<GameObject>();

    public RoleBodySpawnTests()
    {
        BodyFixtures.ResetWorld();
        ZDOMan.instance = new ZDOMan();
        ZNetScene.instance = new ZNetScene();
    }

    public void Dispose() => BodyFixtures.ResetWorld();

    [Fact]
    public void Nothing_is_built_without_a_lease()
    {
        GameObject prefab = Registered();

        Assert.Null(Spawn(null, prefab, out string failure));
        Assert.Contains("no lease", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_lease_disposed_between_the_claim_and_the_build_refuses_the_build()
    {
        // The acceptance criterion, for the products' own prefabs: permission
        // has to exist at the build, not merely at the claim.
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());

        lease.Dispose();

        Assert.Null(Spawn(lease, prefab, out string failure));
        Assert.Contains("no longer held", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_world_that_unloaded_after_the_claim_refuses_the_build()
    {
        GameObject prefab = Registered();
        var registry = new NpcRoleRegistry();
        BodyLease lease = Claim(registry);

        registry.EndWorldLoad();
        registry.BeginWorldLoad(out _);

        Assert.Null(Spawn(lease, prefab, out string failure));
        Assert.Contains("no longer held", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_live_lease_builds_the_roles_own_prefab_and_stamps_it_in_the_same_call()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        var at = new Vector3(3f, 4f, 5f);

        GameObject? body = Spawn(lease, prefab, out string failure, at);

        Assert.NotNull(body);
        Assert.Equal(string.Empty, failure);
        Assert.NotSame(prefab, body);
        Assert.Equal(at, body!.transform.position);
        Assert.Single(_stamped);
        Assert.Same(body, _stamped[0]);
        Assert.False(body.GetComponent<ZNetView>()!.DestroyCalled);
    }

    [Fact]
    public void A_saved_body_for_that_identity_refuses_the_build_even_when_the_role_says_nothing()
    {
        // The tally is not a parameter: the library counts, so a role cannot
        // say "none" for itself.
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        var saved = new ZDO();
        saved.Set(BodyFixtures.WorkerKeyField, BodyFixtures.Thorstein.Value);
        ZDOMan.instance.Saved[BodyFixtures.ForemanPrefab] = new List<ZDO> { saved };

        Assert.Null(Spawn(lease, prefab, out string failure));
        Assert.Contains("census says", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void Somebody_elses_saved_body_does_not_refuse_this_one()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        var other = new ZDO();
        other.Set(BodyFixtures.WorkerKeyField, "foreman/somebody");
        ZDOMan.instance.Saved[BodyFixtures.ForemanPrefab] = new List<ZDO> { other };

        Assert.NotNull(Spawn(lease, prefab, out string failure));
        Assert.Equal(string.Empty, failure);
    }

    [Fact]
    public void A_world_whose_objects_cannot_be_counted_refuses_the_build()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZDOMan.instance = null!;

        Assert.Null(Spawn(lease, prefab, out string failure));
        Assert.Contains("census says", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_prefab_the_network_scene_cannot_name_refuses_the_build()
    {
        // The irreversible case: a body whose prefab the scene does not know is
        // destroyed by the next load with everything in it.
        GameObject unregistered = Prefab(BodyFixtures.ForemanPrefab);
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(Spawn(lease, unregistered, out string failure));
        Assert.Contains("does not know this prefab", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_prefab_registered_under_another_name_refuses_the_build()
    {
        GameObject misnamed = Prefab("CF_SomethingElse");
        ZNetScene.instance.Named[BodyFixtures.ForemanPrefab] = misnamed;
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(Spawn(lease, misnamed, out string failure));
        Assert.Contains("the contract names", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void No_prefab_and_no_scene_each_refuse_the_build()
    {
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(Spawn(lease, null, out string none));
        Assert.Contains("no prefab", none, StringComparison.Ordinal);

        GameObject prefab = Registered();
        ZNetScene.instance = null!;
        Assert.Null(Spawn(lease, prefab, out string noScene));
        Assert.Contains("no network scene", noScene, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void Unloaded_ground_refuses_the_build()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZoneSystem.instance.Loaded = false;

        Assert.Null(Spawn(lease, prefab, out string failure));
        Assert.Contains("not loaded", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_presentation_lease_does_not_build_a_worker_body()
    {
        GameObject prefab = Registered();
        var registry = new NpcRoleRegistry();
        registry.Register(new FakeRole(
            BodyFixtures.Thorstein,
            NpcBodyContract.ForWorkerWithPresentation(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyPrefix)));
        registry.BeginWorldLoad(out _);
        BodyClaim claim = registry.TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Presentation, "figure");
        Assert.True(claim.IsGranted, claim.Reason);

        // The role's own contract, so the refusal is the gate's kind check and
        // not the contract-vs-lease check in front of it.
        Assert.Null(SpawnUnder(claim.Lease,
            NpcBodyContract.ForWorkerWithPresentation(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyPrefix), prefab,
            out string failure));
        Assert.Contains("permits a Presentation body", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_body_that_could_not_be_given_its_identity_is_destroyed_again()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZNetView? view = null;

        GameObject? body = NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease, BodyFixtures.ForemanContract(), prefab, Vector3.zero, Quaternion.identity,
            instance =>
            {
                view = instance.GetComponent<ZNetView>();
                return false;
            },
            out string failure);

        Assert.Null(body);
        Assert.NotNull(view);
        Assert.True(view!.DestroyCalled);
        Assert.Contains("could not be given its identity", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stamp_that_throws_is_a_refusal_and_the_body_is_destroyed_again()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZNetView? view = null;

        GameObject? body = NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease, BodyFixtures.ForemanContract(), prefab, Vector3.zero, Quaternion.identity,
            instance =>
            {
                view = instance.GetComponent<ZNetView>();
                throw new InvalidOperationException("the object refused the write");
            },
            out string failure);

        Assert.Null(body);
        Assert.True(view!.DestroyCalled);
        Assert.Contains("threw", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stamp_that_writes_another_identity_is_a_refusal_and_the_body_is_destroyed_again()
    {
        // The census counts by the lease's identity under the contract's key
        // field; a body stamped as anybody else is one that count never finds.
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZNetView? view = null;

        GameObject? body = NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease, BodyFixtures.ForemanContract(), prefab, Vector3.zero, Quaternion.identity,
            instance =>
            {
                view = instance.GetComponent<ZNetView>();
                view!.GetZDO().Set(BodyFixtures.WorkerKeyField, "foreman/somebody");
                return true;
            },
            out string failure);

        Assert.Null(body);
        Assert.True(view!.DestroyCalled);
        Assert.Contains("identity other than", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lease_never_builds_another_roles_prefab()
    {
        // #372 review: Thorstein's lease, handed in with a contract that names a
        // prefab he never registered. Refused before the census - the census is
        // made unanswerable here, so a refusal that came from it instead would
        // say "census says".
        const string otherPrefab = "CT_SomebodyElse";
        GameObject prefab = Prefab(otherPrefab);
        ZNetScene.instance.Named[otherPrefab] = prefab;
        BodyLease lease = Claim(new NpcRoleRegistry());
        ZDOMan.instance = null!;

        Assert.Null(SpawnUnder(lease, NpcBodyContract.ForWorker(otherPrefab, BodyFixtures.WorkerKeyPrefix), prefab,
            out string failure));
        Assert.Contains("registered body contract", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void The_roles_prefab_under_another_key_prefix_is_not_its_contract()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(SpawnUnder(lease, NpcBodyContract.ForWorker(BodyFixtures.ForemanPrefab, "tcc.other."), prefab,
            out string failure));
        Assert.Contains("registered body contract", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_contract_that_differs_only_in_admitting_a_figure_is_not_the_roles_contract()
    {
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(SpawnUnder(lease,
            NpcBodyContract.ForWorkerWithPresentation(BodyFixtures.ForemanPrefab, BodyFixtures.WorkerKeyPrefix), prefab,
            out string failure));
        Assert.Contains("registered body contract", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    [Fact]
    public void A_lease_from_another_registry_is_judged_by_the_registry_that_granted_it()
    {
        // The contract a lease is checked against is the one its own arbiter
        // recorded, not whatever some other registry was told.
        GameObject prefab = Registered();
        var granting = new NpcRoleRegistry();
        granting.Register(new FakeRole(BodyFixtures.Thorstein,
            NpcBodyContract.ForWorker("CF_Elsewhere", BodyFixtures.WorkerKeyPrefix)));
        granting.BeginWorldLoad(out _);
        BodyClaim claim = granting.TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Worker, "test");
        Assert.True(claim.IsGranted, claim.Reason);
        var other = new NpcRoleRegistry();
        other.Register(new FakeRole(BodyFixtures.Thorstein, BodyFixtures.ForemanContract()));

        Assert.Null(Spawn(claim.Lease, prefab, out string failure));
        Assert.Contains("registered body contract", failure, StringComparison.Ordinal);
        Assert.Empty(_stamped);
    }

    // --- #372 body-inventory move: a role prefab carrying NpcBody ------

    [Fact]
    public void A_role_prefab_carrying_the_librarys_body_is_stamped_by_the_library_with_the_leases_identity()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        GameObject prefab = Registered();
        prefab.Add(new NpcBody());
        BodyLease lease = Claim(new NpcRoleRegistry());

        GameObject? body = SpawnWithLibraryStamp(lease, prefab, out string failure);

        Assert.NotNull(body);
        Assert.Equal(string.Empty, failure);
        ZNetView view = body!.GetComponent<ZNetView>()!;
        Assert.False(view.DestroyCalled);
        // The shipped literals, not composed: the keys Foreman always wrote.
        Assert.Equal("foreman/thorstein", view.GetZDO().GetString("tcc.worker.key", string.Empty));
        Assert.Equal(0, view.GetZDO().GetInt("tcc.worker.revision", -1));
    }

    [Fact]
    public void A_role_prefab_whose_body_was_never_registered_is_refused_before_anything_is_built()
    {
        GameObject prefab = Registered();
        prefab.Add(new NpcBody());
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(SpawnWithLibraryStamp(lease, prefab, out string failure));
        Assert.Contains("no role registered what bodies of", failure, StringComparison.Ordinal);
        Assert.Equal(string.Empty, prefab.GetComponent<ZNetView>()!.GetZDO().GetString(BodyFixtures.WorkerKeyField, string.Empty));
    }

    [Fact]
    public void A_registration_under_another_key_prefix_does_not_admit_the_contract()
    {
        BodyFixtures.Register(NpcBodyContract.ForWorker(BodyFixtures.ForemanPrefab, BodyFixtures.StewardKeyPrefix));
        GameObject prefab = Registered();
        prefab.Add(new NpcBody());
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(SpawnWithLibraryStamp(lease, prefab, out string failure));
        Assert.Contains("no role registered what bodies of", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_prefab_without_the_librarys_body_is_destroyed_again_rather_than_left_unidentified()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        GameObject prefab = Registered();
        BodyLease lease = Claim(new NpcRoleRegistry());

        Assert.Null(SpawnWithLibraryStamp(lease, prefab, out string failure));
        Assert.Contains("could not be given its identity", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_librarys_stamp_still_needs_a_live_lease()
    {
        BodyFixtures.Register(BodyFixtures.ForemanContract());
        GameObject prefab = Registered();
        prefab.Add(new NpcBody());
        BodyLease lease = Claim(new NpcRoleRegistry());
        lease.Dispose();

        Assert.Null(SpawnWithLibraryStamp(lease, prefab, out string failure));
        Assert.Contains("no longer held", failure, StringComparison.Ordinal);
        Assert.Null(SpawnWithLibraryStamp(null, prefab, out failure));
        Assert.Contains("no lease", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_a_role_built_body_is_idempotent_and_refuses_a_second_set_of_facts()
    {
        NpcBodyContract foreman = BodyFixtures.ForemanContract();

        Assert.True(NpcBody.TryRegister(foreman, NpcBodyKeeps.IdentityAndInventory, out string reason), reason);
        Assert.True(NpcBody.TryRegister(foreman, NpcBodyKeeps.IdentityAndInventory, out reason), reason);
        Assert.False(NpcBody.TryRegister(foreman, NpcBodyKeeps.IdentityOnly, out reason));
        Assert.False(NpcBody.TryRegister(
            NpcBodyContract.ForWorker(BodyFixtures.ForemanPrefab, BodyFixtures.StewardKeyPrefix),
            NpcBodyKeeps.IdentityAndInventory, out reason));
        Assert.False(NpcBody.TryRegister(foreman, NpcBodyKeeps.Unspecified, out reason));
        Assert.False(NpcBody.TryRegister(NpcBodyContract.ForPresentation(), NpcBodyKeeps.IdentityOnly, out reason));
    }

    // -----------------------------------------------------------------

    private static GameObject? SpawnWithLibraryStamp(BodyLease? lease, GameObject prefab, out string failure) =>
        NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease, BodyFixtures.ForemanContract(), prefab, Vector3.zero, Quaternion.identity, out failure);

    private GameObject? SpawnUnder(BodyLease? lease, NpcBodyContract contract, GameObject? prefab, out string failure) =>
        NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease,
            contract,
            prefab,
            Vector3.zero,
            Quaternion.identity,
            instance =>
            {
                _stamped.Add(instance);
                return true;
            },
            out failure);

    private GameObject? Spawn(BodyLease? lease, GameObject? prefab, out string failure, Vector3? at = null) =>
        NpcWorkerPrefabFactory.TrySpawnRoleBody(
            lease,
            BodyFixtures.ForemanContract(),
            prefab,
            at ?? Vector3.zero,
            Quaternion.identity,
            instance =>
            {
                _stamped.Add(instance);
                instance.GetComponent<ZNetView>()!.GetZDO()
                    .Set(BodyFixtures.WorkerKeyField, BodyFixtures.Thorstein.Value);
                return true;
            },
            out failure);

    private static GameObject Prefab(string name)
    {
        var prefab = new GameObject(name);
        prefab.Add(new ZNetView());
        return prefab;
    }

    private static GameObject Registered()
    {
        GameObject prefab = Prefab(BodyFixtures.ForemanPrefab);
        ZNetScene.instance.Named[BodyFixtures.ForemanPrefab] = prefab;
        return prefab;
    }

    private static BodyLease Claim(NpcRoleRegistry registry)
    {
        registry.Register(new FakeRole(BodyFixtures.Thorstein, BodyFixtures.ForemanContract()));
        registry.BeginWorldLoad(out _);
        BodyClaim claim = registry.TryClaimBody(BodyFixtures.Thorstein, NpcBodyKind.Worker, "test");
        Assert.True(claim.IsGranted, claim.Reason);
        return claim.Lease!;
    }
}
