namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>One immutable calibration observation (CT-008): at this grade
/// and total cart mass, this outcome happened (or is derived/assumed, per
/// the basis).</summary>
public sealed class CalibrationRow
{
    public CalibrationRow(
        float gradePercent,
        float totalMass,
        CalibrationOutcome outcome,
        CalibrationBasis basis,
        string note)
    {
        GradePercent = gradePercent;
        TotalMass = totalMass;
        Outcome = outcome;
        Basis = basis;
        Note = note;
    }

    public float GradePercent { get; }

    public float TotalMass { get; }

    public CalibrationOutcome Outcome { get; }

    public CalibrationBasis Basis { get; }

    public string Note { get; }
}
