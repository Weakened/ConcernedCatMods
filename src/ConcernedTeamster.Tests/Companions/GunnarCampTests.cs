using ConcernedTeamster.Tests;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Surroundings;
using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
[Collection(ExactAllocationCollection.Name)]
public sealed class GunnarCampTests
{
    // Concerned NPC's public camp facade (#461, PR #515), the one the product
    // consumes. The product build proves it compiles as an assembly reference.
    private sealed class Flat : IPlacementProbe
    {
        public int Calls;
        public bool Unloaded;
        public PlacementProbeSample Probe(WorldPoint p)
        {
            Calls++;
            return Unloaded ? PlacementProbeSample.NotLoaded(p) : new(p,PlacementRejection.None,-1,SeatAvailability.None);
        }
    }
    private sealed class Seats : IPlacementProbe,ISeatFinder
    {
        public PlacementProbeSample Probe(WorldPoint p) => new(p,PlacementRejection.None,-1,SeatAvailability.None);
        public IReadOnlyList<PlacementProbeSample> FindSeats(WorldPoint centre,float radius) => new[] {
            new PlacementProbeSample(new WorldPoint(3,0,0),PlacementRejection.None,2,
                SeatOffer.Free(new WorldPoint(3,0,0),90,"observed_attach_pose")) };
    }
    private sealed class Around : IGunnarRestSurroundings
    {
        public int Revision { get; set; }
        public List<DoorPortal> DoorList { get; } = new();
        public IReadOnlyList<DoorPortal> Doors => DoorList;
        public Func<WorldPoint,bool> Wall { get; set; } = _ => false;
        public Func<WorldPoint,bool> Room { get; set; } = _ => true;
        public IReadOnlyList<WorldPoint>? Keepsakes { get; set; } = Array.Empty<WorldPoint>();
        public int WallCalls;
        public IReadOnlyList<WorldPoint>? OtherKeepsakes(CompanionAnchor anchor) => Keepsakes;
        public bool IsWallBetween(WorldPoint home,WorldPoint spot) { WallCalls++; return Wall(spot); }
        public bool HasRoomToStand(WorldPoint spot) => Room(spot);
    }
    private static GunnarBodyFacts Facts(bool near=true,int workers=0) =>
        new(true,true,true,true,QuestState.Recruited,workers,true,true,near);
    private static (NpcCamp,NpcWorldEpoch,CompanionAnchor) Camp()
    {
        var registry=new NpcRoleRegistry(); var epoch=registry.BeginWorldLoad(out _);
        var camp=new NpcCamp(); camp.BeginWorld(epoch);
        return (camp,epoch,new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(0,0,0)));
    }
    private static NpcCampView Survey(NpcCamp camp,float x=0,float z=0) => camp.Survey(NpcCampHome.PlayerBed,new NpcPoint(x,0,z));

    /// <summary>A door whose opening is the plane at <paramref name="x"/>, wide
    /// enough that every spot from home out past it crosses it.</summary>
    private static DoorPortal DoorAtX(float x,bool open=false,bool allowed=false) =>
        new(new DoorPlace(new WorldPoint(x,0,0),0),new WorldPoint(x,0,0),1f,0f,50f,open,canOpen:true,allowed:allowed);

    [Fact] public void CachedHotPathAllocatesNothingAndDoesNotProbeAgain()
    {
        var(camp,epoch,anchor)=Camp(); var view=Survey(camp); var probe=new Flat(); var around=new Around(); var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.Show,routine.Step(Facts(),anchor,view,true,epoch,probe,around).Action); routine.NoteFigureShown(true);
        for(int i=0;i<1000;i++) routine.Step(Facts(),anchor,view,true,epoch,probe,around);
        int calls=probe.Calls; int walls=around.WallCalls;
        long bytes=ExactAllocations.Measure(() =>
        { for(int i=0;i<100000;i++) routine.Step(Facts(),anchor,view,true,epoch,probe,around); });
        Assert.Equal(0,bytes); Assert.Equal(calls,probe.Calls); Assert.Equal(walls,around.WallCalls);
        Assert.True(calls<=PlacementPlanner.MaximumCandidates);
    }
    [Fact] public void LeavingAndReturningToRangeRetainsTheSameFigureAndPlan()
    {
        var(camp,epoch,anchor)=Camp(); var view=Survey(camp); var probe=new Flat(); var around=new Around(); var routine=new GunnarCampRoutine();
        var first=routine.Step(Facts(),anchor,view,true,epoch,probe,around); routine.NoteFigureShown(true);
        int calls=probe.Calls;
        Assert.Equal(GunnarCampAction.Retain,routine.Step(Facts(near:false),anchor,view,true,epoch,probe,around).Action);
        var back=routine.Step(Facts(),anchor,view,true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Retain,back.Action); Assert.Equal(first.Placement.Position,back.Placement.Position);
        Assert.Equal(calls,probe.Calls);
    }
    [Fact] public void ChangedBedRepositionsOnlyThePresentationAndWorkerSuppressesIt()
    {
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine(); var probe=new Flat(); var around=new Around();
        routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around); routine.NoteFigureShown(true);
        var moved=new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(50,0,50));
        var step=routine.Step(Facts(),moved,Survey(camp,50,50),true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Reposition,step.Action);
        Assert.InRange(step.Placement.Position.HorizontalDistanceTo(moved.Position),1.49f,4.01f);
        int calls=probe.Calls;
        Assert.Equal(GunnarCampAction.Hide,routine.Step(Facts(workers:1),moved,Survey(camp,50,50),true,epoch,probe,around).Action);
        Assert.Equal(calls,probe.Calls);
    }
    [Fact] public void LibraryCampRejectsForeignEpochAndNonStructuresAndCachesSurvey()
    {
        var(camp,epoch,_)=Camp();
        Assert.False(camp.NotePiece("terrain",default,false,epoch));
        Assert.False(camp.NotePiece("stale",default,true,new NpcRoleRegistry().BeginWorldLoad(out _)));
        Assert.True(camp.NotePiece("built",default,true,epoch));
        var first=Survey(camp); Assert.Same(first,Survey(camp));
        Assert.True(first.IsEstablished);
    }
    [Fact] public void OnlyAPlayerPlacedPieceThatIsNeitherATerrainEditNorACropIsCamp()
    {
        Assert.True(GunnarRestBounds.IsCampPiece(placedByPlayer:true,terrainOp:false,crop:false));
        Assert.False(GunnarRestBounds.IsCampPiece(placedByPlayer:false,terrainOp:false,crop:false));
        Assert.False(GunnarRestBounds.IsCampPiece(placedByPlayer:true,terrainOp:true,crop:false));
        Assert.False(GunnarRestBounds.IsCampPiece(placedByPlayer:true,terrainOp:false,crop:true));
    }
    [Fact] public void AStandingFigureIsNeverOfferedASeat()
    {
        // Chairs need a slice that watches for a player sitting down; until
        // then the figure stands on bare ground and no seat is so much as named.
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine();
        var step=routine.Step(Facts(),anchor,Survey(camp),true,epoch,new Seats(),new Around());
        Assert.True(step.Placement.Found);
        Assert.NotEqual(CompanionPose.SitOnSeat,step.Placement.Pose);
        Assert.False(step.Placement.Seat.IsUsable);
    }
    [Fact] public void UnknownCampOrOldEpochHidesAndFacingIsInteractionOnly()
    {
        var(camp,epoch,anchor)=Camp(); var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,null,true,epoch,new Flat(),new Around()).Action);
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,Survey(camp),true,default,new Flat(),new Around()).Action);
        Assert.Equal(90f,GunnarCampRoutine.FacingYaw(default,new WorldPoint(1,0,0)));
        Assert.Null(GunnarCampRoutine.FacingYaw(default,default));
    }

    // --- the door rule, the wall check, room to stand and camp (#461) -------

    [Fact] public void ASpotAcrossADoorwayFromHomeIsNeverHis()
    {
        var(camp,epoch,anchor)=Camp(); var around=new Around();
        around.DoorList.Add(DoorAtX(2f));
        var step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),around);
        Assert.True(step.Placement.Found);
        Assert.True(step.Placement.Position.X<2f,"he was set down on the far side of a doorway: "+step.Placement.Position);
    }
    [Fact] public void EveryDoorCountsAsOneHeMayNotUseOpenOrShut()
    {
        // A door between home and part of the band: shut, open, and allowed but
        // shut. Only an open door a player allowed would pass, and Teamster
        // allows none.
        foreach (DoorPortal door in new[] { DoorAtX(1f), DoorAtX(1f,open:true), DoorAtX(1f,allowed:true) })
        {
            var(camp,epoch,anchor)=Camp(); var around=new Around();
            around.DoorList.Add(door);
            var step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),around);
            Assert.True(step.Placement.Found);
            Assert.True(step.Placement.Position.X<1f,"open="+door.IsOpen+" allowed="+door.Allowed);
        }
        var line=new WorldPoint[2];
        Assert.True(GunnarRestBounds.AcrossADoor(default,new WorldPoint(3,0,0),new[] { DoorAtX(1f,open:true) },line));
        Assert.True(GunnarRestBounds.AcrossADoor(default,new WorldPoint(3,0,0),new[] { DoorAtX(1f,allowed:true) },line));
        Assert.False(GunnarRestBounds.AcrossADoor(default,new WorldPoint(3,0,0),new[] { DoorAtX(1f,open:true,allowed:true) },line));
        Assert.False(GunnarRestBounds.AcrossADoor(default,new WorldPoint(0.5f,0,0),new[] { DoorAtX(1f) },line));
        Assert.False(GunnarRestBounds.AcrossADoor(default,new WorldPoint(3,0,0),Array.Empty<DoorPortal>(),line));
    }
    [Fact] public void ACrossingAloneRefusesASpotClearOfTheDoorway()
    {
        // A door plane 1.6 m east of home, so home is clear of it, with room
        // to stand only far east (past the doorway's clearance) or far west.
        // Without the crossing rule the sweep's first far-east spot wins; with
        // it, that spot is across the doorway and he stands west instead.
        var(camp,epoch,anchor)=Camp();
        var around=new Around { Room = s => s.X>3.2f || s.X< -3.5f };
        around.DoorList.Add(DoorAtX(1.6f));
        var step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),around);
        Assert.True(step.Placement.Found);
        Assert.False(GunnarRestBounds.BesideADoorway(step.Placement.Position,around.Doors));
        Assert.True(step.Placement.Position.X< -3.5f,"he was set down across a doorway from home: "+step.Placement.Position);
    }
    [Fact] public void ASpotInOrBesideADoorwayIsNeverHis()
    {
        // A 2 m door 3 m north of home, opening facing north: home is clear of
        // it, the spots near it are not, and whatever is chosen keeps clear.
        var door=new DoorPortal(new DoorPlace(new WorldPoint(0,0,3),0),new WorldPoint(0,0,3),0f,1f,1f,
            isOpen:true,canOpen:true,allowed:true);
        Assert.True(GunnarRestBounds.BesideADoorway(new WorldPoint(0,0,2),new[] { door }));
        Assert.True(GunnarRestBounds.BesideADoorway(new WorldPoint(2.4f,0,3),new[] { door }));
        Assert.False(GunnarRestBounds.BesideADoorway(new WorldPoint(0,0,1.4f),new[] { door }));
        Assert.False(GunnarRestBounds.BesideADoorway(new WorldPoint(2.6f,0,3),new[] { door }));
        Assert.False(GunnarRestBounds.BesideADoorway(new WorldPoint(0,3,2),new[] { door }));
        // A door beside the spot he would otherwise take first (about 2.3 m
        // east of home), allowed and open so crossing it refuses nothing: only
        // the clearance moves him.
        var(camp,epoch,anchor)=Camp();
        var first=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),new Around());
        Assert.True(first.Placement.Found);
        var beside=new DoorPortal(new DoorPlace(first.Placement.Position,0),first.Placement.Position,0f,1f,1f,
            isOpen:true,canOpen:true,allowed:true);
        var around=new Around(); around.DoorList.Add(beside);
        var step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),around);
        Assert.True(step.Placement.Found);
        Assert.False(GunnarRestBounds.BesideADoorway(step.Placement.Position,new[] { beside }),
            "he was set down in a doorway: "+step.Placement.Position);
    }
    [Fact] public void ASpotBehindAWallOrWithoutRoomToStandIsRefusedAndNothingLeftShowsNothing()
    {
        var(camp,epoch,anchor)=Camp();
        var walled=new Around { Wall = spot => spot.X>0f };
        var step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),walled);
        Assert.True(step.Placement.Found); Assert.True(step.Placement.Position.X<=0f);

        var cramped=new Around { Room = spot => spot.Z<0f };
        step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),cramped);
        Assert.True(step.Placement.Found); Assert.True(step.Placement.Position.Z<0f);

        // A wall check that cannot be asked answers "wall" at the boundary,
        // so nowhere qualifies: no figure rather than a guessed spot.
        var unknown=new Around { Wall = _ => true };
        step=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),unknown);
        Assert.Equal(GunnarCampAction.None,step.Action); Assert.False(step.Placement.Found);
    }
    [Fact] public void AnEstablishedCampBoundsTheSpotAndNoCampLeavesHomeToDecide()
    {
        var(camp,epoch,anchor)=Camp();
        // One player-built piece 20 m east, inside the seed radius: camp is the
        // ground around it, and the band around home lies outside.
        Assert.True(camp.NotePiece("shed",new NpcPoint(20,0,0),true,epoch));
        NpcCampView view=Survey(camp);
        Assert.True(view.IsEstablished);
        var step=new GunnarCampRoutine().Step(Facts(),anchor,view,true,epoch,new Flat(),new Around());
        Assert.Equal(GunnarCampAction.None,step.Action);

        var(open,openEpoch,openAnchor)=Camp();
        NpcCampView none=Survey(open);
        Assert.False(none.IsEstablished);
        Assert.Equal(GunnarCampAction.Show,new GunnarCampRoutine().Step(Facts(),openAnchor,none,true,openEpoch,new Flat(),new Around()).Action);
    }
    [Fact] public void AFailedLookShowsHimNowhereNewAndKeepsTheSpotHeHas()
    {
        var(camp,epoch,anchor)=Camp(); var probe=new Flat(); var around=new Around();
        var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,Survey(camp),false,epoch,probe,around).Action);
        Assert.Equal(0,probe.Calls);

        var first=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around); routine.NoteFigureShown(true);
        int calls=probe.Calls;
        var moved=new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(50,0,50));
        around.Revision++;
        var kept=routine.Step(Facts(),moved,Survey(camp,50,50),false,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Retain,kept.Action);
        Assert.Equal(first.Placement.Position,kept.Placement.Position);
        Assert.Equal(calls,probe.Calls);
    }
    [Fact] public void SomethingBuiltNearHomeReplansAndTheSameGroundKeepsTheSameSpot()
    {
        var(camp,epoch,anchor)=Camp(); var probe=new Flat(); var around=new Around(); var routine=new GunnarCampRoutine();
        var first=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around); routine.NoteFigureShown(true);
        int calls=probe.Calls;
        around.Revision++;
        var again=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around);
        Assert.True(probe.Calls>calls,"a change near home was not asked about");
        Assert.Equal(GunnarCampAction.Retain,again.Action);
        Assert.Equal(first.Placement.Position,again.Placement.Position);

        // Built over: the spot fails the room check now, and he moves.
        WorldPoint taken=first.Placement.Position;
        around.Room = spot => !spot.Equals(taken);
        around.Revision++;
        var moved=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Reposition,moved.Action);
        Assert.NotEqual(taken,moved.Placement.Position);
    }
    [Fact] public void AnUnrelatedChangeNearHomeDoesNotMoveHimOffASpotThatStillPasses()
    {
        var(camp,epoch,anchor)=Camp(); var probe=new Flat(); var routine=new GunnarCampRoutine();
        var best=new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),new Around()).Placement.Position;
        // The best spot is blocked at first, so he takes another.
        var around=new Around { Room = s => !s.Equals(best) };
        var first=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around); routine.NoteFigureShown(true);
        Assert.NotEqual(best,first.Placement.Position);
        // Whatever blocked it is taken down: the better spot is free, his own
        // still passes, and he stays put.
        around.Room = _ => true; around.Revision++;
        var after=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Retain,after.Action);
        Assert.Equal(first.Placement.Position,after.Placement.Position);
        // A moved home is a fresh plan, whatever still fits.
        var moved=new CompanionAnchor(AnchorKind.ClaimedBed,new WorldPoint(5f,0,0));
        around.Revision++;
        Assert.Equal(GunnarCampAction.Reposition,routine.Step(Facts(),moved,Survey(camp,5f,0),true,epoch,probe,around).Action);
    }
    [Fact] public void UnloadedGroundIsAskedAgainAndAShownFigureKeepsItsSpotMeanwhile()
    {
        var(camp,epoch,anchor)=Camp(); var probe=new Flat { Unloaded = true }; var around=new Around(); var routine=new GunnarCampRoutine();
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around).Action);
        int calls=probe.Calls;
        Assert.Equal(GunnarCampAction.None,routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around).Action);
        Assert.True(probe.Calls>calls,"a deferral was remembered instead of asked again");
        probe.Unloaded=false;
        var shown=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Show,shown.Action); routine.NoteFigureShown(true);

        probe.Unloaded=true; around.Revision++;
        var held=routine.Step(Facts(),anchor,Survey(camp),true,epoch,probe,around);
        Assert.Equal(GunnarCampAction.Retain,held.Action);
        Assert.Equal(shown.Placement.Position,held.Placement.Position);
    }
    [Fact] public void UnknownKeepsakesDeferRatherThanGuess()
    {
        var(camp,epoch,anchor)=Camp();
        var around=new Around { Keepsakes = null };
        Assert.Equal(GunnarCampAction.None,new GunnarCampRoutine().Step(Facts(),anchor,Survey(camp),true,epoch,new Flat(),around).Action);
    }
}
