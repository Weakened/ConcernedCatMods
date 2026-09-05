using TheConcernedCat.ConcernedTeamster.Domain.Localization;

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

        string label = TeamsterStrings.Get(Diagnosis switch
        {
            CartDiagnosis.ImpossibleLoad => "diag.labelImpossibleLoad",
            CartDiagnosis.MarginalLoad => "diag.labelMarginalLoad",
            CartDiagnosis.SteepClimb => "diag.labelSteepClimb",
            CartDiagnosis.Obstruction => "diag.labelObstruction",
            _ => "diag.labelUnclear",
        });
        return TeamsterStrings.Format("diag.line", label, Evidence, Action);
    }
}
