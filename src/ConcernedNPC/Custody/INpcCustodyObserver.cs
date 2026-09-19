namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>The actual state of the places the record describes, as the game
/// can see them now.</summary>
internal interface INpcCustodyObserver
{
    /// <summary>How many units of <paramref name="material"/> are actually at
    /// <paramref name="location"/> - counting only material this job is
    /// responsible for, so a container's pre-existing contents are the role's
    /// to exclude - <b>or null when the place cannot be observed now</b>: not
    /// loaded, from another world load, no body.
    ///
    /// <b>Null is the important value.</b> A place that cannot be checked must
    /// not read as zero: zero is a shortfall, and a shortfall a player is asked
    /// to record as lost, for material that is sitting safely in an unloaded
    /// zone.</summary>
    int? Observe(NpcCustodyLocation location, NpcMaterial material);
}
