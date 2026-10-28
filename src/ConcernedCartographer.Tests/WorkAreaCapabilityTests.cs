using System.Reflection;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.Interop.WorkArea;

namespace ConcernedCartographer.Tests;

/// <summary>#296 slice 1: the work-area shape both sides validate, the
/// Cartographer store and sidecar, the read-only concernedcat.workarea/1
/// responder, and the cc_areas development command.</summary>
public class WorkAreaCapabilityTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static WorkAreaPoint P(float x, float z) => new(x, z);

    private static WorkAreaPolygon Square(float size = 20f, float x = 0f, float z = 0f)
    {
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(
            new[] { P(x, z), P(x + size, z), P(x + size, z + size), P(x, z + size) }, out WorkAreaPolygon? polygon));
        return polygon!;
    }

    // ------------------------------------------------------------- shape

    [Fact]
    public void ASimpleSquareIsAWorkAreaWithItsTrueArea()
    {
        WorkAreaPolygon square = Square(20f);
        Assert.Equal(400f, square.AreaSquareMetres, 3);
        Assert.Equal(4, square.Points.Count);
        Assert.Equal(16, square.Revision.Length);
    }

    [Fact]
    public void AClockwiseDrawingIsStoredCounterClockwise()
    {
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(
            new[] { P(0, 0), P(0, 20), P(20, 20), P(20, 0) }, out WorkAreaPolygon? clockwise));

        // Shoelace over the stored order is positive (counter-clockwise).
        double sum = 0;
        IReadOnlyList<WorkAreaPoint> points = clockwise!.Points;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            sum += ((double)points[j].X * points[i].Z) - ((double)points[i].X * points[j].Z);
        }

        Assert.True(sum > 0);
    }

    [Fact]
    public void TheRevisionIsStableAndAnEditChangesIt()
    {
        Assert.Equal(Square().Revision, Square().Revision);
        Assert.NotEqual(Square(20f).Revision, Square(21f).Revision);
        Assert.NotEqual(Square(20f).Revision, Square(20f, x: 1f).Revision);
    }

    [Fact]
    public void ARepeatedClosingPointAndRepeatedVerticesAreTheSameShape()
    {
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(
            new[] { P(0, 0), P(0, 0), P(20, 0), P(20, 20), P(0, 20), P(0, 0) }, out WorkAreaPolygon? written));
        Assert.Equal(Square().Revision, written!.Revision);
    }

    private static readonly Dictionary<string, (WorkAreaPoint[] Points, WorkAreaShapeProblem Expected)> Refused = new()
    {
        ["two points"] = (new[] { P(0, 0), P(10, 0) }, WorkAreaShapeProblem.TooFewPoints),
        ["one point repeated"] = (new[] { P(5, 5), P(5, 5), P(5, 5), P(5, 5) }, WorkAreaShapeProblem.TooFewPoints),
        ["NaN"] = (new[] { P(0, 0), P(float.NaN, 0), P(10, 10) }, WorkAreaShapeProblem.NotFinite),
        ["infinity"] = (new[] { P(0, 0), P(10, 0), P(10, float.PositiveInfinity) }, WorkAreaShapeProblem.NotFinite),
        ["wider than allowed"] = (new[] { P(0, 0), P(300, 0), P(300, 10), P(0, 10) }, WorkAreaShapeProblem.TooLarge),
        ["a sliver"] = (new[] { P(0, 0), P(10, 0), P(10, 1), P(0, 1) }, WorkAreaShapeProblem.TooSmall),
        ["collinear"] = (new[] { P(0, 0), P(10, 0), P(20, 0) }, WorkAreaShapeProblem.SelfIntersecting),
        ["a bow tie"] = (new[] { P(0, 0), P(20, 20), P(20, 0), P(0, 20) }, WorkAreaShapeProblem.SelfIntersecting),
        ["a fold back"] = (new[] { P(0, 0), P(20, 0), P(10, 0), P(10, 20) }, WorkAreaShapeProblem.SelfIntersecting),
        ["a vertex touching an edge"] = (new[] { P(0, 0), P(20, 0), P(20, 20), P(10, 0), P(0, 20) }, WorkAreaShapeProblem.SelfIntersecting),
        ["no points"] = (Array.Empty<WorkAreaPoint>(), WorkAreaShapeProblem.TooFewPoints),
    };

    public static IEnumerable<object[]> RefusedShapes() => Refused.Keys.Select(key => new object[] { key });

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public void ABadBoundaryIsRefusedAndNeverBecomesARegion(string why)
    {
        (WorkAreaPoint[] points, WorkAreaShapeProblem expected) = Refused[why];
        Assert.Equal(expected, WorkAreaPolygon.TryCreate(points, out WorkAreaPolygon? polygon));
        Assert.Null(polygon);
    }

    [Fact]
    public void TooManyCornersIsRefusedNotTruncated()
    {
        var points = new List<WorkAreaPoint>();
        for (int i = 0; i < WorkAreaPolygon.MaxPoints + 1; i++)
        {
            double angle = 2 * Math.PI * i / (WorkAreaPolygon.MaxPoints + 1);
            points.Add(P((float)(50 * Math.Cos(angle)), (float)(50 * Math.Sin(angle))));
        }

        Assert.Equal(WorkAreaShapeProblem.TooManyPoints, WorkAreaPolygon.TryCreate(points, out _));
        Assert.Equal(WorkAreaShapeProblem.TooFewPoints, WorkAreaPolygon.TryCreate(null, out _));
    }

    [Fact]
    public void ContainmentIsEvenOddAndDeterministicOnTheBoundary()
    {
        WorkAreaPolygon square = Square(20f);
        Assert.True(square.Contains(10, 10));
        Assert.False(square.Contains(30, 10));
        Assert.False(square.Contains(-1, 10));
        Assert.False(square.Contains(float.NaN, 10));
        Assert.False(square.Contains(10, float.PositiveInfinity));

        // Half-open: left and bottom edges in, right and top edges out, the
        // same answer every time.
        Assert.True(square.Contains(0, 10));
        Assert.False(square.Contains(20, 10));
        Assert.True(square.Contains(10, 0));
        Assert.False(square.Contains(10, 20));
    }

    [Fact]
    public void AStraightRunThroughAMidSideVertexIsStillAValidSide()
    {
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(
            new[] { P(0, 0), P(10, 0), P(20, 0), P(20, 20), P(0, 20) }, out WorkAreaPolygon? polygon));
        Assert.Equal(400f, polygon!.AreaSquareMetres, 3);
    }

    [Fact]
    public void AConcaveNotchIsOutside()
    {
        // A U shape: the notch between the arms is not part of the area.
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryCreate(
            new[] { P(0, 0), P(30, 0), P(30, 30), P(20, 30), P(20, 10), P(10, 10), P(10, 30), P(0, 30) },
            out WorkAreaPolygon? u));
        Assert.True(u!.Contains(5, 20));
        Assert.True(u.Contains(25, 20));
        Assert.False(u.Contains(15, 20));
        Assert.True(u.Contains(15, 5));
    }

    [Fact]
    public void TheWireFormRoundTripsToTheSameArea()
    {
        WorkAreaPolygon square = Square(20.125f, x: -3.5f, z: 1e3f);
        Assert.Equal(WorkAreaShapeProblem.None, WorkAreaPolygon.TryParseWire(square.ToWire(), out WorkAreaPolygon? back));
        Assert.Equal(square.Revision, back!.Revision);
        Assert.Equal(square.ToWire(), back.ToWire());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0;0|20;0|20;20|0;x")]
    [InlineData("0;0|20;0|20;20|0")]
    [InlineData("0;0|20;0|20;20|0;20;5")]
    [InlineData("0,0|20,0|20,20|0,20")]
    public void AWireStringThatDoesNotParseCompletelyIsNotAPolygon(string? wire)
    {
        Assert.NotEqual(WorkAreaShapeProblem.None, WorkAreaPolygon.TryParseWire(wire, out WorkAreaPolygon? polygon));
        Assert.Null(polygon);
    }

    // ------------------------------------------------------------- store

    [Fact]
    public void AnAreaGetsAStableIdAndKeepsItThroughARename()
    {
        var store = new WorkAreaStore();
        Assert.Equal(WorkAreaRefusal.None, store.TryCreate(
            "  North woods ", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out WorkArea? area));
        Assert.Equal("North woods", area!.Name);
        Assert.True(WorkAreaStore.IsValidId(area.Id));
        Assert.True(store.IsDirty);

        string id = area.Id;
        Assert.Equal(WorkAreaRefusal.None, store.Rename("north WOODS", "Birch grove", Now.AddMinutes(1)));
        Assert.Same(area, store.Find(id));
        Assert.Equal("Birch grove", store.Find(id)!.Name);
        Assert.Equal(Now.AddMinutes(1), area.ModifiedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tname")]
    [InlineData("line\nbreak")]
    [InlineData("0123456789012345678901234567890123456789012345678")]
    public void ANameTheSidecarCannotCarryIsRefused(string name)
    {
        var store = new WorkAreaStore();
        Assert.Equal(WorkAreaRefusal.BadName, store.TryCreate(
            name, WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _));
        Assert.Empty(store.Areas);
    }

    [Fact]
    public void NamesAreUniqueIgnoringCaseAndPurposesAreKnown()
    {
        var store = new WorkAreaStore();
        store.TryCreate("Grove", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _);
        Assert.Equal(WorkAreaRefusal.DuplicateName, store.TryCreate(
            "GROVE", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _));
        Assert.Equal(WorkAreaRefusal.UnknownPurpose, store.TryCreate(
            "Mine", "stoneGathering", Square(), Guid.NewGuid(), Now, out _));

        store.TryCreate("Other", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _);
        Assert.Equal(WorkAreaRefusal.DuplicateName, store.Rename("Other", "grove", Now));
        Assert.Equal(WorkAreaRefusal.NotFound, store.Rename("Nowhere", "x", Now));
        Assert.Equal(WorkAreaRefusal.NotFound, store.Delete("Nowhere"));
    }

    [Fact]
    public void AWorldHoldsABoundedNumberOfAreas()
    {
        var store = new WorkAreaStore();
        for (int i = 0; i < WorkAreaStore.MaxAreas; i++)
        {
            Assert.Equal(WorkAreaRefusal.None, store.TryCreate(
                "Area " + i, WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _));
        }

        Assert.Equal(WorkAreaRefusal.TooMany, store.TryCreate(
            "One more", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _));
    }

    // ----------------------------------------------------------- drafting

    private static List<RoadPoint> Circle(int count, float radius, float closeGap = 0f)
    {
        var points = new List<RoadPoint>();
        for (int i = 0; i <= count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points.Add(new RoadPoint((float)(radius * Math.Cos(angle)), 30f, (float)(radius * Math.Sin(angle))));
        }

        if (closeGap > 0)
        {
            points[points.Count - 1] = new RoadPoint(radius, 30f, closeGap);
        }

        return points;
    }

    [Fact]
    public void AClosedFreehandLoopIsSimplifiedIntoAValidArea()
    {
        Assert.Equal(WorkAreaRefusal.None, WorkAreaDrafting.FromStroke(
            Circle(400, 30f), out WorkAreaPolygon? boundary, out WorkAreaShapeProblem problem));
        Assert.Equal(WorkAreaShapeProblem.None, problem);
        Assert.InRange(boundary!.Points.Count, 3, WorkAreaPolygon.MaxPoints);
        Assert.InRange(boundary.AreaSquareMetres, 0.9f * MathF.PI * 900f, 1.01f * MathF.PI * 900f);
        Assert.True(boundary.Contains(0, 0));
    }

    [Fact]
    public void AnOpenStrokeIsRefusedNotClosedAcrossTheGap()
    {
        var open = new List<RoadPoint>
        {
            new(0, 0, 0), new(40, 0, 0), new(40, 0, 40), new(0, 0, 40),
        };

        Assert.Equal(WorkAreaRefusal.NotClosed, WorkAreaDrafting.FromStroke(open, out WorkAreaPolygon? boundary, out _));
        Assert.Null(boundary);
    }

    [Fact]
    public void ANonFinitePointAnywhereInALongStrokeIsRefusedNotSimplifiedAway()
    {
        List<RoadPoint> loop = Circle(300, 30f);
        loop[150] = new RoadPoint(float.NaN, 0f, 5f);
        Assert.Equal(WorkAreaRefusal.BadShape, WorkAreaDrafting.FromStroke(loop, out _, out WorkAreaShapeProblem problem));
        Assert.Equal(WorkAreaShapeProblem.NotFinite, problem);
    }

    [Fact]
    public void AFigureEightIsRefused()
    {
        var eight = new List<RoadPoint>
        {
            new(0, 0, 0), new(30, 0, 30), new(30, 0, 0), new(0, 0, 30), new(0, 0, 0),
        };

        Assert.Equal(WorkAreaRefusal.BadShape, WorkAreaDrafting.FromStroke(eight, out _, out WorkAreaShapeProblem problem));
        Assert.Equal(WorkAreaShapeProblem.SelfIntersecting, problem);
    }

    // ------------------------------------------------------------- codec

    [Fact]
    public void TheSidecarRoundTripsEveryArea()
    {
        var store = new WorkAreaStore();
        store.TryCreate("North woods", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _);
        store.TryCreate("Ridge", WorkAreaContract.Purposes.WoodGathering, Square(15f, 100f, -40f), Guid.NewGuid(), Now, out _);

        List<string> lines = WorkAreaCodec.Serialize(store.Areas).ToList();
        Assert.Equal(WorkAreaCodec.Header, lines[0]);

        List<WorkArea> back = WorkAreaCodec.Parse(lines, out int malformed, out bool unsupported);
        Assert.Equal(0, malformed);
        Assert.False(unsupported);
        Assert.Equal(store.Areas.Select(a => (a.Id, a.Name, a.Boundary.Revision, a.CreatedUtc)),
            back.Select(a => (a.Id, a.Name, a.Boundary.Revision, a.CreatedUtc)));
    }

    [Fact]
    public void AnUnreadableRowIsSkippedAndCountedNeverLoadedAsAnotherShape()
    {
        string good = WorkAreaCodec.Serialize(new[]
        {
            new WorkArea("wa-" + new string('a', 32), "Good", WorkAreaContract.Purposes.WoodGathering, Square(), Now, Now),
        }).Last();
        string[] lines =
        {
            WorkAreaCodec.Header,
            good,
            "wa-" + new string('b', 32) + "\tBow tie\twoodGathering\t1\t1\t0;0|20;20|20;0|0;20",
            "wa-" + new string('c', 32) + "\tUnknown purpose\tmining\t1\t1\t" + Square().ToWire(),
            "not-an-id\tName\twoodGathering\t1\t1\t" + Square().ToWire(),
            "wa-" + new string('d', 32) + "\tToo few fields",
            "# a comment",
            "",
        };

        List<WorkArea> areas = WorkAreaCodec.Parse(lines, out int malformed, out bool unsupported);
        Assert.Single(areas);
        Assert.Equal("Good", areas[0].Name);
        Assert.Equal(4, malformed);
        Assert.False(unsupported);
    }

    [Fact]
    public void AnUnknownPurposeIsCountedSoALaterBuildsAreasAreNotDropped()
    {
        string[] lines =
        {
            WorkAreaCodec.Header,
            "wa-" + new string('c', 32) + "	Quarry	stoneGathering	1	1	" + Square().ToWire(),
        };

        List<WorkArea> areas = WorkAreaCodec.Parse(lines, out int malformed, out bool unsupported, out int unknownPurpose);
        Assert.Empty(areas);
        Assert.Equal(1, unknownPurpose);
        Assert.Equal(1, malformed);
        Assert.False(unsupported);
    }

    [Fact]
    public void ANewerBuildsFileIsNotReadByGuesswork()
    {
        List<WorkArea> areas = WorkAreaCodec.Parse(
            new[] { "# cc-work-areas v2", "anything" }, out _, out bool unsupported);
        Assert.True(unsupported);
        Assert.Empty(areas);
    }

    [Fact]
    public void TheSupportReportCountsAreasAndNeverNamesThem()
    {
        var store = new WorkAreaStore();
        store.TryCreate("Secret grove", WorkAreaContract.Purposes.WoodGathering, Square(), Guid.NewGuid(), Now, out _);
        string status = SupportReportComposer.DescribeSidecar(
            ".work-areas.tsv", WorkAreaCodec.Serialize(store.Areas).ToList(), 100L);

        Assert.Equal("1 KB, 1 areas, 0 malformed", status);
        Assert.DoesNotContain("Secret", status);
        Assert.Contains(".work-areas.tsv", CartographerWorldSidecars.Suffixes);
    }

    // ---------------------------------------------------------- responder

    private static PublishedWorkAreas Published(params (string Id, string Name, WorkAreaPolygon Shape)[] areas)
    {
        return new PublishedWorkAreas(
            "123456",
            areas.Select(a => new PublishedWorkArea(a.Id, a.Name, WorkAreaContract.Purposes.WoodGathering, a.Shape)).ToList());
    }

    private static IReadOnlyDictionary<string, string> Ask(string op, string? areaId, Func<PublishedWorkAreas?> snapshot)
    {
        return WorkAreaResponder.Answer(WorkAreaContract.Request(op, areaId, "test"), "9.9.9", snapshot);
    }

    [Fact]
    public void HelloAnswersWithoutReadingTheWorld()
    {
        IReadOnlyDictionary<string, string> reply = Ask(WorkAreaContract.Ops.Hello, null,
            () => throw new InvalidOperationException("must not be read"));
        Assert.Equal(WorkAreaContract.Statuses.Ok, reply[WorkAreaContract.Keys.Status]);
        Assert.Equal("9.9.9", reply[WorkAreaContract.Keys.ProviderVersion]);
    }

    [Fact]
    public void NoWorldIsUnavailableNeverAnEmptyList()
    {
        IReadOnlyDictionary<string, string> reply = Ask(WorkAreaContract.Ops.ListAreas, null, () => null);
        Assert.Equal(WorkAreaContract.Statuses.Unavailable, reply[WorkAreaContract.Keys.Status]);
        Assert.False(reply.ContainsKey(WorkAreaContract.Keys.Count));
    }

    [Fact]
    public void ListGivesIdsNamesAndRevisionsButNoGeometry()
    {
        WorkAreaPolygon square = Square();
        IReadOnlyDictionary<string, string> reply = Ask(WorkAreaContract.Ops.ListAreas, null,
            () => Published(("wa-1", "North", square), ("wa-2", "South", Square(30f))));

        Assert.Equal(WorkAreaContract.Statuses.Ok, reply[WorkAreaContract.Keys.Status]);
        Assert.Equal("123456", reply[WorkAreaContract.Keys.WorldUid]);
        Assert.Equal("2", reply[WorkAreaContract.Keys.Count]);
        Assert.Equal("wa-1", reply["areaId.0"]);
        Assert.Equal("South", reply["name.1"]);
        Assert.Equal(square.Revision, reply["revision.0"]);
        Assert.Equal(WorkAreaContract.Purposes.WoodGathering, reply["purpose.1"]);
        Assert.DoesNotContain(reply.Keys, key => key.StartsWith(WorkAreaContract.Keys.Boundary, StringComparison.Ordinal));
    }

    [Fact]
    public void DescribeGivesTheBoundaryAConsumerCanRevalidate()
    {
        WorkAreaPolygon square = Square(25f, 10f, 10f);
        IReadOnlyDictionary<string, string> reply = Ask(WorkAreaContract.Ops.DescribeArea, "wa-1",
            () => Published(("wa-1", "North", square)));

        Assert.Equal(WorkAreaContract.Statuses.Ok, reply[WorkAreaContract.Keys.Status]);
        Assert.Equal(WorkAreaShapeProblem.None,
            WorkAreaPolygon.TryParseWire(reply[WorkAreaContract.Keys.Boundary], out WorkAreaPolygon? back));
        Assert.Equal(reply[WorkAreaContract.Keys.Revision], back!.Revision);
    }

    [Fact]
    public void AnAreaThatIsGoneIsNotFoundAndNothingElseIsOffered()
    {
        IReadOnlyDictionary<string, string> reply = Ask(WorkAreaContract.Ops.DescribeArea, "wa-deleted",
            () => Published(("wa-1", "North", Square())));
        Assert.Equal(WorkAreaContract.Statuses.NotFound, reply[WorkAreaContract.Keys.Status]);
        Assert.False(reply.ContainsKey(WorkAreaContract.Keys.Boundary));
        Assert.Equal("wa-deleted", reply[WorkAreaContract.Keys.AreaId]);
    }

    [Fact]
    public void MalformedAndForeignRequestsAreRefusedPolitely()
    {
        Func<PublishedWorkAreas?> snapshot = () => Published(("wa-1", "North", Square()));
        Assert.Equal(WorkAreaContract.Statuses.BadRequest,
            WorkAreaResponder.Answer(null, "1", snapshot)[WorkAreaContract.Keys.Status]);
        Assert.Equal(WorkAreaContract.Statuses.BadRequest,
            Ask(WorkAreaContract.Ops.DescribeArea, null, snapshot)[WorkAreaContract.Keys.Status]);
        Assert.Equal(WorkAreaContract.Statuses.Unsupported,
            Ask("assignWorker", "wa-1", snapshot)[WorkAreaContract.Keys.Status]);

        var wrongMajor = new Dictionary<string, string>(WorkAreaContract.Request(WorkAreaContract.Ops.ListAreas, null, "t"))
        {
            [WorkAreaContract.Keys.ContractMajor] = "2",
        };
        Assert.Equal(WorkAreaContract.Statuses.Unsupported,
            WorkAreaResponder.Answer(wrongMajor, "1", snapshot)[WorkAreaContract.Keys.Status]);
    }

    [Fact]
    public void TheContractHasOnlyReadingOps()
    {
        string[] ops = typeof(WorkAreaContract.Ops)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .OrderBy(op => op, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "describeArea", "hello", "listAreas" }, ops);
    }

    // ----------------------------------------------------------- command

    private static AtlasRoute Route(string name, IEnumerable<RoadPoint> points, bool archived = false)
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())) { Name = name, Archived = archived };
        route.Points.AddRange(points);
        return route;
    }

    [Fact]
    public void FromRouteSavesAClosedLoopAndNeverStartsAnything()
    {
        var store = new WorkAreaStore();
        string reply = WorkAreaCommands.Execute(
            store, new[] { Route("Loop", Circle(200, 25f)) }, "fromroute", "Loop as North woods", Guid.NewGuid(), Now);

        Assert.StartsWith("Work area \"North woods\" saved", reply);
        Assert.Contains("Nothing starts", reply);
        Assert.Single(store.Areas);
        Assert.Equal(WorkAreaContract.Purposes.WoodGathering, store.Areas[0].Purpose);
    }

    [Fact]
    public void FromRouteRefusesWhatItCannotTurnIntoAnArea()
    {
        var store = new WorkAreaStore();
        AtlasRoute[] routes =
        {
            Route("Open", new[] { new RoadPoint(0, 0, 0), new RoadPoint(50, 0, 0), new RoadPoint(50, 0, 50) }),
            Route("Twin", Circle(50, 20f)),
            Route("twin", Circle(50, 20f)),
            Route("Old", Circle(50, 20f), archived: true),
        };

        Assert.Contains("closed loop", WorkAreaCommands.Execute(store, routes, "fromroute", "Open as A", Guid.NewGuid(), Now));
        Assert.Contains("more than one route", WorkAreaCommands.Execute(store, routes, "fromroute", "TWIN as A", Guid.NewGuid(), Now));
        Assert.Contains("no route called", WorkAreaCommands.Execute(store, routes, "fromroute", "Old as A", Guid.NewGuid(), Now));
        Assert.Contains("no route called", WorkAreaCommands.Execute(store, routes, "fromroute", "Missing as A", Guid.NewGuid(), Now));
        Assert.Equal(WorkAreaCommands.Usage, WorkAreaCommands.Execute(store, routes, "fromroute", "Twin", Guid.NewGuid(), Now));
        Assert.Empty(store.Areas);
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void ListRenameAndDeleteWorkByName()
    {
        var store = new WorkAreaStore();
        Assert.StartsWith("No work areas", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "list", "", Guid.NewGuid(), Now));
        WorkAreaCommands.Execute(store, new[] { Route("Loop", Circle(100, 25f)) }, "fromroute", "Loop as Grove", Guid.NewGuid(), Now);

        Assert.Contains("\"Grove\"", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "list", "", Guid.NewGuid(), Now));
        Assert.StartsWith("Renamed", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "rename", "grove to Birch", Guid.NewGuid(), Now));
        Assert.StartsWith("Deleted", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "delete", "birch", Guid.NewGuid(), Now));
        Assert.Empty(store.Areas);
        Assert.StartsWith("Not deleted", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "delete", "birch", Guid.NewGuid(), Now));
    }

    // ------------------------------------------------- game-facing wiring

    [Fact]
    public void ThePluginPublishesTheCapabilityBesidePresenceAndTheRuntimeSavesIt()
    {
        string plugin = Source("Plugin.cs");
        Assert.Contains("all[WorkAreaCapabilityPublisher.Key] = workAreas.Endpoint;", plugin, StringComparison.Ordinal);
        Assert.Contains("new AreaToolsCommand(_runtime!)", plugin, StringComparison.Ordinal);
        Assert.Contains("_workAreaCapability?.Shutdown();", plugin, StringComparison.Ordinal);

        string runtime = Source("Runtime", "CartographerRuntime.cs");
        int worldSwitch = runtime.IndexOf("_worldUid = uid;", StringComparison.Ordinal);
        int save = runtime.LastIndexOf("SaveWorkAreasIfDirty();", worldSwitch, StringComparison.Ordinal);
        int load = runtime.IndexOf("_workAreas.Load(_workAreaPersistence.Load(uid));", worldSwitch, StringComparison.Ordinal);
        Assert.True(save >= 0 && worldSwitch > save && load > worldSwitch,
            "a world switch must save the old world's areas before loading the new world's");
        Assert.Contains("return GuardConsoleCommand(\"cc_areas\", args, ExecuteAreaCommandCore);", runtime, StringComparison.Ordinal);

        // An unreadable or newer file: Unavailable to other mods, and never
        // written over.
        int snapshot = runtime.IndexOf("internal (long WorldUid, IReadOnlyList<WorkArea> Areas)? WorkAreaSnapshot()", StringComparison.Ordinal);
        int readOnly = runtime.IndexOf("if (_workAreaPersistence.IsReadOnly(uid))", snapshot, StringComparison.Ordinal);
        int answer = runtime.IndexOf("return (uid, _workAreas.Areas);", snapshot, StringComparison.Ordinal);
        Assert.True(snapshot >= 0 && readOnly > snapshot && answer > readOnly);

        string persistence = Source("Persistence", "WorkAreaPersistence.cs");
        int saveMethod = persistence.IndexOf("public bool Save(long worldUid, IEnumerable<WorkArea> areas)", StringComparison.Ordinal);
        int guard = persistence.IndexOf("if (_readOnlyWorlds.Contains(worldUid))", saveMethod, StringComparison.Ordinal);
        int write = persistence.IndexOf("File.WriteAllLines(", saveMethod, StringComparison.Ordinal);
        Assert.True(saveMethod >= 0 && guard > saveMethod && write > guard, "Save must refuse a read-only world before writing");
        Assert.Contains("out int unknownPurpose);", persistence, StringComparison.Ordinal);
    }

    [Fact]
    public void ABareCommandLists()
    {
        // ConsoleArguments maps no arguments to "status".
        var store = new WorkAreaStore();
        Assert.StartsWith("No work areas", WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "status", "", Guid.NewGuid(), Now));
    }

    private static string Source(params string[] parts)
    {
        var segments = new List<string> { RepoRoot, "src", "ConcernedCartographer" };
        segments.AddRange(parts);
        return File.ReadAllText(Path.Combine(segments.ToArray()));
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
