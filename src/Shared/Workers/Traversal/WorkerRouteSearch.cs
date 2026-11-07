using System;
using System.Collections.Generic;

namespace TheConcernedCat.Workers.Traversal;

internal enum WorkerRouteSearchState { Searching, AwaitingWalkEvidence, Complete, Refused }
internal enum WorkerRouteRefusal { None, Disabled, NotAnOrderedWorker, NoAuthority, InvalidInput,
    TooFar, TooManyLinks, QueryBudget, NoRoute, TooManyLegs, LinkChanged, EvidenceTimedOut }
internal enum WorkerWalkEvidenceKind { Unknown, Blocked, Reachable }
internal readonly struct WorkerWalkEvidence
{
    public WorkerWalkEvidence(WorkerWalkEvidenceKind kind,float metres=0) { Kind=kind; Metres=metres; }
    public WorkerWalkEvidenceKind Kind { get; }
    public float Metres { get; }
}
/// <summary>Reads a verified, height-aware walk connection between exact points.
/// Unknown is pending evidence, never permission. The adapter owns its path-query
/// budget and floor validation; a straight-line estimate is not a walk verdict.</summary>
internal interface IWorkerWalkProbe
{
    WorkerWalkEvidence Check(WorkPoint from,WorkPoint to);
}
internal enum WorkerRouteLegKind { Unspecified, Walk, Climb }
internal readonly struct WorkerRouteLeg
{
    public WorkerRouteLeg(WorkerRouteLegKind kind,WorkPoint from,WorkPoint to,
        string? linkId=null,int generation=0,TraversalDirection direction=TraversalDirection.Unspecified)
    { Kind=kind; From=from; To=to; LinkId=linkId; Generation=generation; Direction=direction; }
    public WorkerRouteLegKind Kind { get; }
    public WorkPoint From { get; }
    public WorkPoint To { get; }
    public string? LinkId { get; }
    public int Generation { get; }
    public TraversalDirection Direction { get; }
    // Applied to route legs only, never to the existing ordinary ground planner.
    public bool HasArrived(WorkPoint position,float horizontalTolerance,float heightTolerance) =>
        position.IsFinite && horizontalTolerance>=0 && heightTolerance>=0 &&
        !float.IsInfinity(horizontalTolerance) && !float.IsInfinity(heightTolerance) &&
        position.HorizontalDistanceTo(To)<=horizontalTolerance && position.VerticalDistanceTo(To)<=heightTolerance;
}
internal sealed class WorkerRoutePlan
{
    internal WorkerRoutePlan(IReadOnlyList<WorkerRouteLeg> legs,float metres) { Legs=legs; Metres=metres; }
    public IReadOnlyList<WorkerRouteLeg> Legs { get; }
    public float Metres { get; }
}
internal sealed class WorkerRouteBudget
{
    // A pending answer is not a spent query (#501 review): ground that has not
    // loaded yet would otherwise burn the query budget and come back as a
    // permanent QueryBudget refusal. Waiting has its own cap and its own reason.
    public WorkerRouteBudget(int maximumLinks=16,int maximumLegs=12,int maximumQueries=512,float horizonMetres=64,
        int maximumPendingAnswers=4096)
    {
        if(maximumLinks<1||maximumLinks>64||maximumLegs<1||maximumLegs>128||maximumQueries<1||maximumPendingAnswers<1||
            !(horizonMetres>0)||float.IsInfinity(horizonMetres)) throw new ArgumentOutOfRangeException(nameof(maximumLinks));
        MaximumLinks=maximumLinks; MaximumLegs=maximumLegs; MaximumQueries=maximumQueries; HorizonMetres=horizonMetres;
        MaximumPendingAnswers=maximumPendingAnswers;
    }
    public int MaximumLinks { get; }
    public int MaximumLegs { get; }
    public int MaximumQueries { get; }
    public float HorizonMetres { get; }
    public int MaximumPendingAnswers { get; }
}

/// <summary>#334: a deterministic, incremental Dijkstra search over published
/// links AND verified walk edges. One graph, one query budget for the whole route,
/// not a fresh allowance at every leg. No engine, motor, player input or new grant.
/// Cold state is bounded; Advance issues at most the requested checks per tick.</summary>
internal sealed class WorkerRouteSearch
{
    private readonly TraversalLinkNetwork _network;
    private readonly TraversalCapabilities _capabilities;
    private readonly TraversalActor _actor;
    private readonly WorkerRouteBudget _budget;
    private readonly TraversalLimits _limits;
    private readonly IReadOnlyList<TraversalLink> _links;
    private readonly int[] _generations;
    private readonly WorkPoint[] _nodes;
    private readonly float[] _distance;
    private readonly bool[] _settled;
    private readonly int[] _previous;
    private readonly bool[] _climbed;
    private int _current=-1,_next;
    public WorkerRouteSearch(TraversalLinkNetwork network,TraversalCapabilities capabilities,TraversalActor actor,
        WorkPoint from,WorkPoint to,WorkerRouteBudget? budget=null,TraversalLimits? limits=null)
    {
        _network=network ?? throw new ArgumentNullException(nameof(network));
        _capabilities=capabilities ?? throw new ArgumentNullException(nameof(capabilities)); _actor=actor;
        _budget=budget ?? new WorkerRouteBudget(); _limits=(limits ?? TraversalLimits.Default).Validate();
        _links=network.Count<=_budget.MaximumLinks ? network.All() : Array.Empty<TraversalLink>();
        _generations=new int[_links.Count]; _nodes=new WorkPoint[2+_links.Count*2];
        _distance=new float[_nodes.Length]; _settled=new bool[_nodes.Length];
        _previous=new int[_nodes.Length]; _climbed=new bool[_nodes.Length];
        _nodes[0]=from; _nodes[_nodes.Length-1]=to;
        for(int i=0;i<_nodes.Length;i++) { _distance[i]=float.PositiveInfinity; _previous[i]=-1; }
        _distance[0]=0;
        for(int i=0;i<_links.Count;i++)
        { _nodes[1+i*2]=_links[i].Bottom; _nodes[2+i*2]=_links[i].Top;
          network.TryGet(_links[i].Id,out _,out _generations[i]); }
        if(!from.IsFinite||!to.IsFinite) Refuse(WorkerRouteRefusal.InvalidInput);
        else if(network.Count>_budget.MaximumLinks) Refuse(WorkerRouteRefusal.TooManyLinks);
        else if(Distance(from,to)>_budget.HorizonMetres) Refuse(WorkerRouteRefusal.TooFar);
    }
    public WorkerRouteSearchState State { get; private set; }
    public WorkerRouteRefusal Refusal { get; private set; }
    public int Queries { get; private set; }
    public int PendingAnswers { get; private set; }
    public WorkerRoutePlan? Plan { get; private set; }
    public WorkerRouteSearchState Advance(IWorkerWalkProbe probe,WorkAuthorityVerdict authority,
        bool featureEnabled,bool explicitOrderActive,int maximumChecksThisTick=1)
    {
        if(State==WorkerRouteSearchState.Complete||State==WorkerRouteSearchState.Refused) return State;
        if(!featureEnabled||!_capabilities.Can(_actor,TraversalCapability.Ladders)) return Refuse(WorkerRouteRefusal.Disabled);
        if(!_actor.IsWorker||!explicitOrderActive) return Refuse(WorkerRouteRefusal.NotAnOrderedWorker);
        if(authority!=WorkAuthorityVerdict.Granted) return Refuse(WorkerRouteRefusal.NoAuthority);
        if(probe==null||maximumChecksThisTick<0) return Refuse(WorkerRouteRefusal.InvalidInput);
        if(!LinksCurrent()) return Refuse(WorkerRouteRefusal.LinkChanged);
        State=WorkerRouteSearchState.Searching;
        int checks=0;
        while(true)
        {
            if(_current<0)
            {
                _current=Closest(); _next=0;
                if(_current<0) return Refuse(WorkerRouteRefusal.NoRoute);
                if(_current==_nodes.Length-1) return Finish();
            }
            if(_next==_nodes.Length) { _settled[_current]=true; _current=-1; continue; }
            int target=_next;
            if(target==_current||_settled[target]||Distance(_nodes[0],_nodes[target])>_budget.HorizonMetres)
            { _next++; continue; }
            int climb=ClimbBetween(_current,target);
            if(climb>=0)
            {
                TraversalLink link=_links[climb];
                if(TraversalLinkNetwork.WithinHeightAllowance(_actor,link,_limits))
                    Relax(target,link.EquivalentWalkMetres(_limits),true);
            }
            if(checks>=maximumChecksThisTick) return State;
            if(Queries>=_budget.MaximumQueries) return Refuse(WorkerRouteRefusal.QueryBudget);
            checks++; Queries++;
            WorkerWalkEvidence evidence=probe.Check(_nodes[_current],_nodes[target]);
            if(evidence.Kind==WorkerWalkEvidenceKind.Unknown)
            {
                Queries--;
                if(++PendingAnswers>_budget.MaximumPendingAnswers) return Refuse(WorkerRouteRefusal.EvidenceTimedOut);
                State=WorkerRouteSearchState.AwaitingWalkEvidence; return State;
            }
            if(evidence.Kind==WorkerWalkEvidenceKind.Reachable)
            {
                float minimum=Distance(_nodes[_current],_nodes[target]);
                if(float.IsNaN(evidence.Metres)||float.IsInfinity(evidence.Metres)||evidence.Metres<0||evidence.Metres+0.001f<minimum)
                    return Refuse(WorkerRouteRefusal.InvalidInput);
                Relax(target,evidence.Metres,false);
            }
            else if(evidence.Kind!=WorkerWalkEvidenceKind.Blocked) return Refuse(WorkerRouteRefusal.InvalidInput);
            _next++;
        }
    }
    private void Relax(int target,float metres,bool climb)
    {
        float cost=_distance[_current]+metres;
        if(cost<_distance[target]) { _distance[target]=cost; _previous[target]=_current; _climbed[target]=climb; }
    }
    private int Closest()
    {
        int best=-1; float cost=float.PositiveInfinity;
        for(int i=0;i<_nodes.Length;i++) if(!_settled[i]&&_distance[i]<cost) { best=i; cost=_distance[i]; }
        return best;
    }
    private int ClimbBetween(int from,int to) => from>0&&to>0&&from<_nodes.Length-1&&to<_nodes.Length-1&&
        (from-1)/2==(to-1)/2 ? (from-1)/2 : -1;
    private bool LinksCurrent()
    {
        for(int i=0;i<_links.Count;i++) if(!_network.TryGet(_links[i].Id,out _,out int generation)||generation!=_generations[i]) return false;
        return true;
    }
    private WorkerRouteSearchState Finish()
    {
        if(!LinksCurrent()) return Refuse(WorkerRouteRefusal.LinkChanged);
        var reversed=new List<WorkerRouteLeg>(); int to=_nodes.Length-1;
        while(to!=0)
        {
            int from=_previous[to]; if(from<0) return Refuse(WorkerRouteRefusal.NoRoute);
            int link=_climbed[to] ? ClimbBetween(from,to) : -1;
            reversed.Add(new WorkerRouteLeg(link>=0?WorkerRouteLegKind.Climb:WorkerRouteLegKind.Walk,
                _nodes[from],_nodes[to],link>=0?_links[link].Id:null,link>=0?_generations[link]:0,
                link>=0 ? (from<to?TraversalDirection.Up:TraversalDirection.Down) : TraversalDirection.Unspecified));
            if(reversed.Count>_budget.MaximumLegs) return Refuse(WorkerRouteRefusal.TooManyLegs);
            to=from;
        }
        reversed.Reverse(); Plan=new WorkerRoutePlan(reversed.AsReadOnly(),_distance[_nodes.Length-1]);
        return State=WorkerRouteSearchState.Complete;
    }
    private WorkerRouteSearchState Refuse(WorkerRouteRefusal reason)
    { Refusal=reason; Plan=null; return State=WorkerRouteSearchState.Refused; }
    internal static float Distance(WorkPoint a,WorkPoint b)
    { double x=(double)a.X-b.X,y=(double)a.Y-b.Y,z=(double)a.Z-b.Z; return (float)Math.Sqrt(x*x+y*y+z*z); }
}
