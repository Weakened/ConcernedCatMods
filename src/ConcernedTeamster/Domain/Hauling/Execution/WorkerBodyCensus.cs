namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>Where Gunnar's one body stands, from the adapter's census of the
/// world's persisted worker objects and the live bodies loaded now. Zero is
/// unspecified.</summary>
internal enum WorkerBodyStatus
{
    Unspecified = 0,

    /// <summary>The census of saved objects has not finished; nothing may be
    /// spawned until it has, or a second Gunnar could be created.</summary>
    Searching = 1,

    /// <summary>No body carries Gunnar's identity anywhere in this world.
    /// </summary>
    NotFound = 2,

    /// <summary>Exactly one body, loaded here and working.</summary>
    Bound = 3,

    /// <summary>Exactly one body, saved in ground that is not loaded. He is not
    /// replaced: the player goes to him.</summary>
    NotLoaded = 4,

    /// <summary>More than one body carries Gunnar's identity. Neither is
    /// destroyed automatically; a person decides (CONTRACTS.md §2.6).</summary>
    Duplicated = 5,

    /// <summary>His one body is loaded but its worker faulted and is inert.
    /// </summary>
    Faulted = 6,
}

/// <summary>The binding rule for Gunnar's body (CONTRACTS.md §2.6, DECISIONS.md
/// D9): re-bound by identity on world load, never duplicated, never
/// destroyed to resolve a duplicate.</summary>
internal static class WorkerBodyCensus
{
    /// <param name="censusComplete">The scan of saved objects finished.</param>
    /// <param name="distinctBodies">Distinct network ids carrying Gunnar's key,
    /// saved or live.</param>
    /// <param name="loadedBodies">Of those, bodies loaded and owned here now.
    /// </param>
    /// <param name="boundFaulted">The loaded body's worker latched a fault.
    /// </param>
    public static WorkerBodyStatus Decide(bool censusComplete, int distinctBodies, int loadedBodies, bool boundFaulted)
    {
        if (distinctBodies >= 2 || loadedBodies >= 2)
        {
            return WorkerBodyStatus.Duplicated;
        }

        if (loadedBodies == 1)
        {
            return boundFaulted ? WorkerBodyStatus.Faulted : WorkerBodyStatus.Bound;
        }

        if (!censusComplete)
        {
            return WorkerBodyStatus.Searching;
        }

        return distinctBodies == 1 ? WorkerBodyStatus.NotLoaded : WorkerBodyStatus.NotFound;
    }

    /// <summary>A new body may be created only when the census is complete and
    /// found none.</summary>
    public static bool MaySpawn(WorkerBodyStatus status) => status == WorkerBodyStatus.NotFound;

    /// <summary>Whether the runtime may bind or unbind a body this frame. Never
    /// while a cart's joint holds the current one: the joint is released first,
    /// and the release needs the body it is connected to (review R-313 B1;
    /// CONTRACTS.md §2.5, "before the body is unbound").</summary>
    public static bool MayChangeBinding(bool jointHeld) => !jointHeld;

    public static string Describe(WorkerBodyStatus status)
    {
        switch (status)
        {
            case WorkerBodyStatus.Searching:
                return "Looking for Gunnar in this world.";
            case WorkerBodyStatus.NotFound:
                return "Gunnar is not in this world yet.";
            case WorkerBodyStatus.Bound:
                return "Gunnar is here and ready.";
            case WorkerBodyStatus.NotLoaded:
                return "Gunnar is somewhere that is not loaded; go to where you left him.";
            case WorkerBodyStatus.Duplicated:
                return "There is more than one Gunnar in this world; nothing is removed automatically, so retire the extra one.";
            case WorkerBodyStatus.Faulted:
                return "Gunnar ran into an error and stopped; the log has the details.";
            default:
                return HaulRefusalSentences.BugSentence;
        }
    }
}
