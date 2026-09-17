using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#313: the smaller pieces the executor stands on — stillness,
/// safe parking, the body census, the defaults, and the placeholders that stand
/// in for agent B's navigation until it lands.</summary>
public class HaulingExecutionPartsTests
{
    [Fact]
    public void StillnessNeedsTheWholeWindowWithoutABreak()
    {
        var limits = HaulLimits.Default;
        var still = new CartStillnessTracker(limits);
        still.Observe(10f, 0.1f);
        Assert.False(still.IsStill(10.5f));
        still.Observe(10.5f, 0.1f);
        Assert.True(still.IsStill(11f));

        still.Observe(11.125f, 0.2f);
        Assert.False(still.IsStill(11.125f));
        still.Observe(11.25f, 0f);
        Assert.False(still.IsStill(12f));
        Assert.True(still.IsStill(12.25f));

        still.Observe(13f, float.NaN);
        Assert.False(still.IsStill(20f));
    }

    [Fact]
    public void ParkingNeedsMeasuredDryGentleGroundAnUprightCartAndStillness()
    {
        var limits = HaulLimits.Default;
        var execution = HaulExecutionLimits.Default;
        CartObservation cart = FakeSeam.HealthyCart();
        var flat = new ParkingGround { Measured = true, GradeAlongRatio = 0.05f, GradeAcrossRatio = -0.05f };

        Assert.True(ParkingJudge.Evaluate(flat, cart, cartStill: true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart, cartStill: false, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround(), cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, InWater = true }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, GradeAlongRatio = 0.051f }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(new ParkingGround { Measured = true, GradeAcrossRatio = float.NaN }, cart, true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart.With(o => o.UpDot = 0.4f), true, limits, execution).Safe);
        Assert.False(ParkingJudge.Evaluate(flat, cart.With(o => o.Resolved = false), true, limits, execution).Safe);
    }

    [Fact]
    public void TheCensusNeverDuplicatesAndNeverGuesses()
    {
        Assert.Equal(WorkerBodyStatus.Searching, WorkerBodyCensus.Decide(false, 0, 0, false));
        Assert.Equal(WorkerBodyStatus.NotFound, WorkerBodyCensus.Decide(true, 0, 0, false));
        Assert.Equal(WorkerBodyStatus.NotLoaded, WorkerBodyCensus.Decide(true, 1, 0, false));
        Assert.Equal(WorkerBodyStatus.Bound, WorkerBodyCensus.Decide(true, 1, 1, false));
        Assert.Equal(WorkerBodyStatus.Bound, WorkerBodyCensus.Decide(false, 1, 1, false));
        Assert.Equal(WorkerBodyStatus.Faulted, WorkerBodyCensus.Decide(true, 1, 1, true));
        Assert.Equal(WorkerBodyStatus.Duplicated, WorkerBodyCensus.Decide(true, 2, 1, false));
        Assert.Equal(WorkerBodyStatus.Duplicated, WorkerBodyCensus.Decide(false, 1, 2, false));

        foreach (WorkerBodyStatus status in Enum.GetValues<WorkerBodyStatus>())
        {
            Assert.Equal(status == WorkerBodyStatus.NotFound, WorkerBodyCensus.MaySpawn(status));
            Assert.Equal(
                status == WorkerBodyStatus.Unspecified,
                WorkerBodyCensus.Describe(status) == HaulRefusalSentences.BugSentence);
        }
    }

    [Fact]
    public void TheDefaultsAreFrozenAndHaulingIsOff()
    {
        Assert.False(GunnarHaulingDefaults.HaulingEnabled);
        Assert.Equal(GunnarPullStrength.MatchPlayer, GunnarHaulingDefaults.PullStrength);
        Assert.Equal("Dverger", GunnarHaulingDefaults.WorkerBaseCreature);
        Assert.Equal("CT_TeamsterWorker", GunnarHaulingDefaults.WorkerPrefabName);
        Assert.Equal("tcc.worker.key", GunnarHaulingDefaults.WorkerKeyField);
        Assert.Equal("teamster/gunnar", WorkerKey.Gunnar.Value);
        Assert.Equal(0, (int)GunnarPullStrength.Unspecified);
        Assert.Equal(new[] { "Unspecified", "MatchPlayer" }, Enum.GetNames<GunnarPullStrength>());
    }

    [Fact]
    public void ExecutionLimitsAreValidatedAndDocumentedValuesHold()
    {
        HaulExecutionLimits limits = HaulExecutionLimits.Default.Validate();
        Assert.Equal(0.05f, limits.MaxParkingGradeRatio);
        Assert.Equal(0.8f, limits.JointStrainRatio);
        Assert.Equal(0.1f, HaulExecutionLimits.VanillaTipUpDot);

        limits.MaxParkingGradeRatio = 0.5f;
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.Validate());

        Assert.True(HaulExecutionLimits.Default.MassesAgree(70f, 70.5f));
        Assert.False(HaulExecutionLimits.Default.MassesAgree(70f, 72f));
        Assert.False(HaulExecutionLimits.Default.MassesAgree(float.NaN, 70f));
    }

    [Fact]
    public void AMotionMonitorPlaceholderJudgesProgressFromBothBodies()
    {
        var limits = HaulLimits.Default;
        var monitor = new PlaceholderHaulMotionMonitor(limits);
        var origin = new WorkPoint(0f, 0f, 0f);

        monitor.Sample(0f, origin, origin, motorCommanded: false);
        Assert.Equal(HaulMotion.Idle, monitor.Current);

        monitor.Sample(1f, origin, origin, motorCommanded: true);
        Assert.Equal(HaulMotion.Progressing, monitor.Current);
        monitor.Sample(1f + limits.StallWindowSeconds, new WorkPoint(0f, 0f, 3f), new WorkPoint(0f, 0f, 3f), true);
        Assert.Equal(HaulMotion.Progressing, monitor.Current);

        // Gunnar walks, the cart does not follow: something holds the cart.
        monitor.Sample(1f + (2 * limits.StallWindowSeconds), new WorkPoint(0f, 0f, 5f), new WorkPoint(0f, 0f, 3.1f), true);
        Assert.Equal(HaulMotion.Wedged, monitor.Current);

        // Neither moves: stalled.
        monitor.Sample(1f + (3 * limits.StallWindowSeconds), new WorkPoint(0f, 0f, 5.1f), new WorkPoint(0f, 0f, 3.2f), true);
        Assert.Equal(HaulMotion.Stalled, monitor.Current);

        monitor.Reset();
        Assert.Equal(HaulMotion.Idle, monitor.Current);
    }

    [Fact]
    public void ThePlaceholderPlannerVouchesOnlyForAClearStraightSegmentAhead()
    {
        var probe = new FakeSegmentProbe();
        var planner = new StraightLinePlaceholderPlanner(probe, HaulLimits.Default, HaulExecutionLimits.Default);
        var footprint = new CartFootprint(1.5f, 2.5f, 1.5f);
        CartRouteRequest Ahead(float metres) =>
            new CartRouteRequest(new WorkPoint(0f, 10f, 0f), new WorkPoint(0f, 10f, metres), footprint, 120f, 1);

        CartRoutePlan plan = planner.Plan(Ahead(20f), 0f);
        Assert.True(plan.IsSuitable);
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.Equal(2.1f, plan.NarrowestClearanceMetres, 3);
        Assert.NotNull(plan.StopPoint);
        Assert.Equal(15, probe.Samples);

        SteeringGoal? goal = planner.NextGoal(plan, new WorkPoint(0f, 10f, 2f), new WorkPoint(0f, 10f, 0.5f));
        Assert.NotNull(goal);
        Assert.True(goal!.Value.IsFinalStop);
        Assert.Equal(20f, goal.Value.Target.Z);
        Assert.Null(planner.NextGoal(plan, new WorkPoint(3f, 10f, 5f), new WorkPoint(2f, 10f, 5f)));

        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(Ahead(70f), 1f).Verdict);
        Assert.Equal(
            CartRouteVerdict.NoPath,
            planner.Plan(new CartRouteRequest(new WorkPoint(0f, 10f, 0f), new WorkPoint(0f, 10f, -20f), footprint, 120f, 1), 2f).Verdict);

        probe.UnloadedAfter = 3;
        Assert.Equal(CartRouteVerdict.OutsideLoadedArea, planner.Plan(Ahead(20f), 3f).Verdict);
        probe.Reset();
        probe.GapAt = 4;
        Assert.Equal(CartRouteVerdict.UnsupportedGap, planner.Plan(Ahead(20f), 4f).Verdict);
        probe.Reset();
        probe.WaterAt = 5;
        Assert.Equal(CartRouteVerdict.Water, planner.Plan(Ahead(20f), 5f).Verdict);
        probe.Reset();
        probe.BlockedAt = 6;
        Assert.Equal(CartRouteVerdict.TooNarrow, planner.Plan(Ahead(20f), 6f).Verdict);
        probe.Reset();
        probe.RisePerSample = 0.5f;
        Assert.Equal(CartRouteVerdict.TooSteep, planner.Plan(Ahead(20f), 7f).Verdict);
        probe.Reset();
        probe.LastStepRise = 0.1f;
        Assert.Equal(CartRouteVerdict.UnsafeStop, planner.Plan(Ahead(20f), 8f).Verdict);
        probe.Reset();
        probe.HeadingKnown = false;
        Assert.Equal(CartRouteVerdict.NoPath, planner.Plan(Ahead(20f), 9f).Verdict);
    }

    [Fact]
    public void ThePlaceholderPlannerKeepsItsQueryBudget()
    {
        var limits = HaulLimits.Default;
        limits.PathQueriesPerMinute = 3;
        var planner = new StraightLinePlaceholderPlanner(new FakeSegmentProbe(), limits, HaulExecutionLimits.Default);
        var request = new CartRouteRequest(new WorkPoint(0f, 0f, 0f), new WorkPoint(0f, 0f, 10f), new CartFootprint(1.5f, 2.5f, 1.5f), 50f, 1);

        Assert.True(planner.Plan(request, 0f).IsSuitable);
        Assert.True(planner.Plan(request, 10f).IsSuitable);
        Assert.True(planner.Plan(request, 20f).IsSuitable);
        Assert.Equal(CartRouteVerdict.BudgetExhausted, planner.Plan(request, 30f).Verdict);
        Assert.True(planner.Plan(request, 60.5f).IsSuitable);
    }
}

internal sealed class FakeSegmentProbe : IStraightSegmentProbe
{
    private int _index;

    public int Samples => _index;

    public int UnloadedAfter { get; set; } = -1;

    public int GapAt { get; set; } = -1;

    public int WaterAt { get; set; } = -1;

    public int BlockedAt { get; set; } = -1;

    public float RisePerSample { get; set; }

    public float LastStepRise { get; set; }

    public bool HeadingKnown { get; set; } = true;

    public void Reset()
    {
        _index = 0;
        UnloadedAfter = -1;
        GapAt = -1;
        WaterAt = -1;
        BlockedAt = -1;
        RisePerSample = 0f;
        LastStepRise = 0f;
        HeadingKnown = true;
    }

    public StraightSegmentSample Sample(WorkPoint at, float directionX, float directionZ, float halfWidthMetres, float sampleLengthMetres)
    {
        int index = _index++;
        float height = 10f + (index * RisePerSample);
        if (LastStepRise > 0f && at.Z >= 19.99f)
        {
            height += LastStepRise;
        }

        return new StraightSegmentSample
        {
            Loaded = UnloadedAfter < 0 || index < UnloadedAfter,
            GroundKnown = index != GapAt,
            GroundHeight = height,
            InWater = index == WaterAt,
            Clear = index != BlockedAt,
        };
    }

    public bool TryReadCartHeading(out float headingX, out float headingZ)
    {
        headingX = 0f;
        headingZ = 1f;
        return HeadingKnown;
    }
}
