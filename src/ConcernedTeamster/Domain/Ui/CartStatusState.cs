namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>What the Cart Status panel is actually showing (CT-005), so
/// empty and stale situations render explicitly instead of freezing old
/// numbers.</summary>
public enum CartStatusState
{
    /// <summary>Telemetry is not running at all (defensive; normally the
    /// panel is not armed in this situation).</summary>
    TelemetryOff,

    /// <summary>No cart is currently tracked near the player.</summary>
    NoCart,

    /// <summary>Fresh telemetry is displayed.</summary>
    Live,

    /// <summary>The selected cart's telemetry is older than the staleness
    /// window; values are shown but visibly marked stale.</summary>
    Stale,
}
