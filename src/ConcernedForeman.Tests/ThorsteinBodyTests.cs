using System;
using System.IO;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedNPC.Roles;
using Lib = TheConcernedCat.ConcernedNPC.Bodies;
using Shared = TheConcernedCat.Workers;

namespace ConcernedForeman.Tests;

/// <summary>#295 step 1: Thorstein's two bodies, and the rule that he never
/// has both.
///
/// Every test is an attempt to end with a figure at home AND a worker body, or
/// with a figure while his saved body stands somewhere in the world, or with a
/// hand-over that claims before it releases. The decision table is tested on
/// its own, and the leases against the real Concerned NPC arbiter, never a
/// stand-in for it.</summary>
public sealed class ThorsteinBodyTests
{
    private static readonly string DataRoot = Path.Combine(Path.GetTempPath(),
        "ConcernedCatMods", "ConcernedForeman", "settlements");

    private static ThorsteinBodyFacts AtHome(
        bool registered = true,
        bool worldLoaded = true,
        bool isHost = true,
        bool settlementEnabled = true,
        bool presentationEnabled = true,
        bool recruited = true,
        int savedWorkerBodies = 0,
        bool resting = true,
        bool homeKnown = true,
        bool playerNearHome = true) =>
        new ThorsteinBodyFacts(
            registered, worldLoaded, isHost, settlementEnabled, presentationEnabled, recruited,
            savedWorkerBodies, resting, homeKnown, playerNearHome);

    private static ForemanNpcAdoption Adopted(out NpcRoleRegistry registry)
    {
        registry = new NpcRoleRegistry();
        var adoption = new ForemanNpcAdoption(registry, new ForemanNpcRole(DataRoot));
        Assert.True(adoption.Register().IsRegistered);
        adoption.NoteWorldLoaded();
        return adoption;
    }

    // --- the decision ---------------------------------------------------------------------------------

    [Fact]
    public void RecruitedRestingAndHomeWithNoSavedBodyIsTheFigure()
    {
        Assert.Equal(ThorsteinBodyKind.Presentation, ThorsteinBodyPlan.Decide(AtHome()).Kind);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ASavedWorkerBodyAnywhereInTheWorldOutranksEverything(int bodies)
    {
        // His tools are inside it. Wherever it stands, and whatever else is
        // true, it is his body and no figure is shown beside it.
        ThorsteinBodyDecision decision = ThorsteinBodyPlan.Decide(AtHome(savedWorkerBodies: bodies));
        Assert.Equal(ThorsteinBodyKind.Worker, decision.Kind);

        // Including with every presentation fact pointing the other way.
        Assert.Equal(
            ThorsteinBodyKind.Worker,
            ThorsteinBodyPlan.Decide(AtHome(savedWorkerBodies: bodies, resting: false, recruited: false)).Kind);
    }

    [Fact]
    public void EveryUnknownShowsNothing()
    {
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(registered: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(worldLoaded: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(isHost: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(savedWorkerBodies: -1)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(settlementEnabled: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(presentationEnabled: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(recruited: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(resting: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(homeKnown: false)).Kind);
        Assert.Equal(ThorsteinBodyKind.None, ThorsteinBodyPlan.Decide(AtHome(playerNearHome: false)).Kind);
    }

    [Fact]
    public void EveryRefusalSaysWhy()
    {
        foreach (ThorsteinBodyFacts facts in new[]
                 {
                     AtHome(registered: false), AtHome(worldLoaded: false), AtHome(isHost: false),
                     AtHome(savedWorkerBodies: -1), AtHome(settlementEnabled: false),
                     AtHome(presentationEnabled: false), AtHome(recruited: false), AtHome(resting: false),
                     AtHome(homeKnown: false), AtHome(playerNearHome: false),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(ThorsteinBodyPlan.Decide(facts).Reason));
        }
    }

    [Fact]
    public void AClientNeverShowsAFigureBecauseItCannotSeeEverySavedBody()
    {
        // Even with a census that happens to read zero: off the host it is not
        // the whole world.
        Assert.Equal(
            ThorsteinBodyKind.None,
            ThorsteinBodyPlan.Decide(AtHome(isHost: false, savedWorkerBodies: 0)).Kind);
    }

    // --- the contract ---------------------------------------------------------------------------------

    [Fact]
    public void HisContractAdmitsAFigureWithTheShippedPrefabAndKeysUnchanged()
    {
        var role = new ForemanNpcRole(DataRoot);

        Assert.Equal("CF_SettlementWorker", role.Body.PrefabName);
        Assert.Equal("tcc.worker.", role.Body.ZdoKeyPrefix);
        Assert.Equal(NpcBodyKind.Worker, role.Body.Kind);
        Assert.True(role.Body.Admits(NpcBodyKind.Worker));
        Assert.True(role.Body.Admits(NpcBodyKind.Presentation));
    }

    // --- the leases, against the real arbiter --------------------------------------------------------

    [Fact]
    public void TheFigureIsGrantedWhileHeRestsWithNoBody()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);

        Assert.Equal(ThorsteinBodyKind.Presentation, bodies.Reconcile(ThorsteinBodyKind.Presentation));
        Assert.True(bodies.HoldsPresentation);
        Assert.False(bodies.HoldsWorker);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(adoption.Identity));
    }

    [Fact]
    public void TheHandOverReleasesTheFigureBeforeClaimingTheWorker()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        bodies.Reconcile(ThorsteinBodyKind.Presentation);

        Assert.True(bodies.TryTakeWorker(out string refusal), refusal);

        Assert.False(bodies.HoldsPresentation);
        Assert.True(bodies.HoldsWorker);
        Assert.Equal(ThorsteinBodyKind.Worker, bodies.Held);
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(adoption.Identity));
    }

    [Fact]
    public void NoPassEverEndsHoldingBoth()
    {
        // Walk every transition between the three states, twice, and check the
        // invariant after each one.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        ThorsteinBodyKind[] walk =
        {
            ThorsteinBodyKind.Presentation, ThorsteinBodyKind.Worker, ThorsteinBodyKind.Presentation,
            ThorsteinBodyKind.None, ThorsteinBodyKind.Worker, ThorsteinBodyKind.None,
            ThorsteinBodyKind.Presentation, ThorsteinBodyKind.Presentation, ThorsteinBodyKind.Worker,
            ThorsteinBodyKind.Worker,
        };

        foreach (ThorsteinBodyKind step in walk)
        {
            ThorsteinBodyKind held = bodies.Reconcile(step);
            Assert.Equal(step, held);
            Assert.False(bodies.HoldsPresentation && bodies.HoldsWorker);
        }
    }

    [Fact]
    public void WhileTheWorkerLeaseIsHeldNobodyInTheProcessMayShowAFigure()
    {
        // The arbiter is the backstop: even a second runtime asking directly is
        // refused, with the never-coexist status.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        bodies.Reconcile(ThorsteinBodyKind.Worker);

        Lib.BodyClaim figure = registry.TryClaimBody(adoption.Identity, NpcBodyKind.Presentation, "someone-else");

        Assert.Equal(Lib.BodyClaimStatus.RefusedOtherKindExists, figure.Status);
    }

    [Fact]
    public void AnOrderInFlightRefusesTheFigureEvenWithNoBodyClaimed()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        Assert.Equal(
            Shared.ActorModeOutcome.Entered,
            adoption.Modes.Enter(Shared.ActorMode.Working, "collect-1"));

        Assert.Equal(ThorsteinBodyKind.None, bodies.Reconcile(ThorsteinBodyKind.Presentation));
        Assert.False(bodies.HoldsPresentation);
        Assert.Contains("RefusedHolderConflict", bodies.LastRefusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasingTheWorkerLeaseLeavesAJobsModeAlone()
    {
        // The settlement runtime's lease and a job's mode are different holders.
        // Giving up the lease must not end somebody's order.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        bodies.Reconcile(ThorsteinBodyKind.Worker);
        Assert.Equal(
            Shared.ActorModeOutcome.Entered,
            adoption.Modes.Enter(Shared.ActorMode.Working, "collect-1"));

        bodies.ReleaseWorker();

        Assert.Equal("collect-1", adoption.Modes.JobId);
        Assert.Equal(Shared.ActorMode.Working, adoption.Modes.Mode);
    }

    [Fact]
    public void AWorldUnloadDropsBothAndTheNextWorldStartsClean()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);
        bodies.Reconcile(ThorsteinBodyKind.Presentation);

        adoption.NoteWorldUnloaded();
        Assert.False(bodies.HoldsPresentation);
        bodies.Forget();

        adoption.NoteWorldLoaded();
        Assert.Equal(ThorsteinBodyKind.Worker, bodies.Reconcile(ThorsteinBodyKind.Worker));
    }

    [Fact]
    public void AnUnregisteredThorsteinHoldsNothing()
    {
        // Fails closed: without the arbiter's record there is nothing to hold.
        var registry = new NpcRoleRegistry();
        registry.BeginWorldLoad(out _);
        var bodies = new ThorsteinBodies(registry, ForemanNpcRole.Id);

        Assert.Equal(ThorsteinBodyKind.None, bodies.Reconcile(ThorsteinBodyKind.Presentation));
        Assert.Equal(ThorsteinBodyKind.None, bodies.Reconcile(ThorsteinBodyKind.Worker));
        Assert.False(bodies.TryTakeWorker(out string refusal));
        Assert.False(string.IsNullOrEmpty(refusal));
    }

    [Fact]
    public void ReconcilingTheSameStateTwiceKeepsTheSameLease()
    {
        // A pass runs every few frames. Asking again must be satisfied, not
        // re-claimed into a different lease that a figure was not built under.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        var bodies = new ThorsteinBodies(registry, adoption.Identity);

        bodies.Reconcile(ThorsteinBodyKind.Presentation);
        bodies.Reconcile(ThorsteinBodyKind.Presentation);

        Assert.True(bodies.HoldsPresentation);
        Assert.Equal(
            Lib.BodyClaimStatus.AlreadyHeld,
            registry.TryClaimBody(adoption.Identity, NpcBodyKind.Presentation, ThorsteinBodies.PresentationHolder).Status);
    }
}
