using TheConcernedCat.ConcernedTeamster.Adapters.Workers;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.Workers;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
[CollectionDefinition("recruitment-native",DisableParallelization=true)] public sealed class RecruitmentNativeCollection { }
[Collection("recruitment-native")]
public sealed class GunnarRecruitmentCartPortTests : IDisposable
{
    private readonly Guid _epoch=Guid.NewGuid();
    private readonly Vagon _cart=new();
    public GunnarRecruitmentCartPortTests() { Player.m_localPlayer=new(); Localization.instance=new(); RecruitmentHoverStub.Targets.Clear(); }
    public void Dispose() { Player.m_localPlayer=null; RecruitmentHoverStub.Targets.Clear(); }
    private GunnarCartChoice Read()=>GunnarRecruitmentCartPort.Read(_cart,_epoch,null);
    [Fact] public void OnlyExplicitHoveredCartIsSelectedAndFreshExactObjectIsRead()
    {
        Assert.Null(GunnarRecruitmentCartPort.Hovered());
        var hover=new GameObject(); RecruitmentHoverStub.Targets[hover]=_cart; Player.m_localPlayer!.Hover=hover;
        Assert.Same(_cart,GunnarRecruitmentCartPort.Hovered());
        _cart.transform.position=new Vector3(3,0,0);
        var choice=Read(); Assert.Equal(GunnarCartCheck.Usable,choice.Check);
        Assert.Equal("cart-one",choice.Key.SessionId); Assert.Contains("Cart at (3, 0, 0)",choice.Label);
        // A newly hovered different cart cannot replace the exact retained object.
        RecruitmentHoverStub.Targets[hover]=new Vagon { m_nview=new ZNetView { Data=new ZDO { m_uid="cart-two" } } };
        Assert.Equal(choice.Key,Read().Key);
        _cart.m_nview!.Owner=false; Assert.Equal(GunnarCartCheck.Unauthorized,Read().Check);
    }
    [Fact] public void OccupancyAndLiveIdentityAreRecheckedWithoutInventoryOrOwnershipWrites()
    {
        Assert.Equal(GunnarCartCheck.Usable,Read().Check);
        _cart.Used=true; Assert.Equal(GunnarCartCheck.InUse,Read().Check); _cart.Used=false;
        _cart.m_container=new Container { Used=true }; Assert.Equal(GunnarCartCheck.InUse,Read().Check); _cart.m_container=null;
        _cart.m_chair=new Chair { Used=true }; Assert.Equal(GunnarCartCheck.InUse,Read().Check); _cart.m_chair=null;
        _cart.m_attachJoin=new(); Assert.Equal(GunnarCartCheck.InUse,Read().Check); _cart.m_attachJoin=null;
        _cart.m_nview!.Data.Attached=true; Assert.Equal(GunnarCartCheck.InUse,Read().Check); _cart.m_nview.Data.Attached=false;
        _cart.m_nview.Valid=false; Assert.Equal(GunnarCartCheck.Unloaded,Read().Check);
        Assert.Equal(GunnarCartCheck.Destroyed,GunnarRecruitmentCartPort.Read(null,_epoch,null).Check);
    }
    [Fact] public void WrongKindDistanceUnknownAndConflictingLeaseRefuse()
    {
        _cart.Kind=new Catapult(); Assert.Equal(GunnarCartCheck.WrongKind,Read().Check); _cart.Kind=new SiegeMachine();
        Assert.Equal(GunnarCartCheck.WrongKind,Read().Check); _cart.Kind=null;
        _cart.transform.position=new Vector3(0,9,0); Assert.Equal(GunnarCartCheck.OutOfReach,Read().Check);
        _cart.transform.position=new Vector3(float.NaN,0,0); Assert.Equal(GunnarCartCheck.Unknown,Read().Check);
        _cart.transform.position=new Vector3(0,0,0);
        Assert.Equal(GunnarCartCheck.Unknown,GunnarRecruitmentCartPort.Read(_cart,Guid.Empty,null).Check);
        var book=new CartLeaseBook(_epoch); var different=new CartKey("other-cart",_epoch);
        book.Assign("other-lease",WorkerKey.Gunnar,different); Assert.True(book.TryGetActiveForWorker(WorkerKey.Gunnar,out var lease));
        Assert.Equal(GunnarCartCheck.Leased,GunnarRecruitmentCartPort.Read(_cart,_epoch,lease).Check);
    }
}
