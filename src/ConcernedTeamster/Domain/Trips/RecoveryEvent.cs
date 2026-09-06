namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>One sidecar recovery event this session — a plain-language
/// record of what happened, for the Support Bundle panel (CT-039). Never
/// contains a full path (the sidecar file name only) or any per-item/
/// per-trip content, so it carries nothing the bundle sanitizer would
/// otherwise need to strip.</summary>
public sealed class RecoveryEvent
{
    public RecoveryEvent(string reason, string sidecarFileName, string message)
    {
        Reason = reason;
        SidecarFileName = sidecarFileName;
        Message = message;
    }

    /// <summary>The <see cref="TripPersistPlan.Plan.BackupReason"/> that
    /// triggered this event ("refused", "malformed", or "migrate-v1").</summary>
    public string Reason { get; }

    /// <summary>The sidecar's file name only (for example
    /// "teamster_trips_1234.txt") — never a full path.</summary>
    public string SidecarFileName { get; }

    /// <summary>Plain-language description, safe to show the player
    /// as-is.</summary>
    public string Message { get; }
}
