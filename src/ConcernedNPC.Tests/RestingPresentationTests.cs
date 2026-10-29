using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A worker who rests as a presentation figure (0.5.0, #295):
/// <see cref="NpcBodyContract.ForWorkerWithPresentation"/>.
///
/// The contract widens which kinds an identity may ask for. It must not widen
/// how many it may have. So every test here tries to end up with both: the
/// figure at camp and the saved body at work. Each one has to fail, and fail
/// with the never-coexist refusal, not with an unrelated one that happens to
/// say no.</summary>
public class RestingPresentationTests
{
    private const string Prefab = "CF_SettlementWorker";
    private const string KeyPrefix = "tcc.worker.";

    private static NpcRoleRegistry WithRestingWorker(out NpcIdentity identity)
    {
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        identity = Identities.Thorstein;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForWorkerWithPresentation(Prefab, KeyPrefix))).IsRegistered);
        registry.BeginWorldLoad(Identities.AWorld());
        return registry;
    }

    [Fact]
    public void The_contract_is_still_a_worker_contract_with_the_same_durable_facts()
    {
        // Saved bodies are found by these. Adding presentation to a shipped role
        // must not move a byte.
        NpcBodyContract both = NpcBodyContract.ForWorkerWithPresentation(Prefab, KeyPrefix);
        NpcBodyContract worker = NpcBodyContract.ForWorker(Prefab, KeyPrefix);

        Assert.Equal(NpcBodyKind.Worker, both.Kind);
        Assert.Equal(worker.PrefabName, both.PrefabName);
        Assert.Equal(worker.ZdoKeyPrefix, both.ZdoKeyPrefix);
        Assert.True(both.TryValidate(out _));
    }

    [Fact]
    public void Only_the_new_factory_admits_both_kinds()
    {
        NpcBodyContract both = NpcBodyContract.ForWorkerWithPresentation(Prefab, KeyPrefix);
        NpcBodyContract worker = NpcBodyContract.ForWorker(Prefab, KeyPrefix);
        NpcBodyContract presentation = NpcBodyContract.ForPresentation();

        Assert.True(both.Admits(NpcBodyKind.Worker));
        Assert.True(both.Admits(NpcBodyKind.Presentation));
        Assert.False(both.Admits(NpcBodyKind.Unspecified));

        Assert.True(worker.Admits(NpcBodyKind.Worker));
        Assert.False(worker.Admits(NpcBodyKind.Presentation));

        Assert.True(presentation.Admits(NpcBodyKind.Presentation));
        Assert.False(presentation.Admits(NpcBodyKind.Worker));
    }

    [Fact]
    public void A_bad_worker_fact_is_still_refused_with_presentation_added()
    {
        Assert.False(NpcBodyContract.ForWorkerWithPresentation("", KeyPrefix).TryValidate(out _));
        Assert.False(NpcBodyContract.ForWorkerWithPresentation(Prefab, "").TryValidate(out _));
    }

    [Fact]
    public void A_resting_identity_may_be_shown_as_a_figure()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);

        BodyClaim figure = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation");

        Assert.Equal(BodyClaimStatus.Claimed, figure.Status);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void While_the_figure_is_held_no_worker_body_may_be_claimed()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").IsGranted);

        BodyClaim worker = registry.TryClaimBody(identity, NpcBodyKind.Worker, "settlement");

        Assert.Equal(BodyClaimStatus.RefusedOtherKindExists, worker.Status);
        Assert.Null(worker.Lease);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void While_a_worker_body_is_held_no_figure_may_be_claimed()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Worker, "settlement").IsGranted);

        BodyClaim figure = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation");

        Assert.Equal(BodyClaimStatus.RefusedOtherKindExists, figure.Status);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(identity));
    }

    [Fact]
    public void The_hand_over_is_release_then_claim_and_the_old_lease_dies()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        BodyLease figure = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").Lease!;

        figure.Dispose();
        BodyClaim worker = registry.TryClaimBody(identity, NpcBodyKind.Worker, "settlement");

        Assert.Equal(BodyClaimStatus.Claimed, worker.Status);
        Assert.False(figure.IsActive);
        Assert.True(worker.Lease!.IsActive);

        // And back: the worker body goes, then the figure may return.
        worker.Lease.Dispose();
        Assert.Equal(
            BodyClaimStatus.Claimed,
            registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").Status);
    }

    [Fact]
    public void A_figure_is_refused_while_a_job_holds_the_identity_even_with_no_body()
    {
        // An order in flight with no worker body claimed must still not put the
        // figure at camp: presentation is what he is when nobody has ordered him
        // to do anything.
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        Assert.Equal(ActorModeOutcome.Entered, registry.EnterMode(identity, ActorMode.Working, "collect-1"));

        BodyClaim figure = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation");

        Assert.False(figure.IsGranted);
        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, figure.Status);
    }

    [Fact]
    public void A_job_cannot_start_a_worker_body_under_a_held_figure()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        Assert.True(registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").IsGranted);
        Assert.Equal(ActorModeOutcome.Entered, registry.EnterMode(identity, ActorMode.Working, "collect-1"));

        Assert.Equal(
            BodyClaimStatus.RefusedOtherKindExists,
            registry.TryClaimBody(identity, NpcBodyKind.Worker, "collect-1").Status);
    }

    [Fact]
    public void A_world_unload_ends_the_figure_and_the_next_world_starts_clean()
    {
        NpcRoleRegistry registry = WithRestingWorker(out NpcIdentity identity);
        BodyLease figure = registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").Lease!;

        registry.EndWorldLoad();
        registry.BeginWorldLoad(Identities.AWorld());

        Assert.False(figure.IsActive);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(identity));
        Assert.Equal(
            BodyClaimStatus.Claimed,
            registry.TryClaimBody(identity, NpcBodyKind.Worker, "settlement").Status);
    }

    [Fact]
    public void A_worker_only_role_is_still_refused_a_figure()
    {
        // Unchanged from 0.4.0, and the reason Thorstein had to change contract.
        NpcRoleRegistry registry = Identities.EmptyRegistry();
        NpcIdentity identity = Identities.Thorstein;
        Assert.True(registry.Register(
            new FakeRole(identity, NpcBodyContract.ForWorker(Prefab, KeyPrefix))).IsRegistered);
        registry.BeginWorldLoad(Identities.AWorld());

        Assert.Equal(
            BodyClaimStatus.RefusedKindNotContracted,
            registry.TryClaimBody(identity, NpcBodyKind.Presentation, "presentation").Status);
    }
}
