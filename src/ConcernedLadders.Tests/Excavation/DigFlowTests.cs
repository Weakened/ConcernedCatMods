using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#551 slices 2 and 3, the reworked dig end to end in game-free form: the presses (corner, corner, depth, dig;
/// back; undo armed then confirmed), the preview's confirmation rule and flat outline, the single batch in flight, the
/// planner gate, and - on the transcribed engine (<see cref="VanillaTerrainModel"/>) - that the single-vertex batch makes
/// exactly the planned floor and its undo restores exactly the ground before. In game: OWNER GO-AROUND PENDING.</summary>
public sealed class DigFlowTests
{
    private const float Ground = 50f;

    // ---- The presses ----

    [Fact]
    public void CornerThenCornerThenDepthThenDig()
    {
        var session = new DigSession();
        Assert.Equal(DigAction.None, session.Primary()); // nothing aimed at yet
        session.Aim(10, 20);
        Assert.Equal((10, 20, 12, 22), Box(session.Footprint)); // a 2 x 2 m square follows the aim
        Assert.Equal(DigAction.Advanced, session.Primary());
        Assert.Equal(DigStage.SecondCorner, session.Stage);
        session.Aim(15, 17);
        Assert.Equal((10, 17, 15, 20), Box(session.Footprint)); // stretched from the corner to the aim
        Assert.Equal(DigAction.Advanced, session.Primary());
        Assert.Equal(DigStage.Depth, session.Stage);
        session.Aim(0, 0); // the aim no longer moves the rectangle
        Assert.Equal((10, 17, 15, 20), Box(session.Footprint));
        Assert.False(session.FollowsAim);
        Assert.Equal(DigAction.Dig, session.Primary());
        session.Dug();
        Assert.Equal(DigStage.FirstCorner, session.Stage);
    }

    [Fact]
    public void TheSecondCornerIsSetAsClampedSoWhatIsDugIsWhatWasShown()
    {
        var session = new DigSession();
        session.Aim(0, 0);
        session.Primary();
        session.Aim(30, -30); // beyond 8 m on both sides
        DigFootprint shown = session.Footprint;
        session.Primary();
        Assert.Equal(shown, session.Footprint);
        Assert.Equal((0, -8, 8, 0), Box(session.Footprint));
    }

    [Fact]
    public void ReadyMadeCubesAreTheirSizeDeepSnapToTheirOwnGridAndDigAtOnce()
    {
        var session = new DigSession();
        Assert.Equal(DigPreset.Free, session.Preset);
        Assert.True(session.ChangePreset(1));
        Assert.Equal(DigPreset.Cube1, session.Preset);
        session.ChangePreset(1);
        Assert.Equal(DigPreset.Cube2, session.Preset);
        session.Aim(7, -3);
        // A 2 m cube on the 2 m grid: whatever vertex is aimed in a cell, the cube is that cell's.
        Assert.Equal((6, -4, 8, -2), Box(session.Footprint));
        Assert.Equal(2, session.Footprint.Depth);
        session.Aim(6, -4);
        Assert.Equal((6, -4, 8, -2), Box(session.Footprint));
        session.Aim(8, -4);
        Assert.Equal((8, -4, 10, -2), Box(session.Footprint)); // the neighbouring cube shares the edge x 8: they tile
        Assert.Equal(DigAction.Dig, session.Primary());
        Assert.Equal(DigStage.FirstCorner, session.Stage);
        session.ChangePreset(1);
        Assert.Equal(DigPreset.Cube4, session.Preset);
        session.Aim(-1, 5);
        Assert.Equal((-4, 4, 0, 8, 4), (session.Footprint.MinX, session.Footprint.MinZ, session.Footprint.MaxX, session.Footprint.MaxZ, session.Footprint.Depth));
        session.ChangePreset(1);
        Assert.Equal(DigPreset.Free, session.Preset);
        session.ChangePreset(-1);
        Assert.Equal(DigPreset.Cube4, session.Preset);
        // The size is chosen only at the first stage.
        session.ChangePreset(1);
        session.Primary();
        Assert.False(session.ChangePreset(1));
        Assert.Equal((1, 1, 1), (DigSession.Cube(0, 0, 1).Width, DigSession.Cube(0, 0, 1).Length, DigSession.Cube(0, 0, 1).Depth));
    }

    [Fact]
    public void TheCubePromptAndTheClearingFitOnOneLine()
    {
        DigFootprint cube = DigSession.Cube(0, 0, 4);
        string prompt = DigText.Prompt(DigStage.FirstCorner, cube, DigPreset.Cube4);
        Assert.Equal("4x4x4 m cube: click to dig, scroll for size.", prompt);
        Assert.Equal("4x4x4 m cube: click to dig, scroll for size. Clears 3.", DigText.WithClearing(prompt, 3));
        Assert.Equal(prompt, DigText.WithClearing(prompt, 0));
        string longest = DigText.Prompt(DigStage.Depth, DigFootprint.FromCorners(0, 0, 8, 8, 7));
        Assert.True(DigText.WithClearing(longest, 120).Length <= DigText.MaximumLength);
        Assert.Equal("Set a corner, or scroll for a 1, 2 or 4 m cube.", DigText.Prompt(DigStage.FirstCorner, cube));
    }

    [Fact]
    public void TheWheelSetsTheDepthOnlyInTheDepthStageAndWithinItsRange()
    {
        var session = new DigSession();
        Assert.False(session.ChangeDepth(1)); // not in the depth stage yet
        session.Aim(0, 0);
        session.Primary();
        session.Aim(3, 3);
        session.Primary();
        Assert.Equal(DigFootprint.DefaultDepth, session.Depth);
        Assert.True(session.ChangeDepth(1));
        Assert.Equal(3, session.Footprint.Depth);
        for (int i = 0; i < 20; i++) session.ChangeDepth(1);
        Assert.Equal(DigFootprint.MaximumDepth, session.Depth);
        Assert.False(session.ChangeDepth(1));
        for (int i = 0; i < 20; i++) session.ChangeDepth(-1);
        Assert.Equal(DigFootprint.MinimumDepth, session.Depth);
        // The depth is kept for the next hole.
        session.ChangeDepth(2);
        session.Dug();
        Assert.Equal(3, session.Depth);
    }

    [Fact]
    public void SecondaryStepsBackAndOnlyFromTheStartArmsThenConfirmsUndo()
    {
        var session = new DigSession();
        session.Aim(0, 0);
        session.Primary();
        session.Aim(4, 4);
        session.Primary();
        Assert.Equal(DigAction.SteppedBack, session.Secondary(10f));
        Assert.Equal(DigStage.SecondCorner, session.Stage);
        Assert.Equal(DigAction.SteppedBack, session.Secondary(10.1f));
        Assert.Equal(DigStage.FirstCorner, session.Stage);
        // Spamming back from the depth stage reaches the start, and the next press only arms: the line says so first.
        Assert.Equal(DigAction.UndoArmed, session.Secondary(10.2f));
        Assert.True(session.UndoArmed(11f));
        Assert.Equal(DigAction.Undo, session.Secondary(11f));
        Assert.False(session.UndoArmed(11f));
    }

    [Fact]
    public void AnArmedUndoExpiresAndAnyOtherPressDisarmsIt()
    {
        var session = new DigSession();
        Assert.Equal(DigAction.UndoArmed, session.Secondary(5f));
        Assert.Equal(DigAction.UndoArmed, session.Secondary(5f + DigSession.UndoConfirmSeconds + 0.01f)); // too late: armed again
        Assert.Equal(DigAction.Undo, session.Secondary(6f + DigSession.UndoConfirmSeconds));
        Assert.Equal(DigAction.UndoArmed, session.Secondary(20f));
        session.Aim(0, 0);
        session.Primary(); // a corner disarms
        session.Secondary(20.5f); // back to the start
        Assert.Equal(DigAction.UndoArmed, session.Secondary(20.6f));
        session.Reset(); // the shovel put away disarms
        Assert.Equal(DigAction.UndoArmed, session.Secondary(20.7f));
        Assert.Equal(DigAction.None, new DigSession().Secondary(float.NaN));
    }

    [Theory]
    [InlineData(true, false, (int)ExcavationPress.Primary)]
    [InlineData(false, true, (int)ExcavationPress.Secondary)]
    [InlineData(true, true, (int)ExcavationPress.None)]
    [InlineData(false, false, (int)ExcavationPress.None)]
    public void OnePressAsksForOneThing(bool primary, bool secondary, int expected) =>
        Assert.Equal((ExcavationPress)expected, ExcavationPreviewRules.PressFor(primary, secondary));

    // ---- The refresh bound ----

    [Theory]
    [InlineData(true, 0.049f, false)]
    [InlineData(true, 0.05f, true)]
    [InlineData(false, 0.2f, false)]
    [InlineData(false, 0.25f, true)]
    public void TheTerrainIsReadOnTheIntervals(bool changed, float since, bool reads) =>
        Assert.Equal(reads, ExcavationPreviewRules.ShouldRefresh(changed, since));

    [Fact]
    public void ReadsPerSecondAreBoundedAndOneReadIsBounded()
    {
        float last = float.NaN;
        int reads = 0;
        for (int frame = 0; frame < 1000; frame++)
        {
            float now = frame / 1000f;
            if (!ExcavationPreviewRules.ShouldRefresh(true, now - last)) continue;
            last = now;
            reads++;
        }

        Assert.InRange(reads, 1, 21);
        // One read is one rectangle and its ring: at most 11 x 11 vertices.
        Assert.Equal(11, DigSamples.Capacity);
        Assert.True(ExcavationPreviewRules.ShouldRefresh(false, float.NaN));
    }

    // ---- The preview's confirmation ----

    [Fact]
    public void ADigPressNeedsTheReadyPlanShownForTheDwellInTheLatestFrames()
    {
        DigPlan plan = Ready(out _);
        var shown = new DigPreviewState();
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(100, 10f));
        shown.Show(DigStage.Depth, plan.Footprint, true, plan, ExcavationCommitRefusal.None, "line", 100, 10f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(100, 10f + ExcavationPreviewRules.ConfirmDwellSeconds / 2f));
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(101, 10f + ExcavationPreviewRules.ConfirmDwellSeconds + 0.01f));
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(100 + ExcavationPreviewRules.MaximumFrameAge + 1, 20f));
        // The same plan drawn again keeps the dwell; a different depth restarts it.
        shown.Show(DigStage.Depth, plan.Footprint, true, plan, ExcavationCommitRefusal.None, "line", 110, 11f);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(110, 11f));
        DigPlan deeper = Ready(out _, depth: 3);
        shown.Show(DigStage.Depth, deeper.Footprint, true, deeper, ExcavationCommitRefusal.None, "line", 111, 11.01f);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(111, 11.02f));
        shown.Hide();
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(112, 30f));
    }

    [Fact]
    public void OnlyTheReadyPlanThatWasShownMatchesAFreshOne()
    {
        DigPlan plan = Ready(out _);
        var shown = new DigPreviewState();
        shown.Show(DigStage.Depth, plan.Footprint, true, plan, ExcavationCommitRefusal.None, "line", 1, 1f);
        Assert.True(shown.Matches(Ready(out _)));
        Assert.False(shown.Matches(Ready(out _, depth: 3)));
        Assert.False(shown.Matches(Ready(out _, ground: Ground + 0.5f)));
        Assert.False(shown.Matches(Plan(DigFootprint.FromCorners(0, 0, 4, 3, 2), (x, z) => Ground, out _)));
        shown.Show(DigStage.Depth, plan.Footprint, false, default, ExcavationCommitRefusal.TerrainUnreadable, "line", 2, 2f);
        Assert.False(shown.Matches(plan), "Nothing planned was shown.");
        shown.ShowNoTarget(DigStage.FirstCorner, ExcavationAim.TooFar, "line", 3, 3f);
        Assert.False(shown.HasTarget);
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(3, 9f));
    }

    [Fact]
    public void SayingALineChangesTheVersionButNotTheOutlineOrTheDwell()
    {
        DigPlan plan = Ready(out _);
        var shown = new DigPreviewState();
        shown.Show(DigStage.Depth, plan.Footprint, true, plan, ExcavationCommitRefusal.None, "line", 1, 1f);
        int version = shown.Version;
        shown.Say("other");
        Assert.True(shown.Version > version);
        Assert.Equal(ExcavationCommitRefusal.None, shown.Confirm(1, 2f));
        shown.Invalidate();
        Assert.Equal(ExcavationCommitRefusal.NoPreview, shown.Confirm(1, 2f));
    }

    // ---- The outline ----

    [Fact]
    public void TheOutlineIsAPerfectBoxFromTheGroundToTheFloor()
    {
        DigPlan plan = Ready(out _);
        var shape = new DigPreviewShape();
        shape.Build(plan, null);
        float lift = ExcavationPreviewRules.LineLift;
        Assert.Equal(4, shape.Count(DigPreviewShape.Top));
        Assert.Equal(4, shape.Count(DigPreviewShape.Floor));
        Assert.True(shape.Loop(DigPreviewShape.Top) && shape.Loop(DigPreviewShape.Floor));
        for (int p = 0; p < 4; p++)
        {
            Assert.Equal(plan.Datum + lift, shape.Y(DigPreviewShape.Top, p), 4);
            Assert.Equal(plan.Bottom + lift, shape.Y(DigPreviewShape.Floor, p), 4);
        }

        // #567: a perfect box. The top rectangle is the floor's own, at the ground; the four edges are vertical.
        DigFootprint footprint = plan.Footprint;
        for (int p = 0; p < 4; p++)
        {
            Assert.Equal(shape.X(DigPreviewShape.Floor, p), shape.X(DigPreviewShape.Top, p), 4);
            Assert.Equal(shape.Z(DigPreviewShape.Floor, p), shape.Z(DigPreviewShape.Top, p), 4);
        }

        Assert.Equal(footprint.MinX, shape.X(DigPreviewShape.Floor, 0), 4);
        Assert.Equal(footprint.MaxZ, shape.Z(DigPreviewShape.Floor, 2), 4);
        for (int wall = DigPreviewShape.FirstWall; wall < DigPreviewShape.FirstWall + 4; wall++)
        {
            Assert.Equal(2, shape.Count(wall));
            Assert.Equal(shape.X(wall, 0), shape.X(wall, 1), 4);
            Assert.Equal(shape.Z(wall, 0), shape.Z(wall, 1), 4);
            Assert.Equal(plan.Footprint.Depth, shape.Y(wall, 0) - shape.Y(wall, 1), 4);
        }

        Assert.Equal(0, shape.Count(DigPreviewShape.Water));
    }

    [Fact]
    public void AFloodingFloorShowsTheWaterAndAFootprintAloneIsDrawnBeforeAnyRead()
    {
        DigPlan wet = Ready(out _, water: Ground - 1f);
        var shape = new DigPreviewShape();
        shape.Build(wet, Ground - 1f);
        Assert.Equal(4, shape.Count(DigPreviewShape.Water));
        Assert.Equal(Ground - 1f + ExcavationPreviewRules.LineLift, shape.Y(DigPreviewShape.Water, 0), 4);
        shape.BuildFootprint(DigFootprint.FromCorners(3, 3, 6, 6, 2), 12f);
        Assert.Equal(4, shape.Count(DigPreviewShape.Floor));
        Assert.Equal(0, shape.Count(DigPreviewShape.Top));
        shape.BuildFootprint(DigFootprint.FromCorners(3, 3, 6, 6, 2), float.NaN);
        Assert.Equal(0, shape.Count(DigPreviewShape.Floor));
        Assert.Equal(DigTone.Ready, DigPreviewShape.ToneFor(ExcavationCommitRefusal.None));
        Assert.Equal(DigTone.Idle, DigPreviewShape.ToneFor(ExcavationCommitRefusal.NothingToDig));
        Assert.Equal(DigTone.Refused, DigPreviewShape.ToneFor(ExcavationCommitRefusal.Protected));
    }

    // ---- The gate and the slot ----

    [Fact]
    public void TheGateAsksInOrderAndNeverPassesADefaultPlan()
    {
        DigPlan ready = Ready(out _);
        Assert.Equal(ExcavationCommitRefusal.None, DigGate.BeforeWorldChecks(true, true, true, false, false, ExcavationReadFault.None, ready));
        Assert.Equal(ExcavationCommitRefusal.Disabled, DigGate.BeforeWorldChecks(false, false, false, true, true, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.Unavailable, DigGate.BeforeWorldChecks(true, false, false, true, true, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.NotRegistered, DigGate.BeforeWorldChecks(true, true, false, true, true, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.TerrainOpsDisabled, DigGate.BeforeWorldChecks(true, true, true, true, true, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.Busy, DigGate.BeforeWorldChecks(true, true, true, false, true, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.TerrainUnreadable, DigGate.BeforeWorldChecks(true, true, true, false, false, ExcavationReadFault.Rebuilding, ready));
        Assert.Equal(ExcavationCommitRefusal.PlanRefused, DigGate.BeforeWorldChecks(true, true, true, false, false, ExcavationReadFault.None, default));
        Assert.Equal(ExcavationCommitRefusal.Protected, DigGate.WorldChecks(false, true, 3, true));
        Assert.Equal(ExcavationCommitRefusal.NoBuildArea, DigGate.WorldChecks(true, true, 3, true));
        Assert.Equal(ExcavationCommitRefusal.ScanOverflow, DigGate.WorldChecks(true, false, 3, true));
        Assert.Equal(ExcavationCommitRefusal.Obstructed, DigGate.WorldChecks(true, false, 3, false));
        Assert.Equal(ExcavationCommitRefusal.None, DigGate.WorldChecks(true, false, 0, false));
    }

    [Fact]
    public void OneBatchInFlightEndsOnlyOnAReadBackATimeoutOrTheWorldGoing()
    {
        var record = Record(out _);
        var flight = new DigInFlight();
        Assert.True(flight.TryStart(DigFlightKind.Dig, record, 1f));
        Assert.False(flight.TryStart(DigFlightKind.Undo, record, 1f));
        Assert.Equal(ExcavationOutcome.Waiting, flight.Observe(ExcavationReadFault.Rebuilding, ExcavationProgress.Unreadable, 2f));
        Assert.Equal(ExcavationOutcome.Waiting, flight.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, 3f));
        Assert.Equal(ExcavationOutcome.TimedOut, flight.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, 1f + DigInFlight.TimeoutSeconds + 0.1f));
        Assert.False(flight.IsActive);
        Assert.True(flight.TryStart(DigFlightKind.Undo, record, 20f));
        Assert.Equal(ExcavationOutcome.Unreadable, flight.Observe(ExcavationReadFault.LegacyTerrainModifier, ExcavationProgress.Complete, 21f));
        Assert.True(flight.TryStart(DigFlightKind.Dig, record, 30f));
        Assert.Equal(ExcavationOutcome.Complete, flight.Observe(ExcavationReadFault.None, ExcavationProgress.Complete, 31f));
        Assert.True(flight.TryStart(DigFlightKind.Dig, record, 40f));
        Assert.True(flight.Abandon());
        Assert.False(flight.Abandon());
        Assert.False(flight.TryStart(DigFlightKind.Dig, record, float.NaN));
        Assert.Throws<InvalidOperationException>(() => flight.Observe(ExcavationReadFault.None, ExcavationProgress.Complete, 50f));
    }

    [Fact]
    public void ThePartlyPlacedBatchNarrowsTheSlotToASubsetOnly()
    {
        DigRecord record = Record(out _);
        var flight = new DigInFlight();
        flight.TryStart(DigFlightKind.Dig, record, 1f);
        var part = new DigRecord(record.Footprint, record.Vertices.Take(3).ToList());
        Assert.True(flight.Narrow(part));
        Assert.Same(part, flight.Record);
        var stranger = new DigRecord(record.Footprint, new[] { new DigVertex(99, 99, 1f, 0f) });
        Assert.False(flight.Narrow(stranger));
    }

    // ---- On the engine ----

    [Fact]
    public void TheSingleVertexBatchMakesExactlyThePlannedFloorAndNothingElse()
    {
        var world = new VanillaTerrainModel((x, z) => Ground + 0.3f * x - 0.2f * z);
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 5, 3, 2);
        DigSamples before = world.Sample(footprint);
        DigPlan plan = DigPlanner.Plan(before, footprint, null);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        var record = new DigRecord(footprint, DigPlanner.Lowered(plan, before));
        Assert.Equal(ExcavationProgress.Pending, DigPlanner.Progress(record, world.Read));
        world.Apply(DigFlightKind.Dig, record);
        world.Rebuild();
        Assert.Equal(ExcavationProgress.Complete, DigPlanner.Progress(record, world.Read));
        for (int z = -2; z <= 5; z++)
        {
            for (int x = -2; x <= 7; x++)
            {
                float expected = before.Covers(x, z) ? DigPlanner.PlannedHeight(plan, before, x, z) : world.Base(x, z);
                Assert.Equal(expected, world.Displayed(x, z), 4);
            }
        }

        // Planning again finds nothing to dig.
        Assert.Equal(ExcavationStatus.NothingToDig, DigPlanner.Plan(world.Sample(footprint), footprint, null).Status);
    }

    [Fact]
    public void UndoRestoresTheGroundExactlyAndOnlyOverTheFloorTheDigLeft()
    {
        var world = new VanillaTerrainModel((x, z) => Ground + 0.25f * x * z % 1.5f);
        DigFootprint footprint = DigFootprint.FromCorners(-3, -3, 3, 2, 4);
        DigSamples before = world.Sample(footprint);
        DigPlan plan = DigPlanner.Plan(before, footprint, null);
        var record = new DigRecord(footprint, DigPlanner.Lowered(plan, before));
        world.Apply(DigFlightKind.Dig, record);
        world.Rebuild();
        Assert.Equal(UndoRefusal.None, DigUndo.Check(record, world.Read));
        Assert.All(record.Vertices, vertex => Assert.Equal(1, DigUndo.RaiseOperations(vertex)));
        world.Apply(DigFlightKind.Undo, record);
        Assert.Equal(ExcavationProgress.Pending, DigUndo.Progress(record, world.Read));
        world.Rebuild();
        Assert.Equal(ExcavationProgress.Complete, DigUndo.Progress(record, world.Read));
        for (int z = -4; z <= 3; z++) for (int x = -4; x <= 4; x++) Assert.Equal(world.Base(x, z), world.Displayed(x, z), 4);
    }

    [Fact]
    public void AnEditSinceTheDigRefusesTheUndoAndAStaleSecondBatchOvershoots()
    {
        var world = new VanillaTerrainModel((_, _) => Ground);
        DigRecord record = Record(out DigFootprint footprint, world);
        world.Apply(DigFlightKind.Dig, record);
        world.Rebuild();
        world.Edit(record.Vertices[0].X, record.Vertices[0].Z, -0.5f); // a pickaxe since
        Assert.Equal(UndoRefusal.Changed, DigUndo.Check(record, world.Read));

        Assert.True(footprint.FloorVertices > 0);

        // Why one batch at a time, the engine property behind the in-flight slot: a second batch before the heightmap
        // rebuilds reads the stale heights and changes the ground again. A dig sent twice digs twice ...
        var stale = new VanillaTerrainModel((_, _) => Ground);
        DigRecord twice = Record(out _, stale);
        stale.Apply(DigFlightKind.Dig, twice);
        stale.Apply(DigFlightKind.Dig, twice);
        stale.Rebuild();
        Assert.Equal(Ground - 4f, stale.Displayed(twice.Vertices[0].X, twice.Vertices[0].Z), 4);
        Assert.Equal(ExcavationProgress.Overshot, DigPlanner.Progress(twice, stale.Read));
        // ... while one sent again after the rebuild changes nothing: its target is absolute.
        var settled = new VanillaTerrainModel((_, _) => Ground);
        DigRecord again = Record(out _, settled);
        settled.Apply(DigFlightKind.Dig, again);
        settled.Rebuild();
        settled.Apply(DigFlightKind.Dig, again);
        settled.Rebuild();
        Assert.Equal(ExcavationProgress.Complete, DigPlanner.Progress(again, settled.Read));
        // And an undo sent twice before the rebuild raises twice.
        settled.Apply(DigFlightKind.Undo, again);
        settled.Apply(DigFlightKind.Undo, again);
        settled.Rebuild();
        Assert.Equal(ExcavationProgress.Overshot, DigUndo.Progress(again, settled.Read));
    }

    [Fact]
    public void ADigThatCouldNotBeUndoneInOneRaiseIsRefused()
    {
        // Ground an earlier edit raised 3 m above its natural surface: a 7 m dig from the ring would drop a centre mound 9 m.
        var world = new VanillaTerrainModel((x, z) => Ground);
        world.Edit(1, 1, 3f);
        world.Rebuild();
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 2, 2, 7);
        DigPlan plan = DigPlanner.Plan(world.Sample(footprint), footprint, null);
        Assert.Equal(DigRefusal.UndoLimit, plan.Refusal);
        Assert.Equal("Too much at one spot: dig shallower here.", DigText.Plan(plan));
    }

    [Theory]
    [InlineData(10.5f, 11)]
    [InlineData(-0.5f, 0)]
    [InlineData(-0.51f, -1)]
    [InlineData(-1.5f, -1)]
    [InlineData(0f, 0)]
    public void TheAimSnapsLikeWorldToVertex(float world, int vertex) => Assert.Equal(vertex, ExcavationGeometry.SnapToVertex(world));

    [Theory]
    [InlineData(34, 0, 1, 1)]
    [InlineData(-34, 0, 2, 1)]
    [InlineData(30, 30, 2, 2)]   // a four-tile corner
    [InlineData(28, 96, 2, 2)]
    public void TilesHoldingAFloorVertexAreCountedWithSharedEdges(int minX, int minZ, int tilesX, int tilesZ)
    {
        DigFootprint footprint = DigFootprint.FromCorners(minX, minZ, minX + 4, minZ + 4, 2);
        DigPlan plan = DigPlanner.Plan(new VanillaTerrainModel((_, _) => Ground).Sample(footprint), footprint, null);
        Assert.Equal((tilesX, tilesZ), (plan.TileCountX, plan.TileCountZ));
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
    }

    // ---- Helpers ----

    private static (int, int, int, int) Box(DigFootprint footprint) => (footprint.MinX, footprint.MinZ, footprint.MaxX, footprint.MaxZ);

    private static DigPlan Ready(out DigSamples samples, int depth = 2, float ground = Ground, float? water = null)
    {
        DigPlan plan = Plan(DigFootprint.FromCorners(0, 0, 3, 3, depth), (x, z) => ground, out samples, water);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        return plan;
    }

    private static DigPlan Plan(DigFootprint footprint, Func<int, int, float> height, out DigSamples samples, float? water = null)
    {
        samples = new VanillaTerrainModel((x, z) => height(x, z)).Sample(footprint);
        return DigPlanner.Plan(samples, footprint, water);
    }

    private static DigRecord Record(out DigFootprint footprint, VanillaTerrainModel? world = null)
    {
        footprint = DigFootprint.FromCorners(0, 0, 3, 2, 2);
        DigSamples samples = (world ?? new VanillaTerrainModel((_, _) => Ground)).Sample(footprint);
        return new DigRecord(footprint, DigPlanner.Lowered(DigPlanner.Plan(samples, footprint, null), samples));
    }
}
