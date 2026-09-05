using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-018: summaries compute the documented aggregates, the
/// history listing sorts deterministically with text selection markers and
/// an explicit empty state, the comparison aligns different-length trips on
/// normalized distance with non-color series labels, and deletion removes
/// exactly one trip while cumulative segments stay.</summary>
public class TripHistoryUiTests
{
    private static Trip StraightTrip(
        int id, int samples, float stepMeters, float grade = 4f, float speed = 1.5f,
        float mass = 220f, double startTime = 100.0)
    {
        var list = new TripSample[samples];
        for (int index = 0; index < samples; index++)
        {
            list[index] = new TripSample(
                startTime + index, index * stepMeters, 0f, grade, speed, mass);
        }

        return new Trip(id, "1:1", list);
    }

    // -- summarizer ------------------------------------------------------

    [Fact]
    public void Summarize_ComputesDistanceDurationWorstAndMeans()
    {
        Trip trip = StraightTrip(3, samples: 11, stepMeters: 5f, grade: -7f, speed: 2f, mass: 300f);

        TripSummary summary = TripSummarizer.Summarize(trip);

        Assert.Equal(3, summary.TripId);
        Assert.Equal(50f, summary.DistanceMeters, precision: 3);   // 10 hops x 5 m
        Assert.Equal(10.0, summary.DurationSeconds, precision: 3);
        Assert.Equal(7f, summary.WorstAbsGradePercent, precision: 3);
        Assert.Equal(2f, summary.MeanSpeedMetersPerSecond, precision: 3);
        Assert.Equal(300f, summary.MeanMass, precision: 3);
    }

    [Fact]
    public void Summarize_AllNaNGrades_ReportNaNNotZero()
    {
        var samples = new[]
        {
            new TripSample(0, 0f, 0f, float.NaN, float.NaN, 100f),
            new TripSample(1, 5f, 0f, float.NaN, float.NaN, 100f),
        };
        TripSummary summary = TripSummarizer.Summarize(new Trip(1, "1:1", samples));

        Assert.True(float.IsNaN(summary.WorstAbsGradePercent));
        Assert.True(float.IsNaN(summary.MeanSpeedMetersPerSecond));
    }

    // -- history listing -------------------------------------------------

    private static List<TripSummary> Summaries()
    {
        return new List<TripSummary>
        {
            TripSummarizer.Summarize(StraightTrip(1, 5, 10f, grade: 2f, mass: 100f, startTime: 100)),
            TripSummarizer.Summarize(StraightTrip(2, 20, 10f, grade: 9f, mass: 300f, startTime: 500)),
            TripSummarizer.Summarize(StraightTrip(3, 10, 2f, grade: 5f, mass: 200f, startTime: 300)),
        };
    }

    [Theory]
    [InlineData(TripHistoryPresenter.SortColumn.StartTime, true, new[] { 2, 3, 1 })]
    [InlineData(TripHistoryPresenter.SortColumn.StartTime, false, new[] { 1, 3, 2 })]
    [InlineData(TripHistoryPresenter.SortColumn.Distance, true, new[] { 2, 1, 3 })]
    [InlineData(TripHistoryPresenter.SortColumn.Load, false, new[] { 1, 3, 2 })]
    [InlineData(TripHistoryPresenter.SortColumn.WorstGrade, true, new[] { 2, 3, 1 })]
    [InlineData(TripHistoryPresenter.SortColumn.Duration, true, new[] { 2, 3, 1 })]
    public void Present_SortMatrix_IsDeterministic(
        TripHistoryPresenter.SortColumn column, bool descending, int[] expectedIds)
    {
        TripHistoryPresenter.ViewModel viewModel = TripHistoryPresenter.Present(
            Summaries(), column, descending, null, null);

        Assert.Equal(expectedIds.Length, viewModel.Rows.Count);
        for (int index = 0; index < expectedIds.Length; index++)
        {
            Assert.Equal(expectedIds[index], viewModel.Rows[index].TripId);
        }
    }

    [Fact]
    public void Present_SelectionMarkers_AreTextNotColor()
    {
        TripHistoryPresenter.ViewModel viewModel = TripHistoryPresenter.Present(
            Summaries(), TripHistoryPresenter.SortColumn.StartTime, false,
            selectedAId: 1, selectedBId: 3);

        Assert.StartsWith("[A] ", viewModel.Rows[0].Text); // trip 1 first ascending
        Assert.StartsWith("[B] ", viewModel.Rows[1].Text); // trip 3
        Assert.StartsWith("    ", viewModel.Rows[2].Text);
    }

    [Fact]
    public void Present_Empty_IsExplicit()
    {
        TripHistoryPresenter.ViewModel viewModel = TripHistoryPresenter.Present(
            new List<TripSummary>(), TripHistoryPresenter.SortColumn.StartTime, true, null, null);

        Assert.True(viewModel.Empty);
        Assert.Contains("No trips recorded", viewModel.Message);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void Present_RowFormat_IsInvariantAndComplete()
    {
        TripHistoryPresenter.ViewModel viewModel = TripHistoryPresenter.Present(
            new List<TripSummary> { TripSummarizer.Summarize(StraightTrip(7, 11, 5f, 4f, 1.5f, 220f)) },
            TripHistoryPresenter.SortColumn.StartTime, true, null, null);

        string text = viewModel.Rows[0].Text;
        Assert.Contains("#7", text);
        Assert.Contains("0:10", text);      // duration
        Assert.Contains("50 m", text);
        Assert.Contains("mass 220", text);
        Assert.Contains("worst 4%", text);
        Assert.Contains("avg 1.5 m/s", text);
    }

    // -- comparison ------------------------------------------------------

    [Fact]
    public void Comparison_AlignsDifferentLengthsByNormalizedDistance()
    {
        // Trip A: 100 m with grade 10 in its LAST fifth only; Trip B: 50 m
        // with grade 10 in its FIRST fifth only. Alignment by fraction must
        // put A's spike in bucket 4 and B's in bucket 0.
        var samplesA = new TripSample[21];
        for (int index = 0; index <= 20; index++)
        {
            float grade = index > 16 ? 10f : 0f;
            samplesA[index] = new TripSample(index, index * 5f, 0f, grade, 1f, 100f);
        }

        var samplesB = new TripSample[11];
        for (int index = 0; index <= 10; index++)
        {
            float grade = index > 0 && index <= 2 ? 10f : 0f;
            samplesB[index] = new TripSample(index, index * 5f, 0f, grade, 1f, 100f);
        }

        var tripA = new Trip(1, "1:1", samplesA);
        var tripB = new Trip(2, "1:1", samplesB);

        float[] bucketsA = TripComparisonPresenter.BucketMeanAbsGrades(tripA);
        float[] bucketsB = TripComparisonPresenter.BucketMeanAbsGrades(tripB);

        Assert.True(bucketsA[4] > 5f && bucketsA[0] < 1f,
            $"A spike should sit in the last bucket: [{string.Join(", ", bucketsA)}]");
        Assert.True(bucketsB[0] > 1f && bucketsB[4] < 1f,
            $"B spike should sit in the first bucket: [{string.Join(", ", bucketsB)}]");

        TripComparisonPresenter.ViewModel viewModel = TripComparisonPresenter.Present(tripA, tripB);
        Assert.True(viewModel.HasComparison);
        Assert.StartsWith("A #1", viewModel.HeaderA);   // non-color labels
        Assert.StartsWith("B #2", viewModel.HeaderB);
        Assert.Equal(TripComparisonPresenter.BucketCount, viewModel.BucketLines.Count);
        Assert.All(viewModel.BucketLines, line => Assert.Contains("A ", line));
        Assert.All(viewModel.BucketLines, line => Assert.Contains("B ", line));
    }

    [Fact]
    public void Comparison_MissingSelections_AreExplicit()
    {
        Assert.Contains("Select two trips",
            TripComparisonPresenter.Present(null, null).Message);
        Assert.Contains("Select a second trip",
            TripComparisonPresenter.Present(StraightTrip(1, 6, 5f), null).Message);
        Assert.False(TripComparisonPresenter.Present(null, StraightTrip(2, 6, 5f)).HasComparison);
    }

    // -- deletion --------------------------------------------------------

    [Fact]
    public void Deletion_RemovesExactlyOneTrip_SegmentsStay()
    {
        Trip a = StraightTrip(0, 6, 5f, grade: 2f);
        Trip b = StraightTrip(0, 6, 5f, grade: 8f, startTime: 300.0);
        RoadQualityIndex segments = RoadQualityIndex.ComputeFromTrips(new[] { a, b });
        IReadOnlyList<Trip> stored = TripSidecar.Prune(new[] { a, b }, 50); // ids 1,2
        string original = TripSidecar.Compose(stored, 42L, "0.4.0", segments);

        // Delete trip id 1 the way the service does: filter, renumber,
        // recompose with UNCHANGED segments.
        TripSidecar.ParseResult parsed = TripSidecar.Parse(original, 42L);
        var remaining = new List<Trip>();
        foreach (Trip trip in parsed.Trips)
        {
            if (trip.Id != 1)
            {
                remaining.Add(trip);
            }
        }

        string after = TripSidecar.Compose(
            TripSidecar.Prune(remaining, int.MaxValue), 42L, "0.4.0", parsed.Segments);
        TripSidecar.ParseResult reparsed = TripSidecar.Parse(after, 42L);

        Assert.Single(reparsed.Trips);
        Assert.Equal(1, reparsed.Trips[0].Id); // renumbered densely
        Assert.Equal(300.0, reparsed.Trips[0].StartTimeSeconds); // it is trip B
        // Cumulative segment history is untouched by raw-trip deletion.
        Assert.Equal(segments.Segments.Count, reparsed.Segments.Segments.Count);
    }
}
