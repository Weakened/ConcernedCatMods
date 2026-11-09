using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 4: the press rule, the size selection, the preview's refresh bound, the confirmation a press must
/// pass, and the outlines the preview draws, measured against the engine-equivalent lattice
/// (<see cref="VanillaTerrainModel"/>). How the preview looks and whether it shows through the terrain are OWNER GO-AROUND
/// PENDING.</summary>
public sealed class ExcavationPreviewTests
{
    private const float Lift = ExcavationPreviewRules.LineLift;

    private static VanillaTerrainModel Flat(float height = 50f) => new((_, _) => height);

    /// <summary>A slope rising 0.3 m per metre along +x, plus a little along z.</summary>
    private static VanillaTerrainModel Slope() => new((x, z) => 50f + 0.3f * x + 0.1f * z);

    private static ExcavationPlan Plan(VanillaTerrainModel world, ExcavationMode mode, int cx, int cz, float? water = null) =>
        ExcavationPlanner.Plan(world.Sample(mode, cx, cz), cx, cz, water);

    // ---- The press and the size ----

    [Theory]
    [InlineData(true, false, (int)ExcavationPress.Dig)]
    [InlineData(false, true, (int)ExcavationPress.SwitchSize)]
    [InlineData(true, true, (int)ExcavationPress.None)]
    [InlineData(false, false, (int)ExcavationPress.None)]
    public void OnePressAsksForOneThingAndOnlyAttackDigs(bool primary, bool secondary, int expected)
    {
        Assert.Equal((ExcavationPress)expected, ExcavationPreviewRules.PressFor(primary, secondary));
    }

    [Fact]
    public void TheSizeStartsSmallAndSwitchesBetweenTheTwo()
    {
        var selection = new ExcavationSelection();
        Assert.Equal(ExcavationMode.Small, selection.Selected);
        Assert.Equal(ExcavationMode.Large, selection.Other);
        selection.Switch();
        Assert.Equal(ExcavationMode.Large, selection.Selected);
        Assert.Equal(ExcavationMode.Small, selection.Other);
        selection.Switch();
        Assert.Equal(ExcavationMode.Small, selection.Selected);
    }

    // ---- The refresh bound (BSM-12) ----

    [Fact]
    public void TheFirstReadAndAnUnknownClockAlwaysRead()
    {
        Assert.True(ExcavationPreviewRules.ShouldRefresh(false, float.NaN));
        Assert.True(ExcavationPreviewRules.ShouldRefresh(true, float.PositiveInfinity));
        Assert.True(ExcavationPreviewRules.ShouldRefresh(false, -1f));
    }

    [Theory]
    [InlineData(true, 0.049f, false)]
    [InlineData(true, 0.05f, true)]
    [InlineData(false, 0.2f, false)]
    [InlineData(false, 0.25f, true)]
    public void TheTerrainIsReadOnTheIntervals(bool changed, float since, bool reads)
    {
        Assert.Equal(reads, ExcavationPreviewRules.ShouldRefresh(changed, since));
    }

    [Theory]
    [InlineData(1000, true, 21)]
    [InlineData(240, true, 21)]
    [InlineData(1000, false, 5)]
    [InlineData(30, true, 21)]
    public void ReadsPerSecondAreBoundedWhateverTheFrameRate(int framesPerSecond, bool aimMovesEveryFrame, int maximum)
    {
        float last = float.NaN;
        int reads = 0;
        for (int frame = 0; frame < framesPerSecond; frame++)
        {
            float now = frame / (float)framesPerSecond;
            if (!ExcavationPreviewRules.ShouldRefresh(aimMovesEveryFrame, now - last)) continue;
            last = now;
            reads++;
        }
        Assert.InRange(reads, 1, maximum);
        // One read is one square: at most the large mode's 7 x 7 vertices.
        Assert.Equal(49, ExcavationGeometry.For(ExcavationMode.Large).SampleSide * ExcavationGeometry.For(ExcavationMode.Large).SampleSide);
    }

    // ---- The confirmation a press must pass (BSM-05) ----

    private static ExcavationPreviewState ShownReady(out ExcavationPlan plan, ExcavationMode mode = ExcavationMode.Small, float now = 10f, int frame = 100)
    {
        plan = Plan(Flat(), mode, 4, -3);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        var shown = new ExcavationPreviewState();
        shown.ShowTarget(mode, 4, -3, ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null, 0, frame, now);
        return shown;
    }

    [Fact]
    public void NothingIsConfirmedBeforeThePreviewHasShown()
    {
        var shown = new ExcavationPreviewState();
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 1, 10f));
        Assert.False(shown.HasTarget);
        Assert.False(shown.Matches(Plan(Flat(), ExcavationMode.Small, 0, 0)));
    }

    [Fact]
    public void APressDuringTheDwellIsRefusedAndAfterItConfirms()
    {
        ExcavationPreviewState shown = ShownReady(out _);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 101, 10f));
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 101, 10f + ExcavationPreviewRules.ConfirmDwellSeconds - 0.01f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 101, 10f + ExcavationPreviewRules.ConfirmDwellSeconds + 0.001f));
    }

    [Fact]
    public void ClosingAMenuOrReequippingRestartsTheDwell()
    {
        ExcavationPreviewState shown = ShownReady(out ExcavationPlan plan);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 101, 11f));
        shown.Hide();
        Assert.False(shown.Visible);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 101, 11f));
        // The inventory closes at t = 20 while the Attack button is held: the first press step comes right after.
        shown.ShowTarget(ExcavationMode.Small, 4, -3, ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null, 0, 200, 20f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 201, 20.02f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 201, 20.25f));
    }

    /// <summary>Independent review of slice 4, S1: the dwell counts from the last change of what is shown, not from when the
    /// preview appeared, so nothing is dug in the step after it first appears.</summary>
    [Fact]
    public void ASquareSweptOntoIsNotDugBeforeItHasBeenShownForTheDwell()
    {
        ExcavationPlan plan = Plan(Flat(), ExcavationMode.Small, 4, -3);
        var shown = new ExcavationPreviewState();
        // Looking at the sky for a second: visible, but no square.
        shown.ShowNoTarget(ExcavationMode.Small, ExcavationAim.NoGround, null, 100, 10f);
        shown.ShowNoTarget(ExcavationMode.Small, ExcavationAim.NoGround, null, 160, 11f);
        // Swept onto ground: the next step's press is refused, a press after the dwell is not.
        shown.ShowTarget(ExcavationMode.Small, 4, -3, ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null, 0, 161, 11.01f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 162, 11.03f));
        // Re-read and unchanged: the dwell keeps running.
        shown.ShowTarget(ExcavationMode.Small, 4, -3, ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null, 0, 170, 11.15f);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 171, 11.22f));
        // Moved to the next vertex: a new square, a new dwell.
        ExcavationPlan next = Plan(Flat(), ExcavationMode.Small, 5, -3);
        shown.ShowTarget(ExcavationMode.Small, 5, -3, ExcavationCommitRefusal.None, ExcavationReadFault.None, next, null, 0, 172, 11.25f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 173, 11.27f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 173, 11.46f));
    }

    [Fact]
    public void ASwitchedSizeARefusalTurnedReadyOrAMovedFloorRestartsTheDwell()
    {
        ExcavationPlan small = Plan(Flat(), ExcavationMode.Small, 0, 0);
        ExcavationPlan large = Plan(Flat(), ExcavationMode.Large, 0, 0);
        var shown = new ExcavationPreviewState();
        shown.ShowTarget(ExcavationMode.Small, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, small, null, 0, 1, 10f);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 2, 11f));
        // Size switched and shown.
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, large, null, 0, 3, 11f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 4, 11.05f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Large, 4, 11.21f));
        // Red (tired) turning green.
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.NotEnoughStamina, ExcavationReadFault.None, large, null, 0, 5, 12f);
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, large, null, 0, 6, 12.5f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 7, 12.55f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Large, 7, 12.71f));
        // The floor moved by more than the tolerance (another edit raised the edge).
        ExcavationPlan raised = Plan(Flat(50.1f), ExcavationMode.Large, 0, 0);
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, raised, null, 0, 8, 13f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 9, 13.05f));
        // After a refused match, a second click one step later does not dig the new plan.
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, raised, null, 0, 10, 14f);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Large, 11, 14.3f));
        shown.Invalidate();
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 11, 14.3f));
        shown.ShowTarget(ExcavationMode.Large, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, large, null, 0, 12, 14.31f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 13, 14.33f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Large, 13, 14.52f));
    }

    [Fact]
    public void AStalePreviewIsNotConfirmed()
    {
        ExcavationPreviewState shown = ShownReady(out _);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 100 + ExcavationPreviewRules.MaximumFrameAge, 11f));
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 101 + ExcavationPreviewRules.MaximumFrameAge, 11f));
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 99, 11f));
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Small, 101, float.NaN));
        // Drawn again without a re-read keeps it fresh.
        shown.Touch(500);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(ExcavationMode.Small, 501, 11f));
    }

    [Fact]
    public void ASizeSwitchedButNotYetShownIsNotConfirmed()
    {
        ExcavationPreviewState shown = ShownReady(out _);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(ExcavationMode.Large, 101, 11f));
    }

    [Fact]
    public void OnlyTheReadyPlanThatWasShownMatches()
    {
        ExcavationPreviewState shown = ShownReady(out ExcavationPlan plan);
        Assert.True(shown.Matches(plan));
        Assert.False(shown.Matches(Plan(Flat(), ExcavationMode.Small, 5, -3)));
        Assert.False(shown.Matches(Plan(Flat(), ExcavationMode.Large, 4, -3)));
        // The ground rose a little (another edit): the datum and the floor moved, which the player has not seen.
        Assert.False(shown.Matches(Plan(Flat(50.05f), ExcavationMode.Small, 4, -3)));
        // Within the level tolerance it is the same floor.
        Assert.True(shown.Matches(Plan(Flat(50.01f), ExcavationMode.Small, 4, -3)));
        // Already dug since: nothing to dig is never a match.
        VanillaTerrainModel dug = Flat();
        dug.Apply(plan);
        dug.Rebuild();
        Assert.Equal(ExcavationStatus.NothingToDig, Plan(dug, ExcavationMode.Small, 4, -3).Status);
        Assert.False(shown.Matches(Plan(dug, ExcavationMode.Small, 4, -3)));
    }

    [Theory]
    [InlineData((int)ExcavationCommitRefusal.NotEnoughStamina)]
    [InlineData((int)ExcavationCommitRefusal.Protected)]
    [InlineData((int)ExcavationCommitRefusal.Obstructed)]
    [InlineData((int)ExcavationCommitRefusal.NothingToDig)]
    [InlineData((int)ExcavationCommitRefusal.Busy)]
    public void AShownRefusalNeverMatchesAFreshReadyPlan(int refusal)
    {
        ExcavationPlan plan = Plan(Flat(), ExcavationMode.Small, 0, 0);
        var shown = new ExcavationPreviewState();
        shown.ShowTarget(ExcavationMode.Small, 0, 0, (ExcavationCommitRefusal)refusal, ExcavationReadFault.None, plan, null, 0, 1, 0f);
        Assert.False(shown.Matches(plan));
    }

    [Fact]
    public void AnInvalidatedOrAimlessPreviewNeverMatches()
    {
        ExcavationPreviewState shown = ShownReady(out ExcavationPlan plan);
        int version = shown.Version;
        shown.Invalidate();
        Assert.True(shown.Version > version);
        Assert.Equal(ExcavationCommitRefusal.PreviewChanged, shown.Refusal);
        Assert.False(shown.Matches(plan));
        shown.ShowNoTarget(ExcavationMode.Small, ExcavationAim.Blocked, "wood_floor", 102, 11f);
        Assert.False(shown.HasTarget);
        Assert.Equal(ExcavationCommitRefusal.NotAimed, shown.Refusal);
        Assert.False(shown.Matches(plan));
        // An Ok passed with no target is still no target.
        shown.ShowNoTarget(ExcavationMode.Small, ExcavationAim.Ok, null, 103, 11f);
        Assert.False(shown.HasTarget);
    }

    // ---- The outlines (BSM-03) ----

    private static ExcavationPreviewShape Shape(VanillaTerrainModel world, ExcavationMode mode, int cx, int cz, out ExcavationPlan plan, float? water = null)
    {
        ExcavationSamples samples = world.Sample(mode, cx, cz);
        plan = ExcavationPlanner.Plan(samples, cx, cz, water);
        var shape = new ExcavationPreviewShape();
        shape.Build(plan, samples, water);
        return shape;
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small, 16)]
    [InlineData((int)ExcavationMode.Large, 24)]
    public void TheBorderWalksTheWholeUntouchedRingOnce(int modeValue, int points)
    {
        var mode = (ExcavationMode)modeValue;
        ExcavationPreviewShape shape = Shape(Slope(), mode, 7, -2, out ExcavationPlan plan);
        int ring = plan.Geometry.RingDistance;
        Assert.Equal(points, shape.Count(ExcavationPreviewShape.Border));
        Assert.True(shape.Loop(ExcavationPreviewShape.Border));
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < points; i++)
        {
            int dx = (int)Math.Round(shape.X(ExcavationPreviewShape.Border, i)) - 7;
            int dz = (int)Math.Round(shape.Z(ExcavationPreviewShape.Border, i)) + 2;
            Assert.Equal(ring, Math.Max(Math.Abs(dx), Math.Abs(dz)));
            Assert.True(seen.Add((dx, dz)), "A ring vertex is visited twice.");
            // On the sampled surface (the ring is never changed).
            Assert.Equal(50f + 0.3f * (7 + dx) + 0.1f * (-2 + dz) + Lift, shape.Y(ExcavationPreviewShape.Border, i), 4);
            // Each step, including the closing one, is one lattice edge.
            int next = (i + 1) % points;
            float step = Math.Abs(shape.X(ExcavationPreviewShape.Border, next) - shape.X(ExcavationPreviewShape.Border, i)) +
                         Math.Abs(shape.Z(ExcavationPreviewShape.Border, next) - shape.Z(ExcavationPreviewShape.Border, i));
            Assert.Equal(1f, step, 4);
        }
        Assert.Equal(8 * ring, seen.Count);
    }

    [Theory]
    [InlineData((int)ExcavationMode.Small)]
    [InlineData((int)ExcavationMode.Large)]
    public void TheFloorAndCornersShowTheUsableInteriorTheBottomAndTheDepth(int modeValue)
    {
        var mode = (ExcavationMode)modeValue;
        ExcavationPreviewShape shape = Shape(Slope(), mode, 0, 0, out ExcavationPlan plan);
        Assert.Equal(4, shape.Count(ExcavationPreviewShape.Floor));
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(plan.Bottom + Lift, shape.Y(ExcavationPreviewShape.Floor, i), 4);
            minX = Math.Min(minX, shape.X(ExcavationPreviewShape.Floor, i));
            maxX = Math.Max(maxX, shape.X(ExcavationPreviewShape.Floor, i));
            minZ = Math.Min(minZ, shape.Z(ExcavationPreviewShape.Floor, i));
            maxZ = Math.Max(maxZ, shape.Z(ExcavationPreviewShape.Floor, i));
        }
        Assert.Equal(plan.Geometry.UsableFloorSize, maxX - minX, 4);
        Assert.Equal(plan.Geometry.UsableFloorSize, maxZ - minZ, 4);
        Assert.Equal(plan.FloorMinX, minX, 4);
        Assert.Equal(plan.FloorMaxZ, maxZ, 4);
        for (int corner = 0; corner < 4; corner++)
        {
            int line = ExcavationPreviewShape.FirstCorner + corner;
            Assert.Equal(3, shape.Count(line));
            Assert.False(shape.Loop(line));
            // The post: from the datum straight down by the mode's depth to the floor.
            Assert.Equal(shape.X(line, 0), shape.X(line, 1));
            Assert.Equal(plan.Datum + Lift, shape.Y(line, 0), 4);
            Assert.Equal(plan.Bottom + Lift, shape.Y(line, 1), 4);
            Assert.Equal(plan.Geometry.Depth, shape.Y(line, 0) - shape.Y(line, 1), 4);
            // The transition: one cell out, diagonally, to the ring corner on the surface.
            Assert.Equal(1f, Math.Abs(shape.X(line, 2) - shape.X(line, 1)), 4);
            Assert.Equal(1f, Math.Abs(shape.Z(line, 2) - shape.Z(line, 1)), 4);
        }
        Assert.Equal(0, shape.Count(ExcavationPreviewShape.Water));
    }

    /// <summary>The preview's border is exactly where the engine's change stops, and its floor is exactly the flat floor
    /// the engine leaves, on a slope, after applying the planned operation with the transcribed engine path.</summary>
    [Theory]
    [InlineData((int)ExcavationMode.Small)]
    [InlineData((int)ExcavationMode.Large)]
    public void ThePreviewMatchesTheTerrainTheOperationLeaves(int modeValue)
    {
        var mode = (ExcavationMode)modeValue;
        VanillaTerrainModel world = Slope();
        ExcavationPreviewShape shape = Shape(world, mode, 11, 5, out ExcavationPlan plan);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        world.Apply(plan);
        world.Rebuild();
        int ring = plan.Geometry.RingDistance;
        int half = plan.Geometry.HalfExtent;
        for (int dz = -ring - 2; dz <= ring + 2; dz++)
        {
            for (int dx = -ring - 2; dx <= ring + 2; dx++)
            {
                int x = 11 + dx, z = 5 + dz;
                bool changed = Math.Abs(world.Displayed(x, z) - world.Base(x, z)) > 1e-4f;
                bool inside = Math.Abs(dx) < ring && Math.Abs(dz) < ring;
                Assert.False(changed && !inside, "Changed outside the drawn border at (" + dx + ", " + dz + ").");
                if (Math.Abs(dx) <= half && Math.Abs(dz) <= half) Assert.Equal(plan.Bottom, world.Displayed(x, z), 3);
            }
        }
        // The drawn border lies on the surface after the dig: each segment is a lattice edge between unchanged vertices,
        // and the collider's triangles meet it there.
        int points = shape.Count(ExcavationPreviewShape.Border);
        for (int i = 0; i < points; i++)
        {
            int next = (i + 1) % points;
            float mx = (shape.X(ExcavationPreviewShape.Border, i) + shape.X(ExcavationPreviewShape.Border, next)) / 2f;
            float mz = (shape.Z(ExcavationPreviewShape.Border, i) + shape.Z(ExcavationPreviewShape.Border, next)) / 2f;
            float drawn = (shape.Y(ExcavationPreviewShape.Border, i) + shape.Y(ExcavationPreviewShape.Border, next)) / 2f - Lift;
            Assert.Equal(world.Surface(mx, mz), drawn, 3);
        }
        // The drawn floor is the flat floor.
        Assert.Equal(world.Surface(plan.CentreX + 0.3f, plan.CentreZ - 0.4f), shape.Y(ExcavationPreviewShape.Floor, 0) - Lift, 3);
    }

    [Fact]
    public void AFloodingFloorShowsTheWaterSurfaceAtTheBorder()
    {
        ExcavationPreviewShape shape = Shape(Flat(31f), ExcavationMode.Large, 0, 0, out ExcavationPlan plan, water: 30f);
        Assert.True(plan.Floods);
        Assert.Equal(4, shape.Count(ExcavationPreviewShape.Water));
        Assert.True(shape.Loop(ExcavationPreviewShape.Water));
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(30f + Lift, shape.Y(ExcavationPreviewShape.Water, i), 4);
            Assert.Equal(plan.Geometry.RingDistance, Math.Abs(shape.X(ExcavationPreviewShape.Water, i)), 4);
            Assert.Equal(plan.Geometry.RingDistance, Math.Abs(shape.Z(ExcavationPreviewShape.Water, i)), 4);
        }
        // Above the water: no water line.
        Assert.Equal(0, Shape(Flat(50f), ExcavationMode.Large, 0, 0, out _, water: 30f).Count(ExcavationPreviewShape.Water));
    }

    [Fact]
    public void AnUnreadablePlanDrawsNothingAndAReusedShapeIsCleared()
    {
        VanillaTerrainModel world = Flat();
        world.MarkUnloaded(1, 1);
        ExcavationPreviewShape shape = Shape(world, ExcavationMode.Small, 0, 0, out ExcavationPlan plan);
        Assert.Equal(ExcavationRefusal.Unloaded, plan.Refusal);
        for (int line = 0; line < ExcavationPreviewShape.LineCount; line++) Assert.Equal(0, shape.Count(line));

        // A ring vertex that cannot be read (a refused, deeper floor still has a datum) clears everything drawn before.
        ExcavationSamples samples = Flat().Sample(ExcavationMode.Small, 0, 0);
        ExcavationPlan ready = ExcavationPlanner.Plan(samples, 0, 0, null);
        var reused = new ExcavationPreviewShape();
        reused.Build(ready, samples, null);
        Assert.Equal(16, reused.Count(ExcavationPreviewShape.Border));
        samples.SetUnloaded(2, 0);
        reused.Build(ready, samples, null);
        for (int line = 0; line < ExcavationPreviewShape.LineCount; line++) Assert.Equal(0, reused.Count(line));
        // Samples of the other size are refused rather than read out of shape.
        reused.Build(ready, Flat().Sample(ExcavationMode.Large, 0, 0), null);
        Assert.Equal(0, reused.Count(ExcavationPreviewShape.Border));
    }

    [Fact]
    public void ARefusedPlanWithAFloorStillShowsWhereAndHowDeep()
    {
        // A pit already deeper than the small mode's floor in one vertex: refused, but the datum and floor are known.
        VanillaTerrainModel world = Flat();
        world.Edit(0, 0, -3f);
        ExcavationPreviewShape shape = Shape(world, ExcavationMode.Small, 0, 0, out ExcavationPlan plan);
        Assert.Equal(ExcavationRefusal.FloorAlreadyDeeper, plan.Refusal);
        Assert.Equal(16, shape.Count(ExcavationPreviewShape.Border));
        Assert.Equal(4, shape.Count(ExcavationPreviewShape.Floor));
    }

    [Fact]
    public void TheShapeIsBoundedAndNeverGrows()
    {
        Assert.Equal(7, ExcavationPreviewShape.LineCount);
        Assert.Equal(ExcavationPreviewShape.MaximumPoints, 8 * ExcavationGeometry.For(ExcavationMode.Large).RingDistance);
        ExcavationPreviewShape shape = Shape(Slope(), ExcavationMode.Large, 3, 3, out ExcavationPlan plan, water: 80f);
        for (int line = 0; line < ExcavationPreviewShape.LineCount; line++)
        {
            Assert.InRange(shape.Count(line), 0, ExcavationPreviewShape.MaximumPoints);
            for (int p = 0; p < shape.Count(line); p++)
            {
                Assert.InRange(shape.X(line, p), plan.BorderMinX, plan.BorderMaxX);
                Assert.InRange(shape.Z(line, p), plan.BorderMinZ, plan.BorderMaxZ);
            }
        }
    }

    [Theory]
    [InlineData((int)ExcavationCommitRefusal.None, (int)ExcavationPreviewTone.Ready)]
    [InlineData((int)ExcavationCommitRefusal.NothingToDig, (int)ExcavationPreviewTone.Idle)]
    [InlineData((int)ExcavationCommitRefusal.Busy, (int)ExcavationPreviewTone.Idle)]
    [InlineData((int)ExcavationCommitRefusal.Protected, (int)ExcavationPreviewTone.Refused)]
    [InlineData((int)ExcavationCommitRefusal.PlanRefused, (int)ExcavationPreviewTone.Refused)]
    [InlineData((int)ExcavationCommitRefusal.NotAimed, (int)ExcavationPreviewTone.Refused)]
    public void TheColourSaysWhetherAPressWouldDig(int refusal, int tone)
    {
        Assert.Equal((ExcavationPreviewTone)tone, ExcavationPreviewShape.ToneFor((ExcavationCommitRefusal)refusal));
    }

    // ---- The words (BSM-05) ----

    [Fact]
    public void TheTitleNamesTheSizeInMetres()
    {
        Assert.Equal("Iron Shovel: 2x2x2 m (usable floor 2 x 2 m, 2 m deep)", ExcavationReport.PreviewTitle(ExcavationMode.Small));
        Assert.Equal("Iron Shovel: 4x4x4 m (usable floor 4 x 4 m, 4 m deep)", ExcavationReport.PreviewTitle(ExcavationMode.Large));
    }

    [Fact]
    public void TheHintsUseTheBindingsTheyAreGivenAndFollowTheSelection()
    {
        var selection = new ExcavationSelection();
        string hints = ExcavationReport.PreviewHints(selection, "Mouse 1", "Mouse 3");
        Assert.Equal("[Mouse 1] Dig 2x2x2 m    [Mouse 3] Switch to 4x4x4 m", hints);
        selection.Switch();
        Assert.Equal("[RT] Dig 4x4x4 m    [RB] Switch to 2x2x2 m", ExcavationReport.PreviewHints(selection, "RT", "RB"));
    }

    [Fact]
    public void TheReadyLineCarriesTheMeasuredNumbersAndTheFlood()
    {
        ExcavationPlan plan = Plan(Flat(31f), ExcavationMode.Small, 0, 0, water: 30f);
        var shown = new ExcavationPreviewState();
        shown.ShowTarget(ExcavationMode.Small, 0, 0, ExcavationCommitRefusal.None, ExcavationReadFault.None, plan, null, 3, 1, 0f);
        string text = ExcavationReport.PreviewState(shown);
        Assert.StartsWith("Ready: floor at 29.0 m, 2 m below the highest edge (31.0 m)", text);
        Assert.Contains("walls 2.0 to 2.0 m", text);
        Assert.Contains("4 x 4 m outline", text);
        Assert.Contains("20 stamina and 4 durability", text);
        Assert.Contains("1.0 m below the water surface and will flood", text);
        Assert.Contains("3 building-piece collider(s)", text);
    }

    [Fact]
    public void EveryShownStateSaysWhatToDo()
    {
        ExcavationPlan plan = Plan(Flat(), ExcavationMode.Small, 0, 0);
        var texts = new HashSet<string>();
        foreach (ExcavationCommitRefusal refusal in Enum.GetValues<ExcavationCommitRefusal>())
        {
            if (refusal == ExcavationCommitRefusal.PlanRefused) continue; // the planner's own words, below
            var shown = new ExcavationPreviewState();
            shown.ShowTarget(ExcavationMode.Small, 0, 0, refusal, ExcavationReadFault.Rebuilding, plan, "Rock_3", 0, 1, 0f);
            string text = ExcavationReport.PreviewState(shown);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(texts.Add(text), refusal + " repeats another state's words.");
        }
        foreach (ExcavationAim aim in Enum.GetValues<ExcavationAim>())
        {
            if (aim == ExcavationAim.Ok) continue;
            var shown = new ExcavationPreviewState();
            shown.ShowNoTarget(ExcavationMode.Small, aim, "wood_floor", 1, 0f);
            Assert.True(texts.Add(ExcavationReport.PreviewState(shown)), aim + " repeats another state's words.");
        }
        VanillaTerrainModel world = Flat();
        world.Edit(0, 0, -3f);
        ExcavationPlan deeper = Plan(world, ExcavationMode.Small, 0, 0);
        var refused = new ExcavationPreviewState();
        refused.ShowTarget(ExcavationMode.Small, 0, 0, ExcavationCommitRefusal.PlanRefused, ExcavationReadFault.None, deeper, null, 0, 1, 0f);
        Assert.Contains("already lower than this hole's floor", ExcavationReport.PreviewState(refused));
    }

    [Fact]
    public void TheAimRefusalsNameTheBlockerAndTheReach()
    {
        Assert.Contains("(wood_floor)", ExcavationReport.Aim(ExcavationAim.Blocked, "wood_floor"));
        Assert.Contains("opening in a floor", ExcavationReport.Aim(ExcavationAim.Blocked, null));
        Assert.Contains("within 5 m", ExcavationReport.Aim(ExcavationAim.TooFar, null));
        Assert.Equal(5f, ExcavationLimits.Reach);
    }
}
