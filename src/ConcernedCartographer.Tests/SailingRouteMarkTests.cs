using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>#243: a route must be explicitly marked as a sailing route
/// before sailing Route Follow will ever engage on it.</summary>
/// <remarks>The mark rides in its OWN row rather than a wider meta row. That
/// shape is what bounds the cross-version cost: the meta row keeps its exact
/// v2 bytes, so a pre-#243 Cartographer still parses the route and all of its
/// points and loses only the mark. Had the mark widened the meta row, the old
/// parser would have rejected that row, orphaned the points, and discarded
/// the whole route — which is what the tests below actually pin down.</remarks>
public class SailingRouteMarkTests
{
    /// <summary>A literal, frozen meta row in the v2 shape — 19 fields ending
    /// in the "2" marker. Frozen on purpose: comparing the serializer to
    /// itself would let a field reorder move both sides together and still
    /// pass, while an older Cartographer would break.</summary>
    private const string FrozenMetaV2Row =
        "cc:route:3f2504e04f8911d39a0c0305e82c3301\t4\t638634816000000000\t638635680000000000\tM\t" +
        "Frozen route\t2\t2\t2\t-13369549\taround the reef\t1\t0\t0\t0\t\tauthor-a\tauthor-b\t2";

    /// <summary>The same route in the pre-author v1 shape — 17 fields ending
    /// in the row marker.</summary>
    private const string FrozenMetaV1Row =
        "cc:route:3f2504e04f8911d39a0c0305e82c3301\t4\t638634816000000000\t638635680000000000\tM\t" +
        "Frozen route\t2\t2\t2\t-13369549\taround the reef\t1\t0\t0\t0\t\t1";

    private const string FrozenPointRow =
        "cc:route:3f2504e04f8911d39a0c0305e82c3301\t4\t0\t10\t0\t20\tP\t1";

    private const string FrozenRouteId = "cc:route:3f2504e04f8911d39a0c0305e82c3301";

    private static AtlasRoute Route(RouteTravel travel)
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 3,
            CreatedUtc = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc),
            Name = "Meadows to Swamp",
            Kind = RouteKind.Waypoint,
            Style = RouteStyle.Dashed,
            Status = RouteStatus.Active,
            Travel = travel,
            ColorArgb = unchecked((int)0xFF3366CC),
            Notes = "around the reef",
            Scope = AtlasScope.Private,
        };
        route.Points.Add(new RoadPoint(0f, 0f, 0f));
        route.Points.Add(new RoadPoint(0f, 0f, 250f));
        route.Points.Add(new RoadPoint(180f, 0f, 400f));
        return route;
    }

    private static string MetaRow(AtlasRoute route)
    {
        return RouteCodec.SerializeRoute(route).First(line => line.Split('\t')[4] == "M");
    }

    private static bool IsTravelRow(string line)
    {
        string[] fields = line.Split('\t');
        return fields.Length == 8 && fields[6] == "V";
    }

    private static string[] TravelRows(AtlasRoute route)
    {
        return RouteCodec.SerializeRoute(route).Where(IsTravelRow).ToArray();
    }

    [Fact]
    public void RoutesDefaultToLandTravel()
    {
        Assert.Equal(
            RouteTravel.Land,
            new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())).Travel);
    }

    [Fact]
    public void AnUnmarkedRouteIsByteIdenticalToWhatShippedBefore()
    {
        AtlasRoute land = Route(RouteTravel.Land);

        string[] fields = MetaRow(land).Split('\t');
        Assert.Equal(19, fields.Length);
        Assert.Equal("2", fields[fields.Length - 1]);
        Assert.Empty(TravelRows(land));
    }

    [Fact]
    public void AMarkedRouteKeepsTheSameMetaRowAndAddsOneTravelRow()
    {
        AtlasRoute sea = Route(RouteTravel.Sea);

        // The meta row is unchanged — this is the whole point of the design.
        string[] metaFields = MetaRow(sea).Split('\t');
        Assert.Equal(19, metaFields.Length);
        Assert.Equal("2", metaFields[metaFields.Length - 1]);

        AtlasRoute land = Route(RouteTravel.Land);
        Assert.Equal(MetaRow(land).Split('\t')[4..], metaFields[4..]);

        string[] travelFields = Assert.Single(TravelRows(sea)).Split('\t');
        Assert.Equal(8, travelFields.Length);
        Assert.Equal("V", travelFields[6]);
        Assert.Equal("1", travelFields[7]);
        Assert.Equal(((int)RouteTravel.Sea).ToString(), travelFields[2]);
        Assert.Equal(sea.Revision.ToString(), travelFields[1]);
    }

    [Fact]
    public void AnOlderParserKeepsTheRouteAndLosesOnlyTheMark()
    {
        // A pre-#243 parser accepts the meta row (19 fields, marker "2") and
        // the point rows, and rejects only the travel row, whose tag it does
        // not know. Dropping the travel row models exactly what "counted as
        // malformed and skipped" leaves that parser holding.
        AtlasRoute sea = Route(RouteTravel.Sea);
        string[] all = RouteCodec.Serialize(new[] { sea }).ToArray();
        string[] withoutTravelRow = all.Where(line => !IsTravelRow(line)).ToArray();

        Assert.Equal(all.Length - 1, withoutTravelRow.Length);

        RouteCodec.ParseResult result = RouteCodec.Parse(withoutTravelRow);

        Assert.Equal(0, result.MalformedRows);
        AtlasRoute survivor = Assert.Single(result.Routes);
        Assert.Equal(sea.Name, survivor.Name);
        Assert.Equal(sea.Points, survivor.Points);
        Assert.Equal(RouteTravel.Land, survivor.Travel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TravelSurvivesARoundTrip(bool sailing)
    {
        RouteTravel travel = sailing ? RouteTravel.Sea : RouteTravel.Land;
        AtlasRoute original = Route(travel);

        RouteCodec.ParseResult result = RouteCodec.Parse(
            RouteCodec.Serialize(new[] { original }));

        Assert.Equal(0, result.MalformedRows);
        AtlasRoute parsed = Assert.Single(result.Routes);
        Assert.Equal(travel, parsed.Travel);
        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.Status, parsed.Status);
        Assert.Equal(original.Points, parsed.Points);
    }

    [Fact]
    public void TheFrozenV2RowStillParses()
    {
        RouteCodec.ParseResult result = RouteCodec.Parse(
            new[] { RouteCodec.Header, FrozenMetaV2Row, FrozenPointRow });

        Assert.Equal(0, result.MalformedRows);
        AtlasRoute route = Assert.Single(result.Routes);
        Assert.Equal("Frozen route", route.Name);
        Assert.Equal(RouteTravel.Land, route.Travel);
        Assert.Equal("author-a", route.OwnerAuthor);
        Assert.Equal(new RoadPoint(10f, 0f, 20f), Assert.Single(route.Points));
    }

    [Fact]
    public void TheFrozenV1RowStillParses()
    {
        RouteCodec.ParseResult result = RouteCodec.Parse(
            new[] { RouteCodec.Header, FrozenMetaV1Row, FrozenPointRow });

        Assert.Equal(0, result.MalformedRows);
        AtlasRoute route = Assert.Single(result.Routes);
        Assert.Equal("Frozen route", route.Name);
        Assert.Equal(RouteTravel.Land, route.Travel);
        Assert.Equal("", route.OwnerAuthor);
    }

    [Fact]
    public void AFrozenV2RowPlusATravelRowParsesAsASailingRoute()
    {
        string travelRow = string.Join(
            "\t", FrozenRouteId, "4",
            ((int)RouteTravel.Sea).ToString(), "0", "0", "0", "V", "1");

        RouteCodec.ParseResult result = RouteCodec.Parse(
            new[] { RouteCodec.Header, FrozenMetaV2Row, FrozenPointRow, travelRow });

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(RouteTravel.Sea, Assert.Single(result.Routes).Travel);
    }

    [Fact]
    public void AnUnknownTravelValueIsRejectedInsteadOfGuessed()
    {
        string badTravelRow = string.Join(
            "\t", FrozenRouteId, "4", "99", "0", "0", "0", "V", "1");

        RouteCodec.ParseResult result = RouteCodec.Parse(
            new[] { RouteCodec.Header, FrozenMetaV2Row, FrozenPointRow, badTravelRow });

        // The bad row is dropped; the route itself survives as a land route.
        Assert.Equal(1, result.MalformedRows);
        AtlasRoute route = Assert.Single(result.Routes);
        Assert.Equal(RouteTravel.Land, route.Travel);
        Assert.Single(route.Points);
    }

    [Fact]
    public void CloneAndCopyCarryTheMark()
    {
        AtlasRoute sea = Route(RouteTravel.Sea);
        Assert.Equal(RouteTravel.Sea, sea.Clone().Travel);

        var target = new AtlasRoute(sea.Id);
        target.CopyFrom(sea);
        Assert.Equal(RouteTravel.Sea, target.Travel);

        AtlasRoute land = Route(RouteTravel.Land);
        target.CopyFrom(land);
        Assert.Equal(RouteTravel.Land, target.Travel);
    }

    [Fact]
    public void JournalReplayKeepsTheNewestMark()
    {
        AtlasRoute land = Route(RouteTravel.Land);
        AtlasRoute marked = land.Clone();
        marked.Revision = land.Revision + 1;
        marked.Travel = RouteTravel.Sea;

        var lines = new List<string>(RouteCodec.Serialize(new[] { land }));
        lines.AddRange(RouteCodec.SerializeRoute(marked));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.Equal(RouteTravel.Sea, Assert.Single(result.Routes).Travel);
    }

    [Fact]
    public void ANewerUnmarkedRevisionClearsTheMark()
    {
        // The newest revision wins wholesale, so unmarking is just another
        // revision — a stale travel row can never resurrect the mark.
        AtlasRoute sea = Route(RouteTravel.Sea);
        AtlasRoute unmarked = sea.Clone();
        unmarked.Revision = sea.Revision + 1;
        unmarked.Travel = RouteTravel.Land;

        var lines = new List<string>(RouteCodec.Serialize(new[] { sea }));
        lines.AddRange(RouteCodec.SerializeRoute(unmarked));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.Equal(RouteTravel.Land, Assert.Single(result.Routes).Travel);
        Assert.True(result.SupersededRows > 0);
    }
}
