using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.Companions.Surroundings;

/// <summary>A doorway as a companion sees it: an opening in a wall, with a
/// side to it, that he may or may not walk through.
///
/// The geometry is the game's own. A vanilla door piece is centred on its
/// opening - <c>wood_door</c>'s snap points sit at ±1 m either side and 1 m
/// above and below its origin - and it faces along its forward axis, the axis
/// <c>Door.Open</c> itself measures which way to swing against. So the opening
/// is the plane through the door's centre across that axis, and "through the
/// door" means from one side of that plane to the other, close enough to the
/// centre to be in the opening rather than in the wall beside it.</summary>
internal readonly struct DoorPortal
{
    /// <summary>How far above or below the doorway's floor a crossing may be
    /// and still be a walk through it rather than a pass over or under.
    /// </summary>
    public const float HeightBand = 1.2f;

    public DoorPortal(
        DoorPlace place, WorldPoint floorCentre, float normalX, float normalZ, float halfWidth,
        bool isOpen, bool canOpen, bool allowed)
    {
        float length = (float)Math.Sqrt((normalX * normalX) + (normalZ * normalZ));
        if (length < 0.0001f || float.IsNaN(length))
        {
            normalX = 0f;
            normalZ = 1f;
            length = 1f;
        }

        Place = place;
        FloorCentre = floorCentre;
        NormalX = normalX / length;
        NormalZ = normalZ / length;
        HalfWidth = Math.Max(0.1f, halfWidth);
        IsOpen = isOpen;
        CanOpen = canOpen;
        Allowed = allowed;
    }

    public DoorPlace Place { get; }

    /// <summary>The middle of the opening, at floor height.</summary>
    public WorldPoint FloorCentre { get; }

    public float NormalX { get; }

    public float NormalZ { get; }

    /// <summary>Half the width of the opening, generously.</summary>
    public float HalfWidth { get; }

    public bool IsOpen { get; }

    /// <summary>Whether a player could open it now: no key, closable again,
    /// guard-stone access allowed. A door that is already open needs no
    /// opening.</summary>
    public bool CanOpen { get; }

    /// <summary>Whether its owner lets companions through.</summary>
    public bool Allowed { get; }

    /// <summary>Whether a companion may pass here at all: allowed, and either
    /// open or openable by him.</summary>
    public bool IsPassable => Allowed && (IsOpen || CanOpen);

    /// <summary>+1 on the side the door faces, -1 behind it, measured flat.
    /// A point exactly in the plane counts as in front.</summary>
    public int SideOf(WorldPoint point)
    {
        return SignedDistance(point) >= 0f ? 1 : -1;
    }

    /// <summary>A point <paramref name="metres"/> out from the opening, on
    /// <paramref name="side"/>, at floor height.</summary>
    public WorldPoint OutFrom(int side, float metres)
    {
        float along = side >= 0 ? metres : -metres;
        return new WorldPoint(
            FloorCentre.X + (NormalX * along), FloorCentre.Y, FloorCentre.Z + (NormalZ * along));
    }

    /// <summary>Whether walking straight from <paramref name="from"/> to
    /// <paramref name="to"/> goes through this doorway.</summary>
    public bool IsCrossedBy(WorldPoint from, WorldPoint to)
    {
        float a = SignedDistance(from);
        float b = SignedDistance(to);
        if ((a > 0f && b > 0f) || (a < 0f && b < 0f) || a == b)
        {
            return false;
        }

        float t = a / (a - b);
        float x = from.X + ((to.X - from.X) * t);
        float y = from.Y + ((to.Y - from.Y) * t);
        float z = from.Z + ((to.Z - from.Z) * t);

        float lateral = Math.Abs(((x - FloorCentre.X) * -NormalZ) + ((z - FloorCentre.Z) * NormalX));
        return lateral <= HalfWidth && Math.Abs(y - FloorCentre.Y) <= HeightBand;
    }

    private float SignedDistance(WorldPoint point)
    {
        return ((point.X - FloorCentre.X) * NormalX) + ((point.Z - FloorCentre.Z) * NormalZ);
    }
}

/// <summary>What a route asks of the doors along it.</summary>
internal enum RouteDoorVerdict
{
    /// <summary>No doorway in the way, or only open ones he may use.</summary>
    Clear = 0,

    /// <summary>It passes a door he may use but that is shut. The walk has to
    /// open it - see <see cref="RouteDoorCheck.Door"/>.</summary>
    NeedsOpening = 1,

    /// <summary>It passes a door he may not use, open or shut. Not a way he
    /// goes.</summary>
    Forbidden = 2,
}

/// <summary>The answer for one route: the verdict, the door it is about, and
/// the leg of the route that goes through it.</summary>
internal readonly struct RouteDoorCheck
{
    public RouteDoorCheck(RouteDoorVerdict verdict, int doorIndex, int legIndex)
    {
        Verdict = verdict;
        Door = doorIndex;
        Leg = legIndex;
    }

    public static RouteDoorCheck Clear => new RouteDoorCheck(RouteDoorVerdict.Clear, -1, -1);

    public RouteDoorVerdict Verdict { get; }

    /// <summary>Index into the doors that were checked, or -1.</summary>
    public int Door { get; }

    /// <summary>The route leg (from corner <c>Leg</c> to <c>Leg + 1</c>) that
    /// crosses it, or -1.</summary>
    public int Leg { get; }
}

/// <summary>Reads a route against the doors around it.</summary>
internal static class RouteDoors
{
    /// <summary>Walks <paramref name="route"/> leg by leg, in order. A door he
    /// may not use anywhere on it makes the whole route forbidden, wherever it
    /// comes: a way that goes through somebody's closed-off house is not a way
    /// home just because the first half is fine. Otherwise the first shut door
    /// he may use is reported, so the walk can plan to open it.</summary>
    public static RouteDoorCheck Inspect(IReadOnlyList<WorldPoint> route, IReadOnlyList<DoorPortal> doors)
    {
        if (route == null || doors == null || route.Count < 2 || doors.Count == 0)
        {
            return RouteDoorCheck.Clear;
        }

        RouteDoorCheck firstShut = RouteDoorCheck.Clear;
        for (int leg = 0; leg + 1 < route.Count; leg++)
        {
            for (int index = 0; index < doors.Count; index++)
            {
                DoorPortal door = doors[index];
                if (!door.IsCrossedBy(route[leg], route[leg + 1]))
                {
                    continue;
                }

                if (!door.IsPassable)
                {
                    return new RouteDoorCheck(RouteDoorVerdict.Forbidden, index, leg);
                }

                if (!door.IsOpen && firstShut.Verdict == RouteDoorVerdict.Clear)
                {
                    firstShut = new RouteDoorCheck(RouteDoorVerdict.NeedsOpening, index, leg);
                }
            }
        }

        return firstShut;
    }
}
