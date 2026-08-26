using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadAtlasEditorTests
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
    public void DeleteNearest_RemovesAndUndoRestores()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);
        int pointsBefore = atlas.PointCount;

        Assert.True(editor.DeleteNearest(P(10f, 3f), 10f, out _));
        Assert.Empty(atlas.Strokes);

        Assert.True(editor.Undo(out _));
        Assert.Single(atlas.Strokes);
        Assert.Equal(pointsBefore, atlas.PointCount);
    }

    [Fact]
    public void DeleteNearest_OutOfRange_DoesNothing()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);

        Assert.False(editor.DeleteNearest(P(10f, 50f), 10f, out string summary));
        Assert.Single(atlas.Strokes);
        Assert.Contains("No recorded road", summary);
    }

    [Fact]
    public void Reclassify_TogglesKind_KeepsIdentityAndPoints()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);
        Guid id = atlas.Strokes[0].Id;
        int points = atlas.Strokes[0].Points.Count;

        Assert.True(editor.ReclassifyNearest(P(10f, 0f), 10f, out _));

        Assert.Single(atlas.Strokes);
        Assert.Equal(RoadKind.Paved, atlas.Strokes[0].Kind);
        Assert.Equal(id, atlas.Strokes[0].Id);
        Assert.Equal(points, atlas.Strokes[0].Points.Count);

        Assert.True(editor.Undo(out _));
        Assert.Equal(RoadKind.Dirt, atlas.Strokes[0].Kind);
    }

    [Fact]
    public void HideThenUnhide_TogglesVisibilityFlag()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);

        Assert.True(editor.SetHiddenNearest(P(10f, 0f), 10f, hidden: true, out _));
        Assert.True(atlas.Strokes[0].Hidden);

        // Hiding again finds nothing visible.
        Assert.False(editor.SetHiddenNearest(P(10f, 0f), 10f, hidden: true, out _));

        Assert.True(editor.SetHiddenNearest(P(10f, 0f), 10f, hidden: false, out _));
        Assert.False(atlas.Strokes[0].Hidden);
    }

    [Fact]
    public void HiddenStroke_StillSuppressesReRecording()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);
        editor.SetHiddenNearest(P(10f, 0f), 10f, hidden: true, out _);
        int before = atlas.PointCount;

        atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(10f, 0.5f), DefaultRules, out _);

        Assert.Equal(before, atlas.PointCount);
    }

    [Fact]
    public void SplitNearest_SharesTheJunctionPoint()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        var editor = new RoadAtlasEditor(atlas);
        int pointsBefore = atlas.PointCount;

        Assert.True(editor.SplitNearest(P(10f, 0f), 10f, out _));

        Assert.Equal(2, atlas.Strokes.Count);
        Assert.Equal(pointsBefore + 1, atlas.PointCount);
        var head = atlas.Strokes[0].Points;
        var tail = atlas.Strokes[1].Points;
        Assert.Equal(head[head.Count - 1], tail[0]);

        Assert.True(editor.Undo(out _));
        Assert.Single(atlas.Strokes);
        Assert.Equal(pointsBefore, atlas.PointCount);
    }

    [Fact]
    public void JoinNearest_StitchesTwoFragments_AndUndoSeparates()
    {
        var atlas = new RoadAtlas();
        var first = new RoadStroke(Guid.NewGuid(), RoadKind.Dirt);
        first.Points.Add(P(0f, 0f));
        first.Points.Add(P(10f, 0f));
        var second = new RoadStroke(Guid.NewGuid(), RoadKind.Dirt);
        second.Points.Add(P(24f, 0f));
        second.Points.Add(P(14f, 0f));
        var seeded = new RoadAtlas(new[] { first, second });
        var editor = new RoadAtlasEditor(seeded);

        Assert.True(editor.JoinNearest(P(12f, 0f), 10f, out _));
        Assert.Single(seeded.Strokes);
        var points = seeded.Strokes[0].Points;
        Assert.Equal(P(0f, 0f), points[0]);
        Assert.Equal(P(24f, 0f), points[points.Count - 1]);

        Assert.True(editor.Undo(out _));
        Assert.Equal(2, seeded.Strokes.Count);
    }

    [Fact]
    public void JoinNearest_NeverCrossesKind()
    {
        var dirt = new RoadStroke(Guid.NewGuid(), RoadKind.Dirt);
        dirt.Points.Add(P(0f, 0f));
        dirt.Points.Add(P(10f, 0f));
        var paved = new RoadStroke(Guid.NewGuid(), RoadKind.Paved);
        paved.Points.Add(P(14f, 0f));
        paved.Points.Add(P(24f, 0f));
        var atlas = new RoadAtlas(new[] { dirt, paved });
        var editor = new RoadAtlasEditor(atlas);

        Assert.False(editor.JoinNearest(P(12f, 0f), 10f, out _));
        Assert.Equal(2, atlas.Strokes.Count);
    }

    [Fact]
    public void UndoDepth_IsBounded()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 200f);
        var editor = new RoadAtlasEditor(atlas);

        for (int i = 0; i < 25; i++)
        {
            Assert.True(editor.SetHiddenNearest(P(100f, 0f), 10f, hidden: i % 2 == 0, out _));
        }

        Assert.Equal(20, editor.UndoCount);
    }

    [Fact]
    public void EditedAtlas_IsDirtyForPersistence()
    {
        RoadAtlas atlas = BuildLine(RoadKind.Dirt, 0f, 20f);
        atlas.MarkClean();
        var editor = new RoadAtlasEditor(atlas);

        editor.SetHiddenNearest(P(10f, 0f), 10f, hidden: true, out _);

        Assert.True(atlas.IsDirty);
    }
}
