using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Immutable answer to "will this descent stay controlled?"
/// (CT-011) — the risk level, the basis of the deciding row (null for
/// Unknown), and an explanation naming that row. Levels are
/// hysteresis-friendly: stable enums evaluated per snapshot; the warning
/// layer (CT-013/CT-014) applies enter/exit dynamics on top.</summary>
public sealed class RiskVerdict
{
    public RiskVerdict(RiskLevel level, CalibrationBasis? basis, string explanation)
    {
        Level = level;
        Basis = basis;
        Explanation = explanation;
    }

    public RiskLevel Level { get; }

    public CalibrationBasis? Basis { get; }

    public string Explanation { get; }
}
