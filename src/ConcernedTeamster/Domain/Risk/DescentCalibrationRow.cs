using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>One immutable descent observation (CT-011): at this downgrade,
/// total cart mass, and entry speed, this outcome happened (or is derived/
/// assumed, per the basis — the same basis vocabulary as load rows).</summary>
public sealed class DescentCalibrationRow
{
    public DescentCalibrationRow(
        float downGradePercent,
        float totalMass,
        float entrySpeedMetersPerSecond,
        DescentOutcome outcome,
        CalibrationBasis basis,
        string note)
    {
        DownGradePercent = downGradePercent;
        TotalMass = totalMass;
        EntrySpeedMetersPerSecond = entrySpeedMetersPerSecond;
        Outcome = outcome;
        Basis = basis;
        Note = note;
    }

    /// <summary>Descent magnitude: positive means downhill ahead.</summary>
    public float DownGradePercent { get; }

    public float TotalMass { get; }

    public float EntrySpeedMetersPerSecond { get; }

    public DescentOutcome Outcome { get; }

    public CalibrationBasis Basis { get; }

    public string Note { get; }
}
