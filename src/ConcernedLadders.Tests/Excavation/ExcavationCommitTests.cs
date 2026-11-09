using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 2: the commit gate's order and the one-operation-in-flight rule. The runtime adapter calls these
/// and adds only game reads; in-game behaviour is OWNER GO-AROUND PENDING.</summary>
public sealed class ExcavationCommitTests
{
    private static VanillaTerrainModel Flat(float height = 50f) => new((_, _) => height);

    private static ExcavationPlan Ready(VanillaTerrainModel world, ExcavationMode mode = ExcavationMode.Small, int cx = 0, int cz = 0)
    {
        ExcavationPlan plan = ExcavationPlanner.Plan(world.Sample(mode, cx, cz), cx, cz, null);
        Assert.Equal(ExcavationStatus.Ready, plan.Status);
        return plan;
    }

    private static ExcavationCommitRefusal Before(bool enabled = true, bool available = true, bool registered = true,
        bool opsDisabled = false, bool busy = false, ExcavationReadFault fault = ExcavationReadFault.None, ExcavationPlan? plan = null) =>
        ExcavationGate.BeforeWorldChecks(enabled, available, registered, opsDisabled, busy, fault, plan ?? Ready(Flat()));

    [Fact]
    public void AReadyPlanWithEveryPrerequisitePasses()
    {
        Assert.Equal(ExcavationCommitRefusal.None, Before());
        Assert.Equal(ExcavationCommitRefusal.None, ExcavationGate.WorldChecks(true, false, 0, false));
    }

    [Fact]
    public void TheSwitchIsAskedFirstAndOffRefusesEverything()
    {
        Assert.Equal(ExcavationCommitRefusal.Disabled, Before(enabled: false, available: false, registered: false, opsDisabled: true,
            busy: true, fault: ExcavationReadFault.Rebuilding));
    }

    [Fact]
    public void EachPrerequisiteRefusesInOrder()
    {
        Assert.Equal(ExcavationCommitRefusal.Unavailable, Before(available: false, registered: false, busy: true));
        Assert.Equal(ExcavationCommitRefusal.NotRegistered, Before(registered: false, opsDisabled: true, busy: true));
        Assert.Equal(ExcavationCommitRefusal.TerrainOpsDisabled, Before(opsDisabled: true, busy: true));
        Assert.Equal(ExcavationCommitRefusal.Busy, Before(busy: true, fault: ExcavationReadFault.LegacyTerrainModifier));
        Assert.Equal(ExcavationCommitRefusal.TerrainUnreadable, Before(fault: ExcavationReadFault.UnsupportedLattice));
    }

    [Theory]
    [InlineData((int)ExcavationReadFault.UnsupportedLattice)]
    [InlineData((int)ExcavationReadFault.Rebuilding)]
    [InlineData((int)ExcavationReadFault.LegacyTerrainModifier)]
    [InlineData((int)ExcavationReadFault.CompilerUnreadable)]
    [InlineData((int)ExcavationReadFault.TilesDisagree)]
    public void EveryReadFaultRefusesEvenWithAReadyPlan(int faultValue)
    {
        Assert.Equal(ExcavationCommitRefusal.TerrainUnreadable, Before(fault: (ExcavationReadFault)faultValue));
    }

    [Fact]
    public void ARefusedOrEmptyPlanNeverPasses()
    {
        VanillaTerrainModel world = Flat();
        world.MarkUnloaded(2, 2);
        ExcavationPlan refused = ExcavationPlanner.Plan(world.Sample(ExcavationMode.Small, 0, 0), 0, 0, null);
        Assert.Equal(ExcavationStatus.Refused, refused.Status);
        Assert.Equal(ExcavationCommitRefusal.PlanRefused, Before(plan: refused));

        VanillaTerrainModel dug = Flat();
        ExcavationPlan first = Ready(dug);
        dug.Apply(first);
        dug.Rebuild();
        ExcavationPlan again = ExcavationPlanner.Plan(dug.Sample(ExcavationMode.Small, 0, 0), 0, 0, null);
        Assert.Equal(ExcavationStatus.NothingToDig, again.Status);
        Assert.Equal(ExcavationCommitRefusal.NothingToDig, Before(plan: again));
    }

    [Fact]
    public void ADefaultPlanIsNeverMistakenForReady()
    {
        // default(ExcavationPlan) has Status 0, which is Ready; it lowers nothing and must never pass the gate or the slot.
        ExcavationPlan never = default;
        Assert.Equal(ExcavationStatus.Ready, never.Status);
        Assert.Equal(ExcavationCommitRefusal.PlanRefused, Before(plan: never));
        Assert.False(new ExcavationInFlight().TryStart(never, 1f));
    }

    [Fact]
    public void WorldChecksRefuseInOrder()
    {
        Assert.Equal(ExcavationCommitRefusal.Protected, ExcavationGate.WorldChecks(false, true, 3, true));
        Assert.Equal(ExcavationCommitRefusal.NoBuildArea, ExcavationGate.WorldChecks(true, true, 3, true));
        Assert.Equal(ExcavationCommitRefusal.ScanOverflow, ExcavationGate.WorldChecks(true, false, 0, true));
        Assert.Equal(ExcavationCommitRefusal.Obstructed, ExcavationGate.WorldChecks(true, false, 1, false));
    }

    [Fact]
    public void OnlyOneOperationIsEverInFlight()
    {
        VanillaTerrainModel world = Flat();
        ExcavationPlan plan = Ready(world);
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(plan, 1f));
        Assert.True(slot.IsActive);
        Assert.False(slot.TryStart(Ready(Flat(), ExcavationMode.Large, 20, 20), 1.5f));
        Assert.Equal(ExcavationCommitRefusal.Busy, Before(busy: slot.IsActive));
        Assert.Equal(plan.CentreX, slot.Plan.CentreX);
        Assert.Equal(plan.Bottom, slot.Plan.Bottom);
    }

    [Fact]
    public void TheSlotIsFreedOnlyByAReadBackTheTimeoutOrTheWorldGoingAway()
    {
        VanillaTerrainModel world = Flat();
        ExcavationPlan plan = Ready(world);
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(plan, 10f));
        world.Apply(plan);

        // Before the rebuild the terrain still reads as it was: Pending, keep waiting, and the gate stays Busy.
        Assert.Equal(ExcavationProgress.Pending, ExcavationPlanner.Check(plan, world.Sample(ExcavationMode.Small, 0, 0)));
        Assert.Equal(ExcavationOutcome.Waiting, slot.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, 10.5f));
        Assert.Equal(ExcavationOutcome.Waiting, slot.Observe(ExcavationReadFault.Rebuilding, ExcavationProgress.Unreadable, 11f));
        // One tile under the hole rebuilt, its neighbour not yet: the shared edge disagrees, and that is still arriving.
        Assert.Equal(ExcavationOutcome.Waiting, slot.Observe(ExcavationReadFault.TilesDisagree, ExcavationProgress.Unreadable, 11.2f));
        Assert.True(slot.IsActive);

        world.Rebuild();
        ExcavationProgress progress = ExcavationPlanner.Check(plan, world.Sample(ExcavationMode.Small, 0, 0));
        Assert.Equal(ExcavationOutcome.Complete, slot.Observe(ExcavationReadFault.None, progress, 11.5f));
        Assert.False(slot.IsActive);

        // The next commit is a fresh plan, and on the finished hole it is free and refused as nothing to dig.
        ExcavationPlan again = ExcavationPlanner.Plan(world.Sample(ExcavationMode.Small, 0, 0), 0, 0, null);
        Assert.Equal(ExcavationCommitRefusal.NothingToDig, Before(plan: again));
        Assert.False(slot.TryStart(again, 12f));
    }

    [Fact]
    public void TheBusyRuleIsWhatPreventsTheStaleDoubleCount()
    {
        // Two commits before the rebuild overshoot (the engine reads stale heights). The slot refuses the second.
        VanillaTerrainModel world = Flat();
        ExcavationPlan plan = Ready(world);
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(plan, 1f));
        world.Apply(plan);
        ExcavationPlan second = ExcavationPlanner.Plan(world.Sample(ExcavationMode.Small, 0, 0), 0, 0, null);
        Assert.Equal(ExcavationStatus.Ready, second.Status); // stale heights still look undug
        Assert.Equal(ExcavationCommitRefusal.Busy, Before(busy: slot.IsActive, plan: second));
        Assert.False(slot.TryStart(second, 1f));
        world.Rebuild();
        Assert.Equal(ExcavationOutcome.Complete,
            slot.Observe(ExcavationReadFault.None, ExcavationPlanner.Check(plan, world.Sample(ExcavationMode.Small, 0, 0)), 2f));
        Assert.Equal(48f, world.Displayed(0, 0), 4);
    }

    [Theory]
    [InlineData((int)ExcavationProgress.Overshot, (int)ExcavationOutcome.Overshot)]
    [InlineData((int)ExcavationProgress.Unreadable, (int)ExcavationOutcome.Unreadable)]
    [InlineData((int)ExcavationProgress.Complete, (int)ExcavationOutcome.Complete)]
    public void ATerminalReadBackEndsTheFlightAtOnce(int progressValue, int outcomeValue)
    {
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(Ready(Flat()), 0f));
        Assert.Equal((ExcavationOutcome)outcomeValue, slot.Observe(ExcavationReadFault.None, (ExcavationProgress)progressValue, 0.1f));
        Assert.False(slot.IsActive);
    }

    [Theory]
    [InlineData((int)ExcavationReadFault.UnsupportedLattice)]
    [InlineData((int)ExcavationReadFault.LegacyTerrainModifier)]
    [InlineData((int)ExcavationReadFault.CompilerUnreadable)]
    public void AReadFaultOtherThanARebuildIsUnreadableNotPending(int faultValue)
    {
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(Ready(Flat()), 0f));
        Assert.Equal(ExcavationOutcome.Unreadable, slot.Observe((ExcavationReadFault)faultValue, ExcavationProgress.Complete, 0.1f));
        Assert.False(slot.IsActive);
    }

    [Fact]
    public void APendingFloorTimesOutWithoutARetry()
    {
        var slot = new ExcavationInFlight();
        Assert.True(slot.TryStart(Ready(Flat()), 100f));
        Assert.Equal(ExcavationOutcome.Waiting, slot.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, 100f + ExcavationInFlight.TimeoutSeconds));
        Assert.True(slot.IsActive);
        Assert.Equal(ExcavationOutcome.TimedOut, slot.Observe(ExcavationReadFault.Rebuilding, ExcavationProgress.Pending, 100.01f + ExcavationInFlight.TimeoutSeconds));
        Assert.False(slot.IsActive);
        Assert.Throws<InvalidOperationException>(() => slot.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, 200f));
    }

    [Fact]
    public void AbandoningFreesTheSlotOnce()
    {
        var slot = new ExcavationInFlight();
        Assert.False(slot.Abandon());
        Assert.True(slot.TryStart(Ready(Flat()), 0f));
        Assert.True(slot.Abandon());
        Assert.False(slot.IsActive);
        Assert.False(slot.Abandon());
    }

    [Fact]
    public void ANonFiniteClockNeitherStartsNorWaitsForever()
    {
        var slot = new ExcavationInFlight();
        Assert.False(slot.TryStart(Ready(Flat()), float.NaN));
        Assert.True(slot.TryStart(Ready(Flat()), 0f));
        Assert.Equal(ExcavationOutcome.TimedOut, slot.Observe(ExcavationReadFault.None, ExcavationProgress.Pending, float.NaN));
    }
}
