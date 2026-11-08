using System;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
using UnityEngine;
namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;
/// <summary>Read-only, bounded physics against the REAL collider world. Static
/// supports only; doors and rigidbodies still block clearance, never become a
/// guaranteed landing. A caller supplies current hazard/permission policy.
/// This cannot place a worker, alter a collider or invoke a player motor.</summary>
internal sealed class WorkerLadderLandingProbe : IWorkerLadderSurveyProbe,IWorkerLadderMountProbe
{
    private readonly Func<WorkPoint,bool> _siteAllowed;
    private readonly Action<string> _log;
    private readonly RaycastHit[] _hits=new RaycastHit[16];
    private readonly Collider[] _overlaps=new Collider[32];
    private readonly TheConcernedCat.ConcernedForeman.Runtime.Settlement.ForemanWorkerAI _worker;
    private readonly Humanoid _body;
    private readonly ZNetView _view;
    private readonly CapsuleCollider _capsule;
    private int _remaining,_mask;
    private bool _reported;
    internal WorkerLadderLandingProbe(TheConcernedCat.ConcernedForeman.Runtime.Settlement.ForemanWorkerAI worker,Func<WorkPoint,bool> siteAllowed,Action<string> log)
    {
        _worker=worker??throw new ArgumentNullException(nameof(worker));
        _body=worker.GetComponent<Humanoid>()??throw new ArgumentException("An actual worker humanoid is required.");
        _view=worker.GetComponent<ZNetView>()??throw new ArgumentException("The worker own network view is required.");
        _capsule=worker.GetComponent<CapsuleCollider>()??throw new ArgumentException("An upright worker capsule is required.");
        _siteAllowed=siteAllowed??throw new ArgumentNullException(nameof(siteAllowed)); _log=log;
    }
    internal bool TryBodyShape(out WorkerLadderCapsule shape)
    {
        shape=default;
        try
        {
            if(_worker==null||_body==null||_view==null||!_view.IsValid()||!_view.IsOwner()||_worker.GetComponent<ZNetView>()!=_view||_capsule==null||!_capsule.enabled||_capsule.direction!=1||_capsule.transform.up.y<.999f) return false;
            Bounds bounds=_capsule.bounds; var root=_worker.transform.position;
            if(!WorkerLadderCapsule.Finite(bounds.center.x)||!WorkerLadderCapsule.Finite(bounds.center.y)||!WorkerLadderCapsule.Finite(bounds.center.z)||
                !WorkerLadderCapsule.Finite(bounds.min.y)||!WorkerLadderCapsule.Finite(root.x)||!WorkerLadderCapsule.Finite(root.y)||
                !WorkerLadderCapsule.Finite(root.z)||!WorkerLadderCapsule.Finite(_capsule.transform.up.y)) return false;
            if(Math.Abs(bounds.center.x-root.x)>.03f||Math.Abs(bounds.center.z-root.z)>.03f||
                Math.Abs(bounds.min.y-root.y)>.03f||Math.Abs(bounds.size.x-bounds.size.z)>.01f) return false;
            shape=new WorkerLadderCapsule(Math.Max(bounds.size.x,bounds.size.z)*.5f,bounds.size.y); return true;
        }
        catch { return false; }
    }
    public bool CanMount(WorkPoint actualStanding,WorkPoint checkedEntry)
    {
        try
        {
            if(!TryBodyShape(out var body)) return false;
            var actual=_worker.transform.position;
            float x=actual.x-actualStanding.X,y=actual.y-actualStanding.Y,z=actual.z-actualStanding.Z;
            if(x*x+y*y+z*z>.0009f||actualStanding.HorizontalDistanceTo(checkedEntry)>1.5f||Math.Abs(actualStanding.Y-checkedEntry.Y)>.35f) return false;
            BeginSurvey();
            var supported=Support(actualStanding,body,out var floor);
            return supported==WorkerLadderProbeResult.Supported&&Math.Abs(floor.Y-actualStanding.Y)<=.08f&&
                Clear(actualStanding,checkedEntry,body)==WorkerLadderProbeResult.Supported;
        }
        catch(Exception exception)
        { try { _log("Worker ladder mount refused: "+SafeFailure.Brief(exception)); } catch { } return false; }
    }
    /// <summary>#334: one motor step's capsule sweep, from where the body is to
    /// where the checked path would put it next, with a budget of its own. A
    /// checked publication is not permission to ignore a blocker that moved in
    /// since: anything other than a clear sweep is a refusal.</summary>
    internal bool IsMoveClear(WorkPoint from,WorkPoint to)
    {
        try
        {
            if(!TryBodyShape(out var body)) return false;
            BeginSurvey();
            return Clear(from,to,body)==WorkerLadderProbeResult.Supported;
        }
        catch { return false; }
    }
    /// <summary>#334: the landing is still a supported static floor with room
    /// for this body, at the height the path ends at.</summary>
    internal bool IsLandingSupported(WorkPoint target)
    {
        try
        {
            if(!TryBodyShape(out var body)) return false;
            BeginSurvey();
            return Support(target,body,out var foot)==WorkerLadderProbeResult.Supported&&
                foot.HorizontalDistanceTo(target)<=.05f&&Math.Abs(foot.Y-target.Y)<=.1f;
        }
        catch { return false; }
    }
    public int PhysicsQueries { get; private set; }
    public void BeginSurvey()
    {
        _remaining=256; PhysicsQueries=0;
        _mask=LayerMask.GetMask("piece","Default","static_solid","Default_small","terrain","blocker","vehicle","character","character_net","character_ghost");
    }
    public WorkerLadderProbeResult TryMeasure(WorkerLadderRunData run,WorkerLadderFace face,WorkerLadderCapsule capsule,out WorkerLadderPassage passage)
    {
        passage=null!;
        try
        {
            if(_mask==0||_worker==null||_body==null||_view==null||!_view.IsValid()||!_view.IsOwner()||_worker.GetComponent<ZNetView>()!=_view||!capsule.IsMeasured||_remaining<=0||ZoneSystem.instance==null) return WorkerLadderProbeResult.Unavailable;
            if(!TryBodyShape(out var measured)||Math.Abs(measured.Radius-capsule.Radius)>.001f||Math.Abs(measured.Height-capsule.Height)>.001f) return WorkerLadderProbeResult.Unavailable;
            var bottomLane=run.Lane(false,face,capsule); var topLane=run.Lane(true,face,capsule);
            var bottomResult=Support(bottomLane,capsule,out var bottom);
            if(bottomResult!=WorkerLadderProbeResult.Supported) return bottomResult;
            WorkPoint mount=new(bottomLane.X,Math.Max(bottomLane.Y,bottom.Y),bottomLane.Z);
            bool unknown=false;
            // Front, sides, rear: real support decides; no native door/ladder
            // root or the rung itself is accepted as a guessed top floor.
            float sign=face==WorkerLadderFace.Front?1:-1;
            for(int index=0;index<8;index++)
            {
                double angle=index*Math.PI/4;
                float dx=(float)(run.X*sign*Math.Cos(angle)-run.Z*sign*Math.Sin(angle));
                float dz=(float)(run.Z*sign*Math.Cos(angle)+run.X*sign*Math.Sin(angle));
                var expected=new WorkPoint(run.Top.X+dx*(capsule.Radius+.65f),run.Top.Y,run.Top.Z+dz*(capsule.Radius+.65f));
                var result=Support(expected,capsule,out var top);
                if(result==WorkerLadderProbeResult.Unavailable) { unknown=true; if(_remaining<=0) return result; continue; }
                if(result!=WorkerLadderProbeResult.Supported) continue;
                var crest=new WorkPoint(topLane.X,Math.Max(topLane.Y,top.Y),topLane.Z);
                var path=new WorkerLadderPassage(bottom,mount,crest,top);
                bool clear=true;
                for(int i=1;i<path.Points.Count;i++)
                {
                    result=Clear(path.Points[i-1],path.Points[i],capsule);
                    if(result==WorkerLadderProbeResult.Unavailable) { unknown=true; clear=false; break; }
                    if(result!=WorkerLadderProbeResult.Supported) { clear=false; break; }
                }
                if(clear) { passage=path; _reported=false; return WorkerLadderProbeResult.Supported; }
                if(_remaining<=0) return WorkerLadderProbeResult.Unavailable;
            }
            return unknown?WorkerLadderProbeResult.Unavailable:WorkerLadderProbeResult.Blocked;
        }
        catch(Exception exception)
        {
            if(!_reported) { _reported=true; try { _log("Worker ladder landing checks failed ("+SafeFailure.Brief(exception)+"); no links published. Check game APIs and loaded ground."); } catch { } }
            return WorkerLadderProbeResult.Unavailable;
        }
    }
    private WorkerLadderProbeResult Support(WorkPoint expected,WorkerLadderCapsule capsule,out WorkPoint foot)
    {
        foot=default;
        if(!Loaded(expected,capsule.Radius)||_remaining<=0) return WorkerLadderProbeResult.Unavailable;
        var sampled=Sample(expected,out var centre);
        if(sampled!=WorkerLadderProbeResult.Supported) return sampled;
        foot=centre;
        // A thin rung/rail is not a floor: require support under the capsule
        // footprint, not just one fortunate ray down its centre.
        foreach(var offset in new[]{new Vector3(capsule.Radius,0,0),new Vector3(-capsule.Radius,0,0),
            new Vector3(0,0,capsule.Radius),new Vector3(0,0,-capsule.Radius)})
        {
            var at=new WorkPoint(expected.X+offset.x,expected.Y,expected.Z+offset.z);
            sampled=Sample(at,out var edge);
            if(sampled!=WorkerLadderProbeResult.Supported) return sampled;
            if(Math.Abs(edge.Y-centre.Y)>.15f) return WorkerLadderProbeResult.Blocked;
        }
        if(!Loaded(foot,capsule.Radius)||!_siteAllowed(foot)) return WorkerLadderProbeResult.Unavailable;
        if(_remaining<=0) return WorkerLadderProbeResult.Unavailable;
        Spend(); Capsule(foot,capsule,out var low,out var high);
        int overlaps=Physics.OverlapCapsuleNonAlloc(low,high,capsule.Radius,_overlaps,_mask,QueryTriggerInteraction.Ignore);
        if(overlaps>=_overlaps.Length) { Array.Clear(_overlaps,0,_overlaps.Length); return WorkerLadderProbeResult.Unavailable; }
        bool blocked=false;
        for(int i=0;i<overlaps;i++) if(_overlaps[i]!=null&&!Own(_overlaps[i])) blocked=true;
        Array.Clear(_overlaps,0,_overlaps.Length);
        return blocked?WorkerLadderProbeResult.Blocked:WorkerLadderProbeResult.Supported;
    }
    private WorkerLadderProbeResult Sample(WorkPoint expected,out WorkPoint foot)
    {
        foot=default; if(_remaining<=0) return WorkerLadderProbeResult.Unavailable;
        Spend(); int count=Physics.RaycastNonAlloc(V(expected,.4f),new Vector3(0,-1,0),_hits,.8f,_mask,QueryTriggerInteraction.Ignore);
        if(count>=_hits.Length) { Array.Clear(_hits,0,_hits.Length); return WorkerLadderProbeResult.Unavailable; }
        RaycastHit? closest=null;
        for(int i=0;i<count;i++)
        {
            var hit=_hits[i]; if(hit.collider==null||Own(hit.collider)) continue;
            if(!WorkerLadderCapsule.Finite(hit.distance)||hit.distance<0||!WorkerLadderCapsule.Finite(hit.point.x)||
                !WorkerLadderCapsule.Finite(hit.point.y)||!WorkerLadderCapsule.Finite(hit.point.z)||!WorkerLadderCapsule.Finite(hit.normal.y))
            { Array.Clear(_hits,0,_hits.Length); return WorkerLadderProbeResult.Unavailable; }
            if(!closest.HasValue||hit.distance<closest.Value.distance) closest=hit;
        }
        Array.Clear(_hits,0,_hits.Length);
        if(!closest.HasValue) return WorkerLadderProbeResult.Blocked;
        var nearest=closest.Value;
        if(nearest.collider.attachedRigidbody!=null||nearest.collider.GetComponentInParent<Character>()!=null||nearest.collider.GetComponentInParent<Door>()!=null||nearest.normal.y<.75f||
            Math.Abs(nearest.point.y-expected.Y)>.35f) return WorkerLadderProbeResult.Blocked;
        foot=new WorkPoint(nearest.point.x,nearest.point.y,nearest.point.z); return WorkerLadderProbeResult.Supported;
    }
    private WorkerLadderProbeResult Clear(WorkPoint from,WorkPoint to,WorkerLadderCapsule capsule)
    {
        for(int i=0;i<=8;i++)
        {
            float t=i/8f; var point=new WorkPoint(from.X+(to.X-from.X)*t,from.Y+(to.Y-from.Y)*t,from.Z+(to.Z-from.Z)*t);
            if(!Loaded(point,capsule.Radius)||!_siteAllowed(point)) return WorkerLadderProbeResult.Unavailable;
        }
        var delta=V(to)-V(from); float distance=delta.magnitude;
        if(distance<.0001f) return WorkerLadderProbeResult.Supported;
        if(_remaining<=0) return WorkerLadderProbeResult.Unavailable;
        Capsule(from,capsule,out var low,out var high); Spend();
        var direction=new Vector3(delta.x/distance,delta.y/distance,delta.z/distance);
        int count=Physics.CapsuleCastNonAlloc(low,high,capsule.Radius,direction,_hits,distance,_mask,QueryTriggerInteraction.Ignore);
        if(count>=_hits.Length) { Array.Clear(_hits,0,_hits.Length); return WorkerLadderProbeResult.Unavailable; }
        bool blocked=false;
        for(int i=0;i<count;i++) if(_hits[i].collider!=null&&!Own(_hits[i].collider)) blocked=true;
        Array.Clear(_hits,0,_hits.Length);
        return blocked?WorkerLadderProbeResult.Blocked:WorkerLadderProbeResult.Supported;
    }
    private bool Own(Collider collider)=>collider.GetComponentInParent<TheConcernedCat.ConcernedForeman.Runtime.Settlement.ForemanWorkerAI>()==_worker&&
        collider.GetComponentInParent<Character>()==_body&&collider.GetComponentInParent<ZNetView>()==_view;
    private bool Loaded(WorkPoint point,float radius)
    {
        var zones=ZoneSystem.instance; if(zones==null) return false;
        for(int x=-1;x<=1;x++) for(int z=-1;z<=1;z++)
            if(!zones.IsZoneLoaded(new Vector3(point.X+x*radius,point.Y,point.Z+z*radius))) return false;
        return true;
    }
    private void Spend() { _remaining--; PhysicsQueries++; }
    private static Vector3 V(WorkPoint point,float y=0)=>new(point.X,point.Y+y,point.Z);
    private static void Capsule(WorkPoint foot,WorkerLadderCapsule shape,out Vector3 low,out Vector3 high)
    { low=V(foot,shape.Radius+.03f); high=V(foot,shape.Height-shape.Radius+.03f); }
}
