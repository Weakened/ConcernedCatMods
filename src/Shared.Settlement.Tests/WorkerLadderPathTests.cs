using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
namespace Shared.Settlement.Tests;
public sealed class WorkerLadderPathTests
{
    private sealed class Paths : IWorkerLadderPathSource
    {
        internal WorkerLadderPassage? Path; internal int Generation;
        public bool TryPath(string id,out WorkerLadderPassage path,out int generation) { path=Path!; generation=Generation; return path!=null; }
    }
    private readonly TraversalLinkNetwork _network=new();
    private readonly Paths _paths=new();
    private readonly WorkerLadderPathTraversal _traversal;
    private readonly WorkPoint _bottom=new(0,0,.7f),_top=new(0,2,-.8f);
    public WorkerLadderPathTests()
    {
        var limits=TraversalLimits.Default; var caps=new TraversalCapabilities(true); caps.Grant(WorkerKey.Thorstein,TraversalCapability.Ladders);
        Assert.True(TraversalLink.TryCreate("checked-path",TraversalLinkKind.Ladder,_bottom,_top,TraversalCapability.Ladders,limits,out var link));
        _paths.Generation=_network.Publish(link);
        _paths.Path=new WorkerLadderPassage(_bottom,_bottom,new WorkPoint(0,2,.7f),_top);
        _traversal=new WorkerLadderPathTraversal(new LinkTraversal(_network,caps,limits),_paths,1,WorkerKey.Thorstein);
    }
    private TraversalRequest Request(bool down=false)=>TraversalRequest.ForWorker(WorkerKey.Thorstein,"checked-path",down?_top:_bottom,
        authority:WorkAuthorityVerdict.Granted);
    [Fact] public void UpwardTravelFollowsVerticalLaneBeforeCrossingToThePlatform()
    {
        var entry=_traversal.Enter(Request()); Assert.True(entry.Started);
        TraversalProgress progress=default;
        for(int i=0;i<20;i++) progress=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(1,progress.EstimatedPosition.Y,4); Assert.Equal(.7f,progress.EstimatedPosition.Z,4);
        Assert.Equal(1,progress.MetresFromEntry,4); // not one metre along a diagonal through the rails
        for(int i=0;i<50;i++) progress=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(TraversalPhase.ReachedFarEnd,progress.Phase);
        Assert.Equal(_top,progress.EstimatedPosition); Assert.True(_traversal.Exit(entry.Handle,TraversalExitKind.Arrived).PlaceActor);
    }
    [Fact] public void DescentStartsWithThePlatformExitInReverseThenClimbsDown()
    {
        var entry=_traversal.Enter(Request(true)); Assert.True(entry.Started);
        var progress=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(2,progress.EstimatedPosition.Y); Assert.True(progress.EstimatedPosition.Z>_top.Z);
        for(int i=0;i<70;i++) progress=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(_bottom,progress.EstimatedPosition);
    }
    [Fact] public void APhysicsTickStopsAtTheCornerBeforeMovingOntoTheExit()
    {
        var entry=_traversal.Enter(Request());
        for(int i=0;i<66;i++) _traversal.Traverse(entry.Handle,TraversalIntent.Advance,.03f);
        var corner=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(new WorkPoint(0,2,.7f),corner.EstimatedPosition);
        var exit=_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(2,exit.EstimatedPosition.Y); Assert.True(exit.EstimatedPosition.Z<.7f);
    }
    [Fact] public void PrematureArrivalNeverAuthorizesAnEndpointPlacement()
    {
        var entry=_traversal.Enter(Request());
        Assert.False(_traversal.Exit(entry.Handle,TraversalExitKind.Arrived).PlaceActor);
        Assert.False(_traversal.Exit(entry.Handle,TraversalExitKind.Arrived).PlaceActor);
    }
    [Fact] public void UnknownLostOrRebuiltPathReleasesWithoutGuessingALanding()
    {
        var entry=_traversal.Enter(Request()); _paths.Path=null;
        Assert.Equal(TraversalPhase.Lost,_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f).Phase);
        Assert.False(_traversal.Exit(entry.Handle,TraversalExitKind.Arrived).PlaceActor);
        Assert.False(_traversal.Enter(Request()).Started);
    }
    private sealed class Mount : IWorkerLadderMountProbe
    { internal bool Allowed=true; public bool CanMount(WorkPoint standing,WorkPoint entry)=>Allowed; }
    [Fact] public void ACheckedMountStartsAtTheActualBodyInsteadOfSnappingToTheEntry()
    {
        var caps=new TraversalCapabilities(true); caps.Grant(WorkerKey.Thorstein,TraversalCapability.Ladders);
        var mount=new Mount(); var traversal=new WorkerLadderPathTraversal(new LinkTraversal(_network,caps),_paths,1,WorkerKey.Thorstein,mount);
        var standing=new WorkPoint(0,0,1.2f);
        var request=TraversalRequest.ForWorker(WorkerKey.Thorstein,"checked-path",standing);
        Assert.False(_traversal.Enter(request).Started); // no sweep, no approximate-entry permission
        var entered=traversal.Enter(request); Assert.True(entered.Started);
        Assert.Equal(standing,traversal.Traverse(entered.Handle,TraversalIntent.Hold,0).EstimatedPosition);
        var moved=traversal.Traverse(entered.Handle,TraversalIntent.Advance,.05f);
        Assert.Equal(0,moved.EstimatedPosition.Y); Assert.Equal(1.15f,moved.EstimatedPosition.Z,5);
        traversal.Traverse(entered.Handle,TraversalIntent.Reverse,.05f);
        Assert.Equal(standing,traversal.Exit(entered.Handle,TraversalExitKind.TurnedBack).Landing);
        mount.Allowed=false; Assert.False(traversal.Enter(request).Started);
    }
    [Fact] public void TheBoundWorkerCannotBeReplacedAndAnotherEntryCannotResetProgress()
    {
        Assert.False(_traversal.Enter(TraversalRequest.ForWorker(WorkerKey.Gunnar,"checked-path",_bottom)).Started);
        var entry=_traversal.Enter(Request());
        _traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f);
        var second=_traversal.Enter(Request()); Assert.False(second.Started);
        Assert.Equal(TraversalRefusal.AlreadyTraversing,second.Verdict.Refusal);
        Assert.Equal(.1f,_traversal.Traverse(entry.Handle,TraversalIntent.Advance,.05f).MetresFromEntry,5);
    }
    [Fact] public void InvalidOrLargeDeltaCannotSkipTheCheckedTrackAndPlayersCannotEnter()
    {
        Assert.False(_traversal.Enter(TraversalRequest.ForPlayer("checked-path",_bottom)).Started);
        var entry=_traversal.Enter(Request());
        Assert.Equal(0,_traversal.Traverse(entry.Handle,TraversalIntent.Advance,float.PositiveInfinity).MetresFromEntry);
        Assert.Equal(.05f,_traversal.Traverse(entry.Handle,TraversalIntent.Advance,100).MetresFromEntry,4);
        Assert.Equal(TraversalPhase.ReturnedToStart,_traversal.Traverse(entry.Handle,TraversalIntent.Reverse,.05f).Phase);
        Assert.True(_traversal.Exit(entry.Handle,TraversalExitKind.TurnedBack).PlaceActor);
    }
}
