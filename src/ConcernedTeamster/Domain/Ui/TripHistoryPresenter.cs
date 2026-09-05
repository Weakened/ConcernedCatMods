using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless trip history listing (CT-018): deterministic sorting
/// over precomputed summaries (trip-id tiebreak), invariant formatting,
/// explicit empty state, and A/B selection markers rendered as text so
/// selection is never color-only. Sorting is pure list work — the panel
/// cost at the maximum retained trip count is one summary pass at load
/// plus O(n log n) here.</summary>
public static class TripHistoryPresenter
{
    public enum SortColumn
    {
        StartTime,
        Duration,
        Distance,
        Load,
        WorstGrade,
    }

    public sealed class Row
    {
        public Row(int tripId, string text)
        {
            TripId = tripId;
            Text = text;
        }

        public int TripId { get; }

        public string Text { get; }
    }

    public sealed class ViewModel
    {
        public ViewModel(bool empty, string message, IReadOnlyList<Row> rows)
        {
            Empty = empty;
            Message = message;
            Rows = rows;
        }

        public bool Empty { get; }

        public string Message { get; }

        public IReadOnlyList<Row> Rows { get; }
    }

    public static ViewModel Present(
        IReadOnlyList<TripSummary> summaries,
        SortColumn sortColumn,
        bool descending,
        int? selectedAId,
        int? selectedBId)
    {
        if (summaries.Count == 0)
        {
            return new ViewModel(true, "No trips recorded in this world yet — pull a cart!", Array.Empty<Row>());
        }

        var sorted = new List<TripSummary>(summaries);
        sorted.Sort((left, right) =>
        {
            int comparison = sortColumn switch
            {
                SortColumn.StartTime => left.StartTimeSeconds.CompareTo(right.StartTimeSeconds),
                SortColumn.Duration => left.DurationSeconds.CompareTo(right.DurationSeconds),
                SortColumn.Distance => left.DistanceMeters.CompareTo(right.DistanceMeters),
                SortColumn.Load => left.MeanMass.CompareTo(right.MeanMass),
                _ => CompareWithNaNLast(left.WorstAbsGradePercent, right.WorstAbsGradePercent),
            };
            if (descending)
            {
                comparison = -comparison;
            }

            return comparison != 0 ? comparison : left.TripId.CompareTo(right.TripId);
        });

        var rows = new Row[sorted.Count];
        for (int index = 0; index < sorted.Count; index++)
        {
            TripSummary summary = sorted[index];
            string marker = summary.TripId == selectedAId ? "[A] "
                : summary.TripId == selectedBId ? "[B] "
                : "    ";
            rows[index] = new Row(summary.TripId, marker + Format(summary));
        }

        return new ViewModel(false, string.Empty, rows);
    }

    private static int CompareWithNaNLast(float left, float right)
    {
        bool leftNaN = float.IsNaN(left);
        bool rightNaN = float.IsNaN(right);
        if (leftNaN != rightNaN)
        {
            // NaN sorts as smallest so "descending by worst grade" puts
            // unknown-grade trips last either way after negation quirks;
            // the tiebreak keeps it deterministic.
            return leftNaN ? -1 : 1;
        }

        return leftNaN ? 0 : left.CompareTo(right);
    }

    private static string Format(TripSummary summary)
    {
        return "#" + summary.TripId.ToString(CultureInfo.InvariantCulture) +
            "  " + FormatDuration(summary.DurationSeconds) +
            "  " + summary.DistanceMeters.ToString("F0", CultureInfo.InvariantCulture) + " m" +
            "  mass " + summary.MeanMass.ToString("F0", CultureInfo.InvariantCulture) +
            "  worst " + (float.IsNaN(summary.WorstAbsGradePercent)
                ? "?"
                : summary.WorstAbsGradePercent.ToString("F0", CultureInfo.InvariantCulture) + "%") +
            "  avg " + (float.IsNaN(summary.MeanSpeedMetersPerSecond)
                ? "?"
                : summary.MeanSpeedMetersPerSecond.ToString("F1", CultureInfo.InvariantCulture) + " m/s");
    }

    private static string FormatDuration(double seconds)
    {
        int total = (int)Math.Round(seconds);
        int minutes = total / 60;
        int remainder = total % 60;
        return minutes.ToString(CultureInfo.InvariantCulture) + ":" +
            remainder.ToString("D2", CultureInfo.InvariantCulture);
    }
}
