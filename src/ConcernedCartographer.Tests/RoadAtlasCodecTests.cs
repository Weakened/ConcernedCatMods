using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RoadAtlasCodecTests
{
    private static RoadStroke MakeStroke(RoadKind kind, params (float X, float Z)[] points)
    {
        var stroke = new RoadStroke(Guid.NewGuid(), kind);
        foreach ((float x, float z) in points)
        {
            stroke.Points.Add(new RoadPoint(x, 31.25f, z));
        }

        return stroke;
    }

    [Fact]
    public void Roundtrip_PreservesStrokesPointsAndKinds()
    {
        var strokes = new List<RoadStroke>
        {
            MakeStroke(RoadKind.Dirt, (0f, 0f), (2.5f, -3.75f), (5f, -7.125f)),
            MakeStroke(RoadKind.Paved, (100.25f, 200.5f), (103f, 204f)),
        };

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(RoadAtlasCodec.Serialize(strokes));

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(2, result.Strokes.Count);
        Assert.Equal(strokes[0].Id, result.Strokes[0].Id);
        Assert.Equal(RoadKind.Dirt, result.Strokes[0].Kind);
        Assert.Equal(RoadKind.Paved, result.Strokes[1].Kind);
        Assert.Equal(3, result.Strokes[0].Points.Count);
        Assert.Equal(2.5f, result.Strokes[0].Points[1].X);
        Assert.Equal(-3.75f, result.Strokes[0].Points[1].Z);
    }

    [Fact]
    public void SerializedOutput_StartsWithVersionHeaderAndUsesSevenFields()
    {
        var strokes = new List<RoadStroke> { MakeStroke(RoadKind.Dirt, (1f, 2f)) };
        var lines = RoadAtlasCodec.Serialize(strokes).ToList();

        Assert.Equal(RoadAtlasCodec.Header, lines[0]);
        Assert.Equal(7, lines[1].Split('\t').Length);
        Assert.EndsWith("\t1", lines[1]);
    }

    [Fact]
    public void MalformedRows_AreSkippedWithoutDiscardingValidRows()
    {
        var strokes = new List<RoadStroke> { MakeStroke(RoadKind.Dirt, (0f, 0f), (2f, 0f)) };
        var lines = RoadAtlasCodec.Serialize(strokes).ToList();
        lines.Add("not-a-guid\tDirt\t0\t1.0\t2.0\t3.0\t1");
        lines.Add($"{Guid.NewGuid():D}\tDirt\t5\t1.0\t2.0\t3.0\t1");
        lines.Add($"{Guid.NewGuid():D}\tDirt\t0\t1.0\t2.0\t3.0");
        lines.Add($"{Guid.NewGuid():D}\tLava\t0\t1.0\t2.0\t3.0\t1");
        lines.Add($"{Guid.NewGuid():D}\tDirt\t0\tNaNope\t2.0\t3.0\t1");
        lines.Add($"{Guid.NewGuid():D}\tDirt\t0\t1.0\t2.0\t3.0\t2");

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(6, result.MalformedRows);
        Assert.Single(result.Strokes);
        Assert.Equal(2, result.Strokes[0].Points.Count);
    }

    [Fact]
    public void CommentAndBlankLines_AreIgnoredWithoutCountingAsMalformed()
    {
        var strokes = new List<RoadStroke> { MakeStroke(RoadKind.Paved, (4f, 4f)) };
        var lines = new List<string> { "", "  ", "# a manual note" };
        lines.AddRange(RoadAtlasCodec.Serialize(strokes));

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Single(result.Strokes);
    }

    [Fact]
    public void KindMismatchWithinAStroke_IsMalformed()
    {
        Guid id = Guid.NewGuid();
        var lines = new List<string>
        {
            $"{id:D}\tDirt\t0\t1.0\t2.0\t3.0\t1",
            $"{id:D}\tPaved\t1\t4.0\t5.0\t6.0\t1",
        };

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(1, result.MalformedRows);
        Assert.Single(result.Strokes);
        Assert.Single(result.Strokes[0].Points);
    }

    [Fact]
    public void StrokesWithNoValidPoints_AreDropped()
    {
        var lines = new List<string>
        {
            $"{Guid.NewGuid():D}\tDirt\t3\t1.0\t2.0\t3.0\t1",
        };

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(1, result.MalformedRows);
        Assert.Empty(result.Strokes);
    }

    [Fact]
    public void CaseInsensitiveKind_IsAccepted()
    {
        var lines = new List<string>
        {
            $"{Guid.NewGuid():D}\tdirt\t0\t1.0\t2.0\t3.0\t1",
            $"{Guid.NewGuid():D}\tPAVED\t0\t9.0\t8.0\t7.0\t1",
        };

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(2, result.Strokes.Count);
    }
}
