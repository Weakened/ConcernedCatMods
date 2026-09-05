using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;
using TheConcernedCat.ConcernedTeamster.Domain.Load;

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
                "No active diagnosis — guidance appears here when your cart is stuck.",
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
                title = impossible
                    ? "Overloaded for this grade — the load must come down"
                    : "Marginal load — a lighter cart makes this climb";
                if (brakeStep)
                {
                    steps.Add("Detach, then hold the cart with the parking brake while you work.");
                }

                AddUnloadStep(steps, telemetry, loadModel, grade);
                steps.Add("Retry the climb straight uphill at a steady pace.");
                steps.Add("If it still stalls, route around: a longer, shallower path beats a stuck cart.");
                break;
            }

            case CartDiagnosis.SteepClimb:
                title = "Steep, uncalibrated climb";
                steps.Add("Back the cart down to level ground first.");
                if (brakeStep)
                {
                    steps.Add("Use the parking brake to hold it while you scout.");
                }

                steps.Add("Look for a shallower line — even a few degrees less grade helps more than pushing harder.");
                steps.Add("Cut the slope diagonally (switchback) instead of attacking it straight on.");
                steps.Add("Unloading part of the cargo for a second trip is slower but certain.");
                break;

            case CartDiagnosis.Obstruction:
                title = "Something is physically blocking the cart";
                steps.Add("Walk around the cart and check each wheel for rocks, stumps, or a terrain lip.");
                steps.Add("Back up two or three meters and approach again at a slight angle.");
                steps.Add("A hoe can level the offending lip — the vanilla tool is the intended fix.");
                steps.Add("If a wheel dropped into a hole, pull backward out of it rather than forward through it.");
                break;

            default:
                title = "Cause unclear — safe general steps";
                steps.Add("Detach and re-attach the cart to reset the pull joint.");
                steps.Add("Back up a few meters and try a slightly different line.");
                steps.Add("Check the wheels and the ground line for anything the cart could be caught on.");
                if (brakeStep)
                {
                    steps.Add("On a slope, hold the cart with the parking brake while you investigate.");
                }

                steps.Add("If nothing helps, unload some cargo — a lighter cart forgives more.");
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
                steps.Insert(0, "Crew right now: " + coop + ".");
                if (tally.Helping > 1 &&
                    (diagnostic.Diagnosis == CartDiagnosis.ImpossibleLoad ||
                     diagnostic.Diagnosis == CartDiagnosis.SteepClimb))
                {
                    steps.Insert(1,
                        "Extra hands will not beat this — the fix is less weight or a shallower " +
                        "line, not more pushing.");
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
            steps.Add(
                "No load is proven to climb this grade yet — unload as much as you can carry, " +
                "or pick a shallower path.");
            return;
        }

        float unload = telemetry.TotalMass - recommendation.TotalMass;
        if (unload > 0f)
        {
            steps.Add(
                "Unload at least " + unload.ToString("F0", CultureInfo.InvariantCulture) +
                " weight (down to total mass " +
                recommendation.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                ", the heaviest load a " + recommendation.Basis + " row proved at this grade).");
        }
        else
        {
            steps.Add(
                "Your mass (" + telemetry.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                ") is already at or under the proven " +
                recommendation.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                " for this grade — the load is probably not the blocker; check for obstructions.");
        }
    }
}
