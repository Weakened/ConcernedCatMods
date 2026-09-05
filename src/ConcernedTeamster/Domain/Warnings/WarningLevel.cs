namespace TheConcernedCat.ConcernedTeamster.Domain.Warnings;

/// <summary>Warning severity (CT-009). Order matters: rises are immediate,
/// falls are held (hysteresis), and comparisons use this ordering.</summary>
public enum WarningLevel
{
    None,
    Caution,
    Danger,
}
