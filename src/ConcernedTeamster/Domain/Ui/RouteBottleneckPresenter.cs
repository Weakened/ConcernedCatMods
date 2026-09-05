using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Route bottleneck analysis (CT-019), pure domain math over a
/// recorded trip: the worst-grade point, the roughest crossed segment, and
/// the point where the load model binds for a chosen (hypothetical) cargo
/// mass — all located by distance along the route and explained by naming
/// their constraint. The hypothetical-load pass reuses the recorded grades
/// only; no new sampling, no game access, and Unknown coverage is reported
/// honestly instead of pretending a clear verdict.</summary>
public static class RouteBottleneckPresenter
{
    public sealed class ViewModel
    {
        public ViewModel(bool available, string message, IReadOnlyList<string> lines)
        {
            Available = available;
            Message = message;
            Lines = lines;
        }

        public bool Available { get; }

        public string Message { get; }

        public IReadOnlyList<string> Lines { get; }
    }

    public static ViewModel Present(
        Trip? trip,
        RoadQualityIndex? segments,
        LoadModel? loadModel,
        string? hypotheticalMassText)
    {
        if (trip is null)
        {
            return new ViewModel(false,
                "Select trip [A] to analyze its bottlenecks.", Array.Empty<string>());
        }

        if (!TryParseMass(hypotheticalMassText, out float mass, out string massProblem))
        {
            return new ViewModel(false, massProblem, Array.Empty<string>());
        }

        // One walk computes cumulative distance and everything located on it.
        int count = trip.Samples.Count;
        var cumulative = new float[count];
        float total = 0f;
        for (int index = 1; index < count; index++)
        {
            TripSample previous = trip.Samples[index - 1];
            TripSample sample = trip.Samples[index];
            float deltaX = sample.PositionX - previous.PositionX;
            float deltaZ = sample.PositionZ - previous.PositionZ;
            total += (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            cumulative[index] = total;
        }

        var lines = new List<string>(3)
        {
            DescribeWorstGrade(trip, cumulative, total),
            DescribeWorstQuality(trip, segments, cumulative, total),
            DescribeLoadBinding(trip, loadModel, mass, cumulative, total),
        };

        return new ViewModel(true,
            "Bottlenecks for trip #" + trip.Id.ToString(CultureInfo.InvariantCulture) +
            " at mass " + mass.ToString("F0", CultureInfo.InvariantCulture) + ":",
            lines);
    }

    private static bool TryParseMass(string? text, out float mass, out string problem)
    {
        problem = string.Empty;
        mass = 0f;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            problem = "Enter a cargo total mass to test (for example 220).";
            return false;
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out mass) ||
            float.IsNaN(mass) || float.IsInfinity(mass) || mass <= 0f)
        {
            problem = "\"" + trimmed + "\" is not a usable mass — enter a positive number.";
            return false;
        }

        return true;
    }

    private static string DescribeWorstGrade(Trip trip, float[] cumulative, float total)
    {
        int worstIndex = -1;
        float worst = 0f;
        for (int index = 0; index < trip.Samples.Count; index++)
        {
            float grade = trip.Samples[index].GradePercent;
            if (!float.IsNaN(grade) && (worstIndex < 0 || Math.Abs(grade) > worst))
            {
                worst = Math.Abs(grade);
                worstIndex = index;
            }
        }

        if (worstIndex < 0)
        {
            return "Grade: no grade data on this trip.";
        }

        return "Grade constraint: steepest point is " +
            trip.Samples[worstIndex].GradePercent.ToString("F1", CultureInfo.InvariantCulture) +
            "% at " + Locate(cumulative, worstIndex, total) + ".";
    }

    private static string DescribeWorstQuality(
        Trip trip, RoadQualityIndex? segments, float[] cumulative, float total)
    {
        if (segments is null || segments.Segments.Count == 0)
        {
            return "Quality: no scored segments yet for this world.";
        }

        float worstRoughness = float.NaN;
        int worstIndex = -1;
        RoadSegmentKey worstKey = default;
        var seen = new HashSet<RoadSegmentKey>();
        for (int index = 0; index < trip.Samples.Count; index++)
        {
            TripSample sample = trip.Samples[index];
            RoadSegmentKey key = RoadSegmentKey.FromPosition(sample.PositionX, sample.PositionZ);
            if (!seen.Add(key))
            {
                continue;
            }

            if (segments.Segments.TryGetValue(key, out RoadSegmentStats? stats) &&
                !float.IsNaN(stats.RoughnessGradeJitter) &&
                (float.IsNaN(worstRoughness) || stats.RoughnessGradeJitter > worstRoughness))
            {
                worstRoughness = stats.RoughnessGradeJitter;
                worstIndex = index;
                worstKey = key;
            }
        }

        if (worstIndex < 0)
        {
            return "Quality: crossed segments have no roughness scores yet.";
        }

        return "Quality constraint: roughest crossed segment (jitter " +
            worstRoughness.ToString("F1", CultureInfo.InvariantCulture) + "%) enters at " +
            Locate(cumulative, worstIndex, total) +
            " (cell " + worstKey.CellX.ToString(CultureInfo.InvariantCulture) +
            "," + worstKey.CellZ.ToString(CultureInfo.InvariantCulture) + ").";
    }

    private static string DescribeLoadBinding(
        Trip trip, LoadModel? loadModel, float mass, float[] cumulative, float total)
    {
        if (loadModel is null)
        {
            return "Load: no calibration data — cannot test a load against this route.";
        }

        int climbPoints = 0;
        int unknownPoints = 0;
        int bindingIndex = -1;
        LoadVerdict? bindingVerdict = null;
        int marginalIndex = -1;
        LoadVerdict? marginalVerdict = null;

        for (int index = 0; index < trip.Samples.Count; index++)
        {
            float grade = trip.Samples[index].GradePercent;
            if (float.IsNaN(grade) || grade <= 0f)
            {
                continue;
            }

            climbPoints++;
            LoadVerdict verdict = loadModel.Query(grade, mass);
            switch (verdict.Climbability)
            {
                case Climbability.No:
                    if (bindingIndex < 0)
                    {
                        bindingIndex = index;
                        bindingVerdict = verdict;
                    }

                    break;
                case Climbability.Marginal:
                    if (marginalIndex < 0)
                    {
                        marginalIndex = index;
                        marginalVerdict = verdict;
                    }

                    break;
                case Climbability.Unknown:
                    unknownPoints++;
                    break;
            }
        }

        if (climbPoints == 0)
        {
            return "Load: this route never climbs — mass " +
                mass.ToString("F0", CultureInfo.InvariantCulture) + " is not grade-limited here.";
        }

        if (bindingIndex >= 0)
        {
            return "Load constraint BINDS: mass " + mass.ToString("F0", CultureInfo.InvariantCulture) +
                " is proven to fail at " + Locate(cumulative, bindingIndex, total) +
                " (" + bindingVerdict!.Explanation + "). Lighten below the proven limit or reroute.";
        }

        if (marginalIndex >= 0)
        {
            return "Load constraint is marginal at " + Locate(cumulative, marginalIndex, total) +
                " (" + marginalVerdict!.Explanation + ").";
        }

        if (unknownPoints > 0)
        {
            return "Load: no proven blocker, but " +
                unknownPoints.ToString(CultureInfo.InvariantCulture) + " of " +
                climbPoints.ToString(CultureInfo.InvariantCulture) +
                " climb points are uncalibrated — run the protocol to firm this up.";
        }

        return "Load: every climb point is proven passable at mass " +
            mass.ToString("F0", CultureInfo.InvariantCulture) + ".";
    }

    private static string Locate(float[] cumulative, int index, float total)
    {
        float at = cumulative[index];
        string meters = at.ToString("F0", CultureInfo.InvariantCulture) + " m";
        if (total <= 0f)
        {
            return meters;
        }

        float percent = at / total * 100f;
        return meters + " (" + percent.ToString("F0", CultureInfo.InvariantCulture) + "% of the route)";
    }
}
