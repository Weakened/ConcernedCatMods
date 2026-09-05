using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace ConcernedTeamster.Tests;

/// <summary>CT-017: synthetic trips with known roughness/grade/drag produce
/// the expected scores, incremental equals batch (including byte-identical
/// composed output), per-trip cost stays bounded by sample count, and the
/// v1→v2 migration recompute matches.</summary>
public class RoadQualityTests
{
    private static Trip TripOf(params (float X, float Z, float Grade, float Speed)[] samples)
    {
        var list = new TripSample[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            (float x, float z, float grade, float speed) = samples[index];
            list[index] = new TripSample(index, x, z, grade, speed, 120f);
        }

        return new Trip(0, "1:1", list);
    }

    [Fact]
    public void Key_QuantizesPositionsStably()
    {
        Assert.Equal(new RoadSegmentKey(0, 0), RoadSegmentKey.FromPosition(0f, 7.9f));
        Assert.Equal(new RoadSegmentKey(1, 0), RoadSegmentKey.FromPosition(8f, 0f));
        Assert.Equal(new RoadSegmentKey(-1, -1), RoadSegmentKey.FromPosition(-0.1f, -7.9f));
    }

    [Fact]
    public void Scores_KnownRoughGradeAndDrag_ComputeExactly()
    {
        // One segment (all x,z inside cell 0,0): grades 0,4,0,4 -> deltas
        // 4,4,4 -> roughness 4; mean grade 2; max 4; level samples are the
        // two 0% ones at speeds 2.0 and 1.0 -> drag proxy 1.5.
        Trip trip = TripOf(
            (1f, 1f, 0f, 2.0f),
            (2f, 2f, 4f, 1.6f),
            (3f, 3f, 0f, 1.0f),
            (4f, 4f, 4f, 1.4f));

        RoadQualityIndex index = RoadQualityIndex.ComputeFromTrips(new[] { trip });

        RoadSegmentStats stats = index.Segments[new RoadSegmentKey(0, 0)];
        Assert.Equal(4, stats.SampleCount);
        Assert.Equal(3, stats.PairCount);
        Assert.Equal(4f, stats.RoughnessGradeJitter, precision: 4);
        Assert.Equal(2f, stats.MeanGradePercent, precision: 4);
        Assert.Equal(4f, stats.MaxAbsGrade, precision: 4);
        Assert.Equal(1.5f, stats.DragProxySpeed, precision: 4);
    }

    [Fact]
    public void Scores_SmoothVsRoughSegments_RankCorrectly()
    {
        Trip smooth = TripOf(
            (1f, 1f, 3f, 1f), (2f, 1f, 3.2f, 1f), (3f, 1f, 3.1f, 1f), (4f, 1f, 3f, 1f));
        Trip rough = TripOf(
            (100f, 100f, 0f, 1f), (101f, 100f, 8f, 1f), (102f, 100f, -6f, 1f), (103f, 100f, 7f, 1f));

        RoadQualityIndex index = RoadQualityIndex.ComputeFromTrips(new[] { smooth, rough });

        float smoothRoughness = index.Segments[RoadSegmentKey.FromPosition(1f, 1f)].RoughnessGradeJitter;
        float roughRoughness = index.Segments[RoadSegmentKey.FromPosition(100f, 100f)].RoughnessGradeJitter;
        Assert.True(roughRoughness > smoothRoughness * 10f,
            $"rough {roughRoughness} should dwarf smooth {smoothRoughness}");
    }

    [Fact]
    public void Scores_NaNGradeSamples_BreakPairsAndSkipStats()
    {
        Trip trip = TripOf(
            (1f, 1f, 2f, 1f), (2f, 2f, float.NaN, 1f), (3f, 3f, 4f, 1f));

        RoadSegmentStats stats = RoadQualityIndex.ComputeFromTrips(new[] { trip })
            .Segments[new RoadSegmentKey(0, 0)];

        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(2, stats.GradeCount);   // NaN sample contributes nothing
        Assert.Equal(0, stats.PairCount);    // NaN breaks both adjacent pairs
        Assert.True(float.IsNaN(stats.RoughnessGradeJitter));
    }

    [Fact]
    public void Scores_CrossCellDeltas_BelongToNeitherCell()
    {
        // Two samples in cell (0,0), then two in cell (2,0): the border
        // delta is dropped; each cell pairs only internally.
        Trip trip = TripOf(
            (1f, 1f, 0f, 1f), (2f, 1f, 2f, 1f), (17f, 1f, 10f, 1f), (18f, 1f, 12f, 1f));

        RoadQualityIndex index = RoadQualityIndex.ComputeFromTrips(new[] { trip });

        Assert.Equal(1, index.Segments[new RoadSegmentKey(0, 0)].PairCount);
        Assert.Equal(1, index.Segments[new RoadSegmentKey(2, 0)].PairCount);
    }

    [Fact]
    public void IncrementalEqualsBatch_IncludingByteIdenticalOutput()
    {
        Trip a = TripOf((1f, 1f, 0f, 2f), (2f, 2f, 3f, 1.5f), (9f, 1f, 5f, 1f));
        Trip b = TripOf((1f, 2f, 1f, 2.2f), (2f, 3f, 2f, 1.8f), (30f, 30f, -4f, 1.2f));
        Trip c = TripOf((100f, -50f, 6f, 0.8f), (101f, -50f, 6.5f, 0.7f));

        RoadQualityIndex incremental = new();
        incremental.AddTrip(a);
        incremental.AddTrip(b);
        incremental.AddTrip(c);

        RoadQualityIndex batch = RoadQualityIndex.ComputeFromTrips(new[] { a, b, c });

        string incrementalText = TripSidecar.Compose(new[] { a, b, c }, 42L, "0.4.0", incremental);
        string batchText = TripSidecar.Compose(new[] { a, b, c }, 42L, "0.4.0", batch);
        Assert.Equal(batchText, incrementalText);

        // And determinism: composing again is byte-identical.
        Assert.Equal(incrementalText, TripSidecar.Compose(new[] { a, b, c }, 42L, "0.4.0", incremental));
    }

    [Fact]
    public void AddTrip_CostBound_TouchedSegmentsNeverExceedSampleCount()
    {
        var index = new RoadQualityIndex();
        Trip sprawling = TripOf(
            (1f, 1f, 0f, 1f), (20f, 1f, 1f, 1f), (40f, 1f, 2f, 1f),
            (60f, 1f, 3f, 1f), (60.5f, 1f, 3f, 1f));

        index.AddTrip(sprawling);

        Assert.True(index.LastAddTouchedSegments <= sprawling.Samples.Count);
        Assert.Equal(4, index.LastAddTouchedSegments); // last two share a cell
    }

    [Fact]
    public void SegmentRows_RoundTripThroughTheSidecar()
    {
        Trip trip = TripOf((1f, 1f, 0f, 2f), (2f, 2f, 4f, 1.6f), (3f, 3f, 0f, 1f));
        RoadQualityIndex index = RoadQualityIndex.ComputeFromTrips(new[] { trip });

        string text = TripSidecar.Compose(new[] { trip }, 42L, "0.4.0", index);
        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, 42L);

        Assert.False(parsed.Refused);
        Assert.False(parsed.NeedsMigration);
        Assert.Empty(parsed.Errors);
        RoadSegmentStats restored = parsed.Segments.Segments[new RoadSegmentKey(0, 0)];
        RoadSegmentStats original = index.Segments[new RoadSegmentKey(0, 0)];
        Assert.Equal(original.SampleCount, restored.SampleCount);
        Assert.Equal(original.RoughnessGradeJitter, restored.RoughnessGradeJitter, precision: 3);
        Assert.Equal(original.DragProxySpeed, restored.DragProxySpeed, precision: 3);

        // Restored accumulators keep accumulating identically: add the same
        // extra trip to both and outputs stay byte-identical.
        Trip extra = TripOf((4f, 4f, 2f, 1.2f), (5f, 5f, 2f, 1.2f));
        parsed.Segments.AddTrip(extra);
        index.AddTrip(extra);
        Assert.Equal(
            TripSidecar.Compose(new[] { trip, extra }, 42L, "0.4.0", index),
            TripSidecar.Compose(new[] { trip, extra }, 42L, "0.4.0", parsed.Segments));
    }

    [Fact]
    public void V1File_ParsesTripsAndFlagsMigration_RecomputeMatches()
    {
        Trip trip = TripOf((1f, 1f, 0f, 2f), (2f, 2f, 4f, 1.6f), (3f, 3f, 0f, 1f),
            (4f, 4f, 4f, 1.4f), (5f, 5f, 0f, 1.2f));
        string v2Text = TripSidecar.Compose(new[] { trip }, 42L, "0.3.0");
        string v1Text = v2Text.Replace("format-version: 2", "format-version: 1");

        TripSidecar.ParseResult parsed = TripSidecar.Parse(v1Text, 42L);

        Assert.False(parsed.Refused);
        Assert.True(parsed.NeedsMigration);
        Assert.Single(parsed.Trips);
        Assert.Empty(parsed.Segments.Segments); // v1 had none

        // The migration recompute equals scoring the trips directly.
        RoadQualityIndex recomputed = RoadQualityIndex.ComputeFromTrips(parsed.Trips);
        RoadQualityIndex direct = RoadQualityIndex.ComputeFromTrips(new[] { trip });
        Assert.Equal(
            TripSidecar.Compose(parsed.Trips, 42L, "0.4.0", direct),
            TripSidecar.Compose(parsed.Trips, 42L, "0.4.0", recomputed));
    }
}
