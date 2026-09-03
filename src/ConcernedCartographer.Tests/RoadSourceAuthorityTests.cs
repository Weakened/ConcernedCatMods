using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC8 STRICT PRODUCT RULE: only successful explicit local-player
/// Pathen (Dirt) and Paved construction creates road atlas data. Passive
/// traversal and chunk recovery must never create roads from arbitrary
/// terrain paint, existing passive-only strokes migrate away exactly once
/// (construction preserved), and the rule survives restart/reopen through
/// the sidecar codec.</summary>
public class RoadSourceAuthorityTests
{
    private static readonly RoadSamplingRules Rules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 2.0f);

    private static RoadObservation Obs(RoadObservationSource source, RoadKind kind, float x, float z)
    {
        return new RoadObservation(source, kind, new RoadPoint(x, 30f, z));
    }

    [Fact]
    public void PassiveSources_NeverCreateRoadData()
    {
        foreach ((RoadObservationSource source, RoadKind kind) in new[]
        {
            (RoadObservationSource.Traversal, RoadKind.Dirt),
            (RoadObservationSource.Traversal, RoadKind.Paved),
            (RoadObservationSource.ChunkRecovery, RoadKind.Dirt),
            (RoadObservationSource.ChunkRecovery, RoadKind.Paved),
        })
        {
            var atlas = new RoadAtlas();
            var pipeline = new RoadObservationPipeline(atlas);

            for (float x = 0f; x <= 40f; x += 2f)
            {
                bool produced = pipeline.Observe(Obs(source, kind, x, 0f), Rules, out _);
                Assert.False(produced);
            }

            Assert.Empty(atlas.Strokes);
            Assert.Equal(0, atlas.PointCount);
            Assert.False(atlas.IsDirty);
            Assert.Null(pipeline.LastAccepted);
        }
    }

    [Fact]
    public void ExplicitConstruction_CreatesRoadData()
    {
        foreach (RoadKind kind in new[] { RoadKind.Dirt, RoadKind.Paved })
        {
            var atlas = new RoadAtlas();
            var pipeline = new RoadObservationPipeline(atlas);

            for (float x = 0f; x <= 10f; x += 2f)
            {
                pipeline.Observe(Obs(RoadObservationSource.Construction, kind, x, 0f), Rules, out _);
            }

            Assert.Single(atlas.Strokes);
            Assert.Equal(RoadObservationSource.Construction, atlas.Strokes[0].Source);
            Assert.Equal(kind, atlas.Strokes[0].Kind);
            Assert.Equal(6, atlas.PointCount);
        }
    }

    [Fact]
    public void Migration_RemovesPassiveStrokes_PreservesConstruction()
    {
        // Build a mixed pre-RC8 atlas the way earlier versions could have:
        // traversal walking, chunk recovery, and explicit construction.
        var legacy = new RoadAtlas();
        for (float x = 0f; x <= 20f; x += 2f)
        {
            legacy.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, new RoadPoint(x, 30f, 0f), Rules, out _);
            legacy.RecordSample(RoadObservationSource.ChunkRecovery, RoadKind.Dirt, new RoadPoint(x, 30f, 50f), Rules, out _);
            legacy.RecordSample(RoadObservationSource.Construction, RoadKind.Paved, new RoadPoint(x, 30f, 100f), Rules, out _);
        }

        legacy.EndAllStrokes();
        var constructionId = legacy.Strokes.Find(s => s.Source == RoadObservationSource.Construction)!.Id;

        RoadAtlas.MigrationResult result = legacy.RemoveNonConstructionStrokes();

        Assert.Equal(2, result.RemovedStrokes);
        Assert.Equal(22, result.RemovedPoints);
        Assert.Single(legacy.Strokes);
        Assert.Equal(RoadObservationSource.Construction, legacy.Strokes[0].Source);
        Assert.Equal(constructionId, legacy.Strokes[0].Id);
        Assert.Equal(11, legacy.Strokes[0].Points.Count);
        Assert.True(legacy.IsDirty);
    }

    [Fact]
    public void Migration_IsIdempotent_AndCleanAtlasStaysClean()
    {
        var atlas = new RoadAtlas();
        atlas.RecordSample(RoadObservationSource.Construction, RoadKind.Dirt, new RoadPoint(0f, 30f, 0f), Rules, out _);
        atlas.RecordSample(RoadObservationSource.Construction, RoadKind.Dirt, new RoadPoint(2f, 30f, 0f), Rules, out _);
        atlas.EndAllStrokes();
        atlas.MarkClean();

        RoadAtlas.MigrationResult first = atlas.RemoveNonConstructionStrokes();

        Assert.Equal(0, first.RemovedStrokes);
        Assert.Equal(0, first.RemovedPoints);
        Assert.False(atlas.IsDirty);
        Assert.Single(atlas.Strokes);
    }

    [Fact]
    public void RestartReopen_PassiveStrokesInOldSidecar_DoNotComeBack()
    {
        // Session 1 (pre-RC8): mixed sidecar written by the v3 codec.
        var session1 = new RoadAtlas();
        for (float x = 0f; x <= 20f; x += 2f)
        {
            session1.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, new RoadPoint(x, 30f, 0f), Rules, out _);
            session1.RecordSample(RoadObservationSource.Construction, RoadKind.Paved, new RoadPoint(x, 30f, 100f), Rules, out _);
        }

        session1.EndAllStrokes();
        var sidecar = new List<string>(RoadAtlasCodec.Serialize(session1.Strokes));

        // Session 2 (RC8): load + authority migration, save.
        RoadAtlasCodec.ParseResult parsed2 = RoadAtlasCodec.Parse(sidecar);
        var session2 = new RoadAtlas(parsed2.Strokes);
        RoadAtlas.MigrationResult migration = session2.RemoveNonConstructionStrokes();
        Assert.Equal(1, migration.RemovedStrokes);
        var sidecar2 = new List<string>(RoadAtlasCodec.Serialize(session2.Strokes));

        // Session 3 (reopen): nothing passive resurfaces, construction intact.
        RoadAtlasCodec.ParseResult parsed3 = RoadAtlasCodec.Parse(sidecar2);
        var session3 = new RoadAtlas(parsed3.Strokes);
        RoadAtlas.MigrationResult migration3 = session3.RemoveNonConstructionStrokes();

        Assert.Equal(0, migration3.RemovedStrokes);
        Assert.Single(session3.Strokes);
        Assert.Equal(RoadObservationSource.Construction, session3.Strokes[0].Source);
        Assert.Equal(RoadKind.Paved, session3.Strokes[0].Kind);
        Assert.Equal(11, session3.Strokes[0].Points.Count);
    }

    [Fact]
    public void RestartReopen_LegacyV1Rows_MigrateAwayAsTraversal()
    {
        // A v0.1-era sidecar row has no source column; it parses as
        // Traversal and therefore migrates away (it was walked ink).
        var legacyRows = new[]
        {
            "# ConcernedCartographer roads v1",
            "11111111-1111-1111-1111-111111111111\tDirt\t0\t1\t30\t1\t1",
            "11111111-1111-1111-1111-111111111111\tDirt\t1\t3\t30\t1\t1",
        };

        RoadAtlasCodec.ParseResult parsed = RoadAtlasCodec.Parse(legacyRows);
        Assert.Equal(2, parsed.LegacyRows);
        var atlas = new RoadAtlas(parsed.Strokes);

        RoadAtlas.MigrationResult migration = atlas.RemoveNonConstructionStrokes();

        Assert.Equal(1, migration.RemovedStrokes);
        Assert.Empty(atlas.Strokes);
    }

    [Fact]
    public void PassiveRefusal_EndsThatSourcesLegacyActiveStroke()
    {
        var atlas = new RoadAtlas();

        // A legacy active traversal stroke exists (e.g. built before the
        // rule); a refused observation must end it so nothing can ever
        // chain a connector through the refusal.
        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, new RoadPoint(0f, 30f, 0f), Rules, out _);
        var pipeline = new RoadObservationPipeline(atlas);

        pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, 2f, 0f), Rules, out _);

        // If the stroke had stayed active, this sample would have appended
        // to it; instead a direct atlas sample starts a NEW stroke.
        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, new RoadPoint(4f, 30f, 0f), Rules, out _);
        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void ConstructionRefusal_DoesNotEndConstructionStroke()
    {
        var atlas = new RoadAtlas();
        var pipeline = new RoadObservationPipeline(atlas);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 0f, 0f), Rules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 2f, 0f), Rules, out _);

        // A refused passive observation between construction dabs must not
        // break the construction stroke.
        pipeline.Observe(Obs(RoadObservationSource.Traversal, RoadKind.Dirt, 50f, 50f), Rules, out _);
        pipeline.Observe(Obs(RoadObservationSource.Construction, RoadKind.Dirt, 4f, 0f), Rules, out _);

        Assert.Single(atlas.Strokes);
        Assert.Equal(3, atlas.Strokes[0].Points.Count);
    }
}
