using System;
using System.Reflection;
namespace TheConcernedCat.Workers.Traversal;
/// <summary>Versioned optional runtime capability. Invokes only its exact BCL
/// signature; unknown/false/throwing results never reuse the last good snapshot.</summary>
internal sealed class ReflectedWorkerLadderSource : IWorkerLadderPublicationSource
{
    private readonly Func<Type?> _find;
    private readonly Action<string> _log;
    private string _reported=string.Empty;
    public ReflectedWorkerLadderSource(Func<Type?> find,Action<string> log) { _find=find; _log=log; }
    public bool TryRead(out WorkerLadderPublication publication)
    {
        publication=null!;
        try
        {
            Type? type=_find(); if(type==null) return Refuse("Concerned Ladders worker-link capability is not installed.");
            const BindingFlags flags=BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly;
            var schema=type.GetField("Schema",flags); var stride=type.GetField("SectionStride",flags);
            if(schema==null||stride==null||!schema.IsLiteral||!stride.IsLiteral||!Equals(schema.GetRawConstantValue(),1)||!Equals(stride.GetRawConstantValue(),12))
                return Refuse("Concerned Ladders worker-link schema is unsupported; check compatible mod versions.");
            var method=type.GetMethod("TryRead",flags,null,new[]{typeof(int),typeof(string).MakeByRefType(),typeof(long).MakeByRefType(),
                typeof(string[]).MakeByRefType(),typeof(float[][]).MakeByRefType()},null);
            if(method==null||method.ReturnType!=typeof(bool)||method.ContainsGenericParameters||!OutParameters(method))
                return Refuse("Concerned Ladders worker-link method is incompatible; check installed mod versions.");
            object?[] args={1,null,0L,null,null};
            if(!(method.Invoke(null,args) is bool ready)||!ready)
                return Refuse("Ladder measurements are unavailable; withdraw worker links until the loaded world can be revalidated.");
            if(!(args[1] is string epoch)||!(args[2] is long generation)||
                !WorkerLadderPublication.TryRead(epoch,generation,args[3] as string[],args[4] as float[][],out publication))
                return Refuse("Malformed ladder measurements refused; check the installed provider version.");
            _reported=string.Empty; return true;
        }
        catch(Exception exception)
        {
            publication=null!;
            string name=exception is TargetInvocationException invocation&&invocation.InnerException!=null
                ?invocation.InnerException.GetType().Name:exception.GetType().Name;
            return Refuse("Ladder measurement API failed ("+name+"); worker links were withdrawn. Check installed game/mod versions.");
        }
    }
    private static bool OutParameters(MethodInfo method)
    { var parameters=method.GetParameters(); for(int i=1;i<parameters.Length;i++) if(!parameters[i].IsOut) return false; return true; }
    private bool Refuse(string reason)
    {
        if(reason!=_reported) { _reported=reason; try { _log(reason); } catch { /* A broken logger must not abort all AI. */ } }
        return false;
    }
}
/// <summary>Bounded receiver/probe scheduling, independent of the player motor.
/// Measurement is not movement authority. Native order binding must force a
/// fresh survey at admission and retain all route/body/authority gates.</summary>
internal sealed class WorkerLadderLinkFeed
{
    private readonly IWorkerLadderPublicationSource _source;
    private readonly IWorkerLadderSurveyProbe _probe;
    private readonly WorkerLadderLinkSurvey _survey;
    private float _elapsed=2;
    public WorkerLadderLinkFeed(IWorkerLadderPublicationSource source,IWorkerLadderSurveyProbe probe,WorkerLadderLinkSurvey survey)
    { _source=source; _probe=probe; _survey=survey; }
    public WorkerLadderSurveyResult State { get; private set; }=WorkerLadderSurveyResult.Disabled;
    public WorkerLadderSurveyResult Tick(float deltaSeconds,bool enabled,WorkPoint centre,float radius,WorkerLadderCapsule capsule)
    {
        if(!enabled) { Forget(); return State=WorkerLadderSurveyResult.Disabled; }
        if(!WorkerLadderCapsule.Finite(deltaSeconds)||deltaSeconds<0)
        { Forget(); return State=WorkerLadderSurveyResult.Malformed; }
        _elapsed+=Math.Min(deltaSeconds,2);
        if(_elapsed<2) return State;
        return RefreshNow(enabled,centre,radius,capsule);
    }
    public WorkerLadderSurveyResult RefreshNow(bool enabled,WorkPoint centre,float radius,WorkerLadderCapsule capsule)
    {
        _elapsed=0;
        if(!enabled) { Forget(); return State=WorkerLadderSurveyResult.Disabled; }
        try
        {
            if(!_source.TryRead(out var publication)) { _survey.Forget(); return State=WorkerLadderSurveyResult.Unavailable; }
            _probe.BeginSurvey();
            return State=_survey.Refresh(publication,centre,radius,capsule,_probe,true);
        }
        catch { _survey.Forget(); return State=WorkerLadderSurveyResult.Unavailable; }
    }
    public void Forget() { _survey.Forget(); _elapsed=2; State=WorkerLadderSurveyResult.Unavailable; }
}
