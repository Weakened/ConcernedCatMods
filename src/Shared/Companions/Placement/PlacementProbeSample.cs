namespace TheConcernedCat.Companions.Placement;

/// <summary>What the game reports about one candidate position.
///
/// A sample says what is there, not whether it is acceptable. Deciding is
/// <see cref="PlacementPlanner"/>'s job, which is what lets every placement
/// rule be unit-tested against invented samples with no game running.</summary>
internal readonly struct PlacementProbeSample
{
    public PlacementProbeSample(
        WorldPoint position,
        PlacementRejection rejections,
        float distanceToFire,
        SeatAvailability seat)
    {
        Position = position;
        Rejections = rejections;
        DistanceToFire = distanceToFire;
        Seat = seat;
    }

    /// <summary>The probed position, with its height corrected to the ground
    /// the probe actually found.</summary>
    public WorldPoint Position { get; }

    public PlacementRejection Rejections { get; }

    /// <summary>Horizontal distance to the nearest usable fire, or a negative
    /// value when there is none nearby. Warmth is a preference, never a
    /// requirement.</summary>
    public float DistanceToFire { get; }

    /// <summary>Whether a seat is usable here, purely as a visual pose.</summary>
    public SeatAvailability Seat { get; }

    public bool IsUsable => Rejections == PlacementRejection.None;

    public bool HasFireNearby => DistanceToFire >= 0f;

    public static PlacementProbeSample NotLoaded(WorldPoint position)
    {
        return new PlacementProbeSample(
            position, PlacementRejection.NotLoaded, -1f, SeatAvailability.None);
    }
}
