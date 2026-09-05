using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

namespace ConcernedTeamster.Tests;

/// <summary>CT-021: the route reader must copy living routes faithfully out
/// of a Cartographer-shaped object graph, resolve the chain fresh on every
/// call, skip malformed rows without losing good ones, refuse torn geometry,
/// and fail closed (false + empty, never a throw) on every structural
/// surprise. Fakes here carry hostile values — nulls, wrong types, throwing
/// getters — that the gate's shape probe would already have rejected, because
/// the reader must stay safe even when the world changes under it.</summary>
public class CartographerRouteReaderTests
{
    // -- fake object graph (member names match the contract) --

    private sealed class FakePoint
    {
        public FakePoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }
    }

    private sealed class FakeId
    {
        public FakeId(Guid value)
        {
            Value = value;
        }

        public Guid Value { get; }
    }

    private sealed class FakeRoute
    {
        public FakeId? Id { get; set; }

        public string? Name { get; set; }

        public bool Archived { get; set; }

        public List<FakePoint?>? Points { get; set; } = new();
    }

    private sealed class FakeRouteStringArchived
    {
        public FakeId? Id { get; set; }

        public string? Name { get; set; }

        public string Archived => "yes";

        public List<FakePoint?> Points { get; } = new();
    }

    private sealed class FakeStore
    {
        private readonly List<object?> _living = new();

        public IEnumerable<object?> Living => _living;

        public long ChangeStamp { get; set; }

        public void Add(object? route)
        {
            _living.Add(route);
        }
    }

    private sealed class FakeStoreThrowingLiving
    {
        public IEnumerable<object?> Living => throw new InvalidOperationException("torn mid-switch");

        public long ChangeStamp => 0L;
    }

    private sealed class FakeRuntimeHolder
    {
        private readonly object? _routeStore;

        public FakeRuntimeHolder(object? store)
        {
            _routeStore = store;
        }
    }

    private sealed class FakePluginHolder
    {
        private readonly object? _runtime;

        public FakePluginHolder(object? runtime)
        {
            _runtime = runtime;
        }
    }

    private static FakePluginHolder PluginWith(object? store)
    {
        return new FakePluginHolder(new FakeRuntimeHolder(store));
    }

    private static FakeRoute Route(Guid id, string? name, bool archived, params FakePoint?[] points)
    {
        var route = new FakeRoute { Id = new FakeId(id), Name = name, Archived = archived };
        route.Points!.AddRange(points);
        return route;
    }

    // -- faithful copies --

    [Fact]
    public void TryReadRoutes_TwoRoutes_CopiedFaithfully()
    {
        Guid idA = Guid.NewGuid();
        Guid idB = Guid.NewGuid();
        var store = new FakeStore();
        store.Add(Route(idA, "Ore run", false, new FakePoint(1f, 2f, 3f), new FakePoint(4f, 5f, 6f)));
        store.Add(Route(idB, "Old pass", true, new FakePoint(-7.5f, 0f, 12.25f)));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Equal(2, routes.Count);
        Assert.Equal(idA, routes[0].Id);
        Assert.Equal("Ore run", routes[0].Name);
        Assert.False(routes[0].Archived);
        Assert.Equal(2, routes[0].Points.Count);
        Assert.Equal(1f, routes[0].Points[0].X);
        Assert.Equal(2f, routes[0].Points[0].Y);
        Assert.Equal(3f, routes[0].Points[0].Z);
        Assert.Equal(idB, routes[1].Id);
        Assert.True(routes[1].Archived);
        Assert.Equal(-7.5f, routes[1].Points[0].X);
        Assert.Equal(12.25f, routes[1].Points[0].Z);
    }

    [Fact]
    public void TryReadRoutes_RouteWithZeroPoints_Kept()
    {
        var store = new FakeStore();
        store.Add(Route(Guid.NewGuid(), "Just created", false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Empty(routes[0].Points);
    }

    [Fact]
    public void TryReadRoutes_EmptyStore_TrueAndEmpty()
    {
        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(new FakeStore()), out var routes);

        Assert.True(readable);
        Assert.Empty(routes);
    }

    [Fact]
    public void TryReadRoutes_NullName_BecomesEmpty()
    {
        var store = new FakeStore();
        store.Add(Route(Guid.NewGuid(), null, false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Equal("", routes[0].Name);
    }

    [Fact]
    public void TryReadRoutes_SnapshotsDoNotAliasLiveGeometry()
    {
        var store = new FakeStore();
        FakeRoute live = Route(Guid.NewGuid(), "Live", false, new FakePoint(1f, 1f, 1f));
        store.Add(live);
        CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        live.Points!.Add(new FakePoint(9f, 9f, 9f));

        Assert.Single(routes[0].Points);
    }

    // -- lifecycle nulls: normal "nothing to read now" states --

    [Fact]
    public void TryReadRoutes_NullPlugin_False()
    {
        Assert.False(CartographerRouteReader.TryReadRoutes(null, out var routes));
        Assert.Empty(routes);
    }

    [Fact]
    public void TryReadRoutes_NullRuntime_False()
    {
        Assert.False(CartographerRouteReader.TryReadRoutes(new FakePluginHolder(null), out var routes));
        Assert.Empty(routes);
    }

    [Fact]
    public void TryReadRoutes_NullStore_False()
    {
        Assert.False(CartographerRouteReader.TryReadRoutes(PluginWith(null), out var routes));
        Assert.Empty(routes);
    }

    [Fact]
    public void TryReadRoutes_StoreWithoutLivingProperty_False()
    {
        Assert.False(CartographerRouteReader.TryReadRoutes(PluginWith(new object()), out var routes));
        Assert.Empty(routes);
    }

    [Fact]
    public void TryReadRoutes_ThrowingLivingGetter_FalseNotThrow()
    {
        bool readable = CartographerRouteReader.TryReadRoutes(
            PluginWith(new FakeStoreThrowingLiving()), out var routes);

        Assert.False(readable);
        Assert.Empty(routes);
    }

    // -- malformed rows are skipped; good rows survive --

    [Fact]
    public void TryReadRoutes_NullRouteEntry_SkippedGoodRouteKept()
    {
        var store = new FakeStore();
        store.Add(null);
        store.Add(Route(Guid.NewGuid(), "Good", false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Equal("Good", routes[0].Name);
    }

    [Fact]
    public void TryReadRoutes_RouteWithNullId_Skipped()
    {
        var store = new FakeStore();
        store.Add(new FakeRoute { Id = null, Name = "No identity" });
        store.Add(Route(Guid.NewGuid(), "Good", false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Equal("Good", routes[0].Name);
    }

    [Fact]
    public void TryReadRoutes_RouteWithNullPointsList_Skipped()
    {
        var store = new FakeStore();
        store.Add(new FakeRoute { Id = new FakeId(Guid.NewGuid()), Name = "No geometry", Points = null });
        store.Add(Route(Guid.NewGuid(), "Good", false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Equal("Good", routes[0].Name);
    }

    [Fact]
    public void TryReadRoutes_RouteWithNullPointEntry_WholeRouteDropped()
    {
        // A hole in the polyline means the geometry cannot be trusted;
        // showing the remaining vertices as a complete route would lie.
        var store = new FakeStore();
        store.Add(Route(Guid.NewGuid(), "Torn", false, new FakePoint(1f, 1f, 1f), null));
        store.Add(Route(Guid.NewGuid(), "Good", false, new FakePoint(2f, 2f, 2f)));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Equal("Good", routes[0].Name);
    }

    [Fact]
    public void TryReadRoutes_RouteWithWrongArchivedType_Skipped()
    {
        var store = new FakeStore();
        store.Add(new FakeRouteStringArchived { Id = new FakeId(Guid.NewGuid()), Name = "Odd shape" });
        store.Add(Route(Guid.NewGuid(), "Good", false));

        bool readable = CartographerRouteReader.TryReadRoutes(PluginWith(store), out var routes);

        Assert.True(readable);
        Assert.Single(routes);
        Assert.Equal("Good", routes[0].Name);
    }

    // -- change stamp --

    [Fact]
    public void TryReadChangeStamp_ReadsValue()
    {
        var store = new FakeStore { ChangeStamp = 42L };

        bool readable = CartographerRouteReader.TryReadChangeStamp(PluginWith(store), out long stamp);

        Assert.True(readable);
        Assert.Equal(42L, stamp);
    }

    [Fact]
    public void TryReadChangeStamp_NoStore_False()
    {
        bool readable = CartographerRouteReader.TryReadChangeStamp(PluginWith(null), out long stamp);

        Assert.False(readable);
        Assert.Equal(0L, stamp);
    }

    [Fact]
    public void TryReadChangeStamp_StoreWithoutStamp_False()
    {
        Assert.False(CartographerRouteReader.TryReadChangeStamp(PluginWith(new object()), out _));
    }
}
