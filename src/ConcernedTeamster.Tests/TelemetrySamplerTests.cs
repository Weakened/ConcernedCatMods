using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace ConcernedTeamster.Tests;

/// <summary>CT-003: the sampler must honor its interval and per-tick attempt
/// budget, round-robin so no nearby cart starves, cap the store, evict stale
/// entries (the destroyed-cart path), reset completely (the world-switch
/// path), and allocate nothing in steady state.</summary>
public class TelemetrySamplerTests
{
    /// <summary>A scripted world: carts appear as ids, collection and
    /// sampling are counted, and any id can be made unreadable (null sample)
    /// or removed (destroyed).</summary>
    private sealed class FakeCartWorld
    {
        public readonly List<string> NearbyCartIds = new();
        public readonly HashSet<string> Unreadable = new();
        public int CollectCalls;
        public int SampleAttempts;
        public readonly List<string> AttemptLog = new();

        public void Collect(List<object> buffer, TelemetrySamplerOptions options)
        {
            // Deliberately ignores the options cap: the sampler's own store
            // guard must hold even against an over-filling collector.
            CollectCalls++;
            for (int index = 0; index < NearbyCartIds.Count; index++)
            {
                buffer.Add(NearbyCartIds[index]);
            }
        }

        public IReadOnlyDictionary<string, CartTelemetry>? LastSeenPreviousStore;

        public CartTelemetry? Sample(
            object cart, double nowSeconds, IReadOnlyDictionary<string, CartTelemetry> previousByCartId)
        {
            SampleAttempts++;
            LastSeenPreviousStore = previousByCartId;
            string cartId = (string)cart;
            AttemptLog.Add(cartId);
            if (Unreadable.Contains(cartId))
            {
                return null;
            }

            CartSnapshot snapshot = CartSnapshot.Create(
                cartId, baseMass: 20f, cargoWeight: 10f, cargoDataAvailable: true,
                itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: false);
            return CartTelemetry.Create(snapshot, velocityAvailable: true,
                speedMetersPerSecond: 1f, verticalSpeedMetersPerSecond: 0f,
                gradeAvailable: false, instantGradePercent: 0f, smoothedGradePercent: 0f,
                gradeDirection: TheConcernedCat.ConcernedTeamster.Domain.Terrain.GradeDirection.Level,
                surface: TheConcernedCat.ConcernedTeamster.Domain.Terrain.TerrainSurfaceKind.Unavailable,
                sampleTimeSeconds: nowSeconds);
        }
    }

    private static TelemetrySampler CreateSampler(
        FakeCartWorld world, float interval = 0.5f, int perTick = 2, int tracked = 8)
    {
        return new TelemetrySampler(
            TelemetrySamplerOptions.CreateClamped(interval, 30f, perTick, tracked),
            world.Collect,
            world.Sample);
    }

    [Fact]
    public void Tick_BeforeInterval_DoesNothing()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);

        Assert.True(sampler.Tick(10.0));
        Assert.False(sampler.Tick(10.2));
        Assert.False(sampler.Tick(10.49));

        Assert.Equal(1, world.CollectCalls);
        Assert.Equal(1, world.SampleAttempts);
    }

    [Fact]
    public void Tick_AtInterval_RunsAgain()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);

        Assert.True(sampler.Tick(10.0));
        Assert.True(sampler.Tick(10.5));
        Assert.Equal(2, world.CollectCalls);
    }

    [Fact]
    public void Tick_BudgetBoundsAttemptsNotSuccesses()
    {
        var world = new FakeCartWorld
        {
            NearbyCartIds = { "1:1", "1:2", "1:3", "1:4", "1:5" },
            Unreadable = { "1:1", "1:2" },
        };
        TelemetrySampler sampler = CreateSampler(world, perTick: 2);

        sampler.Tick(0.0);

        // Two attempts were made even though both failed: failing carts must
        // never widen the per-tick budget.
        Assert.Equal(2, world.SampleAttempts);
        Assert.Equal(0, sampler.SampledOnLastDueTick);
        Assert.Equal(0, sampler.TrackedCartCount);
    }

    [Fact]
    public void Tick_RoundRobin_ReachesEveryCartAcrossTicks()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1", "1:2", "1:3" } };
        TelemetrySampler sampler = CreateSampler(world, perTick: 1);

        sampler.Tick(0.0);
        sampler.Tick(0.5);
        sampler.Tick(1.0);

        Assert.Equal(new[] { "1:1", "1:2", "1:3" }, world.AttemptLog);
        Assert.Equal(3, sampler.TrackedCartCount);
    }

    [Fact]
    public void Tick_StoreCapIgnoresNewCartsButUpdatesKnownOnes()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1", "1:2", "1:3" } };
        TelemetrySampler sampler = CreateSampler(world, perTick: 8, tracked: 2);

        sampler.Tick(0.0);

        Assert.Equal(2, sampler.TrackedCartCount);
        Assert.True(sampler.TelemetryByCartId.ContainsKey("1:1"));
        Assert.True(sampler.TelemetryByCartId.ContainsKey("1:2"));

        // Known carts keep refreshing while the cap holds the line.
        sampler.Tick(0.5);
        Assert.Equal(2, sampler.TrackedCartCount);
        Assert.Equal(0.5d, sampler.TelemetryByCartId["1:2"].SampleTimeSeconds);
    }

    [Fact]
    public void Tick_DestroyedCartGoesStaleAndEvicts()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1", "1:2" } };
        TelemetrySampler sampler = CreateSampler(world, perTick: 8);

        sampler.Tick(0.0);
        Assert.Equal(2, sampler.TrackedCartCount);

        // Cart 1:2 is destroyed: it stops being collected, so nothing
        // refreshes it, and after the eviction window it disappears while the
        // surviving cart stays fresh.
        world.NearbyCartIds.Remove("1:2");
        sampler.Tick(0.5);
        sampler.Tick(1.0);
        Assert.True(sampler.TelemetryByCartId.ContainsKey("1:2"));

        sampler.Tick(2.6);
        Assert.False(sampler.TelemetryByCartId.ContainsKey("1:2"));
        Assert.True(sampler.TelemetryByCartId.ContainsKey("1:1"));
    }

    [Fact]
    public void Reset_ClearsEverythingAndSamplesImmediately()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);

        sampler.Tick(100.0);
        Assert.Equal(1, sampler.TrackedCartCount);

        // World switch: everything forgotten, and the next tick is due
        // immediately even though the interval has not elapsed.
        sampler.Reset();
        Assert.Equal(0, sampler.TrackedCartCount);
        Assert.Equal(0, sampler.SampledOnLastDueTick);
        Assert.True(sampler.Tick(100.1));
        Assert.Equal(1, sampler.TrackedCartCount);
    }

    [Fact]
    public void Tick_LatestSampleWinsInTheView()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);

        sampler.Tick(0.0);
        sampler.Tick(0.5);

        Assert.Equal(0.5d, sampler.TelemetryByCartId["1:1"].SampleTimeSeconds);
    }

    [Fact]
    public void Tick_PassesTheLiveStoreToTheSampleCallback()
    {
        // CT-004 grade smoothing reads the previous sample from the store;
        // the sampler must hand its own live dictionary to the callback.
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);

        sampler.Tick(0.0);

        Assert.Same(sampler.TelemetryByCartId, world.LastSeenPreviousStore);
    }

    [Fact]
    public void Tick_NotDueFastPath_AllocatesNothing()
    {
        var world = new FakeCartWorld { NearbyCartIds = { "1:1" } };
        TelemetrySampler sampler = CreateSampler(world);
        sampler.Tick(0.0);

        // Warm up the code path, then measure: 200k not-due ticks must not
        // allocate a single byte on this thread.
        sampler.Tick(0.1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200_000; index++)
        {
            sampler.Tick(0.2);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void Tick_DueTicksWithEmptyWorld_AllocateNothing()
    {
        var world = new FakeCartWorld();
        TelemetrySampler sampler = CreateSampler(world, interval: 0.1f);
        sampler.Tick(0.0);
        sampler.Tick(1.0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        double now = 2.0;
        for (int index = 0; index < 10_000; index++)
        {
            now += 0.11;
            sampler.Tick(now);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }
}
