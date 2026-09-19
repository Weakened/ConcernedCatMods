using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>One identity, one body - tested by trying to end up with two.
///
/// Every test here is an attempt to reach the state the rule forbids: a
/// presentation figure and a saved worker body for the same NPC, or two holders
/// each believing they have it, or a hold that outlives the world its body
/// stood in. The rule is worth this much attention because its failure is not a
/// crash. It looks like everything working, right up until the second Thorstein
/// is standing beside the first with the player's axe inside him.</summary>
public class BodyArbiterTests
{
    private static NpcRoleRegistry WithWorker(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Thorstein;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."))).IsRegistered);
        registry.BeginWorldLoad(Identities.AWorld());
        return registry;
    }

    private static NpcRoleRegistry WithPresentation(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Hulgi;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForPresentation())).IsRegistered);
        registry.BeginWorldLoad(Identities.AWorld());
        return registry;
    }

    [Fact]
    public void A_claim_nobody_made_is_not_a_grant()
    {
        Assert.Equal(BodyClaimStatus.Unspecified, default(BodyClaim).Status);
        Assert.False(default(BodyClaim).IsGranted);
        Assert.Null(default(BodyClaim).Lease);
    }

    [Fact]
    public void A_registered_role_may_claim_its_own_kind_once()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);

        BodyClaim claim = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.Claimed, claim.Status);
        Assert.True(claim.IsGranted);
        Assert.NotNull(claim.Lease);
        Assert.True(claim.Lease!.IsActive);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void The_same_holder_asking_twice_is_satisfied_not_punished()
    {
        // A runtime re-asks every tick. Refusing the second ask would make the
        // correct caller look like a conflict.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        BodyClaim first = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        BodyClaim again = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.AlreadyHeld, again.Status);
        Assert.True(again.IsGranted);
        Assert.Same(first.Lease, again.Lease);
    }

    [Fact]
    public void A_second_holder_is_refused_while_the_first_has_the_body()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        BodyClaim second = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2");

        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, second.Status);
        Assert.False(second.IsGranted);
        Assert.Null(second.Lease);
        Assert.Contains("job-1", second.Reason);
    }

    [Fact]
    public void A_worker_claim_is_refused_while_a_presentation_body_exists()
    {
        // The never-coexist rule, from the direction that matters most: Hulgi is
        // standing at camp and something orders him to work. The existing body
        // is released by whoever holds it, never torn down from here.
        NpcRoleRegistry registry = WithPresentation(out NpcIdentity identity);
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Presentation, "director").IsGranted);

        BodyClaim worker = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.False(worker.IsGranted);
        Assert.Equal(BodyClaimStatus.RefusedOtherKindExists, worker.Status);
        Assert.Contains("released, never replaced", worker.Reason);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void A_presentation_claim_is_refused_while_a_worker_body_exists()
    {
        // The same rule from the other direction: a job is running and something
        // asks to show the figure at camp. Two of him, in two places.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").IsGranted);

        BodyClaim presentation = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "director");

        Assert.Equal(BodyClaimStatus.RefusedOtherKindExists, presentation.Status);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void The_other_kind_refusal_outranks_the_contract_refusal()
    {
        // Both would refuse, and which one the caller is told matters. "A
        // presentation body already exists" is the rule; "you did not contract
        // for this kind" is an accident of a role having exactly one contracted
        // kind today, and it would hide the rule the moment that changed.
        var arbiter = new NpcBodyArbiter();
        NpcWorldEpoch world = Identities.AWorld();
        NpcIdentity identity = Identities.Hulgi;
        Assert.True(arbiter.Track(identity, NpcBodyKind.Presentation));
        Assert.True(arbiter.TryClaim(identity, NpcBodyKind.Presentation, "director", world).IsGranted);

        Assert.Equal(
            BodyClaimStatus.RefusedOtherKindExists,
            arbiter.TryClaim(identity, NpcBodyKind.Worker, "job-1", world).Status);

        // With no body standing, the same claim gets the contract refusal.
        Assert.Equal(
            BodyClaimStatus.Released,
            arbiter.Release(identity, NpcBodyKind.Presentation, "director", world).Status);
        Assert.Equal(
            BodyClaimStatus.RefusedKindNotContracted,
            arbiter.TryClaim(identity, NpcBodyKind.Worker, "job-1", world).Status);
    }

    [Fact]
    public void A_kind_the_role_did_not_contract_for_is_refused()
    {
        NpcRoleRegistry registry = WithPresentation(out NpcIdentity identity);

        Assert.Equal(
            BodyClaimStatus.RefusedKindNotContracted,
            registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Status);
        Assert.Equal(
            BodyClaimStatus.RefusedKindNotContracted,
            registry.TryClaimBody(identity, NpcBodyKind.Unspecified, "job-1").Status);
    }

    [Fact]
    public void An_unregistered_identity_may_not_take_a_body()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        registry.BeginWorldLoad(Identities.AWorld());

        Assert.Equal(
            BodyClaimStatus.RefusedNotRegistered,
            registry.TryClaimBody(Identities.Gunnar, NpcBodyKind.Worker, "job-1").Status);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(Identities.Gunnar));
    }

    [Fact]
    public void A_body_without_a_holder_is_refused()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);

        Assert.Equal(
            BodyClaimStatus.RefusedNoHolder,
            registry.TryClaimBody(identity, NpcBodyKind.Worker, string.Empty).Status);
        Assert.Equal(
            BodyClaimStatus.RefusedNoHolder,
            registry.TryClaimBody(identity, NpcBodyKind.Worker, null!).Status);
    }

    [Fact]
    public void Releasing_a_body_you_do_not_hold_changes_nothing()
    {
        // Cleanup after a reload, a death or a failure releases unconditionally,
        // so this has to be an ordinary answer - and it must never be a way to
        // clear somebody else's claim.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        BodyClaim wrongHolder = registry.ReleaseBody(identity, NpcBodyKind.Worker, "job-2");
        BodyClaim wrongKind = registry.ReleaseBody(identity, NpcBodyKind.Presentation, "job-1");

        Assert.Equal(BodyClaimStatus.NotHeld, wrongHolder.Status);
        Assert.Equal(BodyClaimStatus.NotHeld, wrongKind.Status);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
        Assert.Equal(BodyClaimStatus.AlreadyHeld, registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Status);
    }

    [Fact]
    public void Releasing_frees_the_identity_for_the_next_holder()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.Released, registry.ReleaseBody(identity, NpcBodyKind.Worker, "job-1").Status);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
        Assert.Equal(BodyClaimStatus.Claimed, registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2").Status);
    }

    [Fact]
    public void Releasing_a_body_releases_the_identity_its_holder_was_holding()
    {
        // A job whose body is gone but which still holds the identity leaves the
        // NPC permanently busy: nothing else may ever move, retire or replace it.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");
        ActorModeOwner? mode = registry.ModeOf(identity);
        Assert.NotNull(mode);
        Assert.Equal(ActorModeOutcome.Entered, mode!.Enter(ActorMode.Working, "job-1"));
        Assert.False(mode.MayRetireBody);

        registry.ReleaseBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(ActorMode.Resting, mode.Mode);
        Assert.True(mode.MayRetireBody);
        Assert.Null(mode.JobId);
    }

    [Fact]
    public void A_job_holding_the_identity_blocks_another_holder_from_taking_the_body()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        ActorModeOwner mode = registry.ModeOf(identity)!;
        mode.Enter(ActorMode.Surveying, "job-1");

        BodyClaim other = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2");

        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, other.Status);
        Assert.Contains("job-1", other.Reason);
        Assert.Equal(BodyClaimStatus.Claimed, registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Status);
    }

    [Fact]
    public void A_presentation_body_may_only_be_claimed_while_the_identity_is_resting()
    {
        // Presentation is what an NPC is when nobody has ordered it to do
        // anything. Appearing at camp while a job is in flight would put the
        // figure where home is and the working body where the work is.
        NpcRoleRegistry registry = WithPresentation(out NpcIdentity identity);
        ActorModeOwner mode = registry.ModeOf(identity)!;
        mode.Enter(ActorMode.Working, "director");

        BodyClaim claim = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "director");

        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, claim.Status);
        Assert.Contains("resting", claim.Reason);

        mode.Release("director");
        Assert.Equal(
            BodyClaimStatus.Claimed, registry.TryClaimBody(identity, NpcBodyKind.Presentation, "director").Status);
    }

    [Fact]
    public void There_is_exactly_one_mode_owner_per_identity()
    {
        // The failure this prevents is two owners for one identity, each correct
        // and each unaware of the other. A caller has nowhere to make a second.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);

        Assert.Same(registry.ModeOf(identity), registry.ModeOf(identity));
        Assert.Null(registry.ModeOf(Identities.Gunnar));
    }

    [Fact]
    public void Tracking_an_identity_twice_is_refused()
    {
        var arbiter = new NpcBodyArbiter();

        Assert.True(arbiter.Track(Identities.Thorstein, NpcBodyKind.Worker));
        Assert.False(arbiter.Track(Identities.Thorstein, NpcBodyKind.Worker));
        Assert.False(arbiter.Track(default, NpcBodyKind.Worker));
        Assert.Equal(1, arbiter.Count);
    }

    [Fact]
    public void Two_identities_do_not_interfere()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        registry.Register(new FakeRole(
            Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker.")));
        registry.Register(new FakeRole(
            Identities.Gunnar, NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.")));
        registry.BeginWorldLoad(Identities.AWorld());

        Assert.True(registry.TryClaimBody(Identities.Thorstein, NpcBodyKind.Worker, "job-1").IsGranted);

        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(Identities.Gunnar));
        Assert.True(registry.TryClaimBody(Identities.Gunnar, NpcBodyKind.Worker, "job-1").IsGranted);
        Assert.NotSame(registry.ModeOf(Identities.Thorstein), registry.ModeOf(Identities.Gunnar));
    }
}

/// <summary>A body belongs to one world load, and the arbiter has to know it.
///
/// Roles register once per process; bodies do not survive a trip to the main
/// menu. Holding a claim as though it were process-wide produces two opposite
/// failures, and both are reachable through the front door, so both are driven
/// here - by reloading the world between operations, which nothing in the first
/// version of this suite ever did.</summary>
public class BodyArbiterWorldReloadTests
{
    private static NpcRoleRegistry Registered(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Thorstein;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."))).IsRegistered);
        return registry;
    }

    [Fact]
    public void No_world_means_no_body()
    {
        // Fail closed. A claim before a world is loaded could only ever be a
        // claim on a body that does not exist.
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);

        Assert.True(registry.CurrentWorld.IsUnknown);
        BodyClaim claim = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.RefusedNoWorld, claim.Status);
        Assert.False(claim.IsGranted);
        Assert.Null(claim.Lease);
    }

    [Fact]
    public void A_stable_holder_is_not_told_it_already_has_a_body_in_a_new_world()
    {
        // The first failure a process-wide hold produces, and the worse of the
        // two: a runtime whose holder id is its own identity gets AlreadyHeld -
        // a GRANT - for a body that was destroyed with the previous world and
        // was never built in this one. It would then skip building one and
        // drive nothing at all.
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);
        registry.BeginWorldLoad(Identities.AWorld());
        Assert.Equal(BodyClaimStatus.Claimed, registry.TryClaimBody(identity, NpcBodyKind.Worker, "gunnar").Status);

        registry.BeginWorldLoad(Identities.AWorld());

        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
        BodyClaim again = registry.TryClaimBody(identity, NpcBodyKind.Worker, "gunnar");
        Assert.Equal(BodyClaimStatus.Claimed, again.Status);
    }

    [Fact]
    public void A_holder_that_vanished_with_its_world_does_not_hold_the_identity_forever()
    {
        // The opposite failure: a per-session holder id. After the reload the
        // new holder would be refused by a job that ended with the previous
        // world, and Release could not clear it because Release needs the OLD
        // holder string, which nothing now has. The identity would be unusable
        // for the life of the process.
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);
        registry.BeginWorldLoad(Identities.AWorld());
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Worker, "session-1-job").IsGranted);
        registry.ModeOf(identity)!.Enter(ActorMode.Working, "session-1-job");

        registry.BeginWorldLoad(Identities.AWorld());

        Assert.Equal(ActorMode.Resting, registry.ModeOf(identity)!.Mode);
        Assert.Null(registry.ModeOf(identity)!.JobId);
        BodyClaim fresh = registry.TryClaimBody(identity, NpcBodyKind.Worker, "session-2-job");
        Assert.Equal(BodyClaimStatus.Claimed, fresh.Status);
    }

    [Fact]
    public void Reloading_between_every_pair_of_operations_leaves_no_grant_standing()
    {
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);

        for (int round = 0; round < 3; round++)
        {
            registry.BeginWorldLoad(Identities.AWorld());
            BodyClaim claim = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job");
            Assert.Equal(BodyClaimStatus.Claimed, claim.Status);
            Assert.True(claim.Lease!.IsActive);

            registry.EndWorldLoad();

            Assert.False(claim.Lease.IsActive);
            Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
            Assert.Equal(
                BodyClaimStatus.RefusedNoWorld,
                registry.TryClaimBody(identity, NpcBodyKind.Worker, "job").Status);
            Assert.Equal(
                BodyClaimStatus.NotHeld,
                registry.ReleaseBody(identity, NpcBodyKind.Worker, "job").Status);
        }
    }

    [Fact]
    public void Beginning_the_same_world_twice_is_a_no_op()
    {
        // Several roles may each notice the world load; none of them should have
        // to know whether another got there first, and none of them should
        // destroy a claim another already took.
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);
        NpcWorldEpoch world = Identities.AWorld();
        registry.BeginWorldLoad(world);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job");

        Assert.Equal(0, registry.BeginWorldLoad(world));
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void A_world_load_reports_how_many_bodies_it_forgot()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        registry.Register(new FakeRole(
            Identities.Thorstein, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker.")));
        registry.Register(new FakeRole(
            Identities.Gunnar, NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.")));
        registry.BeginWorldLoad(Identities.AWorld());
        registry.TryClaimBody(Identities.Thorstein, NpcBodyKind.Worker, "job");
        registry.TryClaimBody(Identities.Gunnar, NpcBodyKind.Worker, "job");

        Assert.Equal(2, registry.BeginWorldLoad(Identities.AWorld()));
        Assert.Equal(0, registry.BeginWorldLoad(Identities.AWorld()));
    }

    [Fact]
    public void Registration_survives_a_world_and_a_body_does_not()
    {
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);
        registry.BeginWorldLoad(Identities.AWorld());
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job");

        registry.EndWorldLoad();

        Assert.Equal(1, registry.RoleCount);
        Assert.True(registry.TryGetRole(identity, out _));
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void Beginning_an_unknown_world_is_an_unload_not_an_adoption()
    {
        NpcRoleRegistry registry = Registered(out NpcIdentity identity);
        registry.BeginWorldLoad(Identities.AWorld());
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job");

        Assert.Equal(1, registry.BeginWorldLoad(NpcWorldEpoch.Unknown));

        Assert.True(registry.CurrentWorld.IsUnknown);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
    }
}

/// <summary>The lease: permission in a form the body factory can require.</summary>
public class BodyLeaseTests
{
    private static NpcRoleRegistry Loaded(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Thorstein;
        registry.Register(new FakeRole(
            identity, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker.")));
        registry.BeginWorldLoad(Identities.AWorld());
        return registry;
    }

    [Fact]
    public void A_lease_comes_only_with_a_grant()
    {
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);

        BodyClaim granted = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");
        BodyClaim refused = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2");

        Assert.NotNull(granted.Lease);
        Assert.Null(refused.Lease);
        Assert.Equal(identity, granted.Lease!.Identity);
        Assert.Equal(NpcBodyKind.Worker, granted.Lease.Kind);
        Assert.Equal("job-1", granted.Lease.Holder);
    }

    [Fact]
    public void Disposing_a_lease_gives_the_body_up()
    {
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);
        BodyLease lease = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Lease!;

        lease.Dispose();

        Assert.False(lease.IsActive);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
        Assert.Equal(BodyClaimStatus.Claimed, registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2").Status);
    }

    [Fact]
    public void Disposing_twice_is_safe_and_never_touches_the_next_holder()
    {
        // A finally block that does not know whether the claim succeeded must be
        // able to dispose unconditionally - and must not evict whoever took the
        // body afterwards.
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);
        BodyLease first = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Lease!;
        first.Dispose();
        BodyLease second = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2").Lease!;

        first.Dispose();

        Assert.True(second.IsActive);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void A_lease_from_a_previous_world_is_not_active_and_disposing_it_does_nothing()
    {
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);
        BodyLease old = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job").Lease!;

        registry.BeginWorldLoad(Identities.AWorld());
        BodyLease now = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job").Lease!;

        Assert.False(old.IsActive);
        old.Dispose();
        Assert.True(now.IsActive);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void Releasing_through_the_registry_expires_the_lease()
    {
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);
        BodyLease lease = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1").Lease!;

        registry.ReleaseBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.False(lease.IsActive);
    }

    [Fact]
    public void A_disposed_lease_cannot_release_the_hold_that_replaced_it()
    {
        NpcRoleRegistry registry = Loaded(out NpcIdentity identity);

        BodyClaim first = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");
        Assert.True(first.IsGranted);
        BodyLease stale = first.Lease!;
        stale.Dispose();

        // The same holder, in the same world, immediately afterwards: every
        // value on the old lease matches the new hold, so only the lease's own
        // identity distinguishes them.
        BodyClaim second = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");
        Assert.True(second.IsGranted);

        Assert.False(stale.IsActive);
        stale.Dispose();
        Assert.True(second.Lease!.IsActive);
    }

    [Fact]
    public void A_role_is_given_the_epoch_and_a_second_caller_is_given_the_same_one()
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();

        NpcWorldEpoch first = registry.BeginWorldLoad(out int forgottenFirst);
        NpcWorldEpoch second = registry.BeginWorldLoad(out int forgottenSecond);

        Assert.False(first.IsUnknown);
        Assert.Equal(first, second);
        Assert.Equal(0, forgottenFirst);
        Assert.Equal(0, forgottenSecond);

        // A world that has gone and come back is a different world, whatever it
        // is called: nothing a role holds from the first survives into it.
        registry.EndWorldLoad();
        NpcWorldEpoch afterReload = registry.BeginWorldLoad(out _);
        Assert.NotEqual(first, afterReload);
    }
}
