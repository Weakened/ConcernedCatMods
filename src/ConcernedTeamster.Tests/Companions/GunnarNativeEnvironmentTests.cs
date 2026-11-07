using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedTeamster.Adapters.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarNativeEnvironmentTests
{
    public GunnarNativeEnvironmentTests()
    { Game.instance=new(); ZoneSystem.instance=new(); Physics.Overlaps=_=>Array.Empty<Collider>();
      Physics.Hits=Array.Empty<RaycastHit>(); Physics.Blocked=false; LayerMask.Mask=1; }
    [Fact] public void ClaimedCurrentBedWinsAndDestroyedLoadedBedFallsBackToActualStart()
    {
        Game.instance!.Profile.Custom=true; Game.instance.Profile.Spawn=new(1,0,1);
        Physics.Overlaps=_=>new[] { new Collider { Parents=new object[] { new Bed { Current=true } } } };
        var env=new GunnarKeepsakeEnvironment(); Assert.True(env.TryAnchor(out var anchor));
        Assert.Equal(AnchorKind.ClaimedBed,anchor.Kind); Assert.Equal(1,anchor.Position.X);
        Physics.Overlaps=_=>Array.Empty<Collider>(); Assert.True(env.TryAnchor(out anchor));
        Assert.Equal(AnchorKind.DefaultSpawn,anchor.Kind); Assert.Equal(20,anchor.Position.X);
    }
    [Fact] public void UnloadedBedAndSaturatedBedQueryNeverGuessStart()
    {
        Game.instance!.Profile.Custom=true; ZoneSystem.instance!.Loaded=false;
        var env=new GunnarKeepsakeEnvironment(); Assert.False(env.TryAnchor(out _));
        ZoneSystem.instance.Loaded=true; Physics.Overlaps=_=>Enumerable.Range(0,128).Select(_=>new Collider()).ToArray();
        Assert.False(env.TryAnchor(out _));
    }
    [Fact] public void NativeSnapshotFindsCompassAndSharedKeepsakesButExcludesOwnBuckle()
    {
        var compass=new TheConcernedCat.ConcernedCartographer.Runtime.Companions.BrokenCompassObject();
        compass.transform.position=new(2,0,0);
        var other=new TheConcernedCat.ConcernedNPC.Body.Keepsakes.NpcKeepsakeBehaviour(); other.gameObject.name="Other keepsake";
        other.transform.position=new(4,0,0);
        var own=new TheConcernedCat.ConcernedNPC.Body.Keepsakes.NpcKeepsakeBehaviour(); own.gameObject.name="CT_GunnarBuckle";
        Physics.Overlaps=_=>new[] { new Collider { Parents=new object[]{compass} },new Collider { Parents=new object[]{other} },
            new Collider { Parents=new object[]{own} },new Collider { Parents=new object[]{compass} } };
        var points=new GunnarKeepsakeEnvironment().OtherMementos(new CompanionAnchor(AnchorKind.ClaimedBed,default));
        Assert.NotNull(points); Assert.Equal(2,points!.Count); Assert.Contains(new WorldPoint(2,0,0),points);
        Assert.Contains(new WorldPoint(4,0,0),points); Assert.DoesNotContain(default(WorldPoint),points);
    }
    [Fact] public void SaturatedMementoSnapshotAndMissingAnchorRemainUnknown()
    {
        var env=new GunnarKeepsakeEnvironment(); Assert.Null(env.OtherMementos(default));
        Physics.Overlaps=_=>Enumerable.Range(0,512).Select(_=>new Collider()).ToArray();
        Assert.Null(env.OtherMementos(new CompanionAnchor(AnchorKind.ClaimedBed,default)));
    }
    private static RaycastHit Surface(float y)=>new() { collider=new Collider(),point=new(0,y,0),normal=new(0,1,0) };
    [Fact] public void GroundingUsesNearestLevelAndChoosesFloorInRoofTie()
    {
        Physics.Hits=new[]{Surface(2),Surface(0)};
        var sample=new GunnarKeepsakeEnvironment().Probe(new WorldPoint(0,1,0));
        Assert.Equal(0,sample.Position.Y); Assert.Equal(PlacementRejection.None,sample.Rejections);
    }
    [Fact] public void MovingOrMissingGroundWaterSteepnessAndObstructionAreRefused()
    {
        var env=new GunnarKeepsakeEnvironment();
        Assert.Equal(PlacementRejection.Unsupported,env.Probe(default).Rejections);
        var hit=Surface(0); hit.collider.attachedRigidbody=new(); Physics.Hits=new[]{hit};
        Assert.Equal(PlacementRejection.Unsupported,env.Probe(default).Rejections);
        hit=Surface(0); hit.normal=new(0,0.5f,0); Physics.Hits=new[]{hit}; ZoneSystem.instance!.m_waterLevel=1; Physics.Blocked=true;
        var reasons=env.Probe(default).Rejections;
        Assert.True(reasons.HasFlag(PlacementRejection.TooSteep)); Assert.True(reasons.HasFlag(PlacementRejection.Water));
        Assert.True(reasons.HasFlag(PlacementRejection.Occupied));
    }
    [Fact] public void UnknownWaterOrSaturatedGroundIsNotLoadedRatherThanSafe()
    {
        var env=new GunnarKeepsakeEnvironment(); ZoneSystem.instance!.m_waterLevel=float.NaN;
        Assert.Equal(PlacementRejection.NotLoaded,env.Probe(default).Rejections);
        ZoneSystem.instance.m_waterLevel=-10; Physics.Hits=Enumerable.Range(0,32).Select(_=>Surface(0)).ToArray();
        Assert.Equal(PlacementRejection.NotLoaded,env.Probe(default).Rejections);
    }
    [Fact] public void EveryExistingCensusStateIsConservativeAndLiveBodyAlwaysWins()
    {
        foreach(var state in Enum.GetValues<WorkerBodyStatus>())
        {
            Assert.True(GunnarWorkerPresence.From(state,true));
            bool? expected=state==WorkerBodyStatus.NotFound ? false :
                state==WorkerBodyStatus.Unspecified||state==WorkerBodyStatus.Searching ? null : true;
            Assert.Equal(expected,GunnarWorkerPresence.From(state,false));
        }
        Assert.Null(GunnarWorkerPresence.From((WorkerBodyStatus)999,false));
    }
}
