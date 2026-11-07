using System;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>READ ONLY, on the explicit hovered/previously chosen Vagon only.
/// No cart search, inventory operation, lease, ownership claim or worker action.</summary>
internal static class GunnarRecruitmentCartPort
{
    internal static Vagon? Hovered()
    {
        var player=Player.m_localPlayer;
        var hover=player!=null?player.GetHoverObject():null;
        return hover!=null?hover.GetComponentInParent<Vagon>():null;
    }
    internal static GunnarCartChoice Read(Vagon? cart, Guid epoch, CartLease? activeLease)
    {
        if(cart==null) return new GunnarCartChoice(default,string.Empty,GunnarCartCheck.Destroyed);
        if(epoch==Guid.Empty) return new GunnarCartChoice(default,string.Empty,GunnarCartCheck.Unknown);
        var view=cart.m_nview;
        if(view==null||!view.IsValid()||view.GetZDO()==null)
            return new GunnarCartChoice(default,string.Empty,GunnarCartCheck.Unloaded);
        var key=new CartKey(view.GetZDO().m_uid.ToString(),epoch);
        var at=cart.transform.position;
        string name=cart.m_name;
        if(Localization.instance!=null) name=Localization.instance.Localize(name);
        if(string.IsNullOrWhiteSpace(name)||name.StartsWith("$",StringComparison.Ordinal)) name="Cart";
        string label=name+string.Format(CultureInfo.InvariantCulture," at ({0:0.#}, {1:0.#}, {2:0.#})",at.x,at.y,at.z);
        GunnarCartCheck check=GunnarCartCheck.Usable;
        if(cart.GetComponent<Catapult>()!=null||cart.GetComponent<SiegeMachine>()!=null) check=GunnarCartCheck.WrongKind;
        else if(!view.IsOwner()) check=GunnarCartCheck.Unauthorized;
        else if(cart.InUse()||cart.m_attachJoin!=null||view.GetZDO().GetBool(ZDOVars.s_attachJointHash)||
            (cart.m_container!=null&&cart.m_container.IsInUse())||(cart.m_chair!=null&&cart.m_chair.IsInUse())) check=GunnarCartCheck.InUse;
        else if(activeLease!=null&&activeLease.IsActive&&!activeLease.Cart.Equals(key)) check=GunnarCartCheck.Leased;
        else
        {
            var player=Player.m_localPlayer;
            if(player==null) check=GunnarCartCheck.Unknown;
            else
            {
                var here=player.transform.position;
                float dx=at.x-here.x,dy=at.y-here.y,dz=at.z-here.z;
                float distanceSquared=dx*dx+dy*dy+dz*dz;
                float reach=new HaulExecutionLimits().SelectionReachMetres;
                if(float.IsNaN(distanceSquared)||float.IsInfinity(distanceSquared)) check=GunnarCartCheck.Unknown;
                else if(distanceSquared>reach*reach) check=GunnarCartCheck.OutOfReach;
            }
        }
        return new GunnarCartChoice(key,label,check);
    }
}
