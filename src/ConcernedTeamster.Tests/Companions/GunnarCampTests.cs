using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarCampTests
{
    private sealed class Flat : IPlacementProbe
    {
        public int Calls;
        public PlacementProbeSample Probe(WorldPoint p) { Calls++; return new(p,PlacementRejection.None,-1,SeatAvailability.None); }
    }
    private sealed class Seats : IPlacementProbe,ISeatFinder
    {
        public PlacementProbeSample Probe(WorldPoint p) => new(p,PlacementRejection.None,-1,SeatAvailability.None);
        public IReadOnlyList<PlacementProbeSample> FindSeats(WorldPoint centre,float radius) => new[] {
            new PlacementProbeSample(new WorldPoint(5,0,0),PlacementRejection.None,2,
                SeatOffer.Free(new WorldPoint(5,0,0),90,"observed_attach_pose")) };
    }
    private static GunnarBodyFacts Facts(bool near=true,int workers=0) =>
        new(true,true,true,true,QuestState.Recruited,workers,true,true,near);
    private static (NpcCamp,NpcWorldEpoch,CompanionAnchor) Camp()
    {
        var registry=new NpcRoleRegistry(); var epoch=registry.BeginWorldLoad(out _);
        var camp=new NpcCamp(); camp.BeginWorld(epoch);
        return (camp,epoch,new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(0,0,0)));
    }
    [Fact] public void CachedHotPathAllocatesNothingAndDoesNotProbeAgain()
    {
        var(camp,epoch,anchor)=Camp(); var view=camp.Survey(default,true,true); var probe=new Flat(); var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.Show,routine.Step(Facts(),anchor,view,epoch,probe).Action); routine.NoteFigureShown(true);
        for(int i=0;i<1000;i++) routine.Step(Facts(),anchor,view,epoch,probe);
        int calls=probe.Calls; long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++) routine.Step(Facts(),anchor,view,epoch,probe);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,bytes); Assert.Equal(calls,probe.Calls); Assert.True(calls<=PlacementPlanner.MaximumCandidates);
    }
    [Fact] public void LeavingAndReturningToRangeRetainsTheSameFigureAndPlan()
    {
        var(camp,epoch,anchor)=Camp(); var view=camp.Survey(default,true,true); var probe=new Flat(); var routine=new GunnarCampRoutine();
        var first=routine.Step(Facts(),anchor,view,epoch,probe); routine.NoteFigureShown(true);
        Assert.Equal(GunnarCampAction.Retain,routine.Step(Facts(near:false),anchor,view,epoch,probe).Action);
        var back=routine.Step(Facts(),anchor,view,epoch,probe);
        Assert.Equal(GunnarCampAction.Retain,back.Action); Assert.Equal(first.Placement.Position,back.Placement.Position);
    }
    [Fact] public void ChangedBedRepositionsOnlyThePresentationAndWorkerSuppressesIt()
    {
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine(); var probe=new Flat();
        routine.Step(Facts(),anchor,camp.Survey(default,true,true),epoch,probe); routine.NoteFigureShown(true);
        var moved=new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(50,0,50));
        Assert.Equal(GunnarCampAction.Reposition,routine.Step(Facts(),moved,
            camp.Survey(new NpcPoint(50,0,50),true,true),epoch,probe).Action);
        int calls=probe.Calls;
        Assert.Equal(GunnarCampAction.Hide,routine.Step(Facts(workers:1),moved,
            camp.Survey(new NpcPoint(50,0,50),true,true),epoch,probe).Action);
        Assert.Equal(calls,probe.Calls);
    }
    [Fact] public void LibraryCampRejectsForeignEpochAndNonStructuresAndCachesSurvey()
    {
        var(camp,epoch,_)=Camp();
        Assert.False(camp.ObservePiece("terrain",default,false,epoch));
        Assert.False(camp.ObservePiece("stale",default,true,new NpcRoleRegistry().BeginWorldLoad(out _)));
        Assert.True(camp.ObservePiece("built",default,true,epoch));
        var first=camp.Survey(default,true,true); Assert.Same(first,camp.Survey(default,true,true));
        Assert.Equal(1,first.Members);
    }
    [Fact] public void SharedSeatPoseIsCarriedWithoutClaimingASeat()
    {
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine();
        var step=routine.Step(Facts(),anchor,camp.Survey(default,true,true),epoch,new Seats());
        Assert.Equal(CompanionPose.SitOnSeat,step.Placement.Pose);
        Assert.Equal("observed_attach_pose",step.Placement.Seat.AttachAnimation);
    }
    [Fact] public void UnknownCampOrOldEpochHidesAndFacingIsInteractionOnly()
    {
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,null,epoch,new Flat()).Action);
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,camp.Survey(default,true,true),default,new Flat()).Action);
        Assert.Equal(90f,GunnarCampRoutine.FacingYaw(default,new WorldPoint(1,0,0)));
        Assert.Null(GunnarCampRoutine.FacingYaw(default,default));
    }
}
