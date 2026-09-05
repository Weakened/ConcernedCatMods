using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Maps a stuck diagnosis to concrete, vanilla-legal recovery
/// steps (CT-014). Quantitative advice comes only from the load model's
/// proven rows (never interpolated): the unload target is the heaviest
/// proven-climbable mass at the current grade, cited with its basis. The
/// brake step appears only when the feature is enabled and the slope makes
/// holding genuinely useful. Pure domain: this class references no adapter
/// or game surface, so no guidance path can mutate anything.</summary>
public static class RecoveryGuidancePresenter
{
    /// <summary>Slope magnitude from which "hold it with the brake" is
    /// genuinely relevant while unloading.</summary>
    public const float BrakeRelevantGradePercent = 5f;

    public static RecoveryGuidanceViewModel Present(
        CartDiagnostic? diagnostic,
        CartTelemetry? telemetry,
        LoadModel? loadModel,
        bool brakeFeatureEnabled,
        IReadOnlyList<CoopParticipant>? participants = null)
    {
        if (diagnostic is null || diagnostic.Diagnosis == CartDiagnosis.None || telemetry is null)
        {
            return new RecoveryGuidanceViewModel(
                hasGuidance: false,
                TeamsterStrings.Get("recovery.noDiagnosis"),
                Array.Empty<string>());
        }

        var steps = new List<string>();
        float grade = telemetry.GradeAvailable ? telemetry.SmoothedGradePercent : 0f;
        bool brakeStep = brakeFeatureEnabled && telemetry.GradeAvailable &&
            Math.Abs(grade) >= BrakeRelevantGradePercent;

        string title;
        switch (diagnostic.Diagnosis)
        {
            case CartDiagnosis.ImpossibleLoad:
            case CartDiagnosis.MarginalLoad:
            {
                bool impossible = diagnostic.Diagnosis == CartDiagnosis.ImpossibleLoad;
                title = TeamsterStrings.Get(
                    impossible ? "recovery.titleOverloaded" : "recovery.titleMarginal");
                if (brakeStep)
                {
                    steps.Add(TeamsterStrings.Get("recovery.stepBrakeHold"));
                }

                AddUnloadStep(steps, telemetry, loadModel, grade);
                steps.Add(TeamsterStrings.Get("recovery.stepRetryClimb"));
                steps.Add(TeamsterStrings.Get("recovery.stepRouteAround"));
                break;
            }

            case CartDiagnosis.SteepClimb:
                title = TeamsterStrings.Get("recovery.titleSteep");
                steps.Add(TeamsterStrings.Get("recovery.stepBackDown"));
                if (brakeStep)
                {
                    steps.Add(TeamsterStrings.Get("recovery.stepBrakeScout"));
                }

                steps.Add(TeamsterStrings.Get("recovery.stepShallowerLine"));
                steps.Add(TeamsterStrings.Get("recovery.stepSwitchback"));
                steps.Add(TeamsterStrings.Get("recovery.stepSecondTrip"));
                break;

            case CartDiagnosis.Obstruction:
                title = TeamsterStrings.Get("recovery.titleObstruction");
                steps.Add(TeamsterStrings.Get("recovery.stepCheckWheels"));
                steps.Add(TeamsterStrings.Get("recovery.stepBackUpAngle"));
                steps.Add(TeamsterStrings.Get("recovery.stepHoe"));
                steps.Add(TeamsterStrings.Get("recovery.stepWheelHole"));
                break;

            default:
                title = TeamsterStrings.Get("recovery.titleUnclear");
                steps.Add(TeamsterStrings.Get("recovery.stepReattach"));
                steps.Add(TeamsterStrings.Get("recovery.stepDifferentLine"));
                steps.Add(TeamsterStrings.Get("recovery.stepCheckCaught"));
                if (brakeStep)
                {
                    steps.Add(TeamsterStrings.Get("recovery.stepBrakeInvestigate"));
                }

                steps.Add(TeamsterStrings.Get("recovery.stepUnloadSome"));
                break;
        }

        // CT-028: cooperative context, when other players are observed on
        // the cart. Purely explanatory — it never changes the physical
        // verdict above, and grants no force.
        if (participants is { Count: > 0 })
        {
            string coop = CooperativeEffortClassifier.Summarize(participants);
            if (coop.Length > 0)
            {
                CooperativeEffortClassifier.EffortTally tally =
                    CooperativeEffortClassifier.Tally(participants);
                steps.Insert(0, TeamsterStrings.Format("recovery.crewNow", coop));
                if (tally.Helping > 1 &&
                    (diagnostic.Diagnosis == CartDiagnosis.ImpossibleLoad ||
                     diagnostic.Diagnosis == CartDiagnosis.SteepClimb))
                {
                    steps.Insert(1, TeamsterStrings.Get("recovery.extraHands"));
                }
            }
        }

        return new RecoveryGuidanceViewModel(hasGuidance: true, title, steps);
    }

    /// <summary>The quantitative step: unload down to the heaviest PROVEN
    /// mass at this grade, citing the proving row's basis — or say plainly
    /// that nothing is proven here.</summary>
    private static void AddUnloadStep(
        List<string> steps, CartTelemetry telemetry, LoadModel? loadModel, float grade)
    {
        LoadRecommendation? recommendation =
            telemetry.GradeAvailable ? loadModel?.RecommendedMaxMass(grade) : null;
        if (recommendation is null)
        {
            steps.Add(TeamsterStrings.Get("recovery.unloadNothingProven"));
            return;
        }

        float unload = telemetry.TotalMass - recommendation.TotalMass;
        if (unload > 0f)
        {
            steps.Add(TeamsterStrings.Format(
                "recovery.unloadAtLeast",
                unload.ToString("F0", CultureInfo.InvariantCulture),
                recommendation.TotalMass.ToString("F0", CultureInfo.InvariantCulture),
                LoadText.BasisWord(recommendation.Basis)));
        }
        else
        {
            steps.Add(TeamsterStrings.Format(
                "recovery.unloadAlreadyUnder",
                telemetry.TotalMass.ToString("F0", CultureInfo.InvariantCulture),
                recommendation.TotalMass.ToString("F0", CultureInfo.InvariantCulture)));
        }
    }
}
