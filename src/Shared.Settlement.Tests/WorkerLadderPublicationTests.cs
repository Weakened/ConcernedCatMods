using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;
namespace Shared.Settlement.Tests;
public sealed class WorkerLadderPublicationTests
{
    private readonly string _epoch=Guid.NewGuid().ToString("N");
    private readonly WorkerLadderCapsule _body=new(.35f,1.8f);
    private static float[] Row(float y=0,float x=0,float front=.24f,float rear=.08f)=>new[]{x,y,0,x,y+2,0,0,1,.8f,1f/3,front,rear};
    private WorkerLadderPublication Publication(params float[][] rows)
    {
        Assert.True(WorkerLadderPublication.TryRead(_epoch,1,rows.Select((_,i)=>"section-"+i).ToArray(),rows,out var publication)); return publication;
    }
    private sealed class Probe : IWorkerLadderSurveyProbe
    {
        internal int Calls,Begins; internal bool RearBlocked,Unavailable; internal float TopY;
        internal WorkerLadderRunData? Last;
        public void BeginSurvey()=>Begins++;
        public WorkerLadderProbeResult TryMeasure(WorkerLadderRunData run,WorkerLadderFace face,WorkerLadderCapsule body,out WorkerLadderPassage path)
        {
            Calls++; Last=run; path=null!;
            if(Unavailable) return WorkerLadderProbeResult.Unavailable;
            if(RearBlocked&&face==WorkerLadderFace.Rear) return WorkerLadderProbeResult.Blocked;
            var bottom=run.Lane(false,face,body); var crest=run.Lane(true,face,body);
            var top=new WorkPoint(crest.X,crest.Y+TopY,crest.Z+1);
            path=new WorkerLadderPassage(bottom,bottom,new WorkPoint(crest.X,Math.Max(crest.Y,top.Y),crest.Z),top);
            return WorkerLadderProbeResult.Supported;
        }
    }
    [Fact] public void ChainsFuseExactEndpointsAndKeepIndependentColliderStandOffs()
    {
        var network=new TraversalLinkNetwork(); var survey=new WorkerLadderLinkSurvey(network,TraversalLimits.Default); var probe=new Probe();
        Assert.Equal(WorkerLadderSurveyResult.Ready,survey.Refresh(Publication(Row(2,front:.3f),Row(0,rear:-.1f)),default,20,_body,probe,true));
        Assert.Equal(2,network.Count); Assert.Equal(4,probe.Last!.Top.Y);
        Assert.Equal(.3f+.35f+.08f,probe.Last.Lane(false,WorkerLadderFace.Front,_body).Z,5);
        Assert.Equal(-.1f-.35f-.08f,probe.Last.Lane(false,WorkerLadderFace.Rear,_body).Z,5);
        Assert.All(network.All(),link=>Assert.True(survey.TryPath(link.Id,out _,out _)));
    }
    [Fact] public void UnchangedSamplingKeepsHandlesButFloorOrIntermediateGeometryChangeInvalidates()
    {
        var network=new TraversalLinkNetwork(); var survey=new WorkerLadderLinkSurvey(network,TraversalLimits.Default); var probe=new Probe();
        var publication=Publication(Row(0),Row(2),Row(4));
        survey.Refresh(publication,default,20,_body,probe,true); string id=network.All()[0].Id; network.TryGet(id,out _,out int before);
        survey.Refresh(publication,default,20,_body,probe,true); network.TryGet(id,out _,out int same); Assert.Equal(before,same);
        var changed=Publication(Row(0),Row(2,front:.2f),Row(4));
        survey.Refresh(changed,default,20,_body,probe,true); network.TryGet(id,out _,out int rebuilt); Assert.NotEqual(before,rebuilt);
        probe.TopY=.1f; survey.Refresh(changed,default,20,_body,probe,true); network.TryGet(id,out _,out int floor); Assert.NotEqual(rebuilt,floor);
    }
    [Fact] public void UnknownOrDisabledWithdrawsOnlyThisProvidersLinksAndNeverKeepsLastGoodPermission()
    {
        var network=new TraversalLinkNetwork(); var limits=TraversalLimits.Default;
        Assert.True(TraversalLink.TryCreate("other-provider",TraversalLinkKind.Ladder,default,new WorkPoint(0,2,0),TraversalCapability.Ladders,limits,out var other));
        network.Publish(other); var survey=new WorkerLadderLinkSurvey(network,limits); var probe=new Probe();
        survey.Refresh(Publication(Row()),default,20,_body,probe,true); Assert.Equal(3,network.Count);
        probe.Unavailable=true; Assert.Equal(WorkerLadderSurveyResult.Unavailable,survey.Refresh(Publication(Row()),default,20,_body,probe,true));
        Assert.Single(network.All()); Assert.True(network.TryGet("other-provider",out _,out _));
        Assert.Equal(WorkerLadderSurveyResult.Disabled,survey.Refresh(Publication(Row()),default,20,_body,probe,false));
        Assert.Single(network.All());
    }
    [Fact] public void AmbiguousOverlapAndRunBudgetRefuseWithoutAnyPhysicsGuess()
    {
        var network=new TraversalLinkNetwork(); var survey=new WorkerLadderLinkSurvey(network,TraversalLimits.Default); var probe=new Probe();
        Assert.Equal(WorkerLadderSurveyResult.Ambiguous,survey.Refresh(Publication(Row(),Row()),default,20,_body,probe,true));
        Assert.Equal(0,probe.Calls);
        Assert.Equal(WorkerLadderSurveyResult.OverBudget,survey.Refresh(Publication(Enumerable.Range(0,9).Select(i=>Row(x:i*2)).ToArray()),default,30,_body,probe,true));
        Assert.Equal(0,probe.Calls); Assert.Equal(0,network.Count);
    }
    [Fact] public void RearObstructionDoesNotInventARearLandingAndFarSectionsAreNotPublished()
    {
        var network=new TraversalLinkNetwork(); var survey=new WorkerLadderLinkSurvey(network,TraversalLimits.Default); var probe=new Probe { RearBlocked=true };
        survey.Refresh(Publication(Row(),Row(x:40)),default,20,_body,probe,true);
        Assert.Single(network.All()); Assert.EndsWith(":Front",network.All()[0].Id); Assert.Equal(2,probe.Calls);
    }
    [Fact] public void DecoderRejectsMalformedPayloadAndCopiesCallerArrays()
    {
        var rows=new[]{Row()}; var ids=new[]{"a"};
        Assert.True(WorkerLadderPublication.TryRead(_epoch,1,ids,rows,out var publication));
        ids[0]="changed"; rows[0][0]=100;
        Assert.Equal("a",publication.Sections[0].Id); Assert.Equal(0,publication.Sections[0].Bottom.X);
        Assert.False(WorkerLadderPublication.TryRead("unknown",1,ids,rows,out _));
        Assert.False(WorkerLadderPublication.TryRead(_epoch,0,ids,rows,out _));
        Assert.False(WorkerLadderPublication.TryRead(_epoch,1,new[]{"a","a"},new[]{Row(),Row(2)},out _));
        var invalid=Row(); invalid[10]=float.NaN;
        Assert.False(WorkerLadderPublication.TryRead(_epoch,1,new[]{"a"},new[]{invalid},out _));
    }
    private sealed class Source : IWorkerLadderPublicationSource
    {
        internal WorkerLadderPublication? Publication; internal int Calls;
        public bool TryRead(out WorkerLadderPublication publication) { Calls++; publication=Publication!; return publication!=null; }
    }
    [Fact] public void FeedIsBoundedOptInAndExplicitRefreshWithdrawsUnavailableSnapshot()
    {
        var network=new TraversalLinkNetwork(); var survey=new WorkerLadderLinkSurvey(network,TraversalLimits.Default);
        var probe=new Probe(); var source=new Source { Publication=Publication(Row()) }; var feed=new WorkerLadderLinkFeed(source,probe,survey);
        feed.Tick(.1f,false,default,20,_body); Assert.Equal(0,source.Calls);
        feed.Tick(.1f,true,default,20,_body); Assert.Equal(1,source.Calls); Assert.Equal(2,network.Count);
        for(int i=0;i<10;i++) feed.Tick(.05f,true,default,20,_body); Assert.Equal(1,source.Calls);
        source.Publication=null; feed.RefreshNow(true,default,20,_body); Assert.Equal(0,network.Count);
        feed.Tick(float.NaN,true,default,20,_body); Assert.Equal(WorkerLadderSurveyResult.Malformed,feed.State);
    }
}
