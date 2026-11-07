using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarRecruitmentTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"ct-460-"+Guid.NewGuid().ToString("N"));
    private readonly Guid _epoch=Guid.NewGuid();
    private GunnarCompanionRecord Record() => GunnarCompanionRecord.Open(_root,11,22);
    private GunnarCartChoice Cart(string id="selected-cart",GunnarCartCheck check=GunnarCartCheck.Usable) =>
        new(new CartKey(id,_epoch),"Your selected cart: "+id,check);
    private GunnarCompanionRecord Intro() { var r=Record(); Assert.True(r.BeginIntroduction()); return r; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    [Fact] public void ExactNamedCartConfirmationPersistsRecruitmentWithoutLeaseOrJob()
    {
        var flow=new GunnarRecruitment(); var r=Intro(); var cart=Cart();
        Assert.Equal(GunnarRecruitmentResult.PendingConfirmation,flow.Prepare(r,_epoch,cart,true,out var prompt));
        Assert.Equal(cart.Label,prompt!.Label); Assert.False(r.IsRecruited);
        Assert.Equal(GunnarRecruitmentResult.Recruited,flow.Confirm(r,_epoch,prompt.Token,cart,true));
        Assert.True(Record().IsRecruited);
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(r,_epoch,prompt.Token,cart,true));
    }
    [Fact] public void EveryUnavailableCartReasonRefusesAndDoesNotRecruit()
    {
        var r=Intro();
        foreach(var check in Enum.GetValues<GunnarCartCheck>().Where(x=>x!=GunnarCartCheck.Usable))
        {
            var flow=new GunnarRecruitment();
            Assert.Equal(GunnarRecruitmentResult.CartUnavailable,flow.Prepare(r,_epoch,Cart(check:check),true,out var prompt));
            Assert.Null(prompt); Assert.False(r.IsRecruited);
        }
    }
    [Fact] public void WrongCartAtConfirmationAndStaleReloadIdentityRequireReselection()
    {
        var flow=new GunnarRecruitment(); var r=Intro();
        flow.Prepare(r,_epoch,Cart(),true,out var p);
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(r,_epoch,p!.Token,Cart("other"),true));
        flow.Prepare(r,_epoch,Cart(),true,out p);
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(r,Guid.NewGuid(),p!.Token,Cart(),true));
        Assert.False(Record().IsRecruited);
    }
    [Fact] public void AChangedCartLabelNeedsANewNamedConfirmation()
    {
        var flow=new GunnarRecruitment(); var r=Intro(); var cart=Cart();
        flow.Prepare(r,_epoch,cart,true,out var prompt);
        var renamed=new GunnarCartChoice(cart.Key,"A different displayed name",GunnarCartCheck.Usable);
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(r,_epoch,prompt!.Token,renamed,true));
        Assert.False(Record().IsRecruited);
    }
    [Fact] public void TwoSimultaneousAssignmentsCannotReplaceTheNamedPrompt()
    {
        var flow=new GunnarRecruitment(); var r=Intro();
        Assert.Equal(GunnarRecruitmentResult.PendingConfirmation,flow.Prepare(r,_epoch,Cart("a"),true,out var first));
        Assert.Equal(GunnarRecruitmentResult.Busy,flow.Prepare(r,_epoch,Cart("b"),true,out var second));
        Assert.Null(second);
        Assert.Equal(GunnarRecruitmentResult.Recruited,flow.Confirm(r,_epoch,first!.Token,Cart("a"),true));
    }
    [Fact] public void OccupiedOrUnauthorizedAfterPromptIsRechecked()
    {
        var r=Intro();
        foreach(var check in new[]{GunnarCartCheck.InUse,GunnarCartCheck.Unauthorized,GunnarCartCheck.Leased})
        {
            var flow=new GunnarRecruitment(); flow.Prepare(r,_epoch,Cart(),true,out var p);
            Assert.Equal(GunnarRecruitmentResult.CartUnavailable,flow.Confirm(r,_epoch,p!.Token,Cart(check:check),true));
        }
        Assert.False(Record().IsRecruited);
    }
    [Fact] public void AuthorityMustHoldOnBothSelectionAndConfirmation()
    {
        var flow=new GunnarRecruitment(); var r=Intro();
        Assert.Equal(GunnarRecruitmentResult.AuthorityRequired,flow.Prepare(r,_epoch,Cart(),false,out _));
        flow.Prepare(r,_epoch,Cart(),true,out var p);
        Assert.Equal(GunnarRecruitmentResult.AuthorityRequired,flow.Confirm(r,_epoch,p!.Token,Cart(),false));
        Assert.False(Record().IsRecruited);
    }
    [Fact] public void CartLossNeverRelocksARecordedRecruitment()
    {
        var flow=new GunnarRecruitment(); var r=Intro(); flow.Prepare(r,_epoch,Cart(),true,out var p);
        flow.Confirm(r,_epoch,p!.Token,Cart(),true); flow.Forget();
        Assert.Equal(GunnarRecruitmentResult.AlreadyRecruited,flow.Prepare(Record(),Guid.NewGuid(),default,false,out _));
        Assert.True(Record().IsRecruited);
    }
    [Fact] public void ScopeSwitchCancelsOldConfirmation()
    {
        var flow=new GunnarRecruitment(); var r=Intro(); flow.Prepare(r,_epoch,Cart(),true,out var p);
        var other=GunnarCompanionRecord.Open(_root,11,23); other.BeginIntroduction();
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(other,_epoch,p!.Token,Cart(),true));
        Assert.False(r.IsRecruited); Assert.False(other.IsRecruited);
    }
    [Fact] public void SaveFailureDoesNotCompleteOrLeaveReusableConfirmation()
    {
        var flow=new GunnarRecruitment(); var r=Intro(); flow.Prepare(r,_epoch,Cart(),true,out var p);
        // A directory at the temp-write path forces a real save refusal.
        string data=Directory.GetFiles(_root,"*.companions.tsv").Single(); Directory.CreateDirectory(data+".tmp");
        Assert.Equal(GunnarRecruitmentResult.NotRecorded,flow.Confirm(r,_epoch,p!.Token,Cart(),true));
        Assert.False(r.IsRecruited); Assert.False(Record().IsRecruited);
        Assert.Equal(GunnarRecruitmentResult.Stale,flow.Confirm(r,_epoch,p.Token,Cart(),true));
    }
}
