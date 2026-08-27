using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>CC-056: the migration matrix — every historical row format the
/// mod has ever written must still parse in one sweep, mixed in one file,
/// with no data loss and correct defaults for fields that did not exist
/// yet.</summary>
public class MigrationMatrixTests
{
    [Fact]
    public void Roads_V1_V2_V3_ParseTogether()
    {
        string id1 = Guid.NewGuid().ToString("D");
        string id2 = Guid.NewGuid().ToString("D");
        string id3 = Guid.NewGuid().ToString("D");
        var lines = new[]
        {
            "# ConcernedCartographer roads v1",
            $"{id1}\tDirt\t0\t1.0\t30.0\t2.0\t1",
            $"{id1}\tDirt\t1\t5.0\t30.0\t2.0\t1",
            $"{id2}\tPaved\t0\t9.0\t30.0\t2.0\tConstruction\t2",
            $"{id3}\tDirt\t0\t20.0\t30.0\t2.0\tChunkRecovery\t0\t3",
            $"{id3}\tDirt\t1\t25.0\t30.0\t2.0\tChunkRecovery\t0\t3",
        };

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(3, result.Strokes.Count);
        Assert.Equal(2, result.LegacyRows);
        Assert.Equal(RoadObservationSource.Traversal, result.Strokes[0].Source);
        Assert.Equal(RoadObservationSource.Construction, result.Strokes[1].Source);
        Assert.True(result.Strokes[2].Hidden == false);

        // Round back out in the current format and reparse losslessly.
        RoadAtlasCodec.ParseResult reparsed = RoadAtlasCodec.Parse(
            RoadAtlasCodec.Serialize(result.Strokes));
        Assert.Equal(0, reparsed.MalformedRows);
        Assert.Equal(0, reparsed.LegacyRows);
        Assert.Equal(3, reparsed.Strokes.Count);
    }

    [Fact]
    public void Pins_V1_V2_ParseTogether()
    {
        var v1Pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 3,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Name = "Old pin",
        };

        // Rebuild the 22-field v1 row from the current serializer.
        string[] current = PinCodec.SerializeRow(v1Pin).Split('\t');
        var v1Fields = new List<string>();
        for (int index = 0; index < current.Length; index++)
        {
            if (index == 18 || index == 19)
            {
                continue;
            }

            v1Fields.Add(index == current.Length - 1 ? "1" : current[index]);
        }

        var v2Pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Name = "New pin",
            OwnerAuthor = "author-x",
            LastAuthor = "author-y",
        };

        var lines = new List<string>
        {
            PinCodec.Header,
            string.Join("\t", v1Fields),
            PinCodec.SerializeRow(v2Pin),
        };

        PinCodec.ParseResult result = PinCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(2, result.Pins.Count);
        Assert.Equal("", result.Pins[0].OwnerAuthor);
        Assert.Equal("author-x", result.Pins[1].OwnerAuthor);
    }

    [Fact]
    public void Routes_MetaV1_MetaV2_ParseTogether()
    {
        var v2Route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 2,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Name = "New route",
            OwnerAuthor = "author-x",
        };
        v2Route.Points.Add(new RoadPoint(1f, 30f, 1f));

        // Rebuild a 17-field meta-v1 row by dropping the author columns.
        string[] metaV2 = null!;
        var pointRows = new List<string>();
        foreach (string line in RouteCodec.SerializeRoute(v2Route))
        {
            if (line.Contains("\tM\t"))
            {
                metaV2 = line.Split('\t');
            }
            else
            {
                pointRows.Add(line);
            }
        }

        var v1Fields = new List<string>();
        for (int index = 0; index < metaV2.Length; index++)
        {
            if (index == 16 || index == 17)
            {
                continue;
            }

            v1Fields.Add(index == metaV2.Length - 1 ? "1" : metaV2[index]);
        }

        var otherId = new AtlasId(AtlasId.RouteKind, Guid.NewGuid());
        var lines = new List<string> { RouteCodec.Header, string.Join("\t", v1Fields) };
        lines.AddRange(pointRows);
        var freshRoute = new AtlasRoute(otherId)
        {
            Revision = 1,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Name = "V2 route",
            LastAuthor = "author-z",
        };
        freshRoute.Points.Add(new RoadPoint(5f, 30f, 5f));
        lines.AddRange(RouteCodec.SerializeRoute(freshRoute));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(2, result.Routes.Count);
        Assert.Equal("", result.Routes[0].OwnerAuthor);
        Assert.Equal("author-z", result.Routes[1].LastAuthor);
        Assert.Single(result.Routes[0].Points);
    }
}
