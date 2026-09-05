namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>Immutable per-trip summary (CT-018), computed once per sidecar
/// load so listing and sorting stay cheap at the maximum retained trip
/// count.</summary>
public sealed class TripSummary
{
    public TripSummary(
        int tripId,
        string cartId,
        double startTimeSeconds,
        double durationSeconds,
        float distanceMeters,
        float meanMass,
        float worstAbsGradePercent,
        float meanSpeedMetersPerSecond)
    {
        TripId = tripId;
        CartId = cartId;
        StartTimeSeconds = startTimeSeconds;
        DurationSeconds = durationSeconds;
        DistanceMeters = distanceMeters;
        MeanMass = meanMass;
        WorstAbsGradePercent = worstAbsGradePercent;
        MeanSpeedMetersPerSecond = meanSpeedMetersPerSecond;
    }

    public int TripId { get; }

    public string CartId { get; }

    public double StartTimeSeconds { get; }

    public double DurationSeconds { get; }

    public float DistanceMeters { get; }

    public float MeanMass { get; }

    /// <summary>NaN when no sample had a finite grade.</summary>
    public float WorstAbsGradePercent { get; }

    /// <summary>NaN when no sample had a finite speed.</summary>
    public float MeanSpeedMetersPerSecond { get; }
}
