using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#458, modelled on Foreman's ThorsteinBodyTests: which of Gunnar's
/// two bodies he should have, the contract he registers, and the hand-over,
/// against the real Concerned NPC arbiter rather than a description of it.</summary>
public sealed class GunnarBodyTests
{
    private static GunnarBodyFacts Home(int saved = 0, QuestState? stage = QuestState.IntroPending,
        bool host = true, bool enabled = true, bool registered = true, bool loaded = true,
        bool resting = true, bool anchor = true, bool near = true) =>
        new(registered, loaded, host, enabled, stage, saved, resting, anchor, near);

    private sealed class Paths : INpcDataPaths
    {
        public string Root => Path.GetTempPath();
        public bool TryResolveFile(string purpose, out string path) { path = ""; return false; }
    }

    private sealed class Role : INpcRole
    {
        public Role(NpcBodyContract body) { Body = body; }
        public NpcIdentity Identity => new("teamster", "gunnar");
        public NpcBodyContract Body { get; }
        public INpcDataPaths Paths { get; } = new Paths();
    }

    private static (NpcRoleRegistry, GunnarBodyLeases, NpcIdentity) Setup(NpcBodyContract? contract = null)
    {
        var registry = new NpcRoleRegistry();
        var role = new Role(contract ?? GunnarBodyContract.Create());
        Assert.True(registry.Register(role).IsRegistered);
        registry.BeginWorldLoad(out _);
        return (registry, new GunnarBodyLeases(registry, role.Identity), role.Identity);
    }

    // --- the contract ---------------------------------------------------------------------------

    [Fact]
    public void TheContractKeepsEveryDurableFactAndAdmitsTheFigure()
    {
        // Zero migrations: the prefab every saved Gunnar is found under and the
        // key prefix his body stores itself beneath are the shipped literals.
        NpcBodyContract contract = GunnarBodyContract.Create();
        Assert.Equal("CT_TeamsterWorker", contract.PrefabName);
        Assert.Equal("tcc.worker.", contract.ZdoKeyPrefix);
        Assert.Equal(NpcBodyKind.Worker, contract.Kind);
        Assert.True(contract.Admits(NpcBodyKind.Worker));
        Assert.True(contract.Admits(NpcBodyKind.Presentation));
        Assert.True(contract.TryValidate(out string why), why);
    }

    [Fact]
    public void AWorkerOnlyRoleIsNotRegisteredForAFigure()
    {
        var (_, leases, _) = Setup(NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker."));
        Assert.False(leases.Registered);
        Assert.False(leases.TakePresentation());
        var (_, upgraded, _) = Setup();
        Assert.True(upgraded.Registered);
    }

    [Fact]
    public void AnUnregisteredIdentityHoldsNothing()
    {
        var registry = new NpcRoleRegistry();
        registry.BeginWorldLoad(out _);
        var leases = new GunnarBodyLeases(registry, new NpcIdentity("teamster", "gunnar"));
        Assert.False(leases.Registered);
        Assert.False(leases.TakePresentation());
        Assert.False(leases.TryTakeWorker(() => { }, out string refusal));
        Assert.NotEqual(string.Empty, refusal);
    }

    // --- the plan -------------------------------------------------------------------------------

    [Theory] [InlineData(1)] [InlineData(2)]
    public void SavedWorkerAnywhereOutranksFigure(int bodies)
    { Assert.Equal(GunnarBodyKind.Worker, GunnarBodyPlan.Decide(Home(saved: bodies, enabled: false, host: false))); }

    [Theory] [InlineData((int)QuestState.IntroPending)] [InlineData((int)QuestState.Recruited)]
    public void VisitorAndRecruitedCanPresentAtRest(int stage)
    { Assert.Equal(GunnarBodyKind.Presentation, GunnarBodyPlan.Decide(Home(stage: (QuestState)stage))); }

    [Theory] [InlineData((int)QuestState.Unstarted)] [InlineData((int)QuestState.Discovered)]
    [InlineData((int)QuestState.Collected)] [InlineData((int)QuestState.Skipped)]
    public void OtherStagesDoNotPresent(int stage)
    { Assert.Equal(GunnarBodyKind.None, GunnarBodyPlan.Decide(Home(stage: (QuestState)stage))); }

    [Fact]
    public void UnknownAndEveryMissingRequirementSuppressesFigure()
    {
        var refused = new[] { default(GunnarBodyFacts), Home(saved: -1), Home(stage: null),
            Home(host: false), Home(enabled: false), Home(registered: false), Home(loaded: false),
            Home(resting: false), Home(anchor: false), Home(near: false) };
        foreach (var f in refused) Assert.Equal(GunnarBodyKind.None, GunnarBodyPlan.Decide(f));
        Assert.False(GunnarBodyPlan.MaySurvey(true, true, true, false));
        Assert.False(GunnarBodyPlan.MaySurvey(true, true, false, true));
        Assert.False(GunnarBodyPlan.MaySurvey(false, true, true, true));
        Assert.False(GunnarBodyPlan.MaySurvey(true, false, true, true));
        Assert.True(GunnarBodyPlan.MaySurvey(true, true, true, true));
    }

    [Fact]
    public void PendingVisitorPresentationDoesNotGrantRecruitment()
    {
        string root = Path.Combine(Path.GetTempPath(), "ct-458-visitor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var record = GunnarCompanionRecord.Open(root, 11, 22);
            Assert.True(record.BeginIntroduction()); Assert.True(record.ShouldPresentFigure(true));
            Assert.False(record.IsRecruited); Assert.False(record.ShouldPresentFigure(false));
            Assert.True(record.Skip()); Assert.False(record.ShouldPresentFigure(true)); Assert.False(record.IsRecruited);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // --- the leases: never both, release before claim -------------------------------------------

    [Fact]
    public void HandOverTakesTheFigureDownThenReleasesThenClaimsTheWorker()
    {
        var (registry, leases, id) = Setup();
        Assert.True(leases.TakePresentation());
        var trace = new List<string>();
        Assert.True(leases.TryTakeWorker(() =>
        {
            // The figure comes down while its lease is still held...
            Assert.True(leases.HoldsPresentation);
            Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(id));
            trace.Add("figure down");
        }, out string refusal), refusal);
        // ...and the worker lease is held only after it is gone.
        trace.Add(registry.CurrentBodyKind(id).ToString());
        Assert.Equal(new[] { "figure down", "Worker" }, trace);
        Assert.False(leases.HoldsPresentation);
        Assert.True(leases.HoldsWorker);
    }

    [Fact]
    public void WhileTheArrivalGuardIsHeldNoFigureMayBeClaimed()
    {
        var (registry, leases, id) = Setup();
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        Assert.False(leases.TakePresentation());
        Assert.Contains(BodyClaimStatus.RefusedOtherKindExists.ToString(), leases.LastRefusal);
        // Nor by any other runtime in the process.
        Assert.False(registry.TryClaimBody(id, NpcBodyKind.Presentation, "someone-else").IsGranted);
    }

    [Fact]
    public void ReleasingTheGuardLetsTheFigureAndAJobBackIn()
    {
        var (registry, leases, id) = Setup();
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        leases.ReleaseWorker();
        Assert.False(leases.HoldsWorker);
        // A haul claims the worker kind under its own id once the guard is gone.
        BodyClaim haul = registry.TryClaimBody(id, NpcBodyKind.Worker, "haul-1");
        Assert.True(haul.IsGranted);
        Assert.False(leases.TakePresentation());
        haul.Lease!.Dispose();
        Assert.True(leases.TakePresentation());
        leases.ReleaseWorker(); // idempotent, and never touches somebody else's lease
        Assert.True(leases.HoldsPresentation);
    }

    [Fact]
    public void ASecondHandOverBeforeTheBodyIsCountedIsRefused()
    {
        // Two spawns in one frame would otherwise both pass a stale census.
        var (_, leases, _) = Setup();
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        Assert.False(leases.TryTakeWorker(() => { }, out string refusal));
        Assert.Contains("not been counted", refusal);
        leases.ReleaseWorker();
        Assert.True(leases.TryTakeWorker(() => { }, out _));
    }

    // --- the arrival guard's two decisions ------------------------------------------------------

    [Theory]
    [InlineData(true, true, -1)] [InlineData(true, false, -1)] [InlineData(true, null, -1)]
    [InlineData(false, true, 1)] [InlineData(false, false, 0)] [InlineData(false, null, -1)]
    public void WhileHandingOverHisBodiesAreUnknownNeverZero(bool handingOver, bool? present, int expected)
    { Assert.Equal(expected, GunnarArrival.SavedWorkers(handingOver, present)); }

    [Fact]
    public void TheGuardGoesOnlyOnceTheCensusHasTheBodyOrHasLongSaidNone()
    {
        Assert.True(GunnarArrival.MayRelease(true, 0f));
        // A "no body" that may predate the spawn is not believed at once...
        Assert.False(GunnarArrival.MayRelease(false, 0f));
        Assert.False(GunnarArrival.MayRelease(false, GunnarArrival.PatienceSeconds - 0.01f));
        Assert.True(GunnarArrival.MayRelease(false, GunnarArrival.PatienceSeconds));
        // ...and not knowing never releases it, however long it lasts.
        Assert.False(GunnarArrival.MayRelease(null, 1e6f));
    }

    [Fact]
    public void AJobAlreadyHoldingHimRefusesTheHandOverAndKeepsItsLease()
    {
        // #372: before, this went ahead with no lease at all. A body is now
        // built only under a lease the arbiter granted, and while a job holds
        // him the arbiter grants one only to that job, so the hand-over refuses.
        var (registry, leases, id) = Setup();
        BodyClaim haul = registry.TryClaimBody(id, NpcBodyKind.Worker, "haul-1");
        Assert.True(haul.IsGranted);
        Assert.False(leases.TryTakeWorker(() => { }, out string refusal));
        Assert.Contains("a job holds him", refusal);
        Assert.Contains("'ct_haul detach'", refusal);
        Assert.Equal(refusal, leases.LastRefusal);
        Assert.False(leases.HoldsWorker);
        Assert.Null(leases.WorkerLease);
        // The job's lease is untouched...
        Assert.Equal(NpcBodyKind.Worker, registry.CurrentBodyKind(id));
        Assert.True(haul.Lease!.IsActive);
        // ...and once the job ends, the next hand-over goes through.
        haul.Lease.Dispose();
        Assert.True(leases.TryTakeWorker(() => { }, out string after), after);
        Assert.NotNull(leases.WorkerLease);
    }

    [Fact]
    public void TheWorkerLeaseIsTheArrivalGuardAndOnlyWhileItIsHeld()
    {
        // #372: the lease handed to the gated build is the guard itself, so a
        // release or a world unload between hand-over and build refuses it.
        var (registry, leases, id) = Setup();
        Assert.Null(leases.WorkerLease);
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        BodyLease? lease = leases.WorkerLease;
        Assert.NotNull(lease);
        Assert.True(lease!.IsActive);
        Assert.Equal(id, lease.Identity);
        Assert.Equal(NpcBodyKind.Worker, lease.Kind);
        Assert.Equal(GunnarBodyLeases.ArrivalHolder, lease.Holder);
        leases.ReleaseWorker();
        Assert.Null(leases.WorkerLease);
        Assert.False(lease.IsActive);
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        BodyLease? second = leases.WorkerLease;
        registry.EndWorldLoad();
        Assert.Null(leases.WorkerLease);
        Assert.False(second!.IsActive);
    }

    [Fact]
    public void TheArrivalLeaseIsContractedForExactlyTheContractHisBodyIsBuiltUnder()
    {
        // #372 F2c leftover: the gated build refuses, before it counts anything,
        // a contract that is not the one the lease's identity registered with
        // the arbiter that granted it (BodyLease.IsContractedFor). The library's
        // own test of that rule uses a plain worker contract; Gunnar's is a
        // worker contract that also admits a figure, and his body is built under
        // GunnarNpcRole.Contract (= GunnarBodyContract.Create(), pinned by
        // GunnarHandOverOrderTests). So this is the real arbiter, the real
        // hand-over and the real contract: a drift between what the role
        // registers and what the spawn passes fails here, not in game.
        var registry = new NpcRoleRegistry();
        Assert.True(NpcIdentity.TryParse(TheConcernedCat.Workers.WorkerKey.Gunnar.Value, out NpcIdentity gunnar));
        var role = new GunnarRole(gunnar, GunnarBodyContract.Create());
        Assert.True(registry.Register(role).IsRegistered);
        registry.BeginWorldLoad(out _);
        var leases = new GunnarBodyLeases(registry, gunnar);

        Assert.True(leases.TryTakeWorker(() => { }, out string refusal), refusal);
        BodyLease lease = leases.WorkerLease!;

        Assert.True(lease.IsContractedFor(GunnarBodyContract.Create()));
        // The identity the build checks the stamp against is the one
        // TeamsterWorkerPrefab.Stamp writes (WorkerKey.Gunnar.Value).
        Assert.Equal(TheConcernedCat.Workers.WorkerKey.Gunnar.Value, lease.Identity.Value);
        Assert.Equal("tcc.worker.", GunnarBodyContract.Create().ZdoKeyPrefix);

        // Every near miss is refused: no figure admission, another prefab,
        // another key prefix, another role's real contract, no contract.
        Assert.False(lease.IsContractedFor(NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.")));
        Assert.False(lease.IsContractedFor(NpcBodyContract.ForWorkerWithPresentation("CF_SettlementWorker", "tcc.worker.")));
        Assert.False(lease.IsContractedFor(NpcBodyContract.ForWorkerWithPresentation("CT_TeamsterWorker", "tcc.steward.")));
        Assert.False(lease.IsContractedFor(NpcBodyContract.ForWorker("CS_Steward", "tcc.steward.")));
        Assert.False(lease.IsContractedFor(NpcBodyContract.ForPresentation()));
        Assert.False(lease.IsContractedFor(default));

        // A world unload ends the lease; the contract it names does not change,
        // and the build's last check (the lease is live) is what refuses then.
        registry.EndWorldLoad();
        Assert.False(lease.IsActive);
    }

    private sealed class GunnarRole : INpcRole
    {
        public GunnarRole(NpcIdentity identity, NpcBodyContract body) { Identity = identity; Body = body; }
        public NpcIdentity Identity { get; }
        public NpcBodyContract Body { get; }
        public INpcDataPaths Paths { get; } = new Paths();
    }

    [Fact]
    public void RemovalFailureNeverClaimsWorkerOrReleasesLease()
    {
        var (registry, leases, id) = Setup();
        Assert.True(leases.TakePresentation());
        Assert.Throws<InvalidOperationException>(() =>
            leases.TryTakeWorker(() => throw new InvalidOperationException(), out _));
        Assert.True(leases.HoldsPresentation);
        Assert.False(leases.HoldsWorker);
        Assert.Equal(NpcBodyKind.Presentation, registry.CurrentBodyKind(id));
        Assert.Throws<InvalidOperationException>(() => leases.ReleasePresentation(() => throw new InvalidOperationException()));
        Assert.True(leases.HoldsPresentation);
    }

    [Fact]
    public void WorldUnloadInvalidatesBothLeasesAndForgetDropsThem()
    {
        var (registry, leases, _) = Setup();
        Assert.True(leases.TakePresentation());
        registry.EndWorldLoad();
        Assert.False(leases.HoldsPresentation);
        Assert.False(leases.TakePresentation());
        registry.BeginWorldLoad(out _);
        Assert.True(leases.TryTakeWorker(() => { }, out _));
        registry.EndWorldLoad();
        Assert.False(leases.HoldsWorker);
        leases.Forget();
        registry.BeginWorldLoad(out _);
        Assert.True(leases.TakePresentation());
    }

    // --- where he stands ------------------------------------------------------------------------

    private sealed class Flat : IPlacementProbe
    {
        public int Probes;
        public PlacementProbeSample Probe(WorldPoint point) { Probes++; return new(point, PlacementRejection.None, -1f, SeatAvailability.None); }
    }

    private static readonly CompanionAnchor Bed = new(AnchorKind.ClaimedBed, new WorldPoint(10f, 30f, -4f));

    [Fact]
    public void HeStandsBesideHomeWithinTheBandAndTheProbeIsBounded()
    {
        var probe = new Flat();
        PlacementResult spot = GunnarFigurePlacement.Plan(Bed, probe, Array.Empty<WorldPoint>());
        Assert.True(spot.Found);
        float distance = spot.Position.HorizontalDistanceTo(Bed.Position);
        Assert.InRange(distance, GunnarFigurePlacement.Rules.MinimumRadius - 0.01f, GunnarFigurePlacement.Rules.MaximumRadius + 0.01f);
        Assert.InRange(probe.Probes, 1, PlacementPlanner.MaximumCandidates);
        Assert.Equal(CompanionPose.SitOnGround, spot.Pose);
    }

    [Fact]
    public void HeKeepsClearOfAnotherProductsKeepsake()
    {
        PlacementResult first = GunnarFigurePlacement.Plan(Bed, new Flat(), Array.Empty<WorldPoint>());
        PlacementResult moved = GunnarFigurePlacement.Plan(Bed, new Flat(), new[] { first.Position });
        Assert.True(moved.Found);
        Assert.True(moved.Position.HorizontalDistanceTo(first.Position) >= GunnarKeepsake.Clearance);
    }

    [Fact]
    public void EveryUnknownDefersInsteadOfGuessingASpot()
    {
        Assert.False(GunnarFigurePlacement.Plan(CompanionAnchor.None, new Flat(), Array.Empty<WorldPoint>()).Found);
        Assert.False(GunnarFigurePlacement.Plan(Bed, new Flat(), null).Found);
        var nan = new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(float.NaN, 0f, 0f));
        Assert.False(GunnarFigurePlacement.Plan(nan, new Flat(), Array.Empty<WorldPoint>()).Found);
        Assert.False(GunnarFigurePlacement.Plan(Bed, new Flat(), new[] { new WorldPoint(float.NaN, 0f, 0f) }).Found);
    }
}
