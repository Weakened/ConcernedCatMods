using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>Doorways as geometry: which side he is on, whether a walk goes
/// through one, and what a route asks of the doors along it.</summary>
public sealed class DoorPortalTests
{
    // A door in a wall along the X axis at z = 0, facing +Z, floor at y = 10.
    private static DoorPortal DoorAt(
        float x = 0f, bool open = false, bool canOpen = true, bool allowed = true, float halfWidth = 0.9f)
    {
        return new DoorPortal(
            new DoorPlace(new WorldPoint(x, 11f, 0f), 1), new WorldPoint(x, 10f, 0f),
            normalX: 0f, normalZ: 1f, halfWidth, open, canOpen, allowed);
    }

    private static WorldPoint At(float x, float z, float y = 10f)
    {
        return new WorldPoint(x, y, z);
    }

    [Fact]
    public void WalkingThroughTheOpeningCrossesItAndWalkingAlongsideDoesNot()
    {
        DoorPortal door = DoorAt();

        Assert.True(door.IsCrossedBy(At(0f, -2f), At(0f, 2f)));
        Assert.True(door.IsCrossedBy(At(0.5f, 3f), At(-0.4f, -1f)));

        // Parallel to the wall, on one side.
        Assert.False(door.IsCrossedBy(At(-3f, 1f), At(3f, 1f)));

        // Through the wall beside the door is not through the door - the wall
        // stops that, not the doorway.
        Assert.False(door.IsCrossedBy(At(3f, -2f), At(3f, 2f)));

        // Up to the doorway and back is not through it.
        Assert.False(door.IsCrossedBy(At(0f, -2f), At(0f, -0.1f)));
    }

    [Fact]
    public void AWalkOnTheFloorAboveIsNotThroughTheDoorBelow()
    {
        DoorPortal door = DoorAt();
        Assert.False(door.IsCrossedBy(At(0f, -2f, y: 13f), At(0f, 2f, y: 13f)));
    }

    [Fact]
    public void SidesAndThePointsOutFromTheOpeningAgree()
    {
        DoorPortal door = DoorAt(x: 5f);
        Assert.Equal(1, door.SideOf(At(5f, 2f)));
        Assert.Equal(-1, door.SideOf(At(5f, -2f)));

        WorldPoint front = door.OutFrom(1, 1.1f);
        WorldPoint back = door.OutFrom(-1, 1.1f);
        Assert.Equal(1, door.SideOf(front));
        Assert.Equal(-1, door.SideOf(back));
        Assert.True(door.IsCrossedBy(front, back));
    }

    [Fact]
    public void AnOpenDoorHeMayUseIsClearAndAShutOneMustBeOpened()
    {
        var route = new List<WorldPoint> { At(0f, -5f), At(0f, -1f), At(0f, 1f), At(0f, 5f) };

        Assert.Equal(RouteDoorVerdict.Clear,
            RouteDoors.Inspect(route, new[] { DoorAt(open: true) }).Verdict);

        RouteDoorCheck shut = RouteDoors.Inspect(route, new[] { DoorAt(open: false) });
        Assert.Equal(RouteDoorVerdict.NeedsOpening, shut.Verdict);
        Assert.Equal(0, shut.Door);
        Assert.Equal(1, shut.Leg);
    }

    [Fact]
    public void ADoorHeMayNotUseForbidsTheRouteOpenOrShutWhereverItComes()
    {
        var route = new List<WorldPoint> { At(0f, -5f), At(0f, 5f), At(10f, 5f), At(10f, -5f) };

        // The second door on the way is off limits; the first is fine and shut.
        DoorPortal[] doors = { DoorAt(x: 0f, open: false), DoorAt(x: 10f, open: true, allowed: false) };
        RouteDoorCheck check = RouteDoors.Inspect(route, doors);
        Assert.Equal(RouteDoorVerdict.Forbidden, check.Verdict);
        Assert.Equal(1, check.Door);

        // A door he may use but could not open - a key, a guard stone - is no
        // way through either.
        Assert.Equal(RouteDoorVerdict.Forbidden,
            RouteDoors.Inspect(route, new[] { DoorAt(x: 0f, open: false, canOpen: false) }).Verdict);
    }

    [Fact]
    public void ARouteThatPassesNoDoorwayIsClear()
    {
        var route = new List<WorldPoint> { At(-5f, 3f), At(5f, 3f) };
        Assert.Equal(RouteDoorVerdict.Clear,
            RouteDoors.Inspect(route, new[] { DoorAt(allowed: false) }).Verdict);
        Assert.Equal(RouteDoorVerdict.Clear, RouteDoors.Inspect(route, new DoorPortal[0]).Verdict);
    }
}
