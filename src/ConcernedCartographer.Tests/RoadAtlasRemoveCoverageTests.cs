using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadAtlasRemoveCoverageTests
{
    private static readonly RoadSamplingRules DefaultRules = new(
        minimumSpacingMeters: 1.5f,
        maximumGapMeters: 8.0f,
        duplicateSuppressionMeters: 2.0f);

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static RoadAtlas BuildLine(RoadKind kind, float fromX, float toX, float z = 0f)
    {
        var atlas = new RoadAtlas();
        for (float x = fromX; x <= toX; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, kind, P(x, z), DefaultRules, out _);
        }

        atlas.EndStroke(RoadObservationSource.Traversal);
        return atlas;
    }

    [Fact]
    public void RemovingStrokeInterior_SplitsIntoTwoStrokes()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 40f);
        Guid originalId = atlas.Strokes[0].Id;
        int before = atlas.PointCount;
        atlas.MarkClean();

        int removed = atlas.RemoveCoverage(RoadKind.Dirt, P(20f, 0f), 3f);

        Assert.True(removed > 0);
        Assert.Equal(before - removed, atlas.PointCount);
        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(originalId, atlas.Strokes[0].Id);
        Assert.NotEqual(originalId, atlas.Strokes[1].Id);
        Assert.Equal(RoadKind.Dirt, atlas.Strokes[1].Kind);
        Assert.Equal(RoadObservationSource.Traversal, atlas.Strokes[1].Source);
        Assert.True(atlas.IsDirty);
    }

    [Fact]
    public void RemovingEverything_DeletesTheStroke()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 10f);

        int removed = atlas.RemoveCoverage(RoadKind.Dirt, P(5f, 0f), 50f);

        Assert.Equal(6, removed);
        Assert.Empty(atlas.Strokes);
        Assert.Equal(0, atlas.PointCount);
    }

    [Fact]
    public void OtherKindInk_IsNeverTouched()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        for (float x = 0f; x <= 20f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Paved, P(x, 0.5f), DefaultRules, out _);
        }

        int pavedBefore = CountPoints(atlas, RoadKind.Paved);
        atlas.RemoveCoverage(RoadKind.Dirt, P(10f, 0f), 50f);

        Assert.Equal(0, CountPoints(atlas, RoadKind.Dirt));
        Assert.Equal(pavedBefore, CountPoints(atlas, RoadKind.Paved));
    }

    [Fact]
    public void NearbyParallelRoad_OutsideRadius_IsPreserved()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f, z: 0f);
        for (float x = 0f; x <= 20f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 10f), DefaultRules, out _);
        }

        atlas.EndStroke(RoadObservationSource.Traversal);
        int before = atlas.PointCount;

        int removed = atlas.RemoveCoverage(RoadKind.Dirt, P(10f, 0f), 2f);

        Assert.True(removed > 0);
        Assert.Equal(before - removed, atlas.PointCount);
        bool parallelIntact = atlas.Strokes.Exists(stroke => stroke.Points.Count == 11 && stroke.Points[0].Z == 10f);
        Assert.True(parallelIntact);
    }

    [Fact]
    public void ZeroOrNegativeRadius_RemovesNothing()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 10f);
        int before = atlas.PointCount;

        Assert.Equal(0, atlas.RemoveCoverage(RoadKind.Dirt, P(5f, 0f), 0f));
        Assert.Equal(0, atlas.RemoveCoverage(RoadKind.Dirt, P(5f, 0f), -1f));
        Assert.Equal(before, atlas.PointCount);
    }

    [Fact]
    public void ClearedStretch_CanBeRecordedAgain()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 40f);
        atlas.RemoveCoverage(RoadKind.Dirt, P(20f, 0f), 4f);
        int afterRemoval = atlas.PointCount;

        // The suppression index must have been rebuilt: walking the cleared
        // stretch records fresh points instead of being suppressed by stale
        // entries, while the surviving ink still suppresses its own ground.
        var pipeline = new RoadObservationPipeline(atlas);
        for (float x = 17f; x <= 23f; x += 1.5f)
        {
            pipeline.Observe(
                new RoadObservation(RoadObservationSource.Construction, RoadKind.Dirt, P(x, 0.2f)),
                DefaultRules,
                out _);
        }

        Assert.True(atlas.PointCount > afterRemoval);
    }

    [Fact]
    public void KindChange_RemovesOldInkAndAcceptsNew()
    {
        // Paving over a dirt road: reconciliation removes covered Dirt, then
        // the construction dab records Paved — no duplicate parallel
        // geometry of mixed kinds on the same ground.
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var pipeline = new RoadObservationPipeline(atlas);

        int removed = atlas.RemoveCoverage(RoadKind.Dirt, P(10f, 0f), 2f);
        bool recordedPaved = false;
        pipeline.Observe(
            new RoadObservation(RoadObservationSource.Construction, RoadKind.Paved, P(10f, 0f)),
            DefaultRules,
            out _);
        recordedPaved = CountPoints(atlas, RoadKind.Paved) == 1;

        Assert.True(removed > 0);
        Assert.True(recordedPaved);
    }

    [Fact]
    public void ActiveStrokeHitByRemoval_EndsInsteadOfDangling()
    {
        var atlas = new RoadAtlas();
        for (float x = 0f; x <= 10f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Construction, RoadKind.Dirt, P(x, 0f), DefaultRules, out _);
        }

        atlas.RemoveCoverage(RoadKind.Dirt, P(10f, 0f), 3f);
        int strokesAfterRemoval = atlas.Strokes.Count;

        // The next observation must start a new stroke, not extend the
        // replaced object that no longer belongs to the atlas.
        atlas.RecordSample(RoadObservationSource.Construction, RoadKind.Dirt, P(30f, 0f), DefaultRules, out _);

        Assert.Equal(strokesAfterRemoval + 1, atlas.Strokes.Count);
        Assert.Equal(1, atlas.Strokes[^1].Points.Count);
    }

    private static int CountPoints(RoadAtlas atlas, RoadKind kind)
    {
        int total = 0;
        foreach (RoadStroke stroke in atlas.Strokes)
        {
            if (stroke.Kind == kind)
            {
                total += stroke.Points.Count;
            }
        }

        return total;
    }
}
