using System;
namespace TheConcernedCat.Workers.Traversal;
internal enum WorkerRoutePhase { Idle, Walking, Climbing, Restoring, Complete, Cancelled, Refused }
internal enum WorkerRouteStop { None, Authority, ActorBusy, IdentityLost, LinkChanged, WalkBlocked,
    TraversalRefused, UnsafeLanding, MotorFailure, Cancelled, Fault }
internal interface IWorkerRouteHold : IDisposable { bool IsActive { get; } }
/// <summary>A lease captures ONE worker body and its original motor state.
/// It cannot follow a mutable current-body pointer to a new body. Restore is
/// idempotent; false means still not restored, so identity may not be released.</summary>
internal interface IWorkerLadderMotor
{
    WorkerKey Worker { get; }
    Guid BodyEpoch { get; }
    bool GroundMotorSuspended { get; }
    bool TryPlaceOwnBody(WorkPoint position);
    bool TryRestore();
}
internal interface IWorkerRoutePort
{
    WorkerKey Worker { get; }
    Guid BodyEpoch { get; }
    WorkPoint Position { get; }
    bool FeatureEnabled { get; }
    bool ExplicitOrderActive { get; }
    bool ActorReady { get; }
    WorkAuthorityVerdict Authority { get; }
    // A borrowed job hold must not dispose the surrounding job's identity.
    IWorkerRouteHold? TryHoldRoute(string orderId);
    bool WalkToward(WorkPoint target,float deltaSeconds);
    void StopWalkingForRoute();
    IWorkerLadderMotor? TrySuspendGroundMotor(WorkerKey expectedWorker,Guid expectedBodyEpoch);
    bool IsSupportedLanding(WorkPoint target);
}
/// <summary>Walk -> traverse -> walk with exclusive motor ownership and
/// per-leg failure. This owns no engine object, takes no player input and cannot
/// start a job. The native scoped port/audit remains required before use.</summary>
internal sealed class WorkerRouteRunner
{
    private readonly TraversalLinkNetwork _network;
    private readonly ILadderTraversal _traversal;
    private readonly IWorkerRoutePort _port;
    private readonly WorkerKey _worker;
    private readonly float _horizontalTolerance,_heightTolerance;
    private WorkerRoutePlan? _plan;
    private IWorkerRouteHold? _hold;
    private IWorkerLadderMotor? _motor;
    private Guid _bodyEpoch;
    private TraversalHandle _handle;
    private WorkerRoutePhase _terminal;
    private bool _continueAfterRestore;
    // #501 review: a walk leg that makes no progress ends instead of holding the
    // route and the identity forever (a wedged body, an unloaded zone). Progress
    // is the best distance to the leg's end improving by at least the step.
    private readonly float _walkStallSeconds;
    private const float WalkProgressMetres=0.25f;
    private float _walkBest=float.PositiveInfinity,_walkStalled;
    public WorkerRouteRunner(TraversalLinkNetwork network,ILadderTraversal traversal,IWorkerRoutePort port,
        WorkerKey worker,float horizontalTolerance=1f,float heightTolerance=0.35f,float walkStallSeconds=10f)
    {
        if(!(walkStallSeconds>0)||float.IsInfinity(walkStallSeconds)) throw new ArgumentException("A finite stall budget is required.");
        _walkStallSeconds=walkStallSeconds;
        if(worker.IsEmpty||!(horizontalTolerance>0)||float.IsInfinity(horizontalTolerance)||
            !(heightTolerance>0)||float.IsInfinity(heightTolerance)) throw new ArgumentException("A worker and finite arrival bounds are required.");
        _network=network; _traversal=traversal; _port=port; _worker=worker;
        _horizontalTolerance=horizontalTolerance; _heightTolerance=heightTolerance;
    }
    public WorkerRoutePhase Phase { get; private set; }
    public WorkerRouteStop StopReason { get; private set; }
    public int LegIndex { get; private set; }
    public bool ControlsRoute => Phase==WorkerRoutePhase.Walking||Phase==WorkerRoutePhase.Climbing||Phase==WorkerRoutePhase.Restoring;
    public bool Start(WorkerRoutePlan plan,string orderId)
    {
        if(ControlsRoute||plan==null||plan.Legs.Count==0||string.IsNullOrWhiteSpace(orderId)) return false;
        if(!_port.Worker.Equals(_worker)||_port.BodyEpoch==Guid.Empty||!Allowed())
        { Phase=WorkerRoutePhase.Refused; StopReason=WorkerRouteStop.Authority; return false; }
        if(!_port.ActorReady) { Phase=WorkerRoutePhase.Refused; StopReason=WorkerRouteStop.ActorBusy; return false; }
        if(plan.Legs.Count>128) return false;
        foreach(WorkerRouteLeg leg in plan.Legs)
            if(!leg.From.IsFinite||!leg.To.IsFinite||(leg.Kind!=WorkerRouteLegKind.Walk&&leg.Kind!=WorkerRouteLegKind.Climb)) return false;
        _hold=_port.TryHoldRoute(orderId);
        if(_hold==null||!_hold.IsActive) { _hold?.Dispose(); _hold=null; Phase=WorkerRoutePhase.Refused; StopReason=WorkerRouteStop.IdentityLost; return false; }
        _plan=plan; _bodyEpoch=_port.BodyEpoch; LegIndex=0; StopReason=WorkerRouteStop.None; Phase=WorkerRoutePhase.Walking;
        ResetWalkWatch();
        return true;
    }
    public void Tick(float deltaSeconds)
    {
        if(Phase==WorkerRoutePhase.Restoring) { Clean(); return; }
        if(!ControlsRoute) return;
        try
        {
            if(!Allowed()) { End(WorkerRoutePhase.Refused,WorkerRouteStop.Authority); return; }
            if(!_port.ActorReady) { End(WorkerRoutePhase.Refused,WorkerRouteStop.ActorBusy); return; }
            if(_hold==null||!_hold.IsActive||!_port.Worker.Equals(_worker)||_port.BodyEpoch!=_bodyEpoch)
            { End(WorkerRoutePhase.Refused,WorkerRouteStop.IdentityLost); return; }
            if(!LinksCurrent()) { End(WorkerRoutePhase.Refused,WorkerRouteStop.LinkChanged); return; }
            if(!(deltaSeconds>0)||float.IsInfinity(deltaSeconds)) return;
            WorkerRouteLeg leg=_plan!.Legs[LegIndex];
            if(leg.Kind==WorkerRouteLegKind.Walk)
            {
                Phase=WorkerRoutePhase.Walking;
                if(leg.HasArrived(_port.Position,_horizontalTolerance,_heightTolerance)) { Advance(); return; }
                if(Stalled(leg,deltaSeconds)) { End(WorkerRoutePhase.Refused,WorkerRouteStop.WalkBlocked); return; }
                if(!_port.WalkToward(leg.To,deltaSeconds)) End(WorkerRoutePhase.Refused,WorkerRouteStop.WalkBlocked);
                return;
            }
            if(!_handle.IsValid)
            {
                _port.StopWalkingForRoute();
                var request=new TraversalRequest(TraversalActor.OfWorker(_worker,TraversalCapability.Ladders),leg.LinkId,
                    _port.Position,_port.FeatureEnabled,_port.Authority,_port.ActorReady,false,leg.Direction);
                if(!_traversal.CanTraverse(request).Allowed) { End(WorkerRoutePhase.Refused,WorkerRouteStop.TraversalRefused); return; }
                IWorkerLadderMotor? offered=_port.TrySuspendGroundMotor(_worker,_bodyEpoch);
                if(offered!=null&&offered.Worker.Equals(_worker)&&offered.BodyEpoch==_bodyEpoch) _motor=offered;
                if(!MotorOwned()) { End(WorkerRoutePhase.Refused,WorkerRouteStop.MotorFailure); return; }
                TraversalEntry entered=_traversal.Enter(request);
                if(!entered.Started) { End(WorkerRoutePhase.Refused,WorkerRouteStop.TraversalRefused); return; }
                _handle=entered.Handle; Phase=WorkerRoutePhase.Climbing;
            }
            if(!MotorOwned()) { End(WorkerRoutePhase.Refused,WorkerRouteStop.MotorFailure); return; }
            TraversalProgress progress=_traversal.Traverse(_handle,TraversalIntent.Advance,deltaSeconds);
            if(progress.Phase==TraversalPhase.Lost) { End(WorkerRoutePhase.Refused,WorkerRouteStop.LinkChanged); return; }
            if(progress.Phase==TraversalPhase.ReachedFarEnd)
            {
                if(!_port.IsSupportedLanding(leg.To)) { End(WorkerRoutePhase.Refused,WorkerRouteStop.UnsafeLanding); return; }
                if(!_network.IsCurrent(_handle)) { End(WorkerRoutePhase.Refused,WorkerRouteStop.LinkChanged); return; }
                TraversalRelease release=_traversal.Exit(_handle,TraversalExitKind.Arrived); _handle=default;
                if(!release.PlaceActor||!_motor!.TryPlaceOwnBody(release.Landing))
                { End(WorkerRoutePhase.Refused,WorkerRouteStop.MotorFailure); return; }
                _continueAfterRestore=true; Phase=WorkerRoutePhase.Restoring; Clean(); return;
            }
            if(!_motor!.TryPlaceOwnBody(progress.EstimatedPosition)) End(WorkerRoutePhase.Refused,WorkerRouteStop.MotorFailure);
        }
        catch { End(WorkerRoutePhase.Refused,WorkerRouteStop.Fault); }
    }
    private bool Allowed()=>_port.FeatureEnabled&&_port.ExplicitOrderActive&&_port.Authority==WorkAuthorityVerdict.Granted;
    private bool MotorOwned()=>_motor!=null&&_motor.Worker.Equals(_worker)&&_motor.BodyEpoch==_bodyEpoch&&_motor.GroundMotorSuspended;
    private bool LinksCurrent()
    {
        for(int i=LegIndex;i<_plan!.Legs.Count;i++)
        { WorkerRouteLeg leg=_plan.Legs[i]; if(leg.Kind==WorkerRouteLegKind.Climb&&
            (!_network.TryGet(leg.LinkId,out _,out int generation)||generation!=leg.Generation)) return false; }
        return true;
    }
    private bool Stalled(WorkerRouteLeg leg,float deltaSeconds)
    {
        WorkPoint at=_port.Position;
        float distance=at.HorizontalDistanceTo(leg.To)+at.VerticalDistanceTo(leg.To);
        if(distance<=_walkBest-WalkProgressMetres) { _walkBest=distance; _walkStalled=0; return false; }
        _walkStalled+=deltaSeconds;
        return _walkStalled>=_walkStallSeconds;
    }
    private void ResetWalkWatch() { _walkBest=float.PositiveInfinity; _walkStalled=0; }
    private void Advance()
    {
        ResetWalkWatch();
        LegIndex++;
        if(LegIndex==_plan!.Legs.Count) End(WorkerRoutePhase.Complete,WorkerRouteStop.None);
        else Phase=WorkerRoutePhase.Walking;
    }
    public void Cancel() { if(ControlsRoute) End(WorkerRoutePhase.Cancelled,WorkerRouteStop.Cancelled); }
    private void End(WorkerRoutePhase terminal,WorkerRouteStop reason)
    { _terminal=terminal; StopReason=reason; _continueAfterRestore=false; Phase=WorkerRoutePhase.Restoring; Clean(); }
    private void Clean()
    {
        try
        {
            if(_handle.IsValid) { _traversal.Exit(_handle,TraversalExitKind.Interrupted); _handle=default; }
            _port.StopWalkingForRoute();
            if(_motor!=null&&!_motor.TryRestore()) return;
            _motor=null;
            if(_continueAfterRestore) { _continueAfterRestore=false; Advance(); return; }
            _hold?.Dispose(); _hold=null; Phase=_terminal;
        }
        catch
        {
            _continueAfterRestore=false; _terminal=WorkerRoutePhase.Refused; StopReason=WorkerRouteStop.Fault;
            Phase=WorkerRoutePhase.Restoring; // retry cleanup; never release a still-suspended body's hold
        }
    }
}
