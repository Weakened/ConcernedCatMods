using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>The displayed word for a calibration basis (CT-032). Panels and
/// verdict explanations quote the basis of a deciding row; resolving the
/// word through the catalog keeps those sentences fully translatable while
/// the enum itself stays a stable data-file contract.</summary>
public static class LoadText
{
    public static string BasisWord(CalibrationBasis basis)
    {
        return TeamsterStrings.Get(basis switch
        {
            CalibrationBasis.Measured => "load.basisMeasured",
            CalibrationBasis.DerivedConstant => "load.basisDerivedConstant",
            _ => "load.basisPrior",
        });
    }
}
