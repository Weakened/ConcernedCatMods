using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-014: every diagnostic class produces guidance, the unclear
/// case offers safe generic steps, quantitative unload advice traces to
/// LoadModel's proven rows exactly, and the brake step appears only when
/// the feature is enabled and the slope makes holding useful.</summary>
public class RecoveryGuidancePresenterTests
{
    private static CartTelemetry Telemetry(
        float grade = 12f, float cargo = 300f, bool gradeAvailable = true)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "1:1", baseMass: 20f, cargoWeight: cargo, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(
            snapshot, true, 0.1f, 0f, gradeAvailable, grade, grade,
            GradeDirection.Climbing, TerrainSurfaceKind.Untouched, 10.0);
    }

    private static LoadModel Model()
    {
        return new LoadModel(LoadCalibrationData.Parse(@"data-version: 1
row: 15 | 200 | Climbs | Measured | proven at fifteen"));
    }

    private static CartDiagnostic Diag(CartDiagnosis diagnosis)
    {
        return new CartDiagnostic(diagnosis, "evidence", "action");
    }

    [Fact]
    public void Present_NoDiagnosis_ExplainsInsteadOfSteps()
    {
        RecoveryGuidanceViewModel none = RecoveryGuidancePresenter.Present(
            null, Telemetry(), Model(), brakeFeatureEnabled: true);
        Assert.False(none.HasGuidance);
        Assert.Contains("No active diagnosis", none.Title);
        Assert.Empty(none.Steps);

        RecoveryGuidanceViewModel cleared = RecoveryGuidancePresenter.Present(
            CartDiagnostic.None, Telemetry(), Model(), true);
        Assert.False(cleared.HasGuidance);
    }

    [Fact]
    public void Present_EveryDiagnosticClass_HasTitleAndSteps()
    {
        foreach (CartDiagnosis diagnosis in new[]
        {
            CartDiagnosis.ImpossibleLoad, CartDiagnosis.MarginalLoad,
            CartDiagnosis.SteepClimb, CartDiagnosis.Obstruction, CartDiagnosis.Unclear,
        })
        {
            RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
                Diag(diagnosis), Telemetry(), Model(), brakeFeatureEnabled: true);

            Assert.True(viewModel.HasGuidance);
            Assert.NotEqual(string.Empty, viewModel.Title);
            Assert.InRange(viewModel.Steps.Count, 3, 6);
        }
    }

    [Fact]
    public void Present_Overload_UnloadAmountTracesToTheProvenRow()
    {
        // Mass 320 at 12%: the proven row is (15%, 200, Measured) ->
        // unload 120 down to 200, citing Measured.
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.ImpossibleLoad), Telemetry(grade: 12f, cargo: 300f),
            Model(), brakeFeatureEnabled: false);

        Assert.Contains(viewModel.Steps, step =>
            step.Contains("Unload at least 120") &&
            step.Contains("total mass 200") &&
            step.Contains("Measured"));
    }

    [Fact]
    public void Present_Overload_NoProvenRowAtThisGrade_SaysSo()
    {
        // 20% is above every proven row's grade -> no recommendation.
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.ImpossibleLoad), Telemetry(grade: 20f), Model(), false);

        Assert.Contains(viewModel.Steps, step => step.Contains("No load is proven"));
    }

    [Fact]
    public void Present_MarginalAlreadyUnderProvenMass_PointsAtObstructions()
    {
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.MarginalLoad), Telemetry(grade: 12f, cargo: 150f),
            Model(), false);

        Assert.Contains(viewModel.Steps, step =>
            step.Contains("already at or under the proven 200"));
    }

    [Fact]
    public void Present_NoModel_QuantitativeStepDegradesHonestly()
    {
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.ImpossibleLoad), Telemetry(), loadModel: null, false);

        Assert.Contains(viewModel.Steps, step => step.Contains("No load is proven"));
    }

    [Fact]
    public void Present_BrakeStep_OnlyWhenEnabledAndSloped()
    {
        static bool HasBrakeStep(RecoveryGuidanceViewModel viewModel)
        {
            foreach (string step in viewModel.Steps)
            {
                if (step.Contains("parking brake"))
                {
                    return true;
                }
            }

            return false;
        }

        Assert.True(HasBrakeStep(RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.ImpossibleLoad), Telemetry(grade: 12f), Model(), true)));

        // Feature disabled: no brake step.
        Assert.False(HasBrakeStep(RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.ImpossibleLoad), Telemetry(grade: 12f), Model(), false)));

        // Enabled but nearly level: holding adds nothing.
        Assert.False(HasBrakeStep(RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.Obstruction), Telemetry(grade: 2f), Model(), true)));
    }

    [Fact]
    public void Present_Unclear_OffersSafeGenericSteps()
    {
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.Unclear), Telemetry(grade: 6f), Model(), true);

        Assert.Contains("unclear", viewModel.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.Steps, step => step.Contains("Detach and re-attach"));
        Assert.Contains(viewModel.Steps, step => step.Contains("parking brake"));
    }

    [Fact]
    public void Present_Obstruction_SuggestsVanillaLegalFixesOnly()
    {
        RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
            Diag(CartDiagnosis.Obstruction), Telemetry(grade: 1f), Model(), true);

        Assert.Contains(viewModel.Steps, step => step.Contains("hoe"));
        // Advisory only: no step promises to move the cart for the player.
        Assert.DoesNotContain(viewModel.Steps, step =>
            step.Contains("teleport", StringComparison.OrdinalIgnoreCase));
    }
}
