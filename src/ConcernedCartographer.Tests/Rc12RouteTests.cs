using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>RC12 blocker 2: the route list mirrors the store LIVE. The
/// store exposes a monotonic change stamp the panel polls per frame, and
/// erasing the last of a route's ink tombstones the route instead of
/// leaving a ghost zero-point row.</summary>
public class RouteListLivenessTests
{
    [Fact]
    public void ChangeStamp_BumpsOnEveryPublishedChange()
    {
        var store = new RouteStore();
        long start = store.ChangeStamp;

        AtlasRoute route = store.Create(created => created.Name = "Trail");
        Assert.Equal(start + 1, store.ChangeStamp);

        store.Mutate(route.Id, edited => edited.Name = "Renamed");
        Assert.Equal(start + 2, store.ChangeStamp);

        store.Delete(route.Id);
        Assert.Equal(start + 3, store.ChangeStamp);

        store.Restore(route.Id);
        Assert.Equal(start + 4, store.ChangeStamp);

        AtlasRoute newer = route.Clone();
        newer.Revision = route.Revision + 1;
        newer.Name = "Synced";
        Assert.True(store.Upsert(newer));
        Assert.Equal(start + 5, store.ChangeStamp);
    }

    [Fact]
    public void ChangeStamp_DoesNotBumpOnRejectedChanges()
    {
        var store = new RouteStore();
        AtlasRoute route = store.Create();
        long stamp = store.ChangeStamp;

        Assert.False(store.Mutate(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()), _ => { }));
        Assert.False(store.Delete(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())));

        AtlasRoute stale = route.Clone();
        stale.Revision = route.Revision; // not higher: rejected
        Assert.False(store.Upsert(stale));

        Assert.Equal(stamp, store.ChangeStamp);
    }

    [Fact]
    public void EraseNear_FullCoverage_TombstonesTheRoute()
    {
        var store = new RouteStore();
        var operations = new RouteOperations(store);
        AtlasRoute route = operations.StartRoute(RouteKind.Freehand, "Doomed");
        operations.AppendPoint(route.Id, new RoadPoint(0f, 0f, 0f));
        operations.AppendPoint(route.Id, new RoadPoint(5f, 0f, 0f));

        int removed = operations.EraseNear(route.Id, new RoadPoint(2.5f, 0f, 0f), 10f, out List<AtlasRoute> created);

        Assert.Equal(2, removed);
        Assert.Empty(created);
        Assert.True(store.TryGet(route.Id, out AtlasRoute erased));
        Assert.True(erased.Deleted);
        Assert.DoesNotContain(erased, store.Living);
    }

    [Fact]
    public void EraseNear_FullCoverage_UndoRestoresInkAndLiveness()
    {
        var store = new RouteStore();
        var operations = new RouteOperations(store);
        AtlasRoute route = operations.StartRoute(RouteKind.Freehand, "Comeback");
        operations.AppendPoint(route.Id, new RoadPoint(0f, 0f, 0f));
        operations.AppendPoint(route.Id, new RoadPoint(5f, 0f, 0f));

        operations.EraseNear(route.Id, new RoadPoint(2.5f, 0f, 0f), 10f, out _);
        Assert.True(operations.Undo(out _));

        Assert.True(store.TryGet(route.Id, out AtlasRoute restored));
        Assert.False(restored.Deleted);
        Assert.Equal(2, restored.Points.Count);
    }

    [Fact]
    public void EraseNear_PartialCoverage_KeepsTheRouteLiving()
    {
        var store = new RouteStore();
        var operations = new RouteOperations(store);
        AtlasRoute route = operations.StartRoute(RouteKind.Freehand, "Survivor");
        operations.AppendPoint(route.Id, new RoadPoint(0f, 0f, 0f));
        operations.AppendPoint(route.Id, new RoadPoint(5f, 0f, 0f));
        operations.AppendPoint(route.Id, new RoadPoint(100f, 0f, 0f));

        int removed = operations.EraseNear(route.Id, new RoadPoint(0f, 0f, 0f), 8f, out _);

        Assert.Equal(2, removed);
        Assert.True(store.TryGet(route.Id, out AtlasRoute survivor));
        Assert.False(survivor.Deleted);
        Assert.Single(survivor.Points);
    }
}

/// <summary>RC12 blocker 3: the dash/dot walkers must terminate quickly on
/// ANY input — huge coordinates, non-finite geometry, tiny cadences — and
/// honor their stamp budgets structurally. Before this pass, a segment
/// long enough that float subtraction stalls spun the dot loop forever
/// (a hard game freeze), and dashes with int.MaxValue budgets could walk
/// for seconds.</summary>
public class RoutePatternHardeningTests
{
    private static List<(float X, float Y)> Points(params (float X, float Y)[] points)
    {
        return new List<(float X, float Y)>(points);
    }

    [Fact]
    public void Dots_HugeFiniteSegment_TerminatesAtTheBudget()
    {
        // 1e9 length: the old float countdown stalled (1e9 - 3 == 1e9 in
        // float32) and never exited. The integer restructure must return
        // exactly the budget.
        int stamps = RoutePatternMath.WalkDots(
            Points((0f, 0f), (1e9f, 0f)), spacing: 3f, maxStamps: 50, (_, _) => { });

        Assert.Equal(50, stamps);
    }

    [Fact]
    public void Dashes_HugeFiniteSegment_TerminatesAtTheBudget()
    {
        int stamps = RoutePatternMath.WalkDashes(
            Points((0f, 0f), (1e9f, 0f)), dashOn: 5f, dashOff: 4f, maxStamps: 50, (_, _, _, _) => { });

        Assert.Equal(50, stamps);
    }

    [Fact]
    public void NonFiniteSegments_AreSkipped_AndTheWalkContinues()
    {
        var dots = new List<float>();
        int stamps = RoutePatternMath.WalkDots(
            Points((0f, 0f), (float.PositiveInfinity, 0f), (float.NaN, 0f), (0f, 0f), (9f, 0f)),
            spacing: 9f, maxStamps: 100, (x, _) => dots.Add(x));

        // Only the final finite 9-unit segment stamps (at 0 and 9).
        Assert.Equal(2, stamps);
        Assert.Equal(0f, dots[0], 2);
        Assert.Equal(9f, dots[1], 2);

        int dashStamps = RoutePatternMath.WalkDashes(
            Points((0f, 0f), (float.PositiveInfinity, 0f)), 5f, 4f, 100, (_, _, _, _) => { });
        Assert.Equal(0, dashStamps);
    }

    [Fact]
    public void NonFiniteCadences_StampNothing()
    {
        Assert.Equal(0, RoutePatternMath.WalkDots(
            Points((0f, 0f), (10f, 0f)), float.PositiveInfinity, 100, (_, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDots(
            Points((0f, 0f), (10f, 0f)), float.NaN, 100, (_, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDashes(
            Points((0f, 0f), (10f, 0f)), float.NaN, 4f, 100, (_, _, _, _) => { }));
        Assert.Equal(0, RoutePatternMath.WalkDashes(
            Points((0f, 0f), (10f, 0f)), 5f, float.PositiveInfinity, 100, (_, _, _, _) => { }));
    }

    [Fact]
    public void TinySpacing_IsBoundedByTheBudget_NotByTheGeometry()
    {
        int stamps = RoutePatternMath.WalkDots(
            Points((0f, 0f), (1000f, 0f)), spacing: 1e-4f, maxStamps: 200, (_, _) => { });

        Assert.Equal(200, stamps);
    }
}
