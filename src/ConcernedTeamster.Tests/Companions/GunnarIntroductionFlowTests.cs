using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Workers;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarIntroductionFlowTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"ct-462-"+Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FreshLegacyAndAdvancedProgressWithoutChangingOldFilesOrCreatingAWorkLease(int scenario)
    {
        Directory.CreateDirectory(_root);
        string old=Path.Combine(_root,"teamster_trips_legacy.tsv");
        if(scenario==1) File.WriteAllText(old,"legacy utility data must survive byte-for-byte");
        var epoch=Guid.NewGuid(); var book=new CartLeaseBook(epoch);
        var cart=new GunnarCartChoice(new CartKey(scenario==2 ? "already-existing-cart" : "selected-real-cart",epoch),
            "The cart you explicitly selected",GunnarCartCheck.Usable);
        var record=GunnarCompanionRecord.Open(_root,11,22);
        Assert.False(record.IsRecruited);
        Assert.Equal(GunnarPickupResult.Collected,GunnarKeepsake.Examine(record,true,false));
        Assert.False(record.ShouldPresentMemento(true)); Assert.True(record.BeginIntroduction());
        record=GunnarCompanionRecord.Open(_root,11,22);
        Assert.Equal(GunnarBodyKind.Presentation,GunnarBodyPlan.Decide(new GunnarBodyFacts(
            true,true,true,true,record.State,0,true,true,true)));
        var talk=new GunnarTalk();
        Assert.Equal("Bring Gunnar a usable cart",talk.Interact(record.State,HaulPhase.Unassigned,default));
        var flow=new GunnarRecruitment();
        Assert.Equal(GunnarRecruitmentResult.PendingConfirmation,flow.Prepare(record,epoch,cart,true,out var prompt));
        Assert.False(record.IsRecruited);
        Assert.Equal(GunnarRecruitmentResult.Recruited,flow.Confirm(record,epoch,prompt!.Token,cart,true));
        var reopened=GunnarCompanionRecord.Open(_root,11,22); Assert.True(reopened.IsRecruited);
        Assert.False(reopened.ShouldPresentMemento(true)); Assert.NotNull(talk.Interact(reopened.State,HaulPhase.Ready,default));
        Assert.Equal(0,book.Revision); Assert.False(book.TryGetActiveForWorker(WorkerKey.Gunnar,out _));
        if(scenario==1) Assert.Equal("legacy utility data must survive byte-for-byte",File.ReadAllText(old));
    }
    [Fact] public void RecruitmentSurvivesActualLeaseInvalidationAndRepeatedWorldReload()
    {
        var record=GunnarCompanionRecord.Open(_root,11,22); record.BeginIntroduction();
        var epoch=Guid.NewGuid(); var key=new CartKey("chosen",epoch); var flow=new GunnarRecruitment();
        var choice=new GunnarCartChoice(key,"The chosen cart",GunnarCartCheck.Usable);
        flow.Prepare(record,epoch,choice,true,out var p); flow.Confirm(record,epoch,p!.Token,choice,true);
        var leases=new CartLeaseBook(epoch);
        Assert.Equal(LeaseOutcome.Assigned,leases.Assign("real-work-lease",WorkerKey.Gunnar,key));
        leases.Invalidate("real-work-lease",LeaseInvalidation.CartDestroyed);
        leases.BeginWorldLoad(Guid.NewGuid()); leases.BeginWorldLoad(Guid.NewGuid()); flow.Forget();
        Assert.True(GunnarCompanionRecord.Open(_root,11,22).IsRecruited);
        Assert.False(leases.TryGetActiveForWorker(WorkerKey.Gunnar,out _));
    }
}
