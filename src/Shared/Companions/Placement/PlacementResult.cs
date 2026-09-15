namespace TheConcernedCat.Companions.Placement;

/// <summary>The outcome of planning a companion's spot.</summary>
internal readonly struct PlacementResult
{
    private PlacementResult(
        bool found, WorldPoint position, CompanionPose pose, int candidatesProbed,
        PlacementRejection blockedBy, SeatOffer seat)
    {
        Found = found;
        Position = position;
        Pose = pose;
        CandidatesProbed = candidatesProbed;
        BlockedBy = blockedBy;
        Seat = seat;
    }

    /// <summary>True when a spot was found. False means defer and try again
    /// later - never place anyway.</summary>
    public bool Found { get; }

    public WorldPoint Position { get; }
    public CompanionPose Pose { get; }

    /// <summary>How many candidates were inspected. Bounds are part of the
    /// contract, so this is reported rather than hidden.</summary>
    public int CandidatesProbed { get; }

    /// <summary>The union of everything that ruled candidates out, when nothing
    /// was found. Gives an actionable log line instead of silence.</summary>
    public PlacementRejection BlockedBy { get; }

    /// <summary>The seat this spot sits on, when the pose is
    /// <see cref="CompanionPose.SitOnSeat"/>. The presentation layer needs the
    /// seat's own attachment point, not the probed ground under it.</summary>
    public SeatOffer Seat { get; }

    public static PlacementResult Placed(
        WorldPoint position, CompanionPose pose, int candidatesProbed, SeatOffer seat = default)
    {
        return new PlacementResult(
            true, position, pose, candidatesProbed, PlacementRejection.None, seat);
    }

    public static PlacementResult Deferred(int candidatesProbed, PlacementRejection blockedBy)
    {
        return new PlacementResult(
            false, default, CompanionPose.SitOnGround, candidatesProbed, blockedBy, SeatOffer.None);
    }
}
