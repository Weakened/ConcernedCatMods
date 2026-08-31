using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>DEF-v1.0-005 regression suite: persistent negative terrain
/// intent. Level/Raise/Cultivate/Reset ground must never be rediscovered
/// as road by traversal or chunk recovery; explicit Pathen/Paved always
/// wins; the mask round-trips through its sidecar codec (restart safety)
/// and stays bounded.</summary>
public class TerrainIntentTests
{
    private static readonly RoadSamplingRules Rules = new(
        minimumSpacingMeters: 0.5f,
        maximumGapMeters: 10f,
        duplicateSuppressionMeters: 0f);

    private static RoadObservation Obs(RoadObservationSource source, RoadKind kind, float x, float z)
    {
        return new RoadObservation(source, kind, new RoadPoint(x, 30f, z));
    }

    [Fact]
    public void AddExclusion_CoversBrushFootprint()
    {
        var mask = new TerrainIntentMask();
        mask.AddExclusion(0.5f, 0.5f, 3f);

        Assert.True(mask.IsExcluded(0.5f, 0.5f));
        Assert.True(mask.IsExcluded(2.5f, 0.5f));
        Assert.False(mask.IsExcluded(4.5f, 0.5f));
        Assert.False(mask.IsExcluded(0.5f, 8.5f));
        Assert.True(mask.IsDirty);
    }

    [Fact]
    public void ClearExclusion_ReAllowsTheClearedFootprintOnly()
    {
        var mask = new TerrainIntentMask();
        mask.AddExclusion(0.5f, 0.5f, 5f);
        mask.ClearExclusion(0.5f, 0.5f, 2f);

        Assert.False(mask.IsExcluded(0.5f, 0.5f));
        Assert.True(mask.IsExcluded(4.5f, 0.5f));
    }

    [Fact]
    public void LevelExclusion_BlocksDirtTraversal()
    {
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(10f, 10f, 4f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        Assert.False(pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, 10f, 10f), Rules, out _));
        Assert.Equal(0, atlas.PointCount);
    }

    [Fact]
    public void LevelExclusion_BlocksDirtChunkRecovery()
    {
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(10f, 10f, 4f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        Assert.False(pipeline.Observe(Obs(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, 10f, 10f), Rules, out _));
        Assert.Equal(0, atlas.PointCount);
    }

    [Fact]
    public void ExplicitPathenConstruction_IsNeverBlocked()
    {
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(10f, 10f, 4f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 10f, 10f), Rules, out _);

        Assert.Equal(1, atlas.PointCount);
    }

    [Fact]
    public void PavedObservations_AreNotGated()
    {
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(10f, 10f, 4f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        // RC8: construction is the only creating source; the mask never
        // gates it, and Paved was never gated at all.
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Paved, 10f, 10f), Rules, out _);

        Assert.Equal(1, atlas.PointCount);
    }

    [Fact]
    public void PathenWins_ConstructionRecordsDirtDespiteThePriorExclusion()
    {
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(10f, 10f, 4f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        // RC8 "later explicit Pathen/Paved wins": a passive sighting on the
        // leveled pad records nothing (source rule), while the explicit
        // Pathen observation lands regardless of the mask (the runtime also
        // clears the brush footprint before observing).
        Assert.False(pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, 10f, 10f), Rules, out _));
        Assert.Equal(0, atlas.PointCount);

        mask.ClearExclusion(10f, 10f, 4f);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 10f, 10f), Rules, out _);

        Assert.Equal(1, atlas.PointCount);
        Assert.Equal(RoadObservationSource.Construction, atlas.Strokes[0].Source);
    }

    [Fact]
    public void Construction_IsNeverGatedByTheMask_AndChainsAcrossIt()
    {
        // RC8: the only creating source is construction, and the mask never
        // gates it — a deliberate Pathen path laid across a previously
        // leveled pad records as one continuous stroke. (The runtime also
        // clears the mask under each brush footprint; even without that,
        // the pipeline must not gate construction.)
        var atlas = new RoadAtlas();
        var mask = new TerrainIntentMask();
        mask.AddExclusion(4.5f, 0.5f, 1f);
        var pipeline = new RoadObservationPipeline(atlas, mask);

        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 0.5f, 0.5f), Rules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 2.5f, 0.5f), Rules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 4.5f, 0.5f), Rules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 6.5f, 0.5f), Rules, out _);
        pipeline.EndAllStrokes();

        Assert.Single(atlas.Strokes);
        Assert.Equal(4, atlas.Strokes[0].Points.Count);
    }

    [Fact]
    public void Mask_SurvivesRestart_ThroughCodecRoundTrip()
    {
        var mask = new TerrainIntentMask();
        mask.AddExclusion(100.5f, -50.5f, 3f);
        mask.AddExclusion(-2000.5f, 4000.5f, 5f);

        TerrainIntentCodec.ParseResult reloaded = TerrainIntentCodec.Parse(TerrainIntentCodec.Serialize(mask));

        Assert.False(reloaded.UnsupportedVersion);
        Assert.Equal(0, reloaded.MalformedRows);
        Assert.Equal(mask.Count, reloaded.Mask.Count);
        Assert.True(reloaded.Mask.IsExcluded(100.5f, -50.5f));
        Assert.True(reloaded.Mask.IsExcluded(-2000.5f, 4000.5f));
        Assert.False(reloaded.Mask.IsExcluded(0.5f, 0.5f));
        Assert.False(reloaded.Mask.IsDirty);
    }

    [Fact]
    public void Codec_SkipsMalformedRows()
    {
        TerrainIntentCodec.ParseResult result = TerrainIntentCodec.Parse(new[]
        {
            "cc-terrain-intent\tv1",
            "cell\t5\t7",
            "cell\tfive\tseven",
            "not-a-row",
            "cell\t1",
            "cell\t2\t3",
        });

        Assert.False(result.UnsupportedVersion);
        Assert.Equal(3, result.MalformedRows);
        Assert.Equal(2, result.Mask.Count);
        Assert.True(result.Mask.IsExcluded(5.5f, 7.5f));
    }

    [Fact]
    public void Codec_UnknownHeaderLoadsEmpty_NeverGuesses()
    {
        TerrainIntentCodec.ParseResult result = TerrainIntentCodec.Parse(new[]
        {
            "cc-terrain-intent\tv99",
            "cell\t5\t7",
        });

        Assert.True(result.UnsupportedVersion);
        Assert.Equal(0, result.Mask.Count);
    }

    [Fact]
    public void SeparateWorldMasks_AreIndependent()
    {
        // World isolation is by per-UID sidecar filename; two loaded masks
        // must never share state.
        var worldA = new TerrainIntentMask();
        var worldB = new TerrainIntentMask();
        worldA.AddExclusion(10.5f, 10.5f, 3f);

        Assert.True(worldA.IsExcluded(10.5f, 10.5f));
        Assert.False(worldB.IsExcluded(10.5f, 10.5f));
    }

    [Fact]
    public void OverlappingOperations_ConvergeIdempotently()
    {
        var mask = new TerrainIntentMask();
        int first = mask.AddExclusion(0.5f, 0.5f, 3f);
        int repeat = mask.AddExclusion(0.5f, 0.5f, 3f);
        Assert.True(first > 0);
        Assert.Equal(0, repeat);

        int cleared = mask.ClearExclusion(0.5f, 0.5f, 3f);
        Assert.Equal(first, cleared);
        Assert.Equal(0, mask.ClearExclusion(0.5f, 0.5f, 3f));

        // Re-adding after a clear works and stays idempotent.
        Assert.Equal(first, mask.AddExclusion(0.5f, 0.5f, 3f));
    }

    [Fact]
    public void BoundedStorage_EvictsOldestBeyondTheCap()
    {
        var mask = new TerrainIntentMask(maxCells: 4);
        mask.AddExclusion(0.5f, 0.5f, 0.4f);   // cell (0,0)
        mask.AddExclusion(10.5f, 0.5f, 0.4f);  // cell (10,0)
        mask.AddExclusion(20.5f, 0.5f, 0.4f);
        mask.AddExclusion(30.5f, 0.5f, 0.4f);
        mask.AddExclusion(40.5f, 0.5f, 0.4f);

        Assert.Equal(4, mask.Count);
        Assert.False(mask.IsExcluded(0.5f, 0.5f));
        Assert.True(mask.IsExcluded(40.5f, 0.5f));
    }

    [Fact]
    public void InvalidGeometry_IsIgnoredSafely()
    {
        var mask = new TerrainIntentMask();
        Assert.Equal(0, mask.AddExclusion(float.NaN, 0f, 3f));
        Assert.Equal(0, mask.AddExclusion(0f, 0f, float.PositiveInfinity));
        Assert.Equal(0, mask.AddExclusion(0f, 0f, -2f));
        Assert.Equal(0, mask.Count);
        Assert.False(mask.IsDirty);
    }
}
