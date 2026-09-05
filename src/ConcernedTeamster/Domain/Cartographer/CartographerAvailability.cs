namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>Outcome of the Concerned Cartographer runtime capability probe
/// (CT-021). Only <see cref="Available"/> lets integration features appear;
/// every other state hides them behind one INFO line and nothing errors.</summary>
public enum CartographerAvailability
{
    /// <summary>Cartographer is installed, meets the version floor, and every
    /// contract member verified — route features may appear.</summary>
    Available = 1,

    /// <summary>Cartographer is not installed. Teamster runs standalone.</summary>
    Absent = 2,

    /// <summary>Cartographer is installed but older than the supported floor
    /// (see CartographerContract.FloorVersion).</summary>
    VersionTooLow = 3,

    /// <summary>Cartographer is installed and new enough, but the read
    /// surface Teamster relies on did not verify (a member moved, the lookup
    /// threw, or the plugin instance was unavailable).</summary>
    ProbeFailed = 4,
}
