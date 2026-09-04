namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>Immutable answer to "can this cart mass climb this grade?"
/// (CT-008) — the verdict, the basis of the deciding calibration row (None
/// for Unknown), and a human-readable explanation naming that row so panels
/// can explain every number they show.</summary>
public sealed class LoadVerdict
{
    public LoadVerdict(Climbability climbability, CalibrationBasis? basis, string explanation)
    {
        Climbability = climbability;
        Basis = basis;
        Explanation = explanation;
    }

    public Climbability Climbability { get; }

    /// <summary>Basis of the deciding row; null when no row decides
    /// (Unknown).</summary>
    public CalibrationBasis? Basis { get; }

    public string Explanation { get; }
}
