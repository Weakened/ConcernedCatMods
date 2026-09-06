using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless rendering of a route profile into the picker panel's
/// five fixed lines (CT-023). Invariant formatting; explicit states for
/// "nothing selected", "profiling n/m", and "no grade data"; unsampled
/// meters are always shown when nonzero — the display never hides a gap.</summary>
public static class RouteProfilePresenter
{
    public const int LineCount = 5;

    private static readonly string[] BandLabels = { "<3%", "3-8%", "8-15%", "15-25%", "25%+" };

    public static IReadOnlyList<string> Present(
        bool hasSelection,
        bool profiling,
        int positionsProbed,
        int positionCount,
        RouteProfile? profile,
        RouteLoadBottleneck.Result? bottleneck)
    {
        var lines = new string[LineCount];
        for (int index = 0; index < LineCount; index++)
        {
            lines[index] = string.Empty;
        }

        if (!hasSelection)
        {
            return lines;
        }

        if (profiling || profile is null)
        {
            lines[0] = TeamsterStrings.Format(
                "profile.profiling",
                positionsProbed.ToString(CultureInfo.InvariantCulture),
                positionCount.ToString(CultureInfo.InvariantCulture));
            return lines;
        }

        lines[0] = profile.UnsampledMeters > 0.05f
            ? TeamsterStrings.Format(
                "profile.summaryUnsampled",
                Meters(profile.TotalDistanceMeters), Meters(profile.SampledMeters),
                Meters(profile.UnsampledMeters))
            : TeamsterStrings.Format(
                "profile.summary",
                Meters(profile.TotalDistanceMeters), Meters(profile.SampledMeters));

        lines[1] = SurfaceLine(profile);
        lines[2] = WorstLine(profile);
        lines[3] = BandLine(profile);
        lines[4] = BottleneckLine(bottleneck);
        return lines;
    }

    private static string SurfaceLine(RouteProfile profile)
    {
        if (profile.SampledMeters <= 0f)
        {
            return TeamsterStrings.Get("profile.surfacesNoneSampled");
        }

        var parts = new List<string>(5);
        foreach (TerrainSurfaceKind kind in new[]
        {
            TerrainSurfaceKind.Paved, TerrainSurfaceKind.Dirt,
            TerrainSurfaceKind.Cultivated, TerrainSurfaceKind.Untouched,
        })
        {
            if (profile.SurfaceMeters.TryGetValue(kind, out float meters) && meters > 0f)
            {
                parts.Add(SurfaceWord(kind) + " " + Percent(meters, profile.SampledMeters));
            }
        }

        if (profile.SurfaceUnknownMeters > 0f)
        {
            parts.Add(TeamsterStrings.Get("profile.surfaceUnknown") + " " +
                Percent(profile.SurfaceUnknownMeters, profile.SampledMeters));
        }

        return TeamsterStrings.Format(
            "profile.surfaces",
            parts.Count == 0
                ? TeamsterStrings.Get("profile.surfacesNoneClassified")
                : string.Join(", ", parts));
    }

    private static string SurfaceWord(TerrainSurfaceKind kind)
    {
        return TeamsterStrings.Get(kind switch
        {
            TerrainSurfaceKind.Paved => "profile.surfacePaved",
            TerrainSurfaceKind.Dirt => "profile.surfaceDirt",
            TerrainSurfaceKind.Cultivated => "profile.surfaceCultivated",
            _ => "profile.surfaceUntouched",
        });
    }

    private static string WorstLine(RouteProfile profile)
    {
        if (float.IsNaN(profile.MaxAbsGradePercent) || profile.WorstSegments.Count == 0)
        {
            return TeamsterStrings.Get("profile.gradesNoData");
        }

        RouteProfileSegment worst = profile.WorstSegments[0];
        return TeamsterStrings.Format(
            worst.GradePercent >= 0f ? "profile.worstClimb" : "profile.worstDescent",
            worst.GradePercent.ToString("+0.0;-0.0", CultureInfo.InvariantCulture),
            Meters(worst.StartMeters));
    }

    private static string BandLine(RouteProfile profile)
    {
        float graded = 0f;
        for (int index = 0; index < profile.GradeBandMeters.Count; index++)
        {
            graded += profile.GradeBandMeters[index];
        }

        if (graded <= 0f)
        {
            return string.Empty;
        }

        var parts = new List<string>(RouteProfile.GradeBandCount);
        for (int index = 0; index < profile.GradeBandMeters.Count; index++)
        {
            float meters = profile.GradeBandMeters[index];
            if (meters > 0f)
            {
                parts.Add(BandLabels[index] + " " + Percent(meters, graded));
            }
        }

        return TeamsterStrings.Format("profile.gradeMix", string.Join(", ", parts));
    }

    private static string BottleneckLine(RouteLoadBottleneck.Result? bottleneck)
    {
        if (bottleneck is null)
        {
            return TeamsterStrings.Get("profile.loadNoModel");
        }

        if (!bottleneck.HasGradeData)
        {
            return TeamsterStrings.Get("profile.loadNoGrade");
        }

        if (!bottleneck.MassAdviceReliable)
        {
            return TeamsterStrings.Get("compat.loadAdviceUnavailableLine");
        }

        string grade = bottleneck.BottleneckGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "%";
        string proven = bottleneck.ProvenMaxMass is null
            ? TeamsterStrings.Format("profile.loadNoProven", grade)
            : TeamsterStrings.Format(
                "profile.loadProven",
                bottleneck.ProvenMaxMass.TotalMass.ToString("F0", CultureInfo.InvariantCulture),
                grade,
                Load.LoadText.BasisWord(bottleneck.ProvenMaxMass.Basis));

        if (bottleneck.Verdict is null)
        {
            return TeamsterStrings.Format("profile.loadCheck", proven);
        }

        return TeamsterStrings.Format(
            "profile.loadCheckWithVerdict", proven,
            bottleneck.QueriedMass.ToString("F0", CultureInfo.InvariantCulture),
            VerdictWord(bottleneck.Verdict));
    }

    private static string VerdictWord(Load.LoadVerdict verdict)
    {
        return TeamsterStrings.Get(verdict.Climbability switch
        {
            Load.Climbability.Yes => "verdict.ok",
            Load.Climbability.Marginal => "verdict.marginal",
            Load.Climbability.No => "verdict.tooHeavy",
            _ => "verdict.unknown",
        });
    }

    private static string Meters(float value)
    {
        return TeamsterStrings.Format(
            "unit.meters", value.ToString("F0", CultureInfo.InvariantCulture));
    }

    private static string Percent(float part, float whole)
    {
        return (part / whole * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%";
    }
}
