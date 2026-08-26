using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadAtlasTests
{
    private static readonly RoadSamplingRules DefaultRules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 2.0f);

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static int WalkLine(
        RoadAtlas atlas,
        RoadKind kind,
        float fromX,
        float toX,
        float stepX,
        float z = 0f,
        RoadSamplingRules? rules = null)
    {
        int recorded = 0;
        RoadSamplingRules effective = rules ?? DefaultRules;
        for (float x = fromX; stepX > 0 ? x <= toX : x >= toX; x += stepX)
        {
            if (atlas.RecordSample(RoadObservationSource.Traversal, kind, P(x, z), effective, out _))
            {
                recorded++;
            }
        }

        return recorded;
    }

    [Fact]
    public void ContinuousDirtWalk_ProducesOneCoherentStroke()
    {
        var atlas = new RoadAtlas();
        int segments = WalkLine(atlas, RoadKind.Dirt, 0f, 40f, 2f);

        Assert.Single(atlas.Strokes);
        Assert.Equal(RoadKind.Dirt, atlas.Strokes[0].Kind);
        Assert.Equal(21, atlas.Strokes[0].Points.Count);
        Assert.Equal(20, segments);
        Assert.True(atlas.IsDirty);
    }

    [Fact]
    public void ContinuousPavedWalk_ProducesOneCoherentPavedStroke()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Paved, 0f, 20f, 2f);

        Assert.Single(atlas.Strokes);
        Assert.Equal(RoadKind.Paved, atlas.Strokes[0].Kind);
    }

    [Fact]
    public void KindSwitch_StartsCorrectlyTypedNewStroke()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 10f, 2f);
        WalkLine(atlas, RoadKind.Paved, 12f, 22f, 2f);

        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(RoadKind.Dirt, atlas.Strokes[0].Kind);
        Assert.Equal(RoadKind.Paved, atlas.Strokes[1].Kind);
    }

    [Fact]
    public void SamplesBelowMinimumSpacing_AreNotStored()
    {
        var atlas = new RoadAtlas();
        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(0f, 0f), DefaultRules, out _);
        bool recorded = atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(0.5f, 0f), DefaultRules, out _);

        Assert.False(recorded);
        Assert.Equal(1, atlas.PointCount);
    }

    [Fact]
    public void StandingStill_NeverGrowsTheAtlas()
    {
        var atlas = new RoadAtlas();
        for (int i = 0; i < 500; i++)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(5f, 5f), DefaultRules, out _);
        }

        Assert.Equal(1, atlas.PointCount);
    }

    [Fact]
    public void GapBeyondMaximum_StartsNewStrokeWithoutConnectorSegment()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 10f, 2f);

        bool recorded = atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(500f, 500f), DefaultRules, out RoadSegment segment);

        Assert.False(recorded);
        Assert.Equal(default, segment.Start.X);
        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Single(atlas.Strokes[1].Points);
    }

    [Fact]
    public void TeleportSizedJump_NeverEmitsASegment()
    {
        var atlas = new RoadAtlas();
        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(0f, 0f), DefaultRules, out _);
        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(2f, 0f), DefaultRules, out _);

        bool recorded = atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(4000f, -3000f), DefaultRules, out _);

        Assert.False(recorded);
        // The jump destination begins its own stroke; no segment ever spans it.
        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void EndStroke_ThenResuming_StartsANewStroke()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 10f, 2f);
        atlas.EndStroke(RoadObservationSource.Traversal);
        WalkLine(atlas, RoadKind.Dirt, 30f, 40f, 2f);

        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void DoublingBackOverOwnEndedStroke_IsSuppressed()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 30f, 2f);
        int before = atlas.PointCount;
        atlas.EndStroke(RoadObservationSource.Traversal);

        WalkLine(atlas, RoadKind.Dirt, 30f, 0f, -2f);

        Assert.Equal(before, atlas.PointCount);
    }

    [Fact]
    public void ReversingWithinActiveStroke_IsBoundedBySuppression()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 30f, 2f);
        int before = atlas.PointCount;

        // Turn around without leaving the road: the exempt tail allows at most a
        // few points before older ink suppresses the reversal.
        WalkLine(atlas, RoadKind.Dirt, 28.5f, 0f, -1.5f);

        Assert.InRange(atlas.PointCount, before, before + 4);
    }

    [Fact]
    public void RepeatedTraversal_DoesNotGrowTheAtlas()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 100f, 2f);
        atlas.EndStroke(RoadObservationSource.Traversal);
        int afterFirst = atlas.PointCount;

        for (int pass = 0; pass < 10; pass++)
        {
            WalkLine(atlas, RoadKind.Dirt, 0f, 100f, 2f);
            atlas.EndStroke(RoadObservationSource.Traversal);
        }

        Assert.Equal(afterFirst, atlas.PointCount);
    }

    [Fact]
    public void SuppressionAppliesAcrossLoadedSessions()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 30f, 2f);
        atlas.EndStroke(RoadObservationSource.Traversal);

        var reloaded = new RoadAtlas(atlas.Strokes);
        Assert.False(reloaded.IsDirty);

        int before = reloaded.PointCount;
        WalkLine(reloaded, RoadKind.Dirt, 0f, 30f, 2f);

        Assert.Equal(before, reloaded.PointCount);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void SuppressionIsPerKind_PavedOverDirtStillRecords()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 20f, 2f);
        atlas.EndStroke(RoadObservationSource.Traversal);
        int before = atlas.PointCount;

        WalkLine(atlas, RoadKind.Paved, 0f, 20f, 2f);

        Assert.True(atlas.PointCount > before);
        Assert.Equal(RoadKind.Paved, atlas.Strokes[^1].Kind);
    }

    [Fact]
    public void SuppressionDisabled_AllowsRepeatedTraversalGrowth()
    {
        var rules = new RoadSamplingRules(1.5f, 8.0f, 0f);
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 20f, 2f, rules: rules);
        atlas.EndStroke(RoadObservationSource.Traversal);
        int afterFirst = atlas.PointCount;

        WalkLine(atlas, RoadKind.Dirt, 0f, 20f, 2f, rules: rules);

        Assert.True(atlas.PointCount > afterFirst);
    }

    [Fact]
    public void ParallelRoadOutsideSuppressionRadius_RecordsNormally()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 20f, 2f, z: 0f);
        atlas.EndStroke(RoadObservationSource.Traversal);

        WalkLine(atlas, RoadKind.Dirt, 0f, 20f, 2f, z: 5f);

        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(atlas.Strokes[0].Points.Count, atlas.Strokes[1].Points.Count);
    }

    [Fact]
    public void MarkClean_ClearsDirtyUntilNextRecordedPoint()
    {
        var atlas = new RoadAtlas();
        WalkLine(atlas, RoadKind.Dirt, 0f, 10f, 2f);
        atlas.MarkClean();
        Assert.False(atlas.IsDirty);

        WalkLine(atlas, RoadKind.Dirt, 12f, 16f, 2f);
        Assert.True(atlas.IsDirty);
    }
}
