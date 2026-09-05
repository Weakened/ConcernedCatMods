using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless route report (CT-024): profile summary, ranked problem
/// sections with distances and reasons, and load recommendations. Follows
/// the CT-014 guidance language rules — actionable sentences, numbered
/// ranking (never color-only), quantities only from models. Every advice
/// line is a verbatim LoadModel answer (Query / RecommendedMaxMass at the
/// section's grade); sections without a model answer get facts, not advice.
/// Unsampled stretches are ranked problems too: the report never lets a gap
/// hide behind a clean-looking summary.</summary>
public static class RouteReportPresenter
{
    /// <summary>|grade| at or above this ranks as a problem section —
    /// aligned with the very-steep histogram band boundary and CT-013's
    /// steep-grade classification boundary (15%).</summary>
    public const float ProblemGradePercent = 15f;

    public sealed class ViewModel
    {
        public ViewModel(string title, bool hasProfile, IReadOnlyList<string> lines)
        {
            Title = title;
            HasProfile = hasProfile;
            Lines = lines;
        }

        public string Title { get; }

        public bool HasProfile { get; }

        public IReadOnlyList<string> Lines { get; }
    }

    public static ViewModel Present(
        string routeName, RouteProfile? profile, LoadModel? model, float? cartTotalMass)
    {
        string title = TeamsterStrings.Format(
            "report.title", routeName.Length > 0 ? routeName : TeamsterStrings.Get("routes.unnamed"));
        if (profile is null)
        {
            return new ViewModel(
                title, false,
                new[] { TeamsterStrings.Get("report.needProfile") });
        }

        var lines = new List<string>(20);

        // -- summary --
        lines.Add("Distance " + Meters(profile.TotalDistanceMeters) +
            " — sampled " + Meters(profile.SampledMeters) +
            (profile.UnsampledMeters > 0.05f ? ", UNSAMPLED " + Meters(profile.UnsampledMeters) : ""));
        lines.Add(GradeMixLine(profile));

        // -- ranked problem sections: steep grades first, then gaps --
        int rank = 0;
        foreach (RouteProfileSegment segment in profile.WorstSegments)
        {
            if (Math.Abs(segment.GradePercent) < ProblemGradePercent)
            {
                continue;
            }

            rank++;
            bool climb = segment.GradePercent >= 0f;
            lines.Add(rank.ToString(CultureInfo.InvariantCulture) + ". Steep " +
                (climb ? "climb " : "descent ") +
                segment.GradePercent.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "% at " +
                Meters(segment.StartMeters) + ", " + Meters(segment.LengthMeters) + " long");

            string? advice = SectionAdvice(segment, climb, model, cartTotalMass);
            if (advice is not null)
            {
                lines.Add("   " + advice);
            }
        }

        foreach (RouteProfileSegment span in profile.UnsampledSpans)
        {
            rank++;
            lines.Add(rank.ToString(CultureInfo.InvariantCulture) + ". Unprofiled " +
                Meters(span.LengthMeters) + " starting at " + Meters(span.StartMeters) +
                " — nothing was measured there");
        }

        if (rank == 0)
        {
            lines.Add(TeamsterStrings.Format(
                "report.noProblems",
                ProblemGradePercent.ToString("F0", CultureInfo.InvariantCulture)));
        }

        // -- overall load recommendation (verbatim LoadModel answers) --
        if (model is null)
        {
            lines.Add(TeamsterStrings.Get("report.loadUnavailableNoModel"));
        }
        else
        {
            RouteLoadBottleneck.Result bottleneck =
                RouteLoadBottleneck.Evaluate(profile, model, cartTotalMass);
            if (!bottleneck.HasGradeData)
            {
                lines.Add(TeamsterStrings.Get("report.loadUnavailableNoGrade"));
            }
            else
            {
                string grade = bottleneck.BottleneckGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "%";
                lines.Add(bottleneck.ProvenMaxMass is null
                    ? "Bottleneck " + grade + ": no proven safe load — outside calibrated coverage."
                    : "Bottleneck " + grade + ": keep total mass at or under " +
                        bottleneck.ProvenMaxMass.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                        " (" + bottleneck.ProvenMaxMass.Basis + " calibration).");
                if (bottleneck.Verdict is not null)
                {
                    lines.Add("Your cart (" +
                        bottleneck.QueriedMass.ToString("F0", CultureInfo.InvariantCulture) + " mass): " +
                        VerdictWord(bottleneck.Verdict.Climbability) + " — " +
                        bottleneck.Verdict.Explanation + ".");
                }
            }
        }

        return new ViewModel(title, true, lines);
    }

    /// <summary>One traced advice line for a steep section, or null when
    /// the model answers nothing there. Descents are advised as the return
    /// climb — the same slope hauled the other way — so the quantity still
    /// comes straight from LoadModel.</summary>
    private static string? SectionAdvice(
        RouteProfileSegment segment, bool climb, LoadModel? model, float? cartTotalMass)
    {
        if (model is null)
        {
            return null;
        }

        float gradeMagnitude = Math.Abs(segment.GradePercent);
        string prefix = climb ? "Here: " : "As the return climb: ";
        if (cartTotalMass.HasValue)
        {
            LoadVerdict verdict = model.Query(gradeMagnitude, cartTotalMass.Value);
            if (verdict.Climbability != Climbability.Unknown)
            {
                return prefix + "your cart is " + VerdictWord(verdict.Climbability) +
                    " — " + verdict.Explanation + ".";
            }
        }

        LoadRecommendation? proven = model.RecommendedMaxMass(gradeMagnitude);
        if (proven is not null)
        {
            return prefix + "keep total mass at or under " +
                proven.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                " (" + proven.Basis + " calibration).";
        }

        return null;
    }

    private static string GradeMixLine(RouteProfile profile)
    {
        float graded = 0f;
        for (int index = 0; index < profile.GradeBandMeters.Count; index++)
        {
            graded += profile.GradeBandMeters[index];
        }

        if (graded <= 0f)
        {
            return "Grades: no sampled grade data.";
        }

        string worst = float.IsNaN(profile.MaxAbsGradePercent)
            ? "?"
            : profile.MaxAbsGradePercent.ToString("F1", CultureInfo.InvariantCulture) + "%";
        float steep = profile.GradeBandMeters[3] + profile.GradeBandMeters[4];
        return "Steepest sampled grade " + worst + "; " +
            (steep > 0f
                ? Meters(steep) + " of the route is 15% or steeper."
                : "no sampled stretch reaches 15%.");
    }

    private static string VerdictWord(Climbability climbability)
    {
        return climbability switch
        {
            Climbability.Yes => "OK",
            Climbability.Marginal => "MARGINAL",
            Climbability.No => "TOO HEAVY",
            _ => "UNKNOWN",
        };
    }

    private static string Meters(float value)
    {
        return value.ToString("F0", CultureInfo.InvariantCulture) + " m";
    }
}
