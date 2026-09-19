using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>One identity, one body - tested by trying to end up with two.
///
/// Every test here is an attempt to reach the state the rule forbids: a
/// presentation figure and a saved worker body for the same NPC, or two holders
/// each believing they have it. The rule is worth this much attention because
/// its failure is not a crash. It looks like everything working, right up until
/// the second Thorstein is standing beside the first with the player's axe
/// inside him.</summary>
public class BodyArbiterTests
{
    private static NpcRoleRegistry WithWorker(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Thorstein;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForWorker("CF_SettlementWorker", "tcc.worker."))).IsRegistered);
        return registry;
    }

    private static NpcRoleRegistry WithPresentation(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Hulgi;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForPresentation())).IsRegistered);
        return registry;
    }

    [Fact]
    public void A_claim_nobody_made_is_not_a_grant()
    {
        Assert.Equal(BodyClaimStatus.Unspecified, default(BodyClaim).Status);
        Assert.False(default(BodyClaim).IsGranted);
    }

    [Fact]
    public void A_registered_role_may_claim_its_own_kind_once()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);

        BodyClaim claim = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.Claimed, claim.Status);
        Assert.True(claim.IsGranted);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void The_same_holder_asking_twice_is_satisfied_not_punished()
    {
        // A runtime re-asks every tick and after every reload. Refusing the
        // second ask would make the correct caller look like a conflict.
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        BodyClaim again = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        Assert.Equal(BodyClaimStatus.AlreadyHeld, again.Status);
        Assert.True(again.IsGranted);
    }

    [Fact]
    public void A_second_holder_is_refused_while_the_first_has_the_body()
    {
        NpcRoleRegistry registry = WithWorker(out NpcIdentity identity);
        registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-1");

        BodyClaim second = registry.TryClaimBody(identity, NpcBodyKind.Worker, "job-2");

        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, second.Status);
        Assert.False(second.IsGranted);
        Assert.Contains("job-1", second.Reason);
    }

    [Fact]
    public void A_worker_claim_is_refused_while_a_presentation_body_exists()
    {
        // The never-coexist rule, from the direction that matters most: Hulgi is
        // standing at camp and something orders him to work. The existing body
        // is released by whoever holds it, never torn down from here.
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        NpcIdentity identity = Identities.Hulgi;
        registry.Register(new FakeRole(identity, NpcBodyContract.ForPresentation()));
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
        NpcIdentity identity = Identities.Hulgi;
        Assert.True(arbiter.Track(identity, NpcBodyKind.Presentation));
        Assert.True(arbiter.TryClaim(identity, NpcBodyKind.Presentation, "director").IsGranted);

        Assert.Equal(
            BodyClaimStatus.RefusedOtherKindExists,
            arbiter.TryClaim(identity, NpcBodyKind.Worker, "job-1").Status);

        // With no body standing, the same claim gets the contract refusal.
        Assert.Equal(BodyClaimStatus.Released, arbiter.Release(identity, NpcBodyKind.Presentation, "director").Status);
        Assert.Equal(
            BodyClaimStatus.RefusedKindNotContracted,
            arbiter.TryClaim(identity, NpcBodyKind.Worker, "job-1").Status);
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

        Assert.True(registry.TryClaimBody(Identities.Thorstein, NpcBodyKind.Worker, "job-1").IsGranted);

        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(Identities.Gunnar));
        Assert.True(registry.TryClaimBody(Identities.Gunnar, NpcBodyKind.Worker, "job-1").IsGranted);
        Assert.NotSame(registry.ModeOf(Identities.Thorstein), registry.ModeOf(Identities.Gunnar));
    }
}
