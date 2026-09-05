namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Outcome vocabulary of a descent calibration row (CT-011),
/// matching the protocol document exactly.</summary>
public enum DescentOutcome
{
    Held,
    Dragged,
    Runaway,
    JointBreak,
}
