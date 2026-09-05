namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>One immutable diagnosis with its evidence and suggested action
/// (CT-013). The composed line carries a non-color cue and always names the
/// evidence — a diagnosis the panel cannot explain is not shown.</summary>
public sealed class CartDiagnostic
{
    public static readonly CartDiagnostic None = new(
        CartDiagnosis.None, string.Empty, string.Empty);

    public CartDiagnostic(CartDiagnosis diagnosis, string evidence, string action)
    {
        Diagnosis = diagnosis;
        Evidence = evidence;
        Action = action;
    }

    public CartDiagnosis Diagnosis { get; }

    public string Evidence { get; }

    public string Action { get; }

    public string ComposeLine()
    {
        if (Diagnosis == CartDiagnosis.None)
        {
            return string.Empty;
        }

        string label = Diagnosis switch
        {
            CartDiagnosis.ImpossibleLoad => "overloaded for this grade",
            CartDiagnosis.MarginalLoad => "load is marginal here",
            CartDiagnosis.SteepClimb => "steep climb",
            CartDiagnosis.Obstruction => "obstruction or grounded chassis",
            _ => "cause unclear",
        };
        return "[?] STUCK — " + label + ": " + Evidence + " " + Action;
    }
}
