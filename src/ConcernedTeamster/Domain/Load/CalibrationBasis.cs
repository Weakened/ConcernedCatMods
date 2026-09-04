namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>Where a calibration row's truth comes from (CT-008), ordered by
/// strength: measured protocol runs beat verified-constant derivations beat
/// labeled priors. The order matters — verdicts report the basis of their
/// deciding row.</summary>
public enum CalibrationBasis
{
    Prior,
    DerivedConstant,
    Measured,
}
