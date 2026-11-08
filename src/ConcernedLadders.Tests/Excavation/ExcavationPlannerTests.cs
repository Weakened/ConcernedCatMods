using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 geometry contract and planner. Measurements here are taken on the engine-equivalent lattice
/// and triangulation (<see cref="VanillaTerrainModel"/>), not on the plan's own rectangle; in-game measurement
/// is OWNER GO-AROUND PENDING.</summary>
public sealed class ExcavationPlannerTests
{
    private const float Tol = ExcavationPlanner.LevelTolerance;

    private static VanillaTerrainModel Flat(float height = 50f) => new((_, _) => height);

    private static ExcavationPlan Plan(VanillaTerrainModel world, ExcavationMode mode, int cx, int cz, float? water = null) =>
        ExcavationPlanner.Plan(world.Sample(mode, cx, cz), cx, cz, water);

    private static void Commit(VanillaTerrainModel world, in ExcavationPlan plan)
    {
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        world.Apply(plan);
        world.Rebuild();
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small, 1, 2f, 2f, 4f, 5, 63.43f)]
    [InlineData((int)ExcavationMode.Large, 2, 4f, 4f, 6f, 7, 75.96f)]
    public void GeometryContractIsTheWorkingAssumption(int modeValue, int half, float depth, float floor,
        float border, int side, float wallDegrees)
    {
        var mode = (ExcavationMode)modeValue;
        ExcavationGeometry geometry = ExcavationGeometry.For(mode);
        Assert.Equal(half, geometry.HalfExtent);
        Assert.Equal(depth, geometry.Depth);
        Assert.Equal(floor, geometry.UsableFloorSize);
        Assert.Equal(depth, geometry.UsableFloorSize); // cubical: width = length = depth
        Assert.Equal(border, geometry.BorderSize);
        Assert.Equal(side, geometry.SampleSide);
        Assert.Equal(wallDegrees, geometry.FlatGroundWallAngleDegrees, 2);
        Assert.Equal((half + 1) * Math.Sqrt(2.0), geometry.AccessCheckRadius, 4);
    }

    [Theory]
    [InlineData(10.49f, 10)]
    [InlineData(10.5f, 11)]
    [InlineData(-0.5f, 0)]
    [InlineData(-0.51f, -1)]
    [InlineData(-1.5f, -1)]
    [InlineData(0f, 0)]
    public void CentreSnapsLikeWorldToVertex(float world, int vertex)
    {
        Assert.Equal(vertex, ExcavationGeometry.SnapToVertex(world));
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small)]
    [InlineData((int)ExcavationMode.Large)]
    public void FlatGroundGivesTheFullFloorAtTheAgreedDepth(int modeValue)
    {
        var mode = (ExcavationMode)modeValue;
        VanillaTerrainModel world = Flat();
        ExcavationPlan plan = Plan(world, mode, 7, -3);
        int half = plan.Geometry.HalfExtent;
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(50f, plan.Datum);
        Assert.Equal(50f - plan.Geometry.Depth, plan.Bottom);
        Assert.Equal((2 * half + 1) * (2 * half + 1), plan.LoweredVertices);
        Assert.Equal(plan.Geometry.Depth, plan.MinWallHeight, 4);
        Assert.Equal(plan.Geometry.Depth, plan.MaxWallHeight, 4);
        Commit(world, plan);

        // Measure the surface the collider is built from, along both axes and the diagonal, at 5 cm steps.
        Assert.Equal(plan.Geometry.UsableFloorSize, FlatExtent(world, plan, alongX: true), 3);
        Assert.Equal(plan.Geometry.UsableFloorSize, FlatExtent(world, plan, alongX: false), 3);
        for (int i = -20 * half; i <= 20 * half; i++)
        {
            float t = i * 0.05f;
            Assert.Equal(plan.Bottom, world.Surface(7 + t, -3 + t), 3);
            Assert.Equal(plan.Bottom, world.Surface(7 + t, -3 - t), 3);
        }

        // The changed region is exactly the border square: outside it the surface is untouched.
        Assert.Equal(plan.Geometry.BorderSize, ChangedExtent(world, plan, 50f), 3);
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small)]
    [InlineData((int)ExcavationMode.Large)]
    public void OnlyFloorVerticesChangeAndNoneRises(int modeValue)
    {
        var mode = (ExcavationMode)modeValue;
        var random = new Random(487);
        int ready = 0;
        for (int trial = 0; trial < 300; trial++)
        {
            float a = (float)random.NextDouble() * 0.6f - 0.3f, b = (float)random.NextDouble() * 0.6f - 0.3f;
            int seed = random.Next();
            var world = new VanillaTerrainModel((x, z) => 40f + a * x + b * z + Noise(seed, x, z));
            if (random.Next(3) == 0) world.Edit(random.Next(-3, 4), random.Next(-3, 4), (float)random.NextDouble() * 6f - 3f);
            ExcavationPlan plan = Plan(world, mode, 0, 0);
            ExcavationSamples before = world.Sample(mode, 0, 0);
            if (plan.Status == ExcavationStatus.Ready) { Commit(world, plan); ready++; }
            int reach = before.Reach;
            for (int dz = -reach - 1; dz <= reach + 1; dz++)
            {
                for (int dx = -reach - 1; dx <= reach + 1; dx++)
                {
                    float was = Math.Abs(dx) <= reach && Math.Abs(dz) <= reach ? before.Height(dx, dz) : world.Resolved(dx, dz);
                    float now = world.Displayed(dx, dz);
                    Assert.True(now <= was + 1e-4f, $"trial {trial}: vertex ({dx},{dz}) rose");
                    bool floor = Math.Abs(dx) <= plan.Geometry.HalfExtent && Math.Abs(dz) <= plan.Geometry.HalfExtent;
                    if (!floor || plan.Status != ExcavationStatus.Ready) { Assert.Equal(was, now, 4); continue; }
                    Assert.Equal(ExcavationPlanner.PlannedHeight(plan, before, dx, dz), now, 3);
                    Assert.True(Math.Abs(plan.Bottom - now) <= Tol, $"trial {trial}: floor vertex ({dx},{dz}) off the floor");
                }
            }
        }
        Assert.True(ready > 100, "the property must be exercised on committed plans, not only refusals");
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small)]
    [InlineData((int)ExcavationMode.Large)]
    public void ACompletedPlanFindsTheSameDatumAndNothingToDig(int modeValue)
    {
        var mode = (ExcavationMode)modeValue;
        var world = new VanillaTerrainModel((x, z) => 50f + 0.2f * x);
        ExcavationPlan first = Plan(world, mode, 0, 0);
        Commit(world, first);
        ExcavationPlan again = Plan(world, mode, 0, 0);
        Assert.Equal(ExcavationStatus.NothingToDig, again.Status);
        Assert.Equal(0, again.LoweredVertices);
        Assert.Equal(first.Datum, again.Datum);
        Assert.Equal(first.Bottom, again.Bottom);
    }

    [Fact]
    public void ASlopeGetsOneFlatFloorMeasuredFromTheHighRim()
    {
        const float grade = 0.3f;
        var world = new VanillaTerrainModel((x, _) => 50f + grade * x);
        ExcavationPlan plan = Plan(world, ExcavationMode.Large, 0, 0);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(50f + grade * 3, plan.Datum, 4);
        Assert.Equal(4f, plan.MaxWallHeight, 4);
        Assert.Equal(4f - grade * 6, plan.MinWallHeight, 4);
        Commit(world, plan);
        Assert.Equal(4f, FlatExtent(world, plan, alongX: true), 3);
        Assert.Equal(4f, FlatExtent(world, plan, alongX: false), 3);
    }

    [Fact]
    public void ASlopeTooSteepForALevelFloorRefusesAndNothingChanges()
    {
        var world = new VanillaTerrainModel((x, _) => 50f + 0.8f * x);
        ExcavationPlan plan = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(ExcavationStatus.Refused, plan.Status);
        Assert.Equal(ExcavationRefusal.FloorAlreadyDeeper, plan.Refusal);
        Assert.Equal(3, plan.RefusalVertices); // the downhill floor column
    }

    [Fact]
    public void GroundAlreadyDeeperThanTheFloorIsNeverRaised()
    {
        VanillaTerrainModel world = Flat();
        world.Edit(1, 0, -3f); // an earlier pickaxe hole inside the floor, deeper than the 2 m plan
        ExcavationPlan plan = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(ExcavationRefusal.FloorAlreadyDeeper, plan.Refusal);
        Assert.Equal(1, plan.RefusalVertices);
    }

    [Fact]
    public void ABumpInsideTheFloorDoesNotMoveTheDatum()
    {
        VanillaTerrainModel world = Flat();
        world.Edit(0, 0, 1.5f);
        ExcavationPlan plan = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(50f, plan.Datum);
        Assert.Equal(3.5f, plan.MaxLowering, 4);
    }

    [Fact]
    public void ThePlanRefusesWhereTheEngineWouldClampAndTheClampIsReal()
    {
        VanillaTerrainModel world = Flat();
        for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++) world.Edit(x, z, -5f);
        ExcavationPlan large = Plan(world, ExcavationMode.Large, 0, 0); // 45 - 4 = 41, 9 m below the 50 m base
        Assert.Equal(ExcavationRefusal.DigLimit, large.Refusal);
        Assert.Equal(25, large.RefusalVertices);
        Assert.Equal(ExcavationStatus.Ready, Plan(world, ExcavationMode.Small, 0, 0).Status); // 7 m below: allowed

        // Were it issued anyway, the engine would stop at 8 m: a 3 m floor where the preview promised 4.
        world.SquareRaise(0f, large.OperationY, 0f, large.OperationRadius, large.OperationDelta);
        world.Rebuild();
        Assert.Equal(42f, world.Displayed(0, 0), 4);
        Assert.NotEqual(large.Bottom, world.Displayed(0, 0));
    }

    [Fact]
    public void AFloorWithinACentimetreOfTheDigLimitRefuses()
    {
        VanillaTerrainModel world = Flat();
        for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++) world.Edit(x, z, -4f);
        Assert.Equal(ExcavationRefusal.DigLimit, Plan(world, ExcavationMode.Large, 0, 0).Refusal); // exactly 8 m
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small, 2)]
    [InlineData((int)ExcavationMode.Large, 4)]
    public void AdjacentPlansOfOneModeJoinWithoutARidge(int modeValue, int step)
    {
        var mode = (ExcavationMode)modeValue;
        VanillaTerrainModel world = Flat();
        ExcavationPlan a = Plan(world, mode, 0, 0);
        Commit(world, a);
        ExcavationPlan b = Plan(world, mode, step, 0);
        Assert.Equal(a.Datum, b.Datum);
        Commit(world, b);
        int half = a.Geometry.HalfExtent;
        for (int i = -20 * half; i <= 20 * (step + half); i++)
        {
            for (int j = -4 * half; j <= 4 * half; j++) Assert.Equal(a.Bottom, world.Surface(i * 0.05f, j * 0.25f), 3);
        }
    }

    [Fact]
    public void AShallowPlanReachingIntoADeeperPitRefuses()
    {
        VanillaTerrainModel world = Flat();
        Commit(world, Plan(world, ExcavationMode.Large, 0, 0));
        ExcavationPlan shallow = Plan(world, ExcavationMode.Small, 3, 0);
        Assert.Equal(ExcavationRefusal.FloorAlreadyDeeper, shallow.Refusal);
        Assert.Equal(3, shallow.RefusalVertices);
    }

    [Fact]
    public void APlanWhollyInsideALargerPitIsAVisibleNewDepth()
    {
        VanillaTerrainModel world = Flat();
        Commit(world, Plan(world, ExcavationMode.Large, 0, 0));
        ExcavationPlan sub = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(ExcavationStatus.Ready, sub.Status);
        Assert.Equal(46f, sub.Datum, 4); // its whole ring is the larger pit's floor
        Assert.Equal(44f, sub.Bottom, 4);
    }

    [Fact]
    public void AnUnloadedOrUnreadableVertexRefuses()
    {
        VanillaTerrainModel world = Flat();
        world.MarkUnloaded(2, 2); // a ring corner of the small plan
        ExcavationPlan plan = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(ExcavationRefusal.Unloaded, plan.Refusal);
        Assert.Equal(1, plan.RefusalVertices);

        var nan = new VanillaTerrainModel((x, z) => x == 0 && z == 1 ? float.NaN : 50f);
        Assert.Equal(ExcavationRefusal.NotFinite, Plan(nan, ExcavationMode.Small, 0, 0).Refusal);
    }

    [Theory]
    [InlineData(31f, 30f, true, 1f)]
    [InlineData(32f, 30f, false, 0f)]
    [InlineData(40f, null, false, 0f)]
    public void WaterAboveTheFloorIsReportedNotHidden(float surface, float? water, bool floods, float depth)
    {
        ExcavationPlan plan = Plan(Flat(surface), ExcavationMode.Small, 0, 0, water);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(floods, plan.Floods);
        if (floods) Assert.Equal(depth, plan.WaterDepth, 4);
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small, 0, 0, 1)]
    [InlineData((int)ExcavationMode.Small, 30, 0, 1)]
    [InlineData((int)ExcavationMode.Small, 31, 0, 2)] // floor x 30..32, and x = 32 is the tile edge
    [InlineData((int)ExcavationMode.Small, 33, 0, 2)]
    [InlineData((int)ExcavationMode.Small, 34, 0, 1)]
    [InlineData((int)ExcavationMode.Small, -33, 0, 2)]
    [InlineData((int)ExcavationMode.Large, 32, 32, 4)] // four-tile corner
    [InlineData((int)ExcavationMode.Large, 30, 96, 4)]
    public void TilesHoldingAFloorVertexAreCountedWithSharedEdges(int modeValue, int cx, int cz, int tiles)
    {
        var mode = (ExcavationMode)modeValue;
        ExcavationPlan plan = Plan(Flat(), mode, cx, cz);
        Assert.Equal(tiles, plan.TileCount);
    }

    [Fact]
    public void TileRangeMatchesZoneSystemGetZone()
    {
        ExcavationPlanner.TileRange(30, 32, out int first, out int count);
        Assert.Equal((0, 2), (first, count));
        ExcavationPlanner.TileRange(-34, -32, out first, out count);
        Assert.Equal((-1, 2), (first, count));
        ExcavationPlanner.TileRange(-31, 31, out first, out count);
        Assert.Equal((0, 1), (first, count));
        ExcavationPlanner.TileRange(96, 98, out first, out count);
        Assert.Equal((1, 2), (first, count));
    }

    [Fact]
    public void ProgressIsPendingThenCompleteAndAStaleSecondOperationOvershoots()
    {
        VanillaTerrainModel world = Flat();
        ExcavationPlan plan = Plan(world, ExcavationMode.Small, 0, 0);
        Assert.Equal(ExcavationProgress.Pending, ExcavationPlanner.Check(plan, world.Sample(ExcavationMode.Small, 0, 0)));

        // Retrying after the rebuild changes nothing: the target is absolute.
        world.Apply(plan);
        world.Rebuild();
        world.Apply(plan);
        world.Rebuild();
        Assert.Equal(ExcavationProgress.Complete, ExcavationPlanner.Check(plan, world.Sample(ExcavationMode.Small, 0, 0)));

        // A second operation before the heightmap rebuilds reads the stale heights and digs twice. That is an engine
        // property (the pickaxe has it too), so the runtime keeps one operation in flight and never retries blind.
        VanillaTerrainModel stale = Flat();
        ExcavationPlan twice = Plan(stale, ExcavationMode.Small, 0, 0);
        stale.Apply(twice);
        stale.Apply(twice);
        stale.Rebuild();
        Assert.Equal(46f, stale.Displayed(0, 0), 4);
        Assert.Equal(ExcavationProgress.Overshot, ExcavationPlanner.Check(twice, stale.Sample(ExcavationMode.Small, 0, 0)));

        stale.MarkUnloaded(0, 0);
        Assert.Equal(ExcavationProgress.Unreadable, ExcavationPlanner.Check(twice, stale.Sample(ExcavationMode.Small, 0, 0)));
    }

    [Fact]
    public void ARefusedOrIdlePlanPlansNoChange()
    {
        VanillaTerrainModel world = Flat();
        world.Edit(0, 0, -3f);
        ExcavationSamples samples = world.Sample(ExcavationMode.Small, 0, 0);
        ExcavationPlan refused = ExcavationPlanner.Plan(samples, 0, 0, null);
        Assert.Equal(ExcavationStatus.Refused, refused.Status);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            Assert.Equal(samples.Height(dx, dz), ExcavationPlanner.PlannedHeight(refused, samples, dx, dz));
    }

    [Fact]
    public void ARefilledGridReplansWithoutStaleVertices()
    {
        var samples = new ExcavationSamples(ExcavationMode.Small);
        Assert.Equal(ExcavationRefusal.Unloaded, ExcavationPlanner.Plan(samples, 0, 0, null).Refusal);
        for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) samples.Set(dx, dz, 50f, 50f);
        Assert.Equal(ExcavationStatus.Ready, ExcavationPlanner.Plan(samples, 0, 0, null).Status);
        samples.Clear();
        Assert.Equal(ExcavationRefusal.Unloaded, ExcavationPlanner.Plan(samples, 0, 0, null).Refusal);
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Set(3, 0, 50f, 50f));
    }

    /// <summary>Length of the run, through the plan's centre, where the measured surface is at the floor.</summary>
    private static float FlatExtent(VanillaTerrainModel world, in ExcavationPlan plan, bool alongX)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = -120; i <= 120; i++)
        {
            float t = i * 0.05f;
            float x = plan.CentreX + (alongX ? t : 0f), z = plan.CentreZ + (alongX ? 0f : t);
            if (Math.Abs(world.Surface(x, z) - plan.Bottom) > Tol) continue;
            min = Math.Min(min, t);
            max = Math.Max(max, t);
        }
        return max - min;
    }

    /// <summary>Width of the region, through the centre, where the surface differs from the original flat height.</summary>
    private static float ChangedExtent(VanillaTerrainModel world, in ExcavationPlan plan, float original)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = -120; i <= 120; i++)
        {
            float t = i * 0.05f;
            if (Math.Abs(world.Surface(plan.CentreX + t, plan.CentreZ) - original) < 1e-3f) continue;
            min = Math.Min(min, t);
            max = Math.Max(max, t);
        }
        // The outermost changed samples lie one step inside the untouched ring on each side.
        return max - min + 0.1f;
    }

    private static float Noise(int seed, int x, int z)
    {
        unchecked
        {
            int h = seed ^ (x * 73856093) ^ (z * 19349663);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h & 0xFFFF) / 65535f - 0.5f) * 0.8f;
        }
    }
}
