using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using TheConcernedCat.ConcernedForeman.Domain.Npc;

namespace ConcernedForeman.Tests;

/// <summary>#295 S5: the door rule and camp bound where Thorstein may be set
/// down. A doorway is the shared <see cref="DoorPortal"/>; nothing here runs
/// the game.</summary>
public sealed class ThorsteinRestBoundsTests
{
    private static readonly WorldPoint Home = new WorldPoint(0f, 0f, 0f);

    /// <summary>A doorway 3 m east of home, its opening across the east axis.
    /// </summary>
    private static DoorPortal Door(bool allowed = false, bool open = false, float halfWidth = 1f) =>
        new DoorPortal(
            new DoorPlace(new WorldPoint(3f, 0f, 0f), 0),
            new WorldPoint(3f, 0f, 0f),
            1f,
            0f,
            halfWidth,
            isOpen: open,
            canOpen: true,
            allowed: allowed);

    [Fact]
    public void He_is_never_set_down_on_the_far_side_of_a_doorway_from_home()
    {
        var doors = new List<DoorPortal> { Door() };

        Assert.True(ThorsteinRestBounds.AcrossADoor(Home, new WorldPoint(5f, 0f, 0f), doors));
        Assert.False(ThorsteinRestBounds.AcrossADoor(Home, new WorldPoint(2f, 0f, 0f), doors));
        Assert.False(ThorsteinRestBounds.AcrossADoor(Home, new WorldPoint(-4f, 0f, 0f), doors));
    }

    [Fact]
    public void An_open_door_is_still_a_door_he_may_not_pass_unless_it_was_allowed()
    {
        var spot = new WorldPoint(5f, 0f, 0f);

        Assert.True(ThorsteinRestBounds.AcrossADoor(Home, spot, new List<DoorPortal> { Door(open: true) }));
        Assert.True(ThorsteinRestBounds.AcrossADoor(Home, spot, new List<DoorPortal> { Door(allowed: true) }));
        Assert.False(ThorsteinRestBounds.AcrossADoor(Home, spot, new List<DoorPortal> { Door(allowed: true, open: true) }));
    }

    [Fact]
    public void A_wall_beside_the_doorway_is_not_the_doorway()
    {
        // Crossing the door's plane 4 m to one side of its 1 m half-width is
        // going round, not through. Walls are the probe's to refuse.
        var doors = new List<DoorPortal> { Door() };
        Assert.False(ThorsteinRestBounds.AcrossADoor(new WorldPoint(0f, 0f, 4f), new WorldPoint(5f, 0f, 4f), doors));
    }

    [Fact]
    public void No_doors_known_is_no_door_in_the_way()
    {
        Assert.False(ThorsteinRestBounds.AcrossADoor(Home, new WorldPoint(5f, 0f, 0f), new List<DoorPortal>()));
    }

    [Fact]
    public void Inside_an_established_camp_only_and_anywhere_before_one_exists()
    {
        var none = new List<DoorPortal>();
        var spot = new WorldPoint(2f, 0f, 0f);

        Assert.True(ThorsteinRestBounds.Admits(Home, spot, campEstablished: false, insideCamp: false, none));
        Assert.True(ThorsteinRestBounds.Admits(Home, spot, campEstablished: true, insideCamp: true, none));
        Assert.False(ThorsteinRestBounds.Admits(Home, spot, campEstablished: true, insideCamp: false, none));
        Assert.False(ThorsteinRestBounds.Admits(
            Home, new WorldPoint(5f, 0f, 0f), campEstablished: true, insideCamp: true, new List<DoorPortal> { Door() }));
    }

    [Fact]
    public void The_planner_offers_the_best_spot_he_may_have_and_never_one_behind_the_door()
    {
        // Warmth lies only beyond the doorway (made as wide as the band, so
        // every warm candidate is through it); the planner must pass it over
        // for cold ground on home's side rather than relax the door rule.
        var doors = new List<DoorPortal> { Door(halfWidth: 12f) };
        var unbounded = new PlacementPlanner(ThorsteinRestPlan.Rules).Plan(
            new CompanionAnchor(AnchorKind.ClaimedBed, Home), new WarmBeyondTheDoor());
        Assert.Equal(CompanionPose.SitByFire, unbounded.Pose);

        var planner = new PlacementPlanner(ThorsteinRestPlan.Rules);
        PlacementResult result = planner.Plan(
            new CompanionAnchor(AnchorKind.ClaimedBed, Home),
            new WarmBeyondTheDoor(),
            accept: sample => ThorsteinRestBounds.Admits(Home, sample.Position, false, false, doors));

        Assert.True(result.Found);
        Assert.False(ThorsteinRestBounds.AcrossADoor(Home, result.Position, doors));
        Assert.Equal(CompanionPose.SitOnGround, result.Pose);
    }

    private sealed class WarmBeyondTheDoor : IPlacementProbe
    {
        public PlacementProbeSample Probe(WorldPoint position) =>
            new PlacementProbeSample(position, PlacementRejection.None, position.X > 3f ? 0f : -1f, SeatOffer.None);
    }
}
