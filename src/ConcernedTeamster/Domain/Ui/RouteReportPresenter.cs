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
        lines.Add(profile.UnsampledMeters > 0.05f
            ? TeamsterStrings.Format(
                "report.summaryUnsampled", Meters(profile.TotalDistanceMeters),
                Meters(profile.SampledMeters), Meters(profile.UnsampledMeters))
            : TeamsterStrings.Format(
                "report.summary", Meters(profile.TotalDistanceMeters), Meters(profile.SampledMeters)));
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
            lines.Add(TeamsterStrings.Format(
                climb ? "report.steepClimbSection" : "report.steepDescentSection",
                rank.ToString(CultureInfo.InvariantCulture),
                segment.GradePercent.ToString("+0.0;-0.0", CultureInfo.InvariantCulture),
                Meters(segment.StartMeters), Meters(segment.LengthMeters)));

            string? advice = SectionAdvice(segment, climb, model, cartTotalMass);
            if (advice is not null)
            {
                lines.Add("   " + advice);
            }
        }

        foreach (RouteProfileSegment span in profile.UnsampledSpans)
        {
            rank++;
            lines.Add(TeamsterStrings.Format(
                "report.gapSection",
                rank.ToString(CultureInfo.InvariantCulture),
                Meters(span.LengthMeters), Meters(span.StartMeters)));
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
                    ? TeamsterStrings.Format("report.bottleneckNoProven", grade)
                    : TeamsterStrings.Format(
                        "report.bottleneckKeepUnder", grade,
                        bottleneck.ProvenMaxMass.TotalMass.ToString("F0", CultureInfo.InvariantCulture),
                        LoadText.BasisWord(bottleneck.ProvenMaxMass.Basis)));
                if (bottleneck.Verdict is not null)
                {
                    lines.Add(TeamsterStrings.Format(
                        "report.yourCart",
                        bottleneck.QueriedMass.ToString("F0", CultureInfo.InvariantCulture),
                        VerdictWord(bottleneck.Verdict.Climbability),
                        bottleneck.Verdict.Explanation));
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
        if (cartTotalMass.HasValue)
        {
            LoadVerdict verdict = model.Query(gradeMagnitude, cartTotalMass.Value);
            if (verdict.Climbability != Climbability.Unknown)
            {
                return TeamsterStrings.Format(
                    climb ? "report.adviceVerdictHere" : "report.adviceVerdictReturn",
                    VerdictWord(verdict.Climbability), verdict.Explanation);
            }
        }

        LoadRecommendation? proven = model.RecommendedMaxMass(gradeMagnitude);
        if (proven is not null)
        {
            return TeamsterStrings.Format(
                climb ? "report.adviceProvenHere" : "report.adviceProvenReturn",
                proven.TotalMass.ToString("F0", CultureInfo.InvariantCulture),
                LoadText.BasisWord(proven.Basis));
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
            return TeamsterStrings.Get("report.gradeMixNoData");
        }

        string worst = float.IsNaN(profile.MaxAbsGradePercent)
            ? "?"
            : profile.MaxAbsGradePercent.ToString("F1", CultureInfo.InvariantCulture) + "%";
        float steep = profile.GradeBandMeters[3] + profile.GradeBandMeters[4];
        return steep > 0f
            ? TeamsterStrings.Format("report.gradeMixSteep", worst, Meters(steep))
            : TeamsterStrings.Format("report.gradeMixNoSteep", worst);
    }

    private static string VerdictWord(Climbability climbability)
    {
        return TeamsterStrings.Get(climbability switch
        {
            Climbability.Yes => "verdict.ok",
            Climbability.Marginal => "verdict.marginal",
            Climbability.No => "verdict.tooHeavy",
            _ => "verdict.unknown",
        });
    }

    private static string Meters(float value)
    {
        return TeamsterStrings.Format(
            "unit.meters", value.ToString("F0", CultureInfo.InvariantCulture));
    }
}
