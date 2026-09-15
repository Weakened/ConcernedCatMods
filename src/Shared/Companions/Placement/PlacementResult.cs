namespace TheConcernedCat.Companions.Placement;

/// <summary>The outcome of planning a companion's spot.</summary>
internal readonly struct PlacementResult
{
    private PlacementResult(
        bool found, WorldPoint position, CompanionPose pose, int candidatesProbed,
        PlacementRejection blockedBy)
    {
        Found = found;
        Position = position;
        Pose = pose;
        CandidatesProbed = candidatesProbed;
        BlockedBy = blockedBy;
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

    public static PlacementResult Placed(WorldPoint position, CompanionPose pose, int candidatesProbed)
    {
        return new PlacementResult(true, position, pose, candidatesProbed, PlacementRejection.None);
    }

    public static PlacementResult Deferred(int candidatesProbed, PlacementRejection blockedBy)
    {
        return new PlacementResult(
            false, default, CompanionPose.SitOnGround, candidatesProbed, blockedBy);
    }
}
