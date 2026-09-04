namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>Outcome vocabulary of a calibration row (CT-008), matching the
/// protocol document exactly.</summary>
public enum CalibrationOutcome
{
    Climbs,
    Marginal,
    Stalls,
    JointBreak,
}
