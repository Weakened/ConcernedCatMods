using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Composes per-snapshot descent risk (CT-011): the current-
/// position verdict from the telemetry's own grade/mass/speed, and the
/// lookahead verdict from the adapter's worst-upcoming-downgrade sample.
/// Pure functions — evaluated by the pump on new snapshots only, like
/// warnings.</summary>
public static class DescentRiskEvaluator
{
    public static RiskVerdict EvaluateCurrent(RiskModel? model, CartTelemetry telemetry)
    {
        if (!telemetry.GradeAvailable)
        {
            return new RiskVerdict(RiskLevel.Unknown, null, "grade unavailable");
        }

        if (telemetry.SmoothedGradePercent >= 0f)
        {
            return new RiskVerdict(RiskLevel.Safe, null, "not descending");
        }

        if (model is null)
        {
            return new RiskVerdict(RiskLevel.Unknown, null, "no descent calibration data");
        }

        float speed = telemetry.VelocityAvailable ? telemetry.SpeedMetersPerSecond : 0f;
        return model.Query(-telemetry.SmoothedGradePercent, telemetry.TotalMass, speed);
    }

    public static RiskVerdict? EvaluateLookahead(
        RiskModel? model,
        CartTelemetry telemetry,
        bool lookaheadAvailable,
        float worstAheadDownGradePercent)
    {
        if (!lookaheadAvailable)
        {
            return null;
        }

        if (worstAheadDownGradePercent <= 0f)
        {
            return new RiskVerdict(RiskLevel.Safe, null, "no descent ahead in the lookahead window");
        }

        if (model is null)
        {
            return new RiskVerdict(RiskLevel.Unknown, null, "no descent calibration data");
        }

        float speed = telemetry.VelocityAvailable ? telemetry.SpeedMetersPerSecond : 0f;
        return model.Query(worstAheadDownGradePercent, telemetry.TotalMass, speed);
    }
}
