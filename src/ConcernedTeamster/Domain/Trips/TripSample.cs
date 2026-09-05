namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>One recorded moment of a trip (CT-016): where the cart was,
/// how steep it stood, how fast it moved, what it weighed. Grade is NaN
/// when it was unavailable at sampling time — persisted as an explicit
/// marker, never as a fake zero.</summary>
public readonly struct TripSample
{
    public TripSample(
        double timeSeconds,
        float positionX,
        float positionZ,
        float gradePercent,
        float speedMetersPerSecond,
        float totalMass)
    {
        TimeSeconds = timeSeconds;
        PositionX = positionX;
        PositionZ = positionZ;
        GradePercent = gradePercent;
        SpeedMetersPerSecond = speedMetersPerSecond;
        TotalMass = totalMass;
    }

    public double TimeSeconds { get; }

    public float PositionX { get; }

    public float PositionZ { get; }

    /// <summary>Signed grade along the heading; NaN = unavailable.</summary>
    public float GradePercent { get; }

    public float SpeedMetersPerSecond { get; }

    public float TotalMass { get; }
}
