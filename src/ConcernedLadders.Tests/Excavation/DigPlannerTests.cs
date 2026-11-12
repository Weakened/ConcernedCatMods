using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#551 slice 1, the game-free model of the reworked dig: a rectangle the player sizes corner to corner, a depth
/// in whole metres, a floor that joins ground already lower instead of refusing, an exact undo record, and one short
/// line for every state.</summary>
public sealed class DigPlannerTests
{
    private const float Ground = 30f;

    // ---- The footprint ----

    [Theory]
    [InlineData(10, 20, 14, 23, 10, 20, 14, 23)]   // toward +x +z: the anchor is the min corner
    [InlineData(10, 20, 7, 16, 7, 16, 10, 20)]     // toward -x -z: the anchor is the max corner
    [InlineData(10, 20, 10, 20, 10, 20, 11, 21)]   // aim on the anchor: the smallest floor (1 m since #567), toward +x +z
    [InlineData(10, 20, 11, 19, 10, 19, 11, 20)]   // one metre away: 1 m, in the aimed direction
    [InlineData(10, 20, 40, -40, 10, 12, 18, 20)]  // far away: 8 m at most
    public void TheAnchorIsAlwaysACornerAndEachSideIsOneToEightMetres(int ax, int az, int bx, int bz, int minX, int minZ, int maxX, int maxZ)
    {
        DigFootprint footprint = DigFootprint.FromCorners(ax, az, bx, bz, 3);
        Assert.Equal((minX, minZ, maxX, maxZ), (footprint.MinX, footprint.MinZ, footprint.MaxX, footprint.MaxZ));
        Assert.InRange(footprint.Width, DigFootprint.MinimumSide, DigFootprint.MaximumSide);
        Assert.InRange(footprint.Length, DigFootprint.MinimumSide, DigFootprint.MaximumSide);
        Assert.True((footprint.MinX == ax || footprint.MaxX == ax) && (footprint.MinZ == az || footprint.MaxZ == az));
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(7, 7)]
    [InlineData(8, 7)]
    [InlineData(20, 7)]
    public void TheDepthIsOneToEightWholeMetres(int asked, int depth)
    {
        Assert.Equal(depth, DigFootprint.FromCorners(0, 0, 3, 3, asked).Depth);
        Assert.Equal(depth, DigFootprint.FromCorners(0, 0, 3, 3, 2).WithDepth(asked).Depth);
    }

    // ---- The plan ----

    [Fact]
    public void FlatGroundGivesALevelFloorTheDepthDown()
    {
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 4, 3, 2);
        DigPlan plan = Plan(footprint, (x, z) => Ground);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(Ground, plan.Datum);
        Assert.Equal(Ground - 2f, plan.Bottom);
        Assert.Equal(5 * 4, plan.LoweredVertices);
        Assert.Equal(0, plan.AlreadyLower);
        Assert.Equal(2f, plan.MinWallHeight, 4);
        Assert.Equal(2f, plan.MaxLowering, 4);
    }

    [Fact]
    public void GroundAlreadyLowerIsLeftAsItIsSoADigJoinsAnEarlierHole()
    {
        // An earlier 4 m hole under the west half of the new floor: the old planner refused this (FloorAlreadyDeeper).
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 4, 4, 2);
        Func<int, int, float> terrain = (x, z) => x <= 1 && footprint.ContainsFloor(x, z) ? Ground - 4f : Ground;
        DigPlan plan = Plan(footprint, terrain, out DigSamples samples);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(DigRefusal.None, plan.Refusal);
        Assert.Equal(2 * 5, plan.AlreadyLower);
        Assert.Equal(3 * 5, plan.LoweredVertices);
        IReadOnlyList<DigVertex> lowered = DigPlanner.Lowered(plan, samples);
        Assert.Equal(plan.LoweredVertices, lowered.Count);
        Assert.All(lowered, vertex => Assert.True(vertex.X >= 2));
        Assert.All(lowered, vertex => Assert.Equal((Ground, Ground - 2f), (vertex.Before, vertex.After)));
        // The deeper part is not raised: lowering never raises ground.
        Assert.Equal(Ground - 4f, DigPlanner.PlannedHeight(plan, samples, 0, 0));
        Assert.Equal(Ground - 2f, DigPlanner.PlannedHeight(plan, samples, 3, 3));
        Assert.Equal(Ground, DigPlanner.PlannedHeight(plan, samples, -1, 0));
    }

    [Fact]
    public void AnAdjacentDigOfTheSameDepthLandsOnTheSameFloor()
    {
        // The first hole, x 0..4, has been dug 2 m. The second, x 4..8, shares the edge column x 4, which is in its
        // floor, and its ring at x 3 is in the first hole (lower): the datum is still the untouched ground.
        Func<int, int, float> terrain = (x, z) => x >= 0 && x <= 4 && z >= 0 && z <= 4 ? Ground - 2f : Ground;
        DigPlan second = Plan(DigFootprint.FromCorners(4, 0, 8, 4, 2), terrain);
        Assert.Equal(Ground, second.Datum);
        Assert.Equal(Ground - 2f, second.Bottom);
        Assert.Equal(4 * 5, second.LoweredVertices);
        Assert.True(second.MinWallHeight < 0.01f);
    }

    [Fact]
    public void ReplanningAfterTheDigFindsNothingToDig()
    {
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 3, 3, 3);
        DigPlan first = Plan(footprint, (x, z) => Ground, out DigSamples samples);
        DigPlan again = Plan(footprint, (x, z) => DigPlanner.PlannedHeight(first, samples, x, z));
        Assert.Equal(ExcavationStatus.NothingToDig, again.Status);
        Assert.Equal(first.Bottom, again.Bottom);
    }

    [Fact]
    public void OnASlopeTheFloorIsLevelAndTheDatumIsTheHighestRingVertex()
    {
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 4, 4, 2);
        DigPlan plan = Plan(footprint, (x, z) => Ground + 0.5f * x, out DigSamples samples);
        Assert.Equal(Ground + 0.5f * 5, plan.Datum);
        Assert.Equal(Ground + 0.5f, plan.Bottom);
        Assert.Equal(Ground + 0.5f * -1 - plan.Bottom, plan.MinWallHeight, 4);
        // The west column (x 0) is at the floor already; everything else is lowered to it.
        Assert.Equal(5, plan.AlreadyLower);
        Assert.All(DigPlanner.Lowered(plan, samples), vertex => Assert.Equal(plan.Bottom, vertex.After));
    }

    [Fact]
    public void UnloadedOrUnreadableGroundAndTheEngineClampRefuse()
    {
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 3, 3, 2);
        DigPlan unloaded = Plan(footprint, (x, z) => x == -1 && z == 2 ? float.NaN : Ground, unloadNaN: true);
        Assert.Equal((ExcavationStatus.Refused, DigRefusal.Unloaded, 1), (unloaded.Status, unloaded.Refusal, unloaded.RefusalVertices));
        DigPlan nan = Plan(footprint, (x, z) => x == 1 && z == 1 ? float.PositiveInfinity : Ground);
        Assert.Equal(DigRefusal.NotFinite, nan.Refusal);
        // Ground an earlier dig already took 7 m below its natural surface: a 2 m dig would go 9 m below it.
        DigPlan limit = Plan(footprint, (x, z) => Ground, limitBase: (x, z) => Ground + 7f);
        Assert.Equal((ExcavationStatus.Refused, DigRefusal.DigLimit, 16), (limit.Status, limit.Refusal, limit.RefusalVertices));
        Assert.Empty(DigPlanner.Lowered(limit, new DigSamples()));
    }

    [Fact]
    public void SamplesForAnotherRectangleAreRefusedOutright()
    {
        var samples = new DigSamples();
        samples.Begin(DigFootprint.FromCorners(0, 0, 3, 3, 2));
        Assert.Throws<ArgumentException>(() => DigPlanner.Plan(samples, DigFootprint.FromCorners(0, 0, 4, 3, 2), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Set(10, 10, 0f, 0f));
    }

    [Fact]
    public void TheLargestFootprintFitsTheSampleGrid()
    {
        DigFootprint largest = DigFootprint.FromCorners(-100, 100, 100, -100, DigFootprint.MaximumDepth);
        Assert.Equal((8, 8, 7), (largest.Width, largest.Length, largest.Depth));
        DigPlan plan = Plan(largest, (x, z) => Ground);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        Assert.Equal(81, plan.LoweredVertices);
    }

    [Fact]
    public void TheDeepestDigIsReachableOnNaturalGroundAndOneMoreMetreIsNot()
    {
        // Natural ground: the limit base is the height itself. 7 m is clear of the engine's 8 m clamp; a floor on the
        // clamp is refused (within 1 cm), which is why the depth stops at 7.
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 3, 3, DigFootprint.MaximumDepth);
        Assert.Equal(ExcavationStatus.Ready, Plan(footprint, (x, z) => Ground).Status);
        // Ground an earlier dig already took 1 m (less half a centimetre) below its natural surface: a 7 m floor from
        // here is then on the 8 m clamp below that surface, and refused.
        DigPlan onTheClamp = Plan(footprint, (x, z) => Ground, limitBase: (x, z) => Ground + 1f - 0.005f);
        Assert.Equal(DigRefusal.DigLimit, onTheClamp.Refusal);
    }

    [Fact]
    public void AFloorBelowTheWaterSaysItWillFlood()
    {
        DigFootprint footprint = DigFootprint.FromCorners(0, 0, 3, 3, 2);
        DigPlan dry = Plan(footprint, (x, z) => Ground, water: Ground - 3f);
        DigPlan wet = Plan(footprint, (x, z) => Ground, water: Ground - 1f);
        Assert.False(dry.Floods);
        Assert.True(wet.Floods);
        Assert.Null(DigText.Plan(dry));
        Assert.Contains("flood", DigText.Plan(wet), StringComparison.Ordinal);
    }

    // ---- Read-back ----

    [Fact]
    public void ADigIsCompleteOnlyWhenEveryLoweredVertexIsAtTheFloor()
    {
        var record = new DigRecord(DigFootprint.FromCorners(0, 0, 2, 2, 2), new[] { new DigVertex(0, 0, Ground, Ground - 2f), new DigVertex(1, 0, Ground, Ground - 2f) });
        Assert.Equal(ExcavationProgress.Complete, DigPlanner.Progress(record, (x, z) => Ground - 2f));
        Assert.Equal(ExcavationProgress.Pending, DigPlanner.Progress(record, (x, z) => x == 1 ? Ground : Ground - 2f));
        Assert.Equal(ExcavationProgress.Overshot, DigPlanner.Progress(record, (x, z) => Ground - 2.5f));
        Assert.Equal(ExcavationProgress.Unreadable, DigPlanner.Progress(record, (x, z) => x == 1 ? null : Ground - 2f));
    }

    // ---- Undo ----

    [Fact]
    public void UndoRunsOnlyOverTheFloorTheDigLeft()
    {
        var record = new DigRecord(DigFootprint.FromCorners(0, 0, 2, 2, 2), new[] { new DigVertex(0, 0, Ground, Ground - 2f), new DigVertex(1, 1, Ground + 0.4f, Ground - 2f) });
        Assert.Equal(UndoRefusal.Nothing, DigUndo.Check(null, (x, z) => 0f));
        Assert.Equal(UndoRefusal.None, DigUndo.Check(record, (x, z) => Ground - 2f));
        Assert.Equal(UndoRefusal.Changed, DigUndo.Check(record, (x, z) => x == 1 ? Ground - 2.5f : Ground - 2f));
        Assert.Equal(UndoRefusal.Changed, DigUndo.Check(record, (x, z) => x == 1 ? Ground - 1f : Ground - 2f));
        Assert.Equal(UndoRefusal.Unloaded, DigUndo.Check(record, (x, z) => x == 1 ? null : Ground - 2f));
        // Read back: complete at the heights before, pending below them, overshot above.
        Assert.Equal(ExcavationProgress.Complete, DigUndo.Progress(record, (x, z) => x == 1 ? Ground + 0.4f : Ground));
        Assert.Equal(ExcavationProgress.Pending, DigUndo.Progress(record, (x, z) => Ground));
        Assert.Equal(ExcavationProgress.Overshot, DigUndo.Progress(record, (x, z) => Ground + 1f));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(8f, 1)]
    [InlineData(8.5f, 2)]
    [InlineData(16f, 2)]
    public void AVertexLoweredFurtherThanOneRaiseTakesMore(float rise, int operations) =>
        Assert.Equal(operations, DigUndo.RaiseOperations(new DigVertex(0, 0, Ground, Ground - rise)));

    [Fact]
    public void TheHistoryKeepsTheNewestTenAndUndoesNewestFirst()
    {
        var history = new DigHistory();
        var records = Enumerable.Range(0, 12).Select(i => new DigRecord(DigFootprint.FromCorners(i * 10, 0, i * 10 + 2, 2, 2), new[] { new DigVertex(i * 10, 0, Ground, Ground - 2f) })).ToList();
        foreach (DigRecord record in records) history.Push(record);
        Assert.Equal(DigHistory.Capacity, history.Count);
        Assert.Same(records[11], history.Last);
        Assert.False(history.Remove(records[5]), "Only the newest dig can be undone.");
        Assert.True(history.Remove(records[11]));
        Assert.Same(records[10], history.Last);
        history.Clear();
        Assert.Null(history.Last);
        Assert.Throws<ArgumentException>(() => new DigRecord(DigFootprint.FromCorners(0, 0, 2, 2, 2), Array.Empty<DigVertex>()));
    }

    // ---- The lines ----

    [Fact]
    public void EveryLineIsOneShortLine()
    {
        var lines = new List<string>();
        foreach (DigFootprint footprint in new[] { DigFootprint.FromCorners(0, 0, 2, 2, 1), DigFootprint.FromCorners(0, 0, 8, 8, DigFootprint.MaximumDepth) })
        {
            foreach (DigStage stage in Enum.GetValues(typeof(DigStage))) lines.Add(DigText.Prompt(stage, footprint));
            lines.Add(DigText.Dug(footprint));
            DigPlan flooded = Plan(footprint, (x, z) => Ground, water: Ground);
            lines.Add(DigText.Plan(flooded)!);
        }

        foreach (DigRefusal refusal in Enum.GetValues(typeof(DigRefusal)))
            lines.Add(DigText.Plan(new DigPlan(DigFootprint.FromCorners(0, 0, 2, 2, 2), ExcavationStatus.Refused, refusal, 1, float.NaN, 0, 0f, 0, 0f, float.NaN, 0f, 0, 1, 0, 1))!);
        lines.Add(DigText.Plan(new DigPlan(DigFootprint.FromCorners(0, 0, 2, 2, 2), ExcavationStatus.NothingToDig, DigRefusal.None, 0, Ground, 0, 0f, 0, 0f, 2f, 0f, 0, 1, 0, 1))!);
        foreach (ExcavationCommitRefusal refusal in Enum.GetValues(typeof(ExcavationCommitRefusal)))
        {
            if (refusal == ExcavationCommitRefusal.None) continue;
            foreach (ExcavationReadFault fault in Enum.GetValues(typeof(ExcavationReadFault)))
            {
                lines.Add(DigText.Refusal(refusal, fault, default, new string('W', 200)));
                lines.Add(DigText.Refusal(refusal, fault, default, null));
            }
        }

        foreach (ExcavationAim aim in Enum.GetValues(typeof(ExcavationAim))) lines.Add(DigText.Aim(aim, new string('R', 90)));
        foreach (ExcavationOutcome outcome in Enum.GetValues(typeof(ExcavationOutcome)))
        {
            if (outcome == ExcavationOutcome.Waiting || outcome == ExcavationOutcome.Abandoned) continue;
            foreach (DigFlightKind kind in Enum.GetValues(typeof(DigFlightKind)))
                lines.Add(DigText.Outcome(outcome, kind, DigFootprint.FromCorners(0, 0, 8, 8, DigFootprint.MaximumDepth)));
        }

        lines.Add(DigText.UndoArmed);

        foreach (UndoRefusal refusal in Enum.GetValues(typeof(UndoRefusal))) lines.Add(DigText.Undo(refusal));
        Assert.All(lines, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.True(line.Length <= DigText.MaximumLength, "Too long (" + line.Length + "): " + line);
            Assert.DoesNotContain('\n', line);
        });
    }

    // ---- Helpers ----

    private static DigPlan Plan(DigFootprint footprint, Func<int, int, float> height, float? water = null,
        Func<int, int, float>? limitBase = null, bool unloadNaN = false) =>
        Plan(footprint, height, out _, water, limitBase, unloadNaN);

    private static DigPlan Plan(DigFootprint footprint, Func<int, int, float> height, out DigSamples samples, float? water = null,
        Func<int, int, float>? limitBase = null, bool unloadNaN = false)
    {
        samples = new DigSamples();
        samples.Begin(footprint);
        for (int z = footprint.MinZ - 1; z <= footprint.MaxZ + 1; z++)
        {
            for (int x = footprint.MinX - 1; x <= footprint.MaxX + 1; x++)
            {
                float h = height(x, z);
                if (unloadNaN && float.IsNaN(h)) { samples.SetUnloaded(x, z); continue; }
                samples.Set(x, z, h, limitBase?.Invoke(x, z) ?? h);
            }
        }

        return DigPlanner.Plan(samples, footprint, water);
    }
}
