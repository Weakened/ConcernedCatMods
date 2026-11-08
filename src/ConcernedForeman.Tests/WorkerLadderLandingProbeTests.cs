using TheConcernedCat.ConcernedForeman.Runtime.Ladders;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
using UnityEngine;
namespace ConcernedForeman.Tests;
[Collection("vanilla-stubs")]
public sealed class WorkerLadderLandingProbeTests : IDisposable
{
    private readonly ForemanWorkerAI _worker;
    private readonly Collider _own,_floor;
    private readonly WorkerLadderCapsule _shape=new(.35f,1.8f);
    private readonly List<string> _log=new();
    private WorkerLadderRunData Run()
    {
        float[] row={0,0,0,0,2,0,0,1,.8f,1f/3,.24f,.08f};
        Assert.True(WorkerLadderPublication.TryRead(Guid.NewGuid().ToString("N"),1,new[]{"a"},new[]{row},out var publication));
        return new WorkerLadderRunData(publication.Epoch,publication.Sections);
    }
    public WorkerLadderLandingProbeTests()
    {
        var root=new GameObject("own-worker"); _worker=root.Add(new ForemanWorkerAI()); root.Add(new Humanoid()); root.Add(new ZNetView()); _own=root.Add(new CapsuleCollider());
        _floor=new GameObject("actual-floor").Add(new Collider());
        ZoneSystem.instance=new ZoneSystem(); LayerMask.Mask=1;
        Physics.Rays=origin=>new[]{Hit(_floor,origin)};
        Physics.Overlaps=()=>Array.Empty<Collider>(); Physics.Sweeps=_=>Array.Empty<RaycastHit>();
    }
    public void Dispose() { ZoneSystem.instance=null; Physics.Rays=_=>Array.Empty<RaycastHit>(); Physics.Overlaps=()=>Array.Empty<Collider>(); Physics.Sweeps=_=>Array.Empty<RaycastHit>(); }
    private static RaycastHit Hit(Collider collider,Vector3 origin,float distance=.4f)=>new()
        { collider=collider,point=new Vector3(origin.x,origin.y-distance,origin.z),normal=new Vector3(0,1,0),distance=distance };
    private WorkerLadderLandingProbe Probe(Func<WorkPoint,bool>? policy=null)
    { var probe=new WorkerLadderLandingProbe(_worker,policy??(_=>true),_log.Add); probe.BeginSurvey(); return probe; }
    [Fact] public void BothFacesProduceSeparateCheckedPassagesAndDoNotIgnoreOtherCharacters()
    {
        var probe=Probe(); var run=Run(); Physics.Overlaps=()=>new[]{_own};
        Assert.Equal(WorkerLadderProbeResult.Supported,probe.TryMeasure(run,WorkerLadderFace.Front,_shape,out var front));
        Assert.Equal(WorkerLadderProbeResult.Supported,probe.TryMeasure(run,WorkerLadderFace.Rear,_shape,out var rear));
        Assert.NotEqual(front.Points[0].Z,rear.Points[0].Z); Assert.Contains("character_net",LayerMask.Requested);
        var otherRoot=new GameObject("other-player") { Parent=_worker.gameObject }; otherRoot.Add(new Humanoid()); var other=otherRoot.Add(new Collider());
        Physics.Overlaps=()=>new[]{_own,other};
        Assert.Equal(WorkerLadderProbeResult.Blocked,probe.TryMeasure(run,WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void AForeignChildNetworkObjectIsNotIgnoredAndLostOwnOwnershipRefuses()
    {
        var probe=Probe();
        var child=new GameObject("foreign-network-object") { Parent=_worker.gameObject };
        child.Add(new ZNetView()); var collider=child.Add(new Collider());
        Physics.Overlaps=()=>new[]{_own,collider};
        Assert.Equal(WorkerLadderProbeResult.Blocked,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
        _worker.GetComponent<ZNetView>()!.Owner=false;
        Assert.Equal(WorkerLadderProbeResult.Unavailable,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void ActualCapsuleBoundsAreRequiredInsteadOfGuessedWorkerDimensions()
    {
        var probe=Probe(); Assert.True(probe.TryBodyShape(out var measured)); Assert.Equal(.35f,measured.Radius);
        Assert.Equal(WorkerLadderProbeResult.Unavailable,probe.TryMeasure(Run(),WorkerLadderFace.Front,new WorkerLadderCapsule(.2f,1.2f),out _));
        _worker.GetComponent<CapsuleCollider>()!.direction=0;
        Assert.False(probe.TryBodyShape(out _));
        Assert.Equal(WorkerLadderProbeResult.Unavailable,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void MountSweepRequiresTheActualBodyPositionAndCurrentStandingSupport()
    {
        var probe=Probe(); var entry=new WorkPoint(0,0,.67f);
        Assert.True(probe.CanMount(default,entry));
        Assert.False(probe.CanMount(new WorkPoint(1,0,0),entry));
        Physics.Sweeps=_=>new[]{Hit(_floor,new Vector3())};
        Assert.False(probe.CanMount(default,entry));
    }
    [Fact] public void MovingDoorOrRigidbodyIsNotReplacedByAnUnderlyingFloor()
    {
        var root=new GameObject("moving-door"); root.Add(new Door()); var door=root.Add(new Collider());
        Physics.Rays=origin=>new[]{Hit(_floor,origin),Hit(door,origin,.2f)};
        var probe=Probe(); Assert.Equal(WorkerLadderProbeResult.Blocked,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
        root=new GameObject("moving-platform"); var moving=root.Add(new Collider { attachedRigidbody=new Rigidbody() });
        Physics.Rays=origin=>new[]{Hit(moving,origin)};
        Assert.Equal(WorkerLadderProbeResult.Blocked,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void AThinRailCentreHitDoesNotSupplyAStandingFootprint()
    {
        Physics.Rays=origin=>Math.Abs(origin.x)<.01f?new[]{Hit(_floor,origin)}:Array.Empty<RaycastHit>();
        Assert.Equal(WorkerLadderProbeResult.Blocked,Probe().TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Theory] [InlineData("ray")] [InlineData("overlap")] [InlineData("sweep")]
    public void SaturatedQueriesAreUnavailableInsteadOfPartiallyAccepted(string kind)
    {
        if(kind=="ray") Physics.Rays=origin=>Enumerable.Repeat(Hit(_floor,origin),16).ToArray();
        if(kind=="overlap") Physics.Overlaps=()=>Enumerable.Repeat(_own,32).ToArray();
        if(kind=="sweep") Physics.Sweeps=_=>Enumerable.Repeat(Hit(_own,new Vector3()),16).ToArray();
        Assert.Equal(WorkerLadderProbeResult.Unavailable,Probe().TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void UnloadedCornersOrDeniedSiteNeverBecomeAUsableLanding()
    {
        ZoneSystem.instance!.Unloaded.Add(ZoneSystem.Key(new Vector3(.35f,0,.24f+.35f+.08f+.35f)));
        Assert.Equal(WorkerLadderProbeResult.Unavailable,Probe().TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
        ZoneSystem.instance.Unloaded.Clear();
        Assert.Equal(WorkerLadderProbeResult.Unavailable,Probe(_=>false).TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
    }
    [Fact] public void BlockedClimbLaneAndExhaustedBudgetRefuseAndNativeErrorsAreThrottled()
    {
        var probe=Probe(); Physics.Sweeps=direction=>direction.y>.5f?new[]{Hit(_floor,new Vector3())}:Array.Empty<RaycastHit>();
        Assert.Equal(WorkerLadderProbeResult.Blocked,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
        Physics.Sweeps=_=>Array.Empty<RaycastHit>(); probe.BeginSurvey();
        for(int i=0;i<40;i++) probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _);
        Assert.True(probe.PhysicsQueries<=256);
        Assert.Equal(WorkerLadderProbeResult.Unavailable,probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _));
        probe.BeginSurvey(); Physics.Rays=_=>throw new InvalidOperationException();
        probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _); probe.TryMeasure(Run(),WorkerLadderFace.Front,_shape,out _);
        Assert.Single(_log); Assert.Contains("no links",_log[0]);
    }
}
