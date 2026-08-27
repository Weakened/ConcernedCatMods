using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadObservationPipelineTests
{
    private static readonly RoadSamplingRules DefaultRules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 2.0f);

    private static readonly RoadSamplingRules SuppressionDisabledRules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 0f);

    private static RoadObservation Obs(RoadObservationSource source, RoadKind kind, float x, float z)
    {
        return new RoadObservation(source, kind, new RoadPoint(x, 30f, z));
    }

    private static void WalkLine(
        RoadObservationPipeline pipeline,
        RoadObservationSource source,
        RoadKind kind,
        float fromX,
        float toX,
        float stepX,
        float z,
        RoadSamplingRules rules)
    {
        for (float x = fromX; stepX > 0 ? x <= toX : x >= toX; x += stepX)
        {
            pipeline.Observe(Obs(source, kind, x, z), rules, out _);
        }
    }

    [Fact]
    public void ExactReplay_IsIdempotent_EvenWithSuppressionDisabled()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        WalkLine(pipeline, RoadObservationSource.Traversal, RoadKind.Dirt, 0f, 20f, 2f, 0f, SuppressionDisabledRules);
        pipeline.EndAllStrokes();
        int afterFirst = atlas.PointCount;

        WalkLine(pipeline, RoadObservationSource.Traversal, RoadKind.Dirt, 0f, 20f, 2f, 0f, SuppressionDisabledRules);

        Assert.Equal(afterFirst, atlas.PointCount);
    }

    [Fact]
    public void NearbyButNotIdentical_StillGrowsWhenSuppressionDisabled()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        WalkLine(pipeline, RoadObservationSource.Traversal, RoadKind.Dirt, 0f, 20f, 2f, 0f, SuppressionDisabledRules);
        pipeline.EndAllStrokes();
        int afterFirst = atlas.PointCount;

        // Offset re-walk: same road, non-identical coordinates. With the
        // configurable suppression off this must grow, proving the replay
        // epsilon only catches true replays.
        WalkLine(pipeline, RoadObservationSource.Traversal, RoadKind.Dirt, 0.7f, 20.7f, 2f, 0.7f, SuppressionDisabledRules);

        Assert.True(atlas.PointCount > afterFirst);
    }

    [Fact]
    public void RepeatedConstructionDab_ProducesOneSourcedPoint()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);

        for (int i = 0; i < 5; i++)
        {
            pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Paved, 5f, 5f), DefaultRules, out _);
        }

        Assert.Single(atlas.Strokes);
        Assert.Equal(1, atlas.PointCount);
        Assert.Equal(RoadObservationSource.Construction, atlas.Strokes[0].Source);
        Assert.Equal(RoadKind.Paved, atlas.Strokes[0].Kind);
    }

    [Fact]
    public void InterleavedSources_BuildIndependentCoherentStrokes()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);

        for (float x = 0f; x <= 20f; x += 2f)
        {
            pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, x, 0f), DefaultRules, out _);
            pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, x, 50f), DefaultRules, out _);
        }

        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(RoadObservationSource.Traversal, atlas.Strokes[0].Source);
        Assert.Equal(RoadObservationSource.Construction, atlas.Strokes[1].Source);
        Assert.Equal(11, atlas.Strokes[0].Points.Count);
        Assert.Equal(11, atlas.Strokes[1].Points.Count);
    }

    [Fact]
    public void ConstructionOverTraversedInk_IsSuppressed()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        WalkLine(pipeline, RoadObservationSource.Traversal, RoadKind.Dirt, 0f, 20f, 2f, 0f, DefaultRules);
        pipeline.EndAllStrokes();
        int before = atlas.PointCount;

        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 10f, 0.5f), DefaultRules, out _);

        Assert.Equal(before, atlas.PointCount);
    }

    [Fact]
    public void EndingOneSourcesStroke_DoesNotBreakAnother()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, 0f, 0f), DefaultRules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 0f, 50f), DefaultRules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 2f, 50f), DefaultRules, out _);

        // The traversal source loses its signal (probe failure, death) but
        // construction keeps dabbing: its stroke must keep chaining.
        pipeline.EndStroke(RoadObservationSource.Traversal);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 4f, 50f), DefaultRules, out _);

        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(3, atlas.Strokes[1].Points.Count);
    }

    [Fact]
    public void IsolatedObservations_AreOrderIndependent()
    {
        // Dabs spaced beyond the maximum gap become independent single-point
        // strokes, so any arrival order (with replayed duplicates mixed in)
        // must produce identical coverage.
        var forward = new RoadAtlas();
        var forwardPipeline = new RoadObservationPipeline(forward);
        var reversed = new RoadAtlas();
        var reversedPipeline = new RoadObservationPipeline(reversed);

        var observations = new[]
        {
            Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 0f, 0f),
            Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 20f, 0f),
            Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 40f, 0f),
            Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 20f, 0f),
            Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 60f, 0f),
        };

        foreach (RoadObservation observation in observations)
        {
            forwardPipeline.Observe(observation, DefaultRules, out _);
        }

        for (int index = observations.Length - 1; index >= 0; index--)
        {
            reversedPipeline.Observe(observations[index], DefaultRules, out _);
        }

        Assert.Equal(4, forward.PointCount);
        Assert.Equal(forward.PointCount, reversed.PointCount);
    }

    [Fact]
    public void ReloadedSourcedStrokes_KeepSuppressingAcrossSessions()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        WalkLine(pipeline, RoadObservationSource.Construction, RoadKind.Paved, 0f, 20f, 2f, 0f, DefaultRules);
        pipeline.EndAllStrokes();

        var reloaded = new RoadAtlas(atlas.Strokes);
        var reloadedPipeline = new RoadObservationPipeline(reloaded);
        int before = reloaded.PointCount;

        // Walking the paved road that construction capture already inked must
        // not re-ink it.
        WalkLine(reloadedPipeline, RoadObservationSource.Traversal, RoadKind.Paved, 0f, 20f, 2f, 0f, DefaultRules);

        Assert.Equal(before, reloaded.PointCount);
        Assert.False(reloaded.IsDirty);
    }
}
