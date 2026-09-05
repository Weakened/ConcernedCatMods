using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>The calibrated load/climbability model (CT-008). Every verdict
/// is a *dominance* argument over calibration rows — no interpolation, no
/// curve fitting, no extrapolation:
///
/// - YES when a Climbs row exists at the same-or-harder grade AND mass
///   (what climbed there climbs here);
/// - NO when a Stalls/JointBreak row exists at the same-or-easier grade AND
///   mass (what failed there fails here);
/// - MARGINAL when only a Marginal row dominates the query;
/// - UNKNOWN otherwise, including contradictory data — stated plainly.
///
/// Dominance makes monotonicity structural: increasing grade or mass can
/// only lose success witnesses and gain failure witnesses, so a verdict
/// never improves with difficulty (property-tested). The deciding row's
/// basis (Measured / DerivedConstant / Prior) rides along so consumers can
/// say how much to trust the answer.</summary>
public sealed class LoadModel
{
    private readonly LoadCalibrationData _data;

    public LoadModel(LoadCalibrationData data)
    {
        _data = data;
    }

    public LoadVerdict Query(float gradePercent, float totalMass)
    {
        if (float.IsNaN(gradePercent) || float.IsInfinity(gradePercent) ||
            float.IsNaN(totalMass) || float.IsInfinity(totalMass) || totalMass <= 0f)
        {
            return new LoadVerdict(
                Climbability.Unknown, null, TeamsterStrings.Get("load.invalidQuery"));
        }

        CalibrationRow? successWitness = null;
        CalibrationRow? marginalWitness = null;
        CalibrationRow? failureWitness = null;

        foreach (CalibrationRow row in _data.Rows)
        {
            bool rowIsHarder = row.GradePercent >= gradePercent && row.TotalMass >= totalMass;
            bool rowIsEasier = row.GradePercent <= gradePercent && row.TotalMass <= totalMass;

            switch (row.Outcome)
            {
                case CalibrationOutcome.Climbs when rowIsHarder:
                    successWitness = Stronger(successWitness, row);
                    break;
                case CalibrationOutcome.Marginal when rowIsHarder:
                    marginalWitness = Stronger(marginalWitness, row);
                    break;
                case CalibrationOutcome.Stalls when rowIsEasier:
                case CalibrationOutcome.JointBreak when rowIsEasier:
                    failureWitness = Stronger(failureWitness, row);
                    break;
            }
        }

        if (successWitness is not null && failureWitness is not null)
        {
            return new LoadVerdict(
                Climbability.Unknown, null, TeamsterStrings.Get("load.contradictory"));
        }

        if (failureWitness is not null)
        {
            return new LoadVerdict(
                Climbability.No, failureWitness.Basis,
                Describe("load.verbFailedAt", failureWitness));
        }

        if (successWitness is not null)
        {
            return new LoadVerdict(
                Climbability.Yes, successWitness.Basis,
                Describe("load.verbClimbedAt", successWitness));
        }

        if (marginalWitness is not null)
        {
            return new LoadVerdict(
                Climbability.Marginal, marginalWitness.Basis,
                Describe("load.verbMarginalAt", marginalWitness));
        }

        return new LoadVerdict(
            Climbability.Unknown, null, TeamsterStrings.Get("load.outsideCoverage"));
    }

    /// <summary>The heaviest total mass with a proven Climbs row at this
    /// grade or steeper — the highest *proven* load, never an interpolated
    /// guess. Null when nothing is proven at this grade.</summary>
    public LoadRecommendation? RecommendedMaxMass(float gradePercent)
    {
        if (float.IsNaN(gradePercent) || float.IsInfinity(gradePercent))
        {
            return null;
        }

        CalibrationRow? best = null;
        foreach (CalibrationRow row in _data.Rows)
        {
            if (row.Outcome != CalibrationOutcome.Climbs || row.GradePercent < gradePercent)
            {
                continue;
            }

            if (best is null || row.TotalMass > best.TotalMass ||
                (row.TotalMass == best.TotalMass && row.Basis > best.Basis))
            {
                best = row;
            }
        }

        return best is null ? null : new LoadRecommendation(best.TotalMass, best.Basis);
    }

    private static CalibrationRow Stronger(CalibrationRow? current, CalibrationRow candidate)
    {
        return current is null || candidate.Basis > current.Basis ? candidate : current;
    }

    private static string Describe(string verbKey, CalibrationRow row)
    {
        return TeamsterStrings.Format(
            "load.rowDescribe",
            LoadText.BasisWord(row.Basis),
            TeamsterStrings.Get(verbKey),
            row.GradePercent.ToString("F0", CultureInfo.InvariantCulture),
            row.TotalMass.ToString("F0", CultureInfo.InvariantCulture));
    }
}
