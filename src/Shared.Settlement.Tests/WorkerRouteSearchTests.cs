using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
namespace Shared.Settlement.Tests;
public sealed class WorkerRouteSearchTests
{
    private sealed class Floors : IWorkerWalkProbe
    {
        public int Calls; public bool Pending;
        public WorkerWalkEvidence Check(WorkPoint from,WorkPoint to)
        {
            Calls++;
            if(Pending) return new WorkerWalkEvidence(WorkerWalkEvidenceKind.Unknown);
            return Math.Abs(from.Y-to.Y)<0.1f
                ? new WorkerWalkEvidence(WorkerWalkEvidenceKind.Reachable,WorkerRouteSearch.Distance(from,to))
                : new WorkerWalkEvidence(WorkerWalkEvidenceKind.Blocked);
        }
    }
    private sealed class Stairs : IWorkerWalkProbe
    {
        public WorkerWalkEvidence Check(WorkPoint from,WorkPoint to)=>
            new(WorkerWalkEvidenceKind.Reachable,WorkerRouteSearch.Distance(from,to)+10);
    }
    private static TraversalLink Link(string id,float x,float y,float height=4)
    {
        Assert.True(TraversalLink.TryCreate(id,TraversalLinkKind.Ladder,new WorkPoint(x,y,0),
            new WorkPoint(x+0.6f,y+height,0),TraversalCapability.Ladders,TraversalLimits.Default,out var link));
        return link;
    }
    private static (TraversalLinkNetwork,TraversalCapabilities) Network()
    {
        var network=new TraversalLinkNetwork(); network.Publish(Link("first",0,0)); network.Publish(Link("second",3,4));
        var caps=new TraversalCapabilities(true); caps.Grant(WorkerKey.Thorstein,TraversalCapability.Ladders);
        return (network,caps);
    }
    private static WorkerRouteSearch Search(TraversalLinkNetwork network,TraversalCapabilities caps,WorkerRouteBudget? budget=null) =>
        new(network,caps,TraversalActor.OfWorker(WorkerKey.Thorstein,TraversalCapability.Ladders),
            new WorkPoint(-2,0,0),new WorkPoint(5,8,0),budget);
    private static void Finish(WorkerRouteSearch search,IWorkerWalkProbe probe)
    {
        for(int tick=0;tick<1000 && search.State!=WorkerRouteSearchState.Complete && search.State!=WorkerRouteSearchState.Refused;tick++)
        {
            int before=search.Queries;
            search.Advance(probe,WorkAuthorityVerdict.Granted,true,true);
            Assert.InRange(search.Queries-before,0,1);
        }
    }
    [Fact] public void TwoLaddersProduceWalkClimbWalkClimbWalkWithoutHeightBlindShortcuts()
    {
        var(network,caps)=Network(); var search=Search(network,caps); var probe=new Floors(); Finish(search,probe);
        Assert.Equal(WorkerRouteSearchState.Complete,search.State); Assert.NotNull(search.Plan);
        Assert.Equal(new[]{WorkerRouteLegKind.Walk,WorkerRouteLegKind.Climb,WorkerRouteLegKind.Walk,
            WorkerRouteLegKind.Climb,WorkerRouteLegKind.Walk},search.Plan!.Legs.Select(l=>l.Kind));
        Assert.Equal(new[]{"first","second"},search.Plan.Legs.Where(l=>l.Kind==WorkerRouteLegKind.Climb).Select(l=>l.LinkId));
        Assert.All(search.Plan.Legs.Where(l=>l.Kind==WorkerRouteLegKind.Climb),l=>Assert.True(l.Generation>0));
        Assert.Equal(probe.Calls,search.Queries);
    }
    [Fact] public void VerifiedStairsRemainAValidWalkWithoutChangingTheGroundPlanner()
    {
        var(network,caps)=Network(); var search=Search(network,caps); Finish(search,new Stairs());
        Assert.Equal(WorkerRouteSearchState.Complete,search.State);
        // A valid walk to another height is accepted from evidence; height alone never invents a climb requirement.
        Assert.NotEmpty(search.Plan!.Legs);
    }
    [Fact] public void WaitingForEvidenceDoesNotTurnUnknownIntoAWalkAndHonoursPerTickBudget()
    {
        var(network,caps)=Network(); var search=Search(network,caps); var probe=new Floors { Pending=true };
        Assert.Equal(WorkerRouteSearchState.AwaitingWalkEvidence,search.Advance(probe,WorkAuthorityVerdict.Granted,true,true));
        Assert.Equal(0,search.Queries); Assert.Equal(1,search.PendingAnswers); Assert.Null(search.Plan);
        Assert.Equal(WorkerRouteSearchState.Searching,search.Advance(probe,WorkAuthorityVerdict.Granted,true,true,0));
        Assert.Equal(0,search.Queries); Assert.Equal(1,probe.Calls); probe.Pending=false; Finish(search,probe);
        Assert.Equal(WorkerRouteSearchState.Complete,search.State);
    }
    [Fact] public void DisabledUnorderedAndUnauthorizedRequestsAskNoWalkQuestions()
    {
        var(network,caps)=Network(); var probe=new Floors();
        var disabled=Search(network,caps); disabled.Advance(probe,WorkAuthorityVerdict.Granted,false,true);
        Assert.Equal(WorkerRouteRefusal.Disabled,disabled.Refusal);
        var unordered=Search(network,caps); unordered.Advance(probe,WorkAuthorityVerdict.Granted,true,false);
        Assert.Equal(WorkerRouteRefusal.NotAnOrderedWorker,unordered.Refusal);
        var unauthorized=Search(network,caps); unauthorized.Advance(probe,default,true,true);
        Assert.Equal(WorkerRouteRefusal.NoAuthority,unauthorized.Refusal); Assert.Equal(0,probe.Calls);
    }
    [Fact] public void OffByDefaultAndAuthorityRevocationRemainRefusalsWhileSearching()
    {
        var(network,caps)=Network(); var probe=new Floors();
        var off=Search(network,new TraversalCapabilities()); off.Advance(probe,WorkAuthorityVerdict.Granted,true,true);
        Assert.Equal(WorkerRouteRefusal.Disabled,off.Refusal);
        var search=Search(network,caps); search.Advance(probe,WorkAuthorityVerdict.Granted,true,true);
        search.Advance(probe,default,true,true); Assert.Equal(WorkerRouteRefusal.NoAuthority,search.Refusal); Assert.Null(search.Plan);
    }
    [Fact] public void RetiredOrRebuiltLinksInvalidateTheSnapshotRatherThanUsingOldEndpoints()
    {
        var(network,caps)=Network(); var search=Search(network,caps); search.Advance(new Floors(),WorkAuthorityVerdict.Granted,true,true);
        network.Retire("first"); network.Publish(Link("first",1,0));
        search.Advance(new Floors(),WorkAuthorityVerdict.Granted,true,true);
        Assert.Equal(WorkerRouteRefusal.LinkChanged,search.Refusal); Assert.Null(search.Plan);
    }
    [Fact] public void OneWholeRouteBudgetCannotResetAtEachLegOrUnknownRetry()
    {
        var(network,caps)=Network(); var search=Search(network,caps,new WorkerRouteBudget(maximumQueries:2));
        Finish(search,new Floors()); Assert.Equal(WorkerRouteRefusal.QueryBudget,search.Refusal);
        Assert.Equal(2,search.Queries); Assert.Null(search.Plan);

        // Waiting is bounded too, by its own cap and with its own reason, so a
        // search that never hears back still ends - and is not told it ran out
        // of queries it never spent (#501 review).
        var waiting=Search(network,caps,new WorkerRouteBudget(maximumQueries:2,maximumPendingAnswers:3));
        var silent=new Floors { Pending=true }; Finish(waiting,silent);
        Assert.Equal(WorkerRouteRefusal.EvidenceTimedOut,waiting.Refusal);
        Assert.Equal(0,waiting.Queries); Assert.Equal(4,silent.Calls); Assert.Null(waiting.Plan);
    }
    [Fact] public void GroundThatLoadsLateStillGetsARouteWithinTheQueryBudget()
    {
        var(network,caps)=Network(); var search=Search(network,caps); var probe=new Floors { Pending=true };
        for(int tick=0;tick<600;tick++) search.Advance(probe,WorkAuthorityVerdict.Granted,true,true);
        Assert.Equal(WorkerRouteSearchState.AwaitingWalkEvidence,search.State); Assert.Equal(0,search.Queries);
        probe.Pending=false; Finish(search,probe);
        Assert.Equal(WorkerRouteSearchState.Complete,search.State); Assert.NotNull(search.Plan);
    }
    [Fact] public void SearchAndLegBoundsRefuseBeforeProducingAPartialPlan()
    {
        var(network,caps)=Network(); var tooMany=Search(network,caps,new WorkerRouteBudget(maximumLinks:1));
        Assert.Equal(WorkerRouteRefusal.TooManyLinks,tooMany.Refusal);
        var legs=Search(network,caps,new WorkerRouteBudget(maximumLegs:2)); Finish(legs,new Floors());
        Assert.Equal(WorkerRouteRefusal.TooManyLegs,legs.Refusal); Assert.Null(legs.Plan);
        var tall=new WorkerRouteSearch(network,caps,TraversalActor.OfWorker(WorkerKey.Thorstein,TraversalCapability.Ladders),
            default,new WorkPoint(0,100,0)); Assert.Equal(WorkerRouteRefusal.TooFar,tall.Refusal);
    }
    [Fact] public void RouteArrivalRequiresBothHorizontalAndVerticalTolerance()
    {
        var leg=new WorkerRouteLeg(WorkerRouteLegKind.Walk,default,new WorkPoint(0,8,0));
        Assert.False(leg.HasArrived(default,1,0.35f)); Assert.True(leg.HasArrived(new WorkPoint(0.5f,8.1f,0),1,0.35f));
        Assert.False(leg.HasArrived(new WorkPoint(0,8,0),float.NaN,1));
    }
}
