using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless side-by-side trip comparison (CT-018): both trips are
/// resampled onto shared distance-normalized quintiles (0–20% … 80–100% of
/// each trip's own length), so routes of different lengths align by
/// fraction of the way. Series are labeled "A #id" / "B #id" — text, never
/// color alone. Missing selections render explicit prompts.</summary>
public static class TripComparisonPresenter
{
    public const int BucketCount = 5;

    public sealed class ViewModel
    {
        public ViewModel(
            bool hasComparison,
            string message,
            string headerA,
            string headerB,
            IReadOnlyList<string> bucketLines)
        {
            HasComparison = hasComparison;
            Message = message;
            HeaderA = headerA;
            HeaderB = headerB;
            BucketLines = bucketLines;
        }

        public bool HasComparison { get; }

        public string Message { get; }

        public string HeaderA { get; }

        public string HeaderB { get; }

        public IReadOnlyList<string> BucketLines { get; }
    }

    public static ViewModel Present(Trip? tripA, Trip? tripB)
    {
        if (tripA is null && tripB is null)
        {
            return new ViewModel(false,
                "Select two trips ([A] and [B]) to compare their profiles.",
                string.Empty, string.Empty, Array.Empty<string>());
        }

        if (tripA is null || tripB is null)
        {
            return new ViewModel(false,
                "Select a second trip to compare against [" + (tripA is null ? "B" : "A") + "].",
                string.Empty, string.Empty, Array.Empty<string>());
        }

        TripSummary summaryA = TripSummarizer.Summarize(tripA);
        TripSummary summaryB = TripSummarizer.Summarize(tripB);
        float[] gradesA = BucketMeanAbsGrades(tripA);
        float[] gradesB = BucketMeanAbsGrades(tripB);
        float[] speedsA = BucketMeanSpeeds(tripA);
        float[] speedsB = BucketMeanSpeeds(tripB);

        var lines = new string[BucketCount];
        for (int bucket = 0; bucket < BucketCount; bucket++)
        {
            int fromPercent = bucket * 100 / BucketCount;
            int toPercent = (bucket + 1) * 100 / BucketCount;
            lines[bucket] =
                fromPercent.ToString(CultureInfo.InvariantCulture) + "–" +
                toPercent.ToString(CultureInfo.InvariantCulture) + "%:  " +
                "A " + FormatGrade(gradesA[bucket]) + " @ " + FormatSpeed(speedsA[bucket]) +
                "   |   B " + FormatGrade(gradesB[bucket]) + " @ " + FormatSpeed(speedsB[bucket]);
        }

        return new ViewModel(
            true,
            string.Empty,
            "A #" + tripA.Id + ": " + Describe(summaryA),
            "B #" + tripB.Id + ": " + Describe(summaryB),
            lines);
    }

    /// <summary>Mean |grade| per distance-normalized bucket; NaN when the
    /// bucket has no finite-grade samples.</summary>
    public static float[] BucketMeanAbsGrades(Trip trip)
    {
        return BucketAggregate(trip, sample => float.IsNaN(sample.GradePercent)
            ? float.NaN
            : Math.Abs(sample.GradePercent));
    }

    public static float[] BucketMeanSpeeds(Trip trip)
    {
        return BucketAggregate(trip, sample => sample.SpeedMetersPerSecond);
    }

    private static float[] BucketAggregate(Trip trip, Func<TripSample, float> selector)
    {
        var sums = new float[BucketCount];
        var counts = new int[BucketCount];

        // Cumulative distance locates each sample as a fraction of the
        // trip's own length — that is the shared axis.
        float totalDistance = 0f;
        var cumulative = new float[trip.Samples.Count];
        for (int index = 1; index < trip.Samples.Count; index++)
        {
            TripSample previous = trip.Samples[index - 1];
            TripSample sample = trip.Samples[index];
            float deltaX = sample.PositionX - previous.PositionX;
            float deltaZ = sample.PositionZ - previous.PositionZ;
            totalDistance += (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            cumulative[index] = totalDistance;
        }

        for (int index = 0; index < trip.Samples.Count; index++)
        {
            float value = selector(trip.Samples[index]);
            if (float.IsNaN(value))
            {
                continue;
            }

            float fraction = totalDistance > 0f ? cumulative[index] / totalDistance : 0f;
            int bucket = Math.Min(BucketCount - 1, (int)(fraction * BucketCount));
            sums[bucket] += value;
            counts[bucket]++;
        }

        var means = new float[BucketCount];
        for (int bucket = 0; bucket < BucketCount; bucket++)
        {
            means[bucket] = counts[bucket] > 0 ? sums[bucket] / counts[bucket] : float.NaN;
        }

        return means;
    }

    private static string Describe(TripSummary summary)
    {
        return summary.DistanceMeters.ToString("F0", CultureInfo.InvariantCulture) + " m, mass " +
            summary.MeanMass.ToString("F0", CultureInfo.InvariantCulture) + ", worst " +
            (float.IsNaN(summary.WorstAbsGradePercent)
                ? "?"
                : summary.WorstAbsGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "%");
    }

    private static string FormatGrade(float value)
    {
        return float.IsNaN(value) ? "—" : value.ToString("F1", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatSpeed(float value)
    {
        return float.IsNaN(value) ? "—" : value.ToString("F1", CultureInfo.InvariantCulture) + " m/s";
    }
}
