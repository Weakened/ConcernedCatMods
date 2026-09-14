using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>#243: a route must be explicitly marked as a sailing route
/// before sailing Route Follow will ever engage on it. The mark rides in a
/// v3 meta row that is emitted ONLY for a marked route, so every existing
/// route keeps the byte-identical v2 row it has always had and an older
/// Cartographer reading the same sidecar or sync payload never meets a
/// field it has not seen.</summary>
public class SailingRouteMarkTests
{
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

    [Fact]
    public void RoutesDefaultToLandTravel()
    {
        Assert.Equal(RouteTravel.Land, new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())).Travel);
    }

    [Fact]
    public void ALandRouteSerializesTheExactRowItAlwaysHas()
    {
        // The v2 marker, not a new one: a route nobody marked must not
        // change a single byte in anyone's sidecar or sync payload.
        AtlasRoute land = Route(RouteTravel.Land);
        string meta = RouteCodec.SerializeRoute(land).First(
            line => line.Split('\t')[4] == "M");

        string[] fields = meta.Split('\t');
        Assert.Equal(19, fields.Length);
        Assert.Equal("2", fields[fields.Length - 1]);
    }

    [Fact]
    public void ASailingRouteSerializesTheV3Row()
    {
        AtlasRoute sea = Route(RouteTravel.Sea);
        string meta = RouteCodec.SerializeRoute(sea).First(
            line => line.Split('\t')[4] == "M");

        string[] fields = meta.Split('\t');
        Assert.Equal(20, fields.Length);
        Assert.Equal("3", fields[fields.Length - 1]);
        Assert.Equal(((int)RouteTravel.Sea).ToString(), fields[18]);
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
    public void AnExistingV2FileStillParsesAsALandRoute()
    {
        // Exactly what a pre-#243 Cartographer wrote.
        AtlasRoute land = Route(RouteTravel.Land);
        var lines = new List<string>(RouteCodec.Serialize(new[] { land }));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.Equal(0, result.MalformedRows);
        Assert.Equal(RouteTravel.Land, Assert.Single(result.Routes).Travel);
    }

    [Fact]
    public void AnUnknownTravelValueIsRejectedInsteadOfGuessed()
    {
        AtlasRoute sea = Route(RouteTravel.Sea);
        var lines = new List<string>();
        foreach (string line in RouteCodec.SerializeRoute(sea))
        {
            string[] fields = line.Split('\t');
            if (fields[4] == "M" && fields.Length == 20)
            {
                fields[18] = "99";
                lines.Add(string.Join("\t", fields));
            }
            else
            {
                lines.Add(line);
            }
        }

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.True(result.MalformedRows > 0);
        Assert.Empty(result.Routes);
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
}
