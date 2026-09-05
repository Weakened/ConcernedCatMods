using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>Detects and classifies a stuck pulled cart (CT-013) from
/// telemetry the pipeline already produces — no new game surface. The stuck
/// signature is being pulled with near-zero speed continuously for
/// <see cref="StuckAfterSeconds"/>; while the cart is not being pulled the
/// detector holds no window and does no work (parked carts cost nothing —
/// the pump additionally never calls it for them). Classification is a
/// fixed evidence table; conflicting or unexplained signatures yield
/// Unclear rather than a guess:
///
/// | grade evidence                        | verdict  | diagnosis       |
/// |---------------------------------------|----------|-----------------|
/// | unavailable                           | —        | Unclear         |
/// | mild (|g| &lt; 8%)                    | —        | Obstruction     |
/// | climbing ≥ 8%                         | No       | ImpossibleLoad  |
/// | climbing ≥ 8%                         | Marginal | MarginalLoad    |
/// | climbing ≥ 8%                         | Yes      | Obstruction     |
/// | climbing ≥ 15% (steep)                | Unknown  | SteepClimb      |
/// | climbing 8–15%                        | Unknown  | Unclear         |
/// | descending ≤ −8% (stuck going down?)  | —        | Unclear         |</summary>
public sealed class StuckDetector
{
    /// <summary>Speed below which a pulled cart counts as not moving.</summary>
    public const float StuckSpeedThresholdMetersPerSecond = 0.3f;

    /// <summary>Continuous low-speed time before the stuck signature fires.</summary>
    public const double StuckAfterSeconds = 2.5;

    /// <summary>Grades below this magnitude are "mild" — a stall there has
    /// no terrain explanation.</summary>
    public const float ObstructionMaxGradePercent = 8f;

    /// <summary>Uncalibrated climbs at or above this are blamed on the
    /// grade itself.</summary>
    public const float SteepClimbMinPercent = 15f;

    private readonly LoadModel? _loadModel;
    private string? _watchedCartId;
    private double _lowSpeedSinceSeconds = double.NaN;

    public StuckDetector(LoadModel? loadModel)
    {
        _loadModel = loadModel;
    }

    /// <summary>Evaluates one fresh snapshot of the pulled cart. Returns
    /// <see cref="CartDiagnostic.None"/> (and clears the window) whenever
    /// the cart is not pulled, moving, or unreadable.</summary>
    public CartDiagnostic Update(CartTelemetry telemetry)
    {
        if (!telemetry.IsPulledByLocalPlayer || !telemetry.VelocityAvailable)
        {
            Reset();
            return CartDiagnostic.None;
        }

        if (_watchedCartId != telemetry.CartId)
        {
            // A different cart starts a fresh window.
            _watchedCartId = telemetry.CartId;
            _lowSpeedSinceSeconds = double.NaN;
        }

        if (telemetry.SpeedMetersPerSecond >= StuckSpeedThresholdMetersPerSecond)
        {
            _lowSpeedSinceSeconds = double.NaN;
            return CartDiagnostic.None;
        }

        if (double.IsNaN(_lowSpeedSinceSeconds))
        {
            _lowSpeedSinceSeconds = telemetry.SampleTimeSeconds;
        }

        if (telemetry.SampleTimeSeconds - _lowSpeedSinceSeconds < StuckAfterSeconds)
        {
            return CartDiagnostic.None;
        }

        return Classify(telemetry);
    }

    public void Reset()
    {
        _watchedCartId = null;
        _lowSpeedSinceSeconds = double.NaN;
    }

    private CartDiagnostic Classify(CartTelemetry telemetry)
    {
        if (!telemetry.GradeAvailable)
        {
            return new CartDiagnostic(CartDiagnosis.Unclear,
                TeamsterStrings.Get("diag.noTerrainEvidence"),
                TeamsterStrings.Get("diag.noTerrainAction"));
        }

        float grade = telemetry.SmoothedGradePercent;
        string gradeText = grade.ToString("F0", CultureInfo.InvariantCulture) + "%";

        if (grade <= -ObstructionMaxGradePercent)
        {
            return new CartDiagnostic(CartDiagnosis.Unclear,
                TeamsterStrings.Format("diag.descentEvidence", gradeText),
                TeamsterStrings.Get("diag.descentAction"));
        }

        if (grade < ObstructionMaxGradePercent)
        {
            return new CartDiagnostic(CartDiagnosis.Obstruction,
                TeamsterStrings.Format("diag.mildGradeEvidence", gradeText),
                TeamsterStrings.Get("diag.mildGradeAction"));
        }

        if (_loadModel is not null)
        {
            LoadVerdict verdict = _loadModel.Query(grade, telemetry.TotalMass);
            switch (verdict.Climbability)
            {
                case Climbability.No:
                    return new CartDiagnostic(CartDiagnosis.ImpossibleLoad,
                        TeamsterStrings.Format("diag.verdictEvidence", verdict.Explanation),
                        TeamsterStrings.Get("diag.impossibleAction"));
                case Climbability.Marginal:
                    return new CartDiagnostic(CartDiagnosis.MarginalLoad,
                        TeamsterStrings.Format("diag.verdictEvidence", verdict.Explanation),
                        TeamsterStrings.Get("diag.marginalAction"));
                case Climbability.Yes:
                    return new CartDiagnostic(CartDiagnosis.Obstruction,
                        TeamsterStrings.Format(
                            "diag.provenYetStuckEvidence", gradeText, verdict.Explanation),
                        TeamsterStrings.Get("diag.provenYetStuckAction"));
            }
        }

        if (grade >= SteepClimbMinPercent)
        {
            return new CartDiagnostic(CartDiagnosis.SteepClimb,
                TeamsterStrings.Format("diag.steepClimbEvidence", gradeText),
                TeamsterStrings.Get("diag.steepClimbAction"));
        }

        return new CartDiagnostic(CartDiagnosis.Unclear,
            TeamsterStrings.Format("diag.unclearClimbEvidence", gradeText),
            TeamsterStrings.Get("diag.unclearClimbAction"));
    }
}
