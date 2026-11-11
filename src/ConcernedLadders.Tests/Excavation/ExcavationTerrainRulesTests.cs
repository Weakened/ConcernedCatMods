using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 2: the rules the terrain adapter applies to what it reads - the live lattice check, the
/// dig-limit base from the compiler's deltas, and the agreement of a vertex shared by two tiles. In-game reading is
/// OWNER GO-AROUND PENDING.</summary>
public sealed class ExcavationTerrainRulesTests
{
    [Theory]
    [InlineData(64, 1f, 0f, 0f, true)]
    [InlineData(64, 1f, 64f, -128f, true)]
    [InlineData(64, 1.00005f, 0f, 0f, true)]
    [InlineData(64, 2f, 0f, 0f, false)]
    [InlineData(64, 0.5f, 0f, 0f, false)]
    [InlineData(63, 1f, 0f, 0f, false)]
    [InlineData(32, 1f, 0f, 0f, false)]
    [InlineData(128, 1f, 0f, 0f, false)]
    [InlineData(0, 1f, 0f, 0f, false)]
    [InlineData(-64, 1f, 0f, 0f, false)]
    [InlineData(64, 1f, 0.5f, 0f, false)]
    [InlineData(64, 1f, 0f, 63.9f, false)]
    [InlineData(64, float.NaN, 0f, 0f, false)]
    [InlineData(64, 1f, float.PositiveInfinity, 0f, false)]
    public void OnlyTheWholeMetreOneMetreLatticeIsSupported(int width, float scale, float x, float z, bool supported)
    {
        Assert.Equal(supported, ExcavationTerrainRules.LatticeSupported(width, scale, x, z));
    }

    [Theory]
    [InlineData(50f, 0f, 0f, 50f)]
    [InlineData(47f, -3f, 0f, 50f)]
    [InlineData(50.4f, 0f, 0.4f, 50f)]
    [InlineData(49.6f, 0f, -0.4f, 50f)]
    [InlineData(44.5f, -6f, 0.5f, 50f)]
    [InlineData(57f, 7f, 0f, 50f)]
    public void LimitBaseIsTheHeightBeforeTheCompiler(float height, float level, float smooth, float expected)
    {
        Assert.Equal(expected, ExcavationTerrainRules.LimitBase(height, level, smooth), 4);
    }

    [Theory]
    [InlineData(-8f, 0f)]
    [InlineData(-7.5f, -0.5f)]
    [InlineData(-7.995f, 0f)]
    [InlineData(8f, 0f)]
    [InlineData(7.5f, 0.5f)]
    [InlineData(-8f, 1f)] // -7 is inside the clamp: not NaN
    public void LimitBaseIsUnknownWhereTheFinalClampMayHaveBound(float level, float smooth)
    {
        float result = ExcavationTerrainRules.LimitBase(40f, level, smooth);
        bool atClamp = Math.Abs(level + smooth) >= ExcavationTerrainRules.EngineClamp - ExcavationTerrainRules.FinalClampMargin;
        Assert.Equal(atClamp, float.IsNaN(result));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(50f, float.NaN, 0f)]
    [InlineData(50f, 0f, float.PositiveInfinity)]
    public void LimitBaseOfAnUnreadableNumberIsUnknown(float height, float level, float smooth)
    {
        Assert.True(float.IsNaN(ExcavationTerrainRules.LimitBase(height, level, smooth)));
    }

    /// <summary>TerrainComp.RaiseTerrain (square, lowering) then ApplyToHeightmap, for one vertex with no player
    /// TerrainModifier (so the final clamp's base is the pre-compiler height), transcribed from the decompiled game.</summary>
    private static float EngineLowers(float preCompiler, float level, float smooth, float target)
    {
        float height = Math.Max(preCompiler - 8f, Math.Min(preCompiler + 8f, preCompiler + level + smooth));
        if (target > height) return height;
        float newLevel = Math.Max(-8f, Math.Min(8f, level + target - height + smooth));
        float value = preCompiler + newLevel; // smooth delta is zeroed by the raise
        return Math.Max(preCompiler - 8f, Math.Min(preCompiler + 8f, value));
    }

    [Theory]
    [InlineData(50f, -6f, 0f)]
    [InlineData(50f, -6f, -0.6f)]
    [InlineData(50f, -2f, 0.9f)]
    [InlineData(50f, 3f, -0.9f)]
    [InlineData(50f, 0f, 0f)]
    public void ThePlannerLimitFromLimitBaseIsExactlyWhereTheEngineStopsLowering(float preCompiler, float level, float smooth)
    {
        float height = preCompiler + level + smooth;
        float limitBase = ExcavationTerrainRules.LimitBase(height, level, smooth);
        float deepest = limitBase - ExcavationPlanner.MaximumDigBelowBase;
        // A target the limit admits is reached exactly; one past it is clamped short.
        float admitted = deepest + ExcavationPlanner.DigLimitMargin;
        if (admitted <= height) Assert.Equal(admitted, EngineLowers(preCompiler, level, smooth, admitted), 3);
        float beyond = deepest - 0.25f;
        Assert.True(EngineLowers(preCompiler, level, smooth, beyond) > beyond + 0.2f);
    }

    [Fact]
    public void IgnoringANegativeSmoothDeltaWouldAdmitAFloorTheEngineClamps()
    {
        // Pre-compiler 50, a smoothing pass left the surface at 49.6. The engine stops at 42.
        const float pre = 50f, level = 0f, smooth = -0.4f;
        float height = pre + level + smooth;
        float naive = height - level; // without the smooth delta: 49.6, limit 41.6
        float target = 41.8f;
        Assert.True(target >= naive - ExcavationPlanner.MaximumDigBelowBase + ExcavationPlanner.DigLimitMargin);
        Assert.Equal(42f, EngineLowers(pre, level, smooth, target), 3);
        Assert.True(target < ExcavationTerrainRules.LimitBase(height, level, smooth) - ExcavationPlanner.MaximumDigBelowBase);
    }

    [Fact]
    public void AnAreaAlreadyLoweredRefusesTheDigLimitThatItsCurrentHeightWouldHide()
    {
        // Base 50 everywhere, an earlier tool lowered the whole area by 6 m (level delta -6): the surface is 44.
        var world = new VanillaTerrainModel((_, _) => 50f);
        for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++) world.Edit(x, z, -6f);
        DigFootprint footprint = DigFootprint.FromCorners(-2, -2, 2, 2, 4);
        DigSamples read = ReadLikeTheAdapter(world, footprint);
        DigPlan plan = DigPlanner.Plan(read, footprint, null);
        Assert.Equal(ExcavationStatus.Refused, plan.Status);
        Assert.Equal(DigRefusal.DigLimit, plan.Refusal);

        // Reading the current height as the base (the mistake) lets the plan through, and the engine then stops 2 m
        // short of the floor it promised: a partial floor.
        DigSamples naive = world.Sample(footprint);
        for (int z = -3; z <= 3; z++) for (int x = -3; x <= 3; x++) naive.Set(x, z, naive.Height(x, z), naive.Height(x, z));
        DigPlan wrong = DigPlanner.Plan(naive, footprint, null);
        Assert.Equal(ExcavationStatus.Ready, wrong.Status);
        world.Apply(DigFlightKind.Dig, new DigRecord(footprint, DigPlanner.Lowered(wrong, naive)));
        world.Rebuild();
        Assert.Equal(42f, world.Displayed(0, 0), 3);
        Assert.Equal(40f, wrong.Bottom, 3);
    }

    /// <summary>What the adapter writes: the displayed height, and the limit base from the level delta the model keeps.</summary>
    private static DigSamples ReadLikeTheAdapter(VanillaTerrainModel world, DigFootprint footprint)
    {
        var samples = new DigSamples();
        samples.Begin(footprint);
        for (int z = footprint.MinZ - 1; z <= footprint.MaxZ + 1; z++)
        {
            for (int x = footprint.MinX - 1; x <= footprint.MaxX + 1; x++)
            {
                float height = world.Displayed(x, z);
                float level = height - world.Base(x, z);
                var vertex = new ExcavationVertexRead();
                vertex.Add(height, ExcavationTerrainRules.LimitBase(height, level, 0f));
                vertex.WriteTo(samples, x, z);
            }
        }
        return samples;
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(31, -31, 1)]
    [InlineData(32, 0, 2)]
    [InlineData(-32, 5, 2)]
    [InlineData(7, 96, 2)]
    [InlineData(32, 32, 4)]
    [InlineData(-96, 160, 4)]
    [InlineData(64, 64, 1)]
    public void TileEdgeVerticesBelongToTwoTilesAndCornersToFour(int x, int z, int copies)
    {
        Assert.Equal(copies, ExcavationTerrainRules.ExpectedCopies(x, z));
    }

    [Fact]
    public void AnEdgeVertexMissingOneOfItsTilesIsUnloadedNotASeam()
    {
        var samples = new DigSamples();
        samples.Begin(DigFootprint.FromCorners(0, 0, 2, 2, 2));
        var vertex = new ExcavationVertexRead();
        vertex.Add(48f, 50f);
        vertex.WriteTo(samples, 1, 0, expectedCopies: 2);
        Assert.False(samples.IsLoaded(1, 0));
        vertex.Add(48f, 50f);
        vertex.WriteTo(samples, 1, 0, expectedCopies: 2);
        Assert.True(samples.IsLoaded(1, 0));
    }

    [Fact]
    public void DifferingHeightsAreToldApartFromUnreadableOnes()
    {
        var differ = new ExcavationVertexRead();
        differ.Add(48f, 50f);
        differ.Add(46f, 50f);
        Assert.True(differ.HeightsDiffer);
        var unreadable = new ExcavationVertexRead();
        unreadable.Add(48f, 50f);
        unreadable.Add(48f, float.NaN);
        Assert.False(unreadable.HeightsDiffer);
        Assert.True(float.IsNaN(unreadable.Height));
    }

    [Fact]
    public void AVertexNoTileHoldsIsUnloaded()
    {
        var samples = new DigSamples();
        samples.Begin(DigFootprint.FromCorners(0, 0, 2, 2, 2));
        samples.Set(0, 0, 1f, 1f);
        new ExcavationVertexRead().WriteTo(samples, 0, 0);
        Assert.False(samples.IsLoaded(0, 0));
    }

    [Fact]
    public void ASharedEdgeVertexTakesTheStrictestTilesLimit()
    {
        var vertex = new ExcavationVertexRead();
        vertex.Add(48f, 50f);
        vertex.Add(48.01f, 52f);
        Assert.Equal(2, vertex.Copies);
        Assert.Equal(48f, vertex.Height, 4);
        Assert.Equal(52f, vertex.LimitBase, 4);
    }

    [Theory]
    [InlineData(48f, 50f, 48.5f, 50f)]
    [InlineData(48f, 50f, 48f, float.NaN)]
    [InlineData(float.NaN, 50f, 48f, 50f)]
    public void TilesThatDisagreeOrCannotBeReadRefuseThePlan(float h1, float b1, float h2, float b2)
    {
        var vertex = new ExcavationVertexRead();
        vertex.Add(h1, b1);
        vertex.Add(h2, b2);
        Assert.True(float.IsNaN(vertex.Height));
        Assert.True(float.IsNaN(vertex.LimitBase));

        DigFootprint footprint = DigFootprint.FromCorners(-1, -1, 1, 1, 2);
        var samples = new DigSamples();
        samples.Begin(footprint);
        for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++) samples.Set(x, z, 50f, 50f);
        vertex.WriteTo(samples, 1, 0);
        DigPlan plan = DigPlanner.Plan(samples, footprint, null);
        Assert.Equal(ExcavationStatus.Refused, plan.Status);
        Assert.Equal(DigRefusal.NotFinite, plan.Refusal);
    }
}
