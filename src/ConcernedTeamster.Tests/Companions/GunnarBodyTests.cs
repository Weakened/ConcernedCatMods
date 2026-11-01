using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarBodyTests
{
    private static GunnarBodyFacts Home(int saved = 0, QuestState? stage = QuestState.IntroPending,
        bool host = true, bool enabled = true, bool registered = true, bool loaded = true,
        bool resting = true, bool anchor = true, bool near = true) =>
        new(registered, loaded, host, enabled, stage, saved, resting, anchor, near);
    private sealed class Paths : INpcDataPaths
    {
        public string Root => Path.GetTempPath();
        public bool TryResolveFile(string purpose, out string path) { path=""; return false; }
    }
    private sealed class WorkerRole : INpcRole
    {
        public NpcIdentity Identity => new("teamster", "gunnar");
        public NpcBodyContract Body => NpcBodyContract.ForWorker("CT_TeamsterWorker", "tcc.worker.");
        public INpcDataPaths Paths { get; } = new Paths();
    }
    private static (NpcRoleRegistry, GunnarBodyLeases, NpcIdentity) Setup()
    {
        var registry = new NpcRoleRegistry(); var role = new GunnarPresentationRole(new WorkerRole());
        Assert.True(registry.Register(role).IsRegistered); registry.BeginWorldLoad(out _);
        return (registry, new GunnarBodyLeases(registry, role.Identity), role.Identity);
    }
    [Fact] public void RoleUpgradePreservesEveryDurableIdentity()
    {
        var old = new WorkerRole(); var role = new GunnarPresentationRole(old);
        Assert.Equal(old.Identity, role.Identity); Assert.Equal("CT_TeamsterWorker", role.Body.PrefabName);
        Assert.Equal("tcc.worker.", role.Body.ZdoKeyPrefix); Assert.Same(old.Paths, role.Paths);
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public void SavedWorkerAnywhereOutranksFigure(int bodies)
    { Assert.Equal(GunnarBodyKind.Worker, GunnarBodyPlan.Decide(Home(saved:bodies, enabled:false))); }
    [Theory] [InlineData((int)QuestState.IntroPending)] [InlineData((int)QuestState.Recruited)]
    public void VisitorAndRecruitedCanPresentAtRest(int stage)
    { Assert.Equal(GunnarBodyKind.Presentation, GunnarBodyPlan.Decide(Home(stage:(QuestState)stage))); }
    [Theory] [InlineData((int)QuestState.Unstarted)] [InlineData((int)QuestState.Discovered)]
    [InlineData((int)QuestState.Collected)] [InlineData((int)QuestState.Skipped)]
    public void OtherStagesDoNotPresent(int stage)
    { Assert.Equal(GunnarBodyKind.None, GunnarBodyPlan.Decide(Home(stage:(QuestState)stage))); }
    [Fact] public void UnknownAndEveryMissingRequirementSuppressesFigure()
    {
        var refused = new[] { default(GunnarBodyFacts), Home(saved:-1), Home(stage:null),
            Home(host:false), Home(enabled:false), Home(registered:false), Home(loaded:false),
            Home(resting:false), Home(anchor:false), Home(near:false) };
        foreach (var f in refused) Assert.Equal(GunnarBodyKind.None, GunnarBodyPlan.Decide(f));
        Assert.False(GunnarBodyPlan.MaySurvey(true,true,true,false));
        Assert.False(GunnarBodyPlan.MaySurvey(true,true,false,true));
    }
    [Fact] public void HandOverRemovesThenReleasesThenExistingWorkerClaims()
    {
        var (registry, leases, id) = Setup(); Assert.True(leases.TakePresentation());
        var trace = new List<string>();
        Assert.True(leases.HandOverToExistingWorker(() =>
        { Assert.True(leases.HoldsPresentation); trace.Add("figure down"); }, () =>
        {
            Assert.False(leases.HoldsPresentation); trace.Add("worker claim");
            return registry.TryClaimBody(id, NpcBodyKind.Worker, "existing-worker").IsGranted;
        }));
        Assert.Equal(new[] { "figure down", "worker claim" }, trace);
        Assert.False(leases.TakePresentation());
    }
    [Fact] public void RemovalFailureNeverClaimsWorkerOrReleasesLease()
    {
        var (_, leases, _) = Setup(); Assert.True(leases.TakePresentation()); bool claimed=false;
        Assert.Throws<InvalidOperationException>(() => leases.HandOverToExistingWorker(
            () => throw new InvalidOperationException(), () => { claimed=true; return true; }));
        Assert.False(claimed); Assert.True(leases.HoldsPresentation);
    }
    [Fact] public void WorldUnloadInvalidatesLeaseAndRejectsNewClaim()
    {
        var (registry, leases, _) = Setup(); Assert.True(leases.TakePresentation());
        registry.EndWorldLoad(); Assert.False(leases.HoldsPresentation); Assert.False(leases.TakePresentation());
    }
    [Fact] public void AppearanceLabelsPaletteAndExplicitOverridesArePreserved()
    {
        var spec=GunnarLook.Spec("observed-hair", "observed-beard", "custom-hair", "custom-beard");
        Assert.Equal("Short Curls", spec.Hair!.DisplayName); Assert.Equal("Stonedweller", spec.Beard!.DisplayName);
        Assert.Equal(0.52f, spec.SkinHue); Assert.Equal(0.05f, spec.HairTone); Assert.Equal(0.49f, spec.HairLevel);
        Assert.Equal("custom-hair", spec.HairOverride); Assert.Equal("custom-beard", spec.BeardOverride);
        Assert.Null(spec.Chest); Assert.Null(spec.Legs); // no unapproved costume
        Assert.Empty(GunnarLook.Spec().Hair!.PrefabCandidates);
        var options = new[] { new NpcLookOption("actual-preset", "Short Curls") };
        Assert.Equal(NpcLookMatch.DisplayName, NpcLookPlan.Choose(options,spec.Hair).Match);
        Assert.Equal(NpcLookMatch.None, NpcLookPlan.Choose(new[] { new NpcLookOption("HairOther","Other") },
            GunnarLook.Spec().Hair!).Match);
    }
}
