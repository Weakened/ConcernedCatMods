using System;
using System.Collections.Generic;
using System.Linq;
namespace TheConcernedCat.Workers.Traversal;
/// <summary>Supported bottom -> mount lane -> vertical lane -> supported top.
/// Immutable, bounded and traversed by completed PATH distance, not diagonal Y.</summary>
internal sealed class WorkerLadderPassage
{
    private readonly float[] _up=new float[3],_down=new float[3];
    public WorkerLadderPassage(WorkPoint bottom,WorkPoint mount,WorkPoint crest,WorkPoint top)
    {
        var points=new[]{bottom,mount,crest,top};
        if(points.Any(p=>!WorkerLadderCapsule.Finite(p.X)||!WorkerLadderCapsule.Finite(p.Y)||!WorkerLadderCapsule.Finite(p.Z)))
            throw new ArgumentException("Measured passage points must be finite.");
        Points=Array.AsReadOnly(points); Length=Distance(bottom,mount)+Distance(mount,crest)+Distance(crest,top);
        for(int i=1;i<points.Length;i++)
        { _up[i-1]=(i==1?0:_up[i-2])+Distance(points[i-1],points[i]);
          _down[i-1]=(i==1?0:_down[i-2])+Distance(points[points.Length-i],points[points.Length-i-1]); }
    }
    public IReadOnlyList<WorkPoint> Points { get; }
    public float Length { get; }
    internal bool Fits(WorkerLadderRunData run,WorkerLadderFace face,WorkerLadderCapsule capsule)
    {
        var bottom=run.Lane(false,face,capsule); var top=run.Lane(true,face,capsule);
        return Length>0&&Length<=run.Top.Y-run.Bottom.Y+6 &&
            Points[0].HorizontalDistanceTo(bottom)<=.05f&&Math.Abs(Points[0].Y-bottom.Y)<=.35f&&
            Points[1].HorizontalDistanceTo(bottom)<=.05f&&Math.Abs(Points[1].Y-Math.Max(bottom.Y,Points[0].Y))<=.001f&&
            Points[2].HorizontalDistanceTo(top)<=.05f&&Math.Abs(Points[2].Y-Math.Max(top.Y,Points[3].Y))<=.001f&&
            Points[3].HorizontalDistanceTo(run.Top)<=2.5f&&Math.Abs(Points[3].Y-run.Top.Y)<=.35f;
    }
    internal bool SameAs(WorkerLadderPassage other)=>Points.SequenceEqual(other.Points);
    internal float ClipStep(float current,float desired,TraversalDirection direction)
    {
        var boundaries=direction==TraversalDirection.Down?_down:_up;
        if(desired>current)
        { foreach(float edge in boundaries) if(edge>current+.00001f) return Math.Min(desired,edge); }
        else if(desired<current)
        { float previous=0; foreach(float edge in boundaries) if(edge<current-.00001f) previous=edge; return Math.Max(desired,previous); }
        return desired;
    }
    internal WorkPoint At(float metres,TraversalDirection direction)
    {
        float remaining=Math.Max(0,Math.Min(Length,direction==TraversalDirection.Down?Length-metres:metres));
        for(int i=1;i<Points.Count;i++)
        {
            float length=Distance(Points[i-1],Points[i]);
            if(remaining<=length&&length>.00001f)
            {
                float t=remaining/length; var a=Points[i-1]; var b=Points[i];
                return new WorkPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
            }
            remaining-=length;
        }
        return Points[Points.Count-1];
    }
    private static float Distance(WorkPoint a,WorkPoint b)
    { float x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return (float)Math.Sqrt(x*x+y*y+z*z); }
}
/// <summary>The ordinary seam supplies grants/handles/restoration; this worker-
/// only wrapper supplies the actual checked mount/climb/exit track. No engine
/// body is owned here, and a player can never enter this implementation.</summary>
internal sealed class WorkerLadderPathTraversal : ILadderTraversal
{
    private readonly LinkTraversal _gate;
    private readonly IWorkerLadderPathSource _paths;
    private readonly float _speed;
    private sealed class Flight
    {
        internal Flight(WorkerLadderPassage path) { Path=path; }
        internal WorkerLadderPassage Path; internal float Metres;
    }
    private readonly Dictionary<TraversalHandle,Flight> _active=new();
    public WorkerLadderPathTraversal(LinkTraversal gate,IWorkerLadderPathSource paths,float speed)
    {
        if(!WorkerLadderCapsule.Finite(speed)||speed<=0||speed>10) throw new ArgumentException("Finite worker path speed required.");
        _gate=gate??throw new ArgumentNullException(nameof(gate)); _paths=paths??throw new ArgumentNullException(nameof(paths)); _speed=speed;
    }
    public TraversalVerdict CanTraverse(in TraversalRequest request)
    {
        if(!request.Actor.IsWorker) return TraversalVerdict.Refuse(TraversalRefusal.NotCapable);
        if(request.LinkId==null||!_paths.TryPath(request.LinkId,out var path,out _)) return TraversalVerdict.Refuse(TraversalRefusal.LinkGone);
        var verdict=_gate.CanTraverse(request);
        if(verdict.Allowed&&(!WorkerLadderCapsule.Finite(path.Length)||path.Length<=0||
            !path.Points[0].Equals(verdict.Link.Bottom)||!path.Points[3].Equals(verdict.Link.Top)))
            return TraversalVerdict.Refuse(TraversalRefusal.LinkGone);
        return verdict;
    }
    public TraversalEntry Enter(in TraversalRequest request)
    {
        var verdict=CanTraverse(request); if(!verdict.Allowed) return TraversalEntry.Refused(verdict);
        var entered=_gate.Enter(request);
        if(!entered.Started) return entered;
        if(!_paths.TryPath(entered.Handle.LinkId!,out var path,out int generation)||generation!=entered.Handle.Generation)
        { _gate.Exit(entered.Handle,TraversalExitKind.Interrupted); return TraversalEntry.Refused(TraversalVerdict.Refuse(TraversalRefusal.LinkGone)); }
        _active[entered.Handle]=new Flight(path); return entered;
    }
    public TraversalProgress Traverse(in TraversalHandle handle,TraversalIntent intent,float deltaSeconds)
    {
        if(!_active.TryGetValue(handle,out var flight)) return TraversalProgress.Lost;
        var held=_gate.Traverse(handle,TraversalIntent.Hold,0);
        if(held.Phase==TraversalPhase.Lost||!_paths.TryPath(handle.LinkId!,out var path,out int generation)||
            generation!=handle.Generation||!path.SameAs(flight.Path))
        { _active.Remove(handle); _gate.Exit(handle,TraversalExitKind.Interrupted); return TraversalProgress.Lost; }
        float step=WorkerLadderCapsule.Finite(deltaSeconds)&&deltaSeconds>0?_speed*Math.Min(deltaSeconds,.05f):0;
        if(intent==TraversalIntent.Reverse) step=-step; else if(intent!=TraversalIntent.Advance) step=0;
        flight.Metres=path.ClipStep(flight.Metres,Math.Max(0,Math.Min(path.Length,flight.Metres+step)),handle.Direction);
        if(step>0&&path.Length-flight.Metres<=.001f) flight.Metres=path.Length;
        if(step<0&&flight.Metres<=.001f) flight.Metres=0;
        var phase=step==0?TraversalPhase.Holding:step>0?(flight.Metres>=path.Length-.001f?TraversalPhase.ReachedFarEnd:TraversalPhase.Advancing):
            (flight.Metres<=.001f?TraversalPhase.ReturnedToStart:TraversalPhase.Reversing);
        return new TraversalProgress(phase,flight.Metres/path.Length,flight.Metres,path.At(flight.Metres,handle.Direction));
    }
    public TraversalRelease Exit(in TraversalHandle handle,TraversalExitKind kind)
    {
        if(!_active.TryGetValue(handle,out var flight)) { _gate.Exit(handle,TraversalExitKind.Interrupted); return TraversalRelease.Release(TraversalExitKind.Interrupted); }
        _active.Remove(handle);
        if(kind==TraversalExitKind.Arrived&&flight.Metres<flight.Path.Length-.001f||kind==TraversalExitKind.TurnedBack&&flight.Metres>.001f)
            kind=TraversalExitKind.Interrupted;
        return _gate.Exit(handle,kind);
    }
}
