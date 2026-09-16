using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>His camp as he understands it at one moment: the fires and whether
/// they burn and have a roof over them, the beds and whether anybody claimed
/// them, the doors and whether he may use them, and whether it is night or wet.
/// </summary>
internal sealed class CampView
{
    public CampView(
        CampSnapshot snapshot, List<Fireplace> fireplaces, List<Bed> beds, List<Door> doors,
        List<DoorPortal> portals, Vector3 home, Vector3? outdoors)
    {
        Snapshot = snapshot;
        Fireplaces = fireplaces;
        Beds = beds;
        Doors = doors;
        Portals = portals;
        Home = home;
        Outdoors = outdoors;
    }

    public CampSnapshot Snapshot { get; }

    /// <summary>The components behind <see cref="CampSnapshot.Fires"/>, same
    /// order.</summary>
    public List<Fireplace> Fireplaces { get; }

    /// <summary>The components behind <see cref="CampSnapshot.Beds"/>, same
    /// order.</summary>
    public List<Bed> Beds { get; }

    public List<Door> Doors { get; }

    /// <summary>The doorways behind <see cref="Doors"/>, same order.</summary>
    public List<DoorPortal> Portals { get; }

    public Vector3 Home { get; }

    /// <summary>A point in the open near home: where "outside" is. A spot is
    /// somewhere he may be when he could walk to it from here without a door
    /// he may not use - which is what keeps a companion out of a house whose
    /// doors are closed to him, even when his home bed is inside it. Null when
    /// no open ground could be found; then home stands in for it.</summary>
    public Vector3? Outdoors { get; }
}

/// <summary>A walk worked out in advance: the route, and the door on it, if
/// any, with where to stand on either side of it.</summary>
internal sealed class WalkPlan
{
    public readonly List<Vector3> Route = new List<Vector3>();

    /// <summary>After the door: from its far side to the destination.
    /// </summary>
    public readonly List<Vector3> AfterDoor = new List<Vector3>();

    public Door? Door;

    /// <summary>Whether the door was shut when planned, so the walk opens it
    /// and closes it again behind him. An open door he just walks through and
    /// leaves as he found it.</summary>
    public bool DoorShut;

    public Vector3 DoorCentre;

    /// <summary>Where he stands to open it, on his side.</summary>
    public Vector3 Approach;

    /// <summary>Where he steps through to, on the far side.</summary>
    public Vector3 Exit;

    public float Length;

    public void Clear()
    {
        Route.Clear();
        AfterDoor.Clear();
        Door = null;
        DoorShut = false;
        DoorCentre = Vector3.zero;
        Approach = Vector3.zero;
        Exit = Vector3.zero;
        Length = 0f;
    }

    public void CopyFrom(WalkPlan other)
    {
        Clear();
        Route.AddRange(other.Route);
        AfterDoor.AddRange(other.AfterDoor);
        Door = other.Door;
        DoorShut = other.DoorShut;
        DoorCentre = other.DoorCentre;
        Approach = other.Approach;
        Exit = other.Exit;
        Length = other.Length;
    }
}

/// <summary>Reads the camp and works out walks through it - the "deep
/// understanding of their surroundings" the owner asked every companion to
/// have, as the part that has to touch the game.
///
/// Everything it reads comes from the game's own registries - loaded pieces,
/// comfort pieces, the navmesh - and nothing it does changes the world. A walk
/// it plans may include opening a door, and only a door the owner lets
/// companions use; the walk itself does the opening, through the door's own
/// RPC.</summary>
internal sealed class CampSense
{
    /// <summary>How far from a doorway he stands to open it, and how far past
    /// it he steps.</summary>
    private const float ApproachMetres = 1.0f;

    private const float ExitMetres = 1.1f;

    /// <summary>How far a door may be from either end of a walk to be
    /// considered as the way through.</summary>
    private const float DoorSearchMetres = 20f;

    private readonly CompanionDoors _doors;
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly List<Vector3> _legA = new List<Vector3>();
    private readonly List<Vector3> _legB = new List<Vector3>();
    private readonly List<WorldPoint> _points = new List<WorldPoint>();

    public CampSense(CompanionDoors doors)
    {
        _doors = doors;
    }

    public CampView Scan(Vector3 home, float radius)
    {
        var fireplaces = new List<Fireplace>();
        var fires = new List<CampFire>();
        var beds = new List<Bed>();
        var campBeds = new List<CampBed>();

        _pieces.Clear();
        try
        {
            // Fires and beds are comfort pieces; the game keeps them in their own
            // short list.
            Piece.GetAllComfortPiecesInRadius(home, radius + 2f, _pieces);
        }
        catch
        {
            _pieces.Clear();
        }

        foreach (Piece piece in _pieces)
        {
            if (piece == null)
            {
                continue;
            }

            // A fire he would sit by is a comfort piece in the game's own Fire
            // group: campfires, hearths, bonfires, braziers. The hot tub also
            // burns fuel through a Fireplace but is Leisure, and torches are not
            // comfort pieces at all (read from the game's prefabs, 1.0.12).
            Fireplace? fire = piece.m_comfortGroup == Piece.ComfortGroup.Fire
                ? piece.GetComponent<Fireplace>()
                : null;
            if (fire != null)
            {
                Vector3 at = fire.transform.position;
                fireplaces.Add(fire);
                fires.Add(new CampFire(
                    new WorldPoint(at.x, at.y, at.z), IsBurning(fire), IsSheltered(at), HazardOf(fire)));
                continue;
            }

            Bed? bed = piece.GetComponent<Bed>();
            if (bed != null && bed.m_spawnPoint != null)
            {
                Vector3 at = bed.m_spawnPoint.position;
                beds.Add(bed);
                campBeds.Add(new CampBed(
                    new WorldPoint(at.x, at.y, at.z), bed.m_spawnPoint.rotation.eulerAngles.y,
                    IsClaimed(bed), IsSheltered(at)));
            }
        }

        var doors = new List<Door>();
        CompanionDoors.FindDoors(home, radius + DoorSearchMetres, doors);
        var portals = new List<DoorPortal>(doors.Count);
        foreach (Door door in doors)
        {
            portals.Add(_doors.PortalOf(door));
        }

        var snapshot = new CampSnapshot(
            new WorldPoint(home.x, home.y, home.z), IsNight(), IsWet(), fires, campBeds);
        return new CampView(snapshot, fireplaces, beds, doors, portals, home, FindOutdoors(home));
    }

    /// <summary>Plans a walk from <paramref name="from"/> to
    /// <paramref name="to"/> that goes through no door he may not use. False
    /// when there is none.</summary>
    public bool TryPlanWalk(Vector3 from, Vector3 to, CampView view, WalkPlan plan)
    {
        plan.Clear();

        if (TryRoute(from, to, _legA))
        {
            RouteDoorCheck check = Inspect(_legA, view);
            if (check.Verdict == RouteDoorVerdict.Clear)
            {
                plan.Route.AddRange(_legA);
                plan.Length = LengthOf(_legA);
                return true;
            }

            if (check.Verdict == RouteDoorVerdict.NeedsOpening &&
                TryThroughDoor(from, to, view, check.Door, plan))
            {
                return true;
            }
        }

        // No route, or one that goes where he may not: try each door he may use
        // near either end as the way through, nearest first.
        var order = new List<int>();
        for (int index = 0; index < view.Portals.Count; index++)
        {
            DoorPortal portal = view.Portals[index];
            if (!portal.IsPassable)
            {
                continue;
            }

            Vector3 centre = ToVector(portal.FloorCentre);
            if (Flat(from, centre) > DoorSearchMetres && Flat(to, centre) > DoorSearchMetres)
            {
                continue;
            }

            order.Add(index);
        }

        order.Sort((a, b) =>
        {
            Vector3 ca = ToVector(view.Portals[a].FloorCentre);
            Vector3 cb = ToVector(view.Portals[b].FloorCentre);
            return (Flat(from, ca) + Flat(ca, to)).CompareTo(Flat(from, cb) + Flat(cb, to));
        });

        foreach (int index in order)
        {
            if (TryThroughDoor(from, to, view, index, plan))
            {
                return true;
            }
        }

        plan.Clear();
        return false;
    }

    /// <summary>Whether he may be at <paramref name="point"/> at all: reachable
    /// from the open ground near home without a door he may not use.</summary>
    public bool IsAllowedPlace(Vector3 point, CampView view, WalkPlan scratch)
    {
        Vector3 origin = view.Outdoors ?? view.Home;
        return TryPlanWalk(origin, point, view, scratch);
    }

    private bool TryThroughDoor(Vector3 from, Vector3 to, CampView view, int index, WalkPlan plan)
    {
        if (index < 0 || index >= view.Portals.Count)
        {
            return false;
        }

        DoorPortal portal = view.Portals[index];
        WorldPoint fromPoint = ToPoint(from);
        int side = portal.SideOf(fromPoint);
        if (!portal.IsPassable || portal.SideOf(ToPoint(to)) == side)
        {
            return false;
        }

        Vector3 approach = Grounded(ToVector(portal.OutFrom(side, ApproachMetres)));
        Vector3 exit = Grounded(ToVector(portal.OutFrom(-side, ExitMetres)));

        if (!TryRoute(from, approach, _legA) || Inspect(_legA, view).Verdict != RouteDoorVerdict.Clear)
        {
            return false;
        }

        if (!TryRoute(exit, to, _legB) || Inspect(_legB, view).Verdict != RouteDoorVerdict.Clear)
        {
            return false;
        }

        plan.Clear();
        plan.Route.AddRange(_legA);
        plan.AfterDoor.AddRange(_legB);
        plan.Door = view.Doors[index];
        plan.DoorShut = !portal.IsOpen;
        plan.DoorCentre = ToVector(portal.FloorCentre);
        plan.Approach = approach;
        plan.Exit = exit;
        plan.Length = LengthOf(_legA) + Vector3.Distance(approach, exit) + LengthOf(_legB);
        return true;
    }

    private RouteDoorCheck Inspect(List<Vector3> route, CampView view)
    {
        _points.Clear();
        foreach (Vector3 corner in route)
        {
            _points.Add(ToPoint(corner));
        }

        return RouteDoors.Inspect(_points, view.Portals);
    }

    /// <summary>A way from <paramref name="from"/> to <paramref name="point"/>
    /// that does not go through anything solid.
    ///
    /// Asked of the game's own navmesh first - the pathfinding its creatures
    /// use, which knows walls from doorways - as a local query only: no BaseAI,
    /// nothing networked, nothing written. A full route or nothing. The navmesh
    /// builds its tiles on first request, so an early ask can come back empty
    /// for a place that is perfectly reachable; then, and only then, a straight
    /// line is accepted if nothing solid stands anywhere on it. Doors are
    /// checked by the caller either way.</summary>
    public static bool TryRoute(Vector3 from, Vector3 point, List<Vector3> route)
    {
        route.Clear();

        try
        {
            Pathfinding pathfinding = Pathfinding.instance;
            if (pathfinding != null &&
                pathfinding.GetPath(from, point, route, Pathfinding.AgentType.HumanoidNoSwim, requireFullPath: true) &&
                route.Count >= 2)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // A navmesh that will not answer is treated like one not built yet.
        }

        route.Clear();
        if (CompanionFooting.IsWayBlocked(from, point))
        {
            return false;
        }

        route.Add(from);
        route.Add(point);
        return true;
    }

    /// <summary>Whether that point has a roof over it: the game's own test, the
    /// one it uses to decide whether a player is sheltered from rain.</summary>
    public static bool IsSheltered(Vector3 point)
    {
        try
        {
            return Cover.IsUnderRoof(point + (Vector3.up * 0.5f));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsNight()
    {
        try
        {
            return EnvMan.instance != null && EnvMan.IsNight();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsWet()
    {
        try
        {
            return EnvMan.instance != null && EnvMan.IsWet();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Somebody's bed: an owner on its ZDO, read directly rather than
    /// through the bed's private accessors.</summary>
    public static bool IsClaimed(Bed bed)
    {
        try
        {
            ZNetView? view = bed.GetComponent<ZNetView>();
            ZDO? zdo = view == null ? null : view.GetZDO();
            return zdo == null || zdo.GetLong(ZDOVars.s_owner, 0L) != 0L;
        }
        catch (Exception)
        {
            // Not knowing whose it is makes it somebody's.
            return true;
        }
    }

    private static bool IsBurning(Fireplace fire)
    {
        try
        {
            return fire.IsBurning();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>How close to a fire's centre is too close: the furthest reach
    /// of its solid colliders, which for a vanilla fire includes the
    /// pathfinding blocker the game itself keeps creatures out of - 1 m for a
    /// fire pit, 2.4 m for a bonfire.</summary>
    private static float HazardOf(Fireplace fire)
    {
        float reach = 0.7f;
        try
        {
            Vector3 centre = fire.transform.position;
            foreach (Collider collider in fire.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                float dx = Mathf.Max(Mathf.Abs(bounds.max.x - centre.x), Mathf.Abs(bounds.min.x - centre.x));
                float dz = Mathf.Max(Mathf.Abs(bounds.max.z - centre.z), Mathf.Abs(bounds.min.z - centre.z));
                reach = Mathf.Max(reach, Mathf.Min(Mathf.Max(dx, dz), 4f));
            }
        }
        catch (Exception)
        {
            // The default reach stands.
        }

        return reach;
    }

    /// <summary>The nearest open ground to home with nothing overhead and room
    /// to stand, on a few rings around it.</summary>
    private static Vector3? FindOutdoors(Vector3 home)
    {
        float[] radii = { 6f, 10f, 14f };
        for (int ring = 0; ring < radii.Length; ring++)
        {
            for (int step = 0; step < 12; step++)
            {
                float angle = ((step * 30f) + (ring * 15f)) * Mathf.Deg2Rad;
                var candidate = new Vector3(
                    home.x + (Mathf.Cos(angle) * radii[ring]), home.y, home.z + (Mathf.Sin(angle) * radii[ring]));
                if (!CompanionFooting.TryFind(candidate, 6f, 12f, out Vector3 ground, out Vector3 normal) ||
                    Vector3.Dot(normal, Vector3.up) < 0.75f ||
                    IsSheltered(ground) ||
                    CompanionFooting.IsBodyObstructed(ground) ||
                    IsUnderWater(ground))
                {
                    continue;
                }

                return ground;
            }
        }

        return null;
    }

    private static bool IsUnderWater(Vector3 point)
    {
        try
        {
            return ZoneSystem.instance != null && point.y < ZoneSystem.instance.m_waterLevel - 0.3f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Vector3 Grounded(Vector3 point)
    {
        return CompanionFooting.TryFind(point, 2f, 4f, out Vector3 ground, out _) ? ground : point;
    }

    private static float LengthOf(List<Vector3> route)
    {
        float length = 0f;
        for (int index = 1; index < route.Count; index++)
        {
            length += Vector3.Distance(route[index - 1], route[index]);
        }

        return length;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    public static WorldPoint ToPoint(Vector3 v)
    {
        return new WorldPoint(v.x, v.y, v.z);
    }

    public static Vector3 ToVector(WorldPoint p)
    {
        return new Vector3(p.X, p.Y, p.Z);
    }
}
