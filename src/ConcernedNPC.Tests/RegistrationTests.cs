using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>What a role may declare, and what the registry does with a role that
/// declares it badly.</summary>
public class BodyContractTests
{
    [Fact]
    public void A_worker_contract_carries_both_durable_facts()
    {
        NpcBodyContract body = NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker.");

        Assert.Equal(NpcBodyKind.Worker, body.Kind);
        Assert.Equal("CF_SettlementWorker", body.PrefabName);
        Assert.Equal("tcc.worker.", body.ZdoKeyPrefix);
        Assert.True(body.Validate(out string reason), reason);
    }

    [Fact]
    public void A_presentation_contract_carries_neither()
    {
        NpcBodyContract body = NpcBodyContract.ForPresentation();

        Assert.Equal(NpcBodyKind.Presentation, body.Kind);
        Assert.Equal(string.Empty, body.PrefabName);
        Assert.Equal(string.Empty, body.ZdoKeyPrefix);
        Assert.True(body.Validate(out string reason), reason);
    }

    [Fact]
    public void A_defaulted_contract_is_not_a_body()
    {
        Assert.Equal(NpcBodyKind.Unspecified, default(NpcBodyContract).Kind);
        Assert.False(default(NpcBodyContract).Validate(out _));
    }

    [Theory]
    [InlineData("", "tcc.worker.")]
    [InlineData("CF SettlementWorker", "tcc.worker.")]
    [InlineData("CF-Settlement/Worker", "tcc.worker.")]
    [InlineData("CF_SettlementWorker", "")]
    [InlineData("CF_SettlementWorker", "tcc.worker")]
    [InlineData("CF_SettlementWorker", ".tcc.worker.")]
    [InlineData("CF_SettlementWorker", "tcc..worker.")]
    [InlineData("CF_SettlementWorker", "TCC.Worker.")]
    public void A_worker_contract_with_a_malformed_durable_fact_is_refused(string prefab, string prefix)
    {
        Assert.False(NpcBodyContract.ForWorker(prefab, prefix).Validate(out string reason));
        Assert.NotEqual(string.Empty, reason);
    }

    [Fact]
    public void A_presentation_contract_that_carries_durable_facts_is_refused()
    {
        // Not harmless extra detail: a presentation body is local only and never
        // saved, so a prefab name or a key prefix on one means somebody intends
        // to persist a figure the safety rules say must not be.
        Assert.False(
            NpcBodyContract.Compose(NpcBodyKind.Presentation, "CC_Hulgi", string.Empty).Validate(out string prefab));
        Assert.Contains("no registered prefab", prefab);

        Assert.False(
            NpcBodyContract.Compose(NpcBodyKind.Presentation, string.Empty, "tcc.hulgi.").Validate(out string prefix));
        Assert.Contains("stores nothing in the world", prefix);
    }

    [Fact]
    public void A_registry_refuses_a_presentation_role_that_smuggled_durable_facts_in()
    {
        RoleRegistration outcome = Identities.EmptyRegistry().Register(new FakeRole(
            Identities.Hulgi, NpcBodyContract.Compose(NpcBodyKind.Presentation, "CC_Hulgi", string.Empty)));

        Assert.Equal(RoleRegistrationStatus.InvalidBodyContract, outcome.Status);
    }
}

/// <summary>Registering roles: all of it, or none of it, and never a
/// throw.</summary>
public class RoleRegistryTests
{
    [Fact]
    public void A_valid_role_registers()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        RoleRegistration outcome = registry.Register(
            new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker.")));

        Assert.True(outcome.IsRegistered);
        Assert.Equal(RoleRegistrationStatus.Registered, outcome.Status);
        Assert.Equal(Identities.Thorstein, outcome.Identity);
        Assert.Equal(string.Empty, outcome.Reason);
        Assert.Equal(1, registry.RoleCount);
        Assert.Equal(new[] { Identities.Thorstein }, registry.Identities);
    }

    [Fact]
    public void A_status_nobody_computed_is_not_a_grant()
    {
        // The defect inherited from CompanionRegistry, which put Registered at
        // zero. A caller that believes it registered goes on to build a body.
        Assert.Equal(RoleRegistrationStatus.Unspecified, default(RoleRegistration).Status);
        Assert.False(default(RoleRegistration).IsRegistered);
    }

    [Fact]
    public void A_null_role_is_an_outcome_rather_than_an_exception()
    {
        RoleRegistration outcome = Identities.EmptyRegistry().Register(null);

        Assert.Equal(RoleRegistrationStatus.NoRole, outcome.Status);
        Assert.False(outcome.IsRegistered);
        Assert.NotEqual(string.Empty, outcome.Reason);
    }

    [Fact]
    public void A_role_whose_own_getter_throws_does_not_take_the_process_down()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        RoleRegistration bad = registry.Register(new ThrowingRole());
        RoleRegistration good = registry.Register(
            new FakeRole(Identities.Gunnar, NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.")));

        Assert.False(bad.IsRegistered);
        Assert.Contains("InvalidOperationException", bad.Reason);
        Assert.True(good.IsRegistered);
        Assert.Equal(1, registry.RoleCount);
    }

    [Fact]
    public void An_empty_identity_is_refused()
    {
        RoleRegistration outcome = Identities.EmptyRegistry()
            .Register(new FakeRole(default, NpcBodyContract.ForPresentation()));

        Assert.Equal(RoleRegistrationStatus.InvalidIdentity, outcome.Status);
    }

    [Fact]
    public void A_contradictory_body_contract_is_refused_and_says_which_fact()
    {
        RoleRegistration outcome = Identities.EmptyRegistry()
            .Register(new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "nope")));

        Assert.Equal(RoleRegistrationStatus.InvalidBodyContract, outcome.Status);
        Assert.Contains("key prefix", outcome.Reason);
    }

    [Fact]
    public void A_role_without_usable_paths_is_refused()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        NpcBodyContract body = NpcBodyContract.ForPresentation();

        Assert.Equal(
            RoleRegistrationStatus.InvalidDataPaths,
            registry.Register(new FakeRole(Identities.Hulgi, body, new FakePaths(string.Empty))).Status);
        Assert.Equal(
            RoleRegistrationStatus.InvalidDataPaths,
            registry.Register(new FakeRole(Identities.Hulgi, body, new FakePaths("relative/path"))).Status);
        Assert.Equal(
            RoleRegistrationStatus.InvalidDataPaths,
            registry.Register(new FakeRole(Identities.Hulgi, body, new ThrowingPaths())).Status);
        Assert.Equal(0, registry.RoleCount);
    }

    [Fact]
    public void One_identity_registers_once()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        var first = new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."));
        var second = new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_Other", "tcc.worker."));

        Assert.True(registry.Register(first).IsRegistered);
        Assert.Equal(RoleRegistrationStatus.DuplicateIdentity, registry.Register(second).Status);
        Assert.Equal(1, registry.RoleCount);
    }

    [Fact]
    public void Two_roles_may_share_a_key_prefix_and_never_a_prefab_name()
    {
        // Foreman and Teamster both write tcc.worker.*, separated only by the
        // prefab their bodies are registered under. That is legal, and it is
        // exactly why the prefab must not also be shared: the census filters by
        // prefab first, so two roles on one prefab bind each other's bodies and
        // the visible failure is a second Thorstein holding the player's axe.
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        Assert.True(registry.Register(new FakeRole(
            Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."))).IsRegistered);
        Assert.True(registry.Register(new FakeRole(
            Identities.Gunnar, NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker."))).IsRegistered);

        RoleRegistration collision = registry.Register(new FakeRole(
            Identities.Steward, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.steward.")));

        Assert.Equal(RoleRegistrationStatus.DuplicatePrefabName, collision.Status);
        Assert.Contains("foreman/thorstein", collision.Reason);
        Assert.Equal(2, registry.RoleCount);
    }

    [Fact]
    public void Any_number_of_presentation_roles_may_coexist()
    {
        // They share the empty prefab name, which is not a prefab name at all.
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        Assert.True(registry.Register(
            new FakeRole(Identities.Hulgi, NpcBodyContract.ForPresentation())).IsRegistered);
        Assert.True(registry.Register(
            new FakeRole(new NpcIdentity("cartographer", "someone-else"), NpcBodyContract.ForPresentation()))
            .IsRegistered);
        Assert.Equal(2, registry.RoleCount);
    }

    [Fact]
    public void A_refused_registration_leaves_nothing_behind()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        Assert.False(registry.Register(
            new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "bad-prefix")))
            .IsRegistered);

        Assert.Equal(0, registry.RoleCount);
        Assert.Empty(registry.Identities);
        Assert.False(registry.TryGetRole(Identities.Thorstein, out _));
        // No mode owner was created, so no body may be claimed for it either.
        Assert.Equal(
            Bodies.BodyClaimStatus.RefusedNotRegistered,
            registry.TryClaimBody(Identities.Thorstein, NpcBodyKind.Worker, "job").Status);
        // And the prefab name is free for the role that should have it.
        Assert.True(registry.Register(
            new FakeRole(Identities.Steward, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.steward.")))
            .IsRegistered);
    }

    [Fact]
    public void A_role_is_only_reachable_through_its_own_identity()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        var role = new FakeRole(Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."));
        registry.Register(role);

        Assert.True(registry.TryGetRole(Identities.Thorstein, out INpcRole found));
        Assert.Same(role, found);

        // Same worker slug, different product: a different NPC entirely.
        Assert.False(registry.TryGetRole(new NpcIdentity("teamster", "thorstein"), out _));
        Assert.False(registry.TryGetRole(default, out _));
    }
}
