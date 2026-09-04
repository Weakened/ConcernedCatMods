namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>Climbability verdict (CT-008). Unknown is a first-class answer:
/// the model refuses to extrapolate beyond its calibration rows.</summary>
public enum Climbability
{
    No,
    Unknown,
    Marginal,
    Yes,
}
