namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Descent risk level (CT-011), ordered by increasing risk so the
/// monotonicity property ("risk never decreases with difficulty") is a
/// simple enum comparison. Unknown sits between Safe and Caution: it is
/// riskier than proven-safe and less alarming than a witnessed drag.</summary>
public enum RiskLevel
{
    Safe,
    Unknown,
    Caution,
    Danger,
}
