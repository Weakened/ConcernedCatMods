using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>What the camp routine asks of the world beyond the ground under a
/// spot (#461). The boundary answers from bounded read-only queries; nothing
/// here opens, claims or writes anything.</summary>
internal interface IGunnarRestSurroundings
{
    /// <summary>Increases whenever the pieces loaded around home changed, so a
    /// spot chosen before something was built or taken down is asked again.
    /// </summary>
    int Revision { get; }

    /// <summary>The doorways near home as of the last look. Teamster keeps no
    /// list of doors opened to companions, so every one says it is not
    /// allowed.</summary>
    IReadOnlyList<DoorPortal> Doors { get; }

    /// <summary>Other products' keepsakes to keep clear of, or null when they
    /// could not be read (the plan then defers).</summary>
    IReadOnlyList<WorldPoint>? OtherKeepsakes(CompanionAnchor anchor);

    /// <summary>True when something solid stands between home and the spot at
    /// chest height - a wall, a shut door - and true when that could not be
    /// asked.</summary>
    bool IsWallBetween(WorldPoint home, WorldPoint spot);

    /// <summary>True when a standing figure fits at the spot: nothing solid
    /// from knee to head height. False when that could not be asked.</summary>
    bool HasRoomToStand(WorldPoint spot);
}

/// <summary>The limits on where Gunnar's figure is set down that the ground
/// itself cannot tell the probe (#461). Game-free.
///
/// <b>The door rule</b> is the lead default recorded in
/// <c>docs/settlement/cart-and-collection/DECISIONS.md</c> ("a resting figure
/// and doors", #295 S5, #461), applied exactly as Thorstein's figure applies
/// it. His figure never walks and never touches a door: it is set down at a
/// spot. So it is never set down on the far side of a doorway, or of a wall,
/// from home. The doorway half is the shared
/// <see cref="RouteDoors.Inspect"/> on the straight line from home to the spot:
/// anything but <see cref="RouteDoorVerdict.Clear"/> refuses it, because a door
/// he would need to open is a door he does not pass. Teamster keeps no list of
/// doors opened to companions, so every door is one he may not use, which is
/// the rule's own default.
///
/// A spot in or right beside a doorway is refused too
/// (<see cref="BesideADoorway"/>): the decision's first line.
///
/// <b>Camp</b>, once established (a home and at least one player-built piece),
/// bounds the spot; before that, home alone decides.</summary>
internal static class GunnarRestBounds
{
    /// <summary>How far from a doorway's opening, either way and past either
    /// jamb, a spot counts as in or right beside it: the clearance Thorstein's
    /// probe keeps from a door's collider.</summary>
    public const float DoorwayClearanceMetres = 1.5f;

    /// <summary>True when <paramref name="spot"/> is in or right beside one of
    /// <paramref name="doors"/>: within <see cref="DoorwayClearanceMetres"/> of
    /// the opening's plane, no further than that past its jambs, and at the
    /// doorway's height. Nobody is set down where a door swings or a player
    /// walks through.</summary>
    public static bool BesideADoorway(WorldPoint spot, IReadOnlyList<DoorPortal>? doors)
    {
        if (doors == null) return false;
        for (int index = 0; index < doors.Count; index++)
        {
            DoorPortal door = doors[index];
            float dx = spot.X - door.FloorCentre.X, dz = spot.Z - door.FloorCentre.Z;
            float along = (dx * door.NormalX) + (dz * door.NormalZ);
            float lateral = (dx * -door.NormalZ) + (dz * door.NormalX);
            if (System.Math.Abs(along) < DoorwayClearanceMetres &&
                System.Math.Abs(lateral) < door.HalfWidth + DoorwayClearanceMetres &&
                System.Math.Abs(spot.Y - door.FloorCentre.Y) <= DoorPortal.HeightBand) return true;
        }

        return false;
    }

    /// <summary>True when setting him down at <paramref name="spot"/> would put
    /// a doorway he may not pass between him and <paramref name="home"/>.
    /// <paramref name="line"/> is a caller-owned two-point buffer, so the
    /// check allocates nothing.</summary>
    public static bool AcrossADoor(WorldPoint home, WorldPoint spot, IReadOnlyList<DoorPortal>? doors, WorldPoint[] line)
    {
        if (doors == null || doors.Count == 0) return false;
        line[0] = home;
        line[1] = spot;
        return RouteDoors.Inspect(line, doors).Verdict != RouteDoorVerdict.Clear;
    }

    /// <summary>Which of the pieces loaded around home camp is built from: a
    /// piece a player placed, and neither a terrain tool's piece nor a crop.
    /// Everything else - a terrain edit, a planted field, a piece the world
    /// generated - is refused at camp's door.</summary>
    public static bool IsCampPiece(bool placedByPlayer, bool terrainOp, bool crop) =>
        placedByPlayer && !terrainOp && !crop;
}
