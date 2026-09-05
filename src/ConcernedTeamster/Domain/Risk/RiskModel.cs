using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>The calibrated descent/runaway risk model (CT-011). Verdicts
/// are three-dimensional *dominance* arguments over descent rows — no
/// interpolation, no extrapolation:
///
/// - SAFE when a Held row exists at same-or-harder downgrade AND mass AND
///   entry speed (what stayed controlled there stays controlled here);
/// - DANGER when a Runaway/JointBreak row exists at same-or-easier all
///   three (what ran away there runs away here);
/// - CAUTION when a Dragged row exists at same-or-easier all three (what
///   already dragged there drags-or-worse here);
/// - a Safe witness conflicting with a Caution/Danger witness is
///   contradictory data → UNKNOWN, stated plainly;
/// - UNKNOWN otherwise ("outside calibrated coverage").
///
/// Dominance makes the risk ordering structural: increasing downgrade,
/// mass, or speed can only lose Held witnesses and gain Dragged/failure
/// witnesses, so risk never decreases with difficulty (property-tested).</summary>
public sealed class RiskModel
{
    private readonly DescentCalibrationData _data;

    public RiskModel(DescentCalibrationData data)
    {
        _data = data;
    }

    public RiskVerdict Query(float downGradePercent, float totalMass, float speedMetersPerSecond)
    {
        if (float.IsNaN(downGradePercent) || float.IsInfinity(downGradePercent) || downGradePercent < 0f ||
            float.IsNaN(totalMass) || float.IsInfinity(totalMass) || totalMass <= 0f ||
            float.IsNaN(speedMetersPerSecond) || float.IsInfinity(speedMetersPerSecond) || speedMetersPerSecond < 0f)
        {
            return new RiskVerdict(RiskLevel.Unknown, null, "invalid downgrade, mass, or speed");
        }

        DescentCalibrationRow? safeWitness = null;
        DescentCalibrationRow? cautionWitness = null;
        DescentCalibrationRow? dangerWitness = null;

        foreach (DescentCalibrationRow row in _data.Rows)
        {
            bool rowIsHarder = row.DownGradePercent >= downGradePercent &&
                row.TotalMass >= totalMass &&
                row.EntrySpeedMetersPerSecond >= speedMetersPerSecond;
            bool rowIsEasier = row.DownGradePercent <= downGradePercent &&
                row.TotalMass <= totalMass &&
                row.EntrySpeedMetersPerSecond <= speedMetersPerSecond;

            switch (row.Outcome)
            {
                case DescentOutcome.Held when rowIsHarder:
                    safeWitness = Stronger(safeWitness, row);
                    break;
                case DescentOutcome.Dragged when rowIsEasier:
                    cautionWitness = Stronger(cautionWitness, row);
                    break;
                case DescentOutcome.Runaway when rowIsEasier:
                case DescentOutcome.JointBreak when rowIsEasier:
                    dangerWitness = Stronger(dangerWitness, row);
                    break;
            }
        }

        if (safeWitness is not null && (dangerWitness is not null || cautionWitness is not null))
        {
            return new RiskVerdict(RiskLevel.Unknown, null,
                "contradictory calibration rows cover this descent; re-run the protocol");
        }

        if (dangerWitness is not null)
        {
            return new RiskVerdict(RiskLevel.Danger, dangerWitness.Basis,
                Describe("ran away at", dangerWitness));
        }

        if (cautionWitness is not null)
        {
            return new RiskVerdict(RiskLevel.Caution, cautionWitness.Basis,
                Describe("was dragged at", cautionWitness));
        }

        if (safeWitness is not null)
        {
            return new RiskVerdict(RiskLevel.Safe, safeWitness.Basis,
                Describe("stayed controlled at", safeWitness));
        }

        return new RiskVerdict(RiskLevel.Unknown, null,
            "outside calibrated coverage — no descent row answers this downgrade, mass, and speed");
    }

    private static DescentCalibrationRow Stronger(DescentCalibrationRow? current, DescentCalibrationRow candidate)
    {
        return current is null || candidate.Basis > current.Basis ? candidate : current;
    }

    private static string Describe(string verb, DescentCalibrationRow row)
    {
        return "a " + row.Basis + " row " + verb + " " +
            row.DownGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "% down with mass " +
            row.TotalMass.ToString("F0", CultureInfo.InvariantCulture) + " at " +
            row.EntrySpeedMetersPerSecond.ToString("F1", CultureInfo.InvariantCulture) + " m/s";
    }
}
