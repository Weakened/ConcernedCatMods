using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.Companions.Placement;
using UnityEngine;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Bounded, read-only native placement. No scene-wide search, network
/// data, item operation or world write. Every saturated/unknown query defers.</summary>
internal sealed class GunnarKeepsakeEnvironment : IPlacementProbe
{
    private readonly Collider[] _nearby=new Collider[512];
    private readonly Collider[] _beds=new Collider[128];
    private readonly RaycastHit[] _ground=new RaycastHit[32];
    private readonly List<WorldPoint> _otherMementos=new(16);
    private readonly FieldInfo? _water=typeof(ZoneSystem).GetField("m_waterLevel",
        BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    private int _mask;
    private readonly List<MonoBehaviour> _scripts=new(16);
    public string? Limitation { get; private set; }

    public bool TryAnchor(out CompanionAnchor anchor)
    {
        anchor=default; Limitation=null;
        Game? game=Game.instance; ZoneSystem? zones=ZoneSystem.instance;
        if(game==null||zones==null) return false;
        PlayerProfile? profile=game.GetPlayerProfile();
        if(profile==null) return false;
        if(profile.HaveCustomSpawnPoint())
        {
            Vector3 bed=profile.GetCustomSpawnPoint();
            if(!Finite(bed)||!zones.IsZoneLoaded(bed)) return false;
            int count=Physics.OverlapSphereNonAlloc(bed,3f,_beds,~0,QueryTriggerInteraction.Collide);
            if(count>=_beds.Length) { Limitation="Bed validation query is full; buckle placement is deferred rather than guessing a respawn anchor."; return false; }
            for(int i=0;i<count;i++)
            {
                Bed? current=_beds[i]!=null ? _beds[i].GetComponentInParent<Bed>() : null;
                if(current!=null&&current.IsCurrent())
                { anchor=new CompanionAnchor(AnchorKind.ClaimedBed,Point(bed)); return true; }
            }
            // Loaded and no current bed: follow vanilla's world-start fallback.
        }
        if(zones.GetLocationIcon(game.m_StartLocation,out Vector3 start)&&Finite(start)&&zones.IsZoneLoaded(start))
        { anchor=new CompanionAnchor(AnchorKind.DefaultSpawn,Point(start)); return true; }
        return false;
    }

    public IReadOnlyList<WorldPoint>? OtherMementos(CompanionAnchor anchor)
    {
        _otherMementos.Clear(); Limitation=null;
        if(!anchor.IsValid) return null;
        int knownLayers=LayerMask.GetMask("piece_nonsolid","item");
        int count=Physics.OverlapSphereNonAlloc(Vector(anchor.Position),8f,_nearby,knownLayers!=0?knownLayers:~0,QueryTriggerInteraction.Collide);
        if(count>=_nearby.Length) { Limitation="Nearby keepsake query is full; buckle placement is deferred to preserve separation."; return null; }
        for(int i=0;i<count;i++)
        {
            Collider collider=_nearby[i];
            if(collider==null||!collider.gameObject.activeInHierarchy) continue;
            _scripts.Clear();
            collider.GetComponentsInParent<MonoBehaviour>(false,_scripts);
            foreach(MonoBehaviour script in _scripts)
            {
                if(script==null||!script.gameObject.activeInHierarchy) continue;
                string type=script.GetType().FullName ?? string.Empty;
                if(type!="TheConcernedCat.ConcernedNPC.Body.Keepsakes.NpcKeepsakeBehaviour"&&
                    type!="TheConcernedCat.ConcernedCartographer.Runtime.Companions.BrokenCompassObject") continue;
                if(type=="TheConcernedCat.ConcernedNPC.Body.Keepsakes.NpcKeepsakeBehaviour"&&
                    script.gameObject.name=="CT_GunnarBuckle") continue; // never reserve against our own existing object
                WorldPoint point=Point(script.transform.position);
                if(!_otherMementos.Contains(point)) _otherMementos.Add(point);
            }
        }
        return _otherMementos;
    }

    public PlacementProbeSample Probe(WorldPoint position)
    {
        Vector3 requested=Vector(position); ZoneSystem? zones=ZoneSystem.instance;
        if(zones==null||!Finite(requested)||!zones.IsZoneLoaded(requested)) return PlacementProbeSample.NotLoaded(position);
        if(_mask==0) _mask=LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain");
        if(_mask==0||_water==null||!(_water.GetValue(zones) is float water)||float.IsNaN(water)||float.IsInfinity(water))
        { Limitation="Water-level or solid-layer metadata is unavailable; buckle placement is deferred until supported."; return PlacementProbeSample.NotLoaded(position); }
        int count=Physics.RaycastNonAlloc(requested+Vector3.up*3f,Vector3.down,_ground,6f,_mask,QueryTriggerInteraction.Ignore);
        if(count>=_ground.Length) return PlacementProbeSample.NotLoaded(position);
        int best=-1; float gap=float.MaxValue;
        for(int i=0;i<count;i++)
        {
            RaycastHit hit=_ground[i];
            if(hit.collider==null||hit.collider.attachedRigidbody!=null||!Finite(hit.point)||!Finite(hit.normal)) continue;
            float candidate=Math.Abs(hit.point.y-requested.y);
            if(candidate>gap||(candidate==gap&&best>=0&&hit.point.y>=_ground[best].point.y)) continue;
            // A small object needs actual clear space, not the roof above it.
            bool covered=false;
            for(int j=0;j<count;j++)
                if(j!=i&&_ground[j].collider!=null&&_ground[j].collider.attachedRigidbody==null&&
                    _ground[j].point.y>hit.point.y+0.03f&&_ground[j].point.y<hit.point.y+0.3f) { covered=true; break; }
            if(!covered) { gap=candidate; best=i; }
        }
        if(best<0) return new PlacementProbeSample(position,PlacementRejection.Unsupported,-1f,SeatOffer.None);
        RaycastHit ground=_ground[best]; Vector3 spot=new(requested.x,ground.point.y,requested.z);
        PlacementRejection rejected=PlacementRejection.None;
        if(ground.normal.y<0.76f) rejected|=PlacementRejection.TooSteep;
        if(ground.point.y<water+0.05f) rejected|=PlacementRejection.Water;
        if(Physics.CheckSphere(spot+Vector3.up*0.18f,0.12f,_mask,QueryTriggerInteraction.Ignore))
            rejected|=PlacementRejection.Occupied;
        return new PlacementProbeSample(Point(spot),rejected,-1f,SeatOffer.None);
    }
    public void Forget()
    {
        Array.Clear(_nearby,0,_nearby.Length); Array.Clear(_beds,0,_beds.Length);
        Array.Clear(_ground,0,_ground.Length); _scripts.Clear(); _otherMementos.Clear(); Limitation=null;
    }
    private static bool Finite(Vector3 p) => !(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||
        float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z));
    private static WorldPoint Point(Vector3 v)=>new(v.x,v.y,v.z);
    private static Vector3 Vector(WorldPoint p)=>new(p.X,p.Y,p.Z);
}
