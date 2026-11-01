using System;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;
/// <summary>Assembled optional worker receiver, suitable for the ordered-job
/// integration. No movement is installed here. The active runtime must bind its
/// own actor hold/scoped motor and run the identity/authority checks every tick.</summary>
internal sealed class WorkerLadderReceiver
{
    private readonly WorkerLadderLandingProbe _probe;
    private readonly WorkerLadderLinkFeed _feed;
    private readonly TraversalCapabilities _capabilities;
    private readonly Action<string> _log;
    private WorkerLadderSurveyResult? _last;
    internal WorkerLadderReceiver(ForemanWorkerAI worker,TraversalCapabilities capabilities,
        Func<WorkPoint,bool> siteAllowed,Action<string> log,TraversalLimits? limits=null)
    {
        _log=log??throw new ArgumentNullException(nameof(log)); _capabilities=capabilities??throw new ArgumentNullException(nameof(capabilities));
        limits=(limits??TraversalLimits.Default).Validate();
        limits=new TraversalLimits { ApproachRadiusMetres=limits.ApproachRadiusMetres,MinHeightMetres=limits.MinHeightMetres,
            MaxWorkerHeightMetres=limits.MaxWorkerHeightMetres,TraversalSpeedMetresPerSecond=limits.TraversalSpeedMetresPerSecond,
            WalkSpeedMetresPerSecond=limits.WalkSpeedMetresPerSecond,LinkOverheadMetres=limits.LinkOverheadMetres,MaxSearchMetres=limits.MaxSearchMetres }.Validate();
        Network=new TraversalLinkNetwork();
        var survey=new WorkerLadderLinkSurvey(Network,limits);
        _probe=new WorkerLadderLandingProbe(worker,siteAllowed,log);
        _feed=new WorkerLadderLinkFeed(new ConcernedLaddersWorkerSource(log),_probe,survey);
        Traversal=new WorkerLadderPathTraversal(new LinkTraversal(Network,capabilities,limits),survey,limits.TraversalSpeedMetresPerSecond);
    }
    internal TraversalLinkNetwork Network { get; }
    internal ILadderTraversal Traversal { get; }
    internal WorkerLadderSurveyResult Tick(float dt,bool npcClimbing,bool explicitOrder,WorkAuthorityVerdict authority,WorkPoint settlement,float radius)
    {
        bool allowed=npcClimbing&&explicitOrder&&authority==WorkAuthorityVerdict.Granted&&_capabilities.NpcClimbingEnabled;
        WorkerLadderSurveyResult result;
        if(!allowed) result=_feed.Tick(dt,false,settlement,radius,default);
        else if(!_probe.TryBodyShape(out var body)) { _feed.Forget(); result=WorkerLadderSurveyResult.Unavailable; }
        else result=_feed.Tick(dt,true,settlement,radius,body);
        if(result!=_last)
        {
            _last=result;
            if(result!=WorkerLadderSurveyResult.Ready&&result!=WorkerLadderSurveyResult.Disabled)
                _log("Worker ladder link survey: "+result+". No movement permission is supplied; revalidate the loaded settlement, compatible ladder provider and upright worker capsule.");
        }
        return result;
    }
    internal bool RevalidateForAdmission(bool npcClimbing,bool explicitOrder,WorkAuthorityVerdict authority,WorkPoint settlement,float radius)
    {
        if(!npcClimbing||!explicitOrder||authority!=WorkAuthorityVerdict.Granted||!_capabilities.NpcClimbingEnabled||!_probe.TryBodyShape(out var body))
        { _feed.Forget(); return false; }
        return _feed.RefreshNow(true,settlement,radius,body)==WorkerLadderSurveyResult.Ready;
    }
    internal void Forget()=>_feed.Forget();
}
