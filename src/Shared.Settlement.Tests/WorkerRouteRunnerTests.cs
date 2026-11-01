using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
namespace Shared.Settlement.Tests;
public sealed class WorkerRouteRunnerTests
{
    private sealed class Hold : IWorkerRouteHold
    {
        public bool IsActive {get;private set;}=true; public int Releases;
        public void Dispose(){ if(IsActive) Releases++; IsActive=false; }
    }
    private sealed class Port : IWorkerRoutePort,IWorkerLadderMotor
    {
        public WorkerKey Worker=>WorkerKey.Thorstein;
        public Guid BodyEpoch {get;set;}=Guid.NewGuid();
        public WorkPoint Position {get;set;}
        public bool FeatureEnabled {get;set;}=true;
        public bool ExplicitOrderActive {get;set;}=true;
        public bool ActorReady {get;set;}=true;
        public WorkAuthorityVerdict Authority {get;set;}=WorkAuthorityVerdict.Granted;
        public bool GroundMotorSuspended {get;private set;}
        public bool LandingSafe=true,RestoreAllowed=true;
        public int WalkCalls,PlaceCalls,RestoreCalls;
        public readonly List<string> Trace=new(); public readonly Hold Held=new();
        public IWorkerRouteHold? TryHoldRoute(string orderId)=>Held;
        public bool WalkToward(WorkPoint target,float delta)
        { Assert.False(GroundMotorSuspended); WalkCalls++; Position=target; Trace.Add("walk"); return true; }
        public void StopWalkingForRoute(){ Trace.Add("stop walk"); }
        public IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey worker,Guid epoch)
        { Assert.Equal(Worker,worker); Assert.Equal(BodyEpoch,epoch); Trace.Add("suspend"); GroundMotorSuspended=true; return this; }
        public bool IsSupportedLanding(WorkPoint target)=>LandingSafe;
        public bool TryPlaceOwnBody(WorkPoint point)
        { Assert.True(GroundMotorSuspended); Assert.True(Held.IsActive); PlaceCalls++; Position=point; Trace.Add("climb"); return true; }
        public bool TryRestore()
        { RestoreCalls++; Trace.Add("restore"); if(!RestoreAllowed) return false; GroundMotorSuspended=false; return true; }
    }
    private static (TraversalLinkNetwork,LinkTraversal,WorkerRoutePlan) Setup()
    {
        var limits=TraversalLimits.Default; var network=new TraversalLinkNetwork();
        Assert.True(TraversalLink.TryCreate("link",TraversalLinkKind.Ladder,default,new WorkPoint(0,4,0),
            TraversalCapability.Ladders,limits,out var link)); int gen=network.Publish(link);
        var caps=new TraversalCapabilities(true); caps.Grant(WorkerKey.Thorstein,TraversalCapability.Ladders);
        var plan=new WorkerRoutePlan(Array.AsReadOnly(new[]{
            new WorkerRouteLeg(WorkerRouteLegKind.Walk,new WorkPoint(-2,0,0),default),
            new WorkerRouteLeg(WorkerRouteLegKind.Climb,default,new WorkPoint(0,4,0),"link",gen,TraversalDirection.Up),
            new WorkerRouteLeg(WorkerRouteLegKind.Walk,new WorkPoint(0,4,0),new WorkPoint(2,4,0)) }),8);
        return(network,new LinkTraversal(network,caps),plan);
    }
    [Fact] public void WalkClimbWalkRestoresBeforeGroundWalkingAndReleasesOneOwnHold()
    {
        var(network,traversal,plan)=Setup(); var port=new Port{Position=new WorkPoint(-2,0,0)};
        var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein); Assert.True(runner.Start(plan,"ordered-job"));
        for(int i=0;i<30&&runner.ControlsRoute;i++) runner.Tick(0.5f);
        Assert.Equal(WorkerRoutePhase.Complete,runner.Phase); Assert.False(port.GroundMotorSuspended);
        Assert.Equal(1,port.Held.Releases); Assert.Equal(2,port.WalkCalls); Assert.True(port.PlaceCalls>0);
        Assert.True(port.Trace.IndexOf("stop walk")<port.Trace.IndexOf("suspend"));
        Assert.True(port.Trace.LastIndexOf("restore")<port.Trace.LastIndexOf("walk")); Assert.Equal(0,traversal.ActiveCount);
    }
    [Fact] public void LostLinkMidClimbReleasesWithoutSteppingOntoAnyLanding()
    {
        var(network,traversal,plan)=Setup(); var port=new Port(); var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein);
        runner.Start(plan,"ordered-job"); runner.Tick(0.1f); runner.Tick(0.1f);
        Assert.Equal(WorkerRoutePhase.Climbing,runner.Phase); WorkPoint last=port.Position; int placements=port.PlaceCalls;
        network.Retire("link"); runner.Tick(0.1f);
        Assert.Equal(WorkerRouteStop.LinkChanged,runner.StopReason); Assert.Equal(last,port.Position);
        Assert.Equal(placements,port.PlaceCalls); Assert.False(port.GroundMotorSuspended); Assert.Equal(0,traversal.ActiveCount);
    }
    [Fact] public void AuthorityLossRestoresAndCancelIsIdempotent()
    {
        var(network,traversal,plan)=Setup(); var port=new Port(); var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein);
        runner.Start(plan,"ordered-job"); runner.Tick(0.1f); runner.Tick(0.1f); port.Authority=default; runner.Tick(0.1f);
        Assert.Equal(WorkerRouteStop.Authority,runner.StopReason); Assert.False(port.GroundMotorSuspended);
        runner.Cancel(); runner.Cancel(); Assert.Equal(1,port.Held.Releases); Assert.Equal(0,traversal.ActiveCount);
    }
    [Fact] public void FailedRestorationKeepsIdentityAndRetriesWithoutMovement()
    {
        var(network,traversal,plan)=Setup(); var port=new Port(); var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein);
        runner.Start(plan,"ordered-job"); runner.Tick(0.1f); runner.Tick(0.1f); port.RestoreAllowed=false; runner.Cancel();
        int placements=port.PlaceCalls;
        Assert.Equal(WorkerRoutePhase.Restoring,runner.Phase); Assert.True(port.Held.IsActive);
        Assert.False(runner.Start(plan,"another-job")); runner.Tick(1); Assert.Equal(placements,port.PlaceCalls);
        port.RestoreAllowed=true; runner.Tick(1); Assert.Equal(WorkerRoutePhase.Cancelled,runner.Phase);
        Assert.False(port.GroundMotorSuspended); Assert.Equal(1,port.Held.Releases);
    }
    [Fact] public void UnsupportedTopDoesNotPlaceAtTheFarEnd()
    {
        var(network,traversal,plan)=Setup(); var port=new Port{LandingSafe=false};
        var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein); runner.Start(plan,"ordered-job");
        runner.Tick(0.1f); runner.Tick(10);
        Assert.Equal(WorkerRouteStop.UnsafeLanding,runner.StopReason); Assert.Equal(0,port.PlaceCalls);
        Assert.False(port.GroundMotorSuspended); Assert.Equal(default(WorkPoint),port.Position);
    }
    [Fact] public void RebuiltLinkNeverSuppliesANewLandingToAnOldHandleEvenOnDirectExit()
    {
        var(network,traversal,_)=Setup(); var entered=traversal.Enter(TraversalRequest.ForWorker(WorkerKey.Thorstein,"link",default));
        Assert.True(entered.Started); network.Retire("link");
        Assert.True(TraversalLink.TryCreate("link",TraversalLinkKind.Ladder,default,new WorkPoint(0,8,0),
            TraversalCapability.Ladders,TraversalLimits.Default,out var replacement)); network.Publish(replacement);
        Assert.False(traversal.Exit(entered.Handle,TraversalExitKind.Arrived).PlaceActor);
        Assert.Equal(0,traversal.ActiveCount);
    }
    [Fact] public void WrongFloorAndInvalidDefaultLegNeverCountAsArrival()
    {
        var(network,traversal,_)=Setup(); var port=new Port();
        var runner=new WorkerRouteRunner(network,traversal,port,WorkerKey.Thorstein);
        Assert.False(runner.Start(new WorkerRoutePlan(new[]{default(WorkerRouteLeg)},0),"ordered-job"));
        var high=new WorkerRoutePlan(new[]{new WorkerRouteLeg(WorkerRouteLegKind.Walk,default,new WorkPoint(0,4,0))},4);
        Assert.True(runner.Start(high,"ordered-job")); runner.Tick(0.1f);
        Assert.Equal(1,port.WalkCalls); Assert.Equal(WorkerRoutePhase.Walking,runner.Phase);
    }
}
