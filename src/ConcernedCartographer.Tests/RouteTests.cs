using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RouteCodecTests
{
    private static AtlasRoute FullRoute()
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 4,
            CreatedUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc),
            Name = "Trade\troute",
            Kind = RouteKind.Waypoint,
            Style = RouteStyle.Dashed,
            Status = RouteStatus.Active,
            ColorArgb = unchecked((int)0xFF00AAFF),
            Notes = "north\nleg",
            Scope = AtlasScope.Table,
            Locked = true,
            Archived = false,
        };
        route.Points.Add(new RoadPoint(0f, 30f, 0f));
        route.Points.Add(new RoadPoint(100f, 31f, 50f));
        route.Points.Add(new RoadPoint(200f, 32f, 100f));
        return route;
    }

    [Fact]
    public void Roundtrip_PreservesMetadataAndOrderedPoints()
    {
        AtlasRoute original = FullRoute();
        RouteCodec.ParseResult result = RouteCodec.Parse(RouteCodec.Serialize(new[] { original }));

        Assert.Equal(0, result.MalformedRows);
        AtlasRoute parsed = Assert.Single(result.Routes);
        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.Revision, parsed.Revision);
        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.Kind, parsed.Kind);
        Assert.Equal(original.Style, parsed.Style);
        Assert.Equal(original.Status, parsed.Status);
        Assert.Equal(original.ColorArgb, parsed.ColorArgb);
        Assert.Equal(original.Notes, parsed.Notes);
        Assert.Equal(original.Scope, parsed.Scope);
        Assert.True(parsed.Locked);
        Assert.Equal(original.Points, parsed.Points);
    }

    [Fact]
    public void JournalReplay_KeepsOnlyTheHighestRevision()
    {
        AtlasRoute route = FullRoute();
        AtlasRoute older = route.Clone();
        older.Revision = 2;
        older.Points.RemoveAt(2);
        older.Name = "Old";

        var lines = new List<string>(RouteCodec.Serialize(new[] { older }));
        lines.AddRange(RouteCodec.SerializeRoute(route));
        lines.AddRange(RouteCodec.SerializeRoute(older));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        AtlasRoute winner = Assert.Single(result.Routes);
        Assert.Equal(4, winner.Revision);
        Assert.Equal(3, winner.Points.Count);
        Assert.Equal("Trade\troute", winner.Name);
        Assert.True(result.SupersededRows > 0);
    }

    [Fact]
    public void TruncatedPointRow_LosesOnlyThatRow()
    {
        AtlasRoute route = FullRoute();
        var lines = new List<string>(RouteCodec.Serialize(new[] { route }));
        lines.Add(lines[lines.Count - 1].Substring(0, 30));

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);

        Assert.Equal(1, result.MalformedRows);
        Assert.Single(result.Routes);
        Assert.Equal(3, result.Routes[0].Points.Count);
    }

    [Fact]
    public void PointsWithoutMeta_AreDropped()
    {
        AtlasRoute route = FullRoute();
        var lines = new List<string>();
        foreach (string line in RouteCodec.SerializeRoute(route))
        {
            if (!line.Contains("\tM\t"))
            {
                lines.Add(line);
            }
        }

        RouteCodec.ParseResult result = RouteCodec.Parse(lines);
        Assert.Empty(result.Routes);
        Assert.True(result.MalformedRows >= 1);
    }
}

public class RouteOperationsTests
{
    private static DateTime _now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    private static (RouteStore Store, RouteOperations Ops) Fixture()
    {
        var store = new RouteStore(() => _now);
        return (store, new RouteOperations(store));
    }

    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static AtlasRoute Line(RouteOperations ops, float fromX, float toX, float step = 5f)
    {
        AtlasRoute route = ops.StartRoute(RouteKind.Freehand, "Line");
        for (float x = fromX; x <= toX; x += step)
        {
            ops.AppendPoint(route.Id, P(x, 0f));
        }

        return route;
    }

    [Fact]
    public void Freehand_AppendsWithMinimumSpacing()
    {
        (RouteStore _, RouteOperations ops) = Fixture();
        AtlasRoute route = ops.StartRoute(RouteKind.Freehand, "Draw");

        Assert.True(ops.AppendPoint(route.Id, P(0f, 0f)));
        Assert.False(ops.AppendPoint(route.Id, P(0.5f, 0f)));
        Assert.True(ops.AppendPoint(route.Id, P(5f, 0f)));
        Assert.Equal(2, route.Points.Count);
    }

    [Fact]
    public void EraseNear_SplitsIntoRuns_AndUndoRestores()
    {
        (RouteStore store, RouteOperations ops) = Fixture();
        AtlasRoute route = Line(ops, 0f, 100f);
        int pointsBefore = route.Points.Count;

        int removed = ops.EraseNear(route.Id, P(50f, 0f), 7f, out List<AtlasRoute> created);

        Assert.True(removed > 0);
        Assert.Single(created);
        Assert.True(route.Points.Count < pointsBefore);
        Assert.True(created[0].Points.Count > 0);
        Assert.Contains("(part)", created[0].Name);

        Assert.True(ops.Undo(out _));
        Assert.Equal(pointsBefore, route.Points.Count);
        Assert.True(store.TryGet(created[0].Id, out AtlasRoute tail));
        Assert.True(tail.Deleted);
    }

    [Fact]
    public void WaypointOps_InsertMoveRemove_AreUndoable()
    {
        (RouteStore _, RouteOperations ops) = Fixture();
        AtlasRoute route = ops.StartRoute(RouteKind.Waypoint, "Way");
        ops.AppendPoint(route.Id, P(0f, 0f));
        ops.AppendPoint(route.Id, P(100f, 0f));

        Assert.True(ops.InsertWaypoint(route.Id, 0, P(50f, 20f)));
        Assert.Equal(3, route.Points.Count);
        Assert.Equal(P(50f, 20f), route.Points[1]);

        Assert.True(ops.MoveWaypoint(route.Id, 1, P(50f, -20f)));
        Assert.Equal(P(50f, -20f), route.Points[1]);

        Assert.True(ops.RemoveWaypoint(route.Id, 1));
        Assert.Equal(2, route.Points.Count);

        Assert.True(ops.Undo(out _));
        Assert.Equal(3, route.Points.Count);
        Assert.True(ops.Undo(out _));
        Assert.Equal(P(50f, 20f), route.Points[1]);
    }

    [Fact]
    public void Split_SharesThePoint_AndMergeRejoins()
    {
        (RouteStore store, RouteOperations ops) = Fixture();
        AtlasRoute route = Line(ops, 0f, 50f);
        int total = route.Points.Count;

        AtlasRoute? tail = ops.Split(route.Id, 5);

        Assert.NotNull(tail);
        Assert.Equal(6, route.Points.Count);
        Assert.Equal(total - 5, tail!.Points.Count);
        Assert.Equal(route.Points[5], tail.Points[0]);

        Assert.True(ops.Merge(route.Id, tail.Id));
        Assert.True(store.TryGet(tail.Id, out AtlasRoute merged));
        Assert.True(merged.Deleted);
        Assert.Equal(total + 1, route.Points.Count);
    }

    [Fact]
    public void LockedRoute_RejectsGeometryEdits_UntilUnlocked()
    {
        (RouteStore _, RouteOperations ops) = Fixture();
        AtlasRoute route = Line(ops, 0f, 20f);

        Assert.True(ops.SetLocked(route.Id, true));
        Assert.False(ops.AppendPoint(route.Id, P(40f, 0f)));
        Assert.False(ops.MoveWaypoint(route.Id, 0, P(1f, 1f)));
        Assert.Equal(0, ops.EraseNear(route.Id, P(10f, 0f), 50f, out _));

        Assert.True(ops.SetLocked(route.Id, false));
        Assert.True(ops.AppendPoint(route.Id, P(40f, 0f)));
    }

    [Fact]
    public void DeleteRestore_AndArchive_Work()
    {
        (RouteStore _, RouteOperations ops) = Fixture();
        AtlasRoute route = Line(ops, 0f, 10f);

        Assert.True(ops.SetArchived(route.Id, true));
        Assert.True(route.Archived);
        Assert.True(ops.Delete(route.Id));
        Assert.True(route.Deleted);
        Assert.True(ops.RestoreDeleted(route.Id));
        Assert.False(route.Deleted);
    }

    [Fact]
    public void UndoNeverRollsRevisionsBackward_AndReplayConverges()
    {
        (RouteStore store, RouteOperations ops) = Fixture();
        var journal = new List<string> { RouteCodec.Header };
        store.Changed += route => journal.AddRange(RouteCodec.SerializeRoute(route));

        AtlasRoute route = Line(ops, 0f, 20f);
        long revision = route.Revision;
        ops.MoveWaypoint(route.Id, 0, P(999f, 999f));
        Assert.True(route.Revision > revision);
        revision = route.Revision;
        ops.Undo(out _);
        Assert.True(route.Revision > revision);

        RouteCodec.ParseResult replay = RouteCodec.Parse(journal);
        AtlasRoute replayed = Assert.Single(replay.Routes);
        Assert.Equal(route.Points[0], replayed.Points[0]);
        Assert.Equal(P(0f, 0f), replayed.Points[0]);
    }
}

public class RouteEstimatorTests
{
    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static RoadAtlas RoadLine()
    {
        var rules = new RoadSamplingRules(1.5f, 8f, 2f);
        var atlas = new RoadAtlas();
        for (float x = 0f; x <= 100f; x += 2f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 0f), rules, out _);
        }

        return atlas;
    }

    [Fact]
    public void Distance_And_OnRoadFraction_AreComputed()
    {
        RoadAtlas roads = RoadLine();
        var onRoad = new List<RoadPoint> { P(0f, 0f), P(100f, 0f) };
        var offRoad = new List<RoadPoint> { P(0f, 500f), P(100f, 500f) };

        RouteEstimator.Estimate on = RouteEstimator.Compute(onRoad, roads, 3f, 2f, 4f);
        RouteEstimator.Estimate off = RouteEstimator.Compute(offRoad, roads, 3f, 2f, 4f);

        Assert.Equal(100f, on.DistanceMeters, 1f);
        Assert.True(on.OnRoadFraction > 0.9f);
        Assert.Equal(0f, off.OnRoadFraction);
        // On-road at 4 m/s ≈ 0.42 min; off-road at 2 m/s ≈ 0.83 min.
        Assert.True(on.EstimatedMinutes < off.EstimatedMinutes);
    }

    [Fact]
    public void EmptyRoute_IsZero()
    {
        RouteEstimator.Estimate estimate = RouteEstimator.Compute(
            new List<RoadPoint>(), RoadLine(), 3f, 2f, 4f);
        Assert.Equal(0f, estimate.DistanceMeters);
        Assert.Equal(0f, estimate.EstimatedMinutes);
    }
}

public class RoadGraphRouterTests
{
    private static RoadPoint P(float x, float z) => new(x, 30f, z);

    private static RoadAtlas TShapedRoads()
    {
        var rules = new RoadSamplingRules(1.5f, 8f, 2f);
        var atlas = new RoadAtlas();
        for (float x = 0f; x <= 200f; x += 4f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Dirt, P(x, 0f), rules, out _);
        }

        atlas.EndAllStrokes();
        for (float z = 4f; z <= 200f; z += 4f)
        {
            atlas.RecordSample(RoadObservationSource.Traversal, RoadKind.Paved, P(100f, z), rules, out _);
        }

        atlas.EndAllStrokes();
        return atlas;
    }

    [Fact]
    public void Path_FollowsRoads_AcrossTheJunction()
    {
        RoadAtlas roads = TShapedRoads();

        List<RoadPoint>? path = RoadGraphRouter.FindPath(roads, P(5f, 3f), P(102f, 195f), 15f);

        Assert.NotNull(path);
        // The road path runs along z=0 then up x=100: total ≈ 95 + 195 ≈ 290 m,
        // far longer than the straight line but on roads. Verify it visits the
        // junction area rather than cutting the corner.
        bool visitsJunction = false;
        foreach (RoadPoint point in path!)
        {
            if (Math.Abs(point.X - 100f) < 12f && Math.Abs(point.Z) < 12f)
            {
                visitsJunction = true;
            }
        }

        Assert.True(visitsJunction);
        Assert.Equal(P(5f, 3f), path[0]);
        Assert.Equal(P(102f, 195f), path[path.Count - 1]);
    }

    [Fact]
    public void UnsnappableEndpoints_ReturnNull()
    {
        RoadAtlas roads = TShapedRoads();
        Assert.Null(RoadGraphRouter.FindPath(roads, P(5000f, 5000f), P(0f, 0f), 15f));
        Assert.Null(RoadGraphRouter.FindPath(roads, P(0f, 0f), P(5000f, 5000f), 15f));
    }

    [Fact]
    public void EmptyAtlas_ReturnsNull()
    {
        Assert.Null(RoadGraphRouter.FindPath(new RoadAtlas(), P(0f, 0f), P(10f, 0f), 15f));
    }
}
