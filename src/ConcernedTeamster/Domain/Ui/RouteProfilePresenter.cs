using System.Collections.Generic;
using System.Globalization;
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
            lines[0] = "Profiling route… " +
                positionsProbed.ToString(CultureInfo.InvariantCulture) + "/" +
                positionCount.ToString(CultureInfo.InvariantCulture) + " samples";
            return lines;
        }

        lines[0] = "Route " + Meters(profile.TotalDistanceMeters) +
            ": sampled " + Meters(profile.SampledMeters) +
            (profile.UnsampledMeters > 0.05f
                ? ", UNSAMPLED " + Meters(profile.UnsampledMeters) + " (unloaded terrain)"
                : "");

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
            return "Surfaces: none sampled";
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
                parts.Add(kind.ToString().ToLowerInvariant() + " " + Percent(meters, profile.SampledMeters));
            }
        }

        if (profile.SurfaceUnknownMeters > 0f)
        {
            parts.Add("unknown " + Percent(profile.SurfaceUnknownMeters, profile.SampledMeters));
        }

        return "Surfaces: " + (parts.Count == 0 ? "none classified" : string.Join(", ", parts));
    }

    private static string WorstLine(RouteProfile profile)
    {
        if (float.IsNaN(profile.MaxAbsGradePercent) || profile.WorstSegments.Count == 0)
        {
            return "Grades: no sampled grade data";
        }

        RouteProfileSegment worst = profile.WorstSegments[0];
        string direction = worst.GradePercent >= 0f ? "climb" : "descent";
        return "Worst " + direction + " " +
            worst.GradePercent.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "% at " +
            Meters(worst.StartMeters) + " from start";
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

        return "Grade mix: " + string.Join(", ", parts);
    }

    private static string BottleneckLine(RouteLoadBottleneck.Result? bottleneck)
    {
        if (bottleneck is null)
        {
            return "Load check: no load model available";
        }

        if (!bottleneck.HasGradeData)
        {
            return "Load check: no grade data yet";
        }

        string grade = bottleneck.BottleneckGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "%";
        string proven = bottleneck.ProvenMaxMass is null
            ? "no proven load at " + grade
            : "proven " + bottleneck.ProvenMaxMass.TotalMass.ToString("F0", CultureInfo.InvariantCulture) +
                " mass at " + grade + " (" + bottleneck.ProvenMaxMass.Basis + ")";

        if (bottleneck.Verdict is null)
        {
            return "Load check: " + proven;
        }

        return "Load check: " + proven + " · your cart (" +
            bottleneck.QueriedMass.ToString("F0", CultureInfo.InvariantCulture) + "): " +
            VerdictWord(bottleneck.Verdict);
    }

    private static string VerdictWord(Load.LoadVerdict verdict)
    {
        return verdict.Climbability switch
        {
            Load.Climbability.Yes => "OK",
            Load.Climbability.Marginal => "MARGINAL",
            Load.Climbability.No => "TOO HEAVY",
            _ => "UNKNOWN",
        };
    }

    private static string Meters(float value)
    {
        return value.ToString("F0", CultureInfo.InvariantCulture) + " m";
    }

    private static string Percent(float part, float whole)
    {
        return (part / whole * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%";
    }
}
