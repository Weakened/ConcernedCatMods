using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;
using static ConcernedTeamster.Tests.NavigationFixtures;

namespace ConcernedTeamster.Tests;

/// <summary>#314 CART-05/CART-06: local steering, plan refresh, progress judged
/// from both bodies, the failure cache, bounded recovery and staging choice.
/// </summary>
public class HaulingNavigationMotionTests
{
    // -- steering --------------------------------------------------------------

    [Fact]
    public void SteeringHandsOutWaypointsInOrderAndOnlyTheStopIsFinal()
    {
        var (planner, _, paths) = Planner();
        paths.Corners = new List<WorkPoint> { P(0, 0), P(15, 0), P(15, 15), P(30, 15) };
        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(30, 15)), 0f);
        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());

        var targets = new List<int>();
        WorkPoint cart = P(-2.21f, 0f);
        for (int index = 0; index < plan.Waypoints.Count; index++)
        {
            WorkPoint puller = plan.Waypoints[index];
            CartSteering steering = planner.Follow(plan, puller, cart);
            if (index == plan.Waypoints.Count - 1)
            {
                Assert.Equal(CartSteeringStatus.Finished, steering.Status);
                Assert.Null(planner.NextGoal(plan, puller, cart));
                break;
            }

            Assert.Equal(CartSteeringStatus.Following, steering.Status);
            SteeringGoal goal = steering.Goal!.Value;
            Assert.Equal(index + 1, goal.WaypointIndex);
            Assert.Equal(plan.Waypoints[index + 1], goal.Target);
            Assert.Equal(index + 1 == plan.Waypoints.Count - 1, goal.IsFinalStop);
            Assert.Equal(CartRouteGeometry.WaypointArrivalRadiusMetres, goal.ArrivalRadiusMetres);
            Assert.Equal(plan.RequestRevision, goal.PlanRevision);
            targets.Add(goal.WaypointIndex);
            cart = planner.LastAssessment!.CartTrack[Math.Min(planner.LastAssessment.CartTrack.Count - 1, index)];
        }

        Assert.Equal(targets.OrderBy(value => value), targets);
    }

    [Fact]
    public void SteeringNeverGoesBackToAWaypointAlreadyPassed()
    {
        var (planner, _, _) = Planner();
        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(30, 0)), 0f);
        var assessment = planner.LastAssessment!;
        Assert.True(plan.IsSuitable);

        // A straight route simplifies to its ends; walk well along it and back.
        Assert.Equal(1, planner.NextGoal(plan, P(20, 0), P(17.79f, 0))!.Value.WaypointIndex);
        Assert.Equal(1, planner.NextGoal(plan, P(2, 0), P(-0.21f, 0))!.Value.WaypointIndex);
        Assert.Equal(CartSteeringStatus.Finished, planner.Follow(plan, P(30, 0), P(27.79f, 0)).Status);
        Assert.Equal(CartSteeringStatus.Finished, planner.Follow(plan, P(2, 0), P(-0.21f, 0)).Status);
        Assert.NotEmpty(assessment.CartTrack);
    }

    [Fact]
    public void LeavingTheVerifiedCorridorEndsTheGoals()
    {
        var (planner, _, _) = Planner();
        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(30, 0)), 0f);
        float half = plan.NarrowestClearanceMetres / 2f;

        Assert.Equal(CartSteeringStatus.LeftCorridor, planner.Follow(plan, P(10, half + 0.2f), P(8, 0)).Status);
        Assert.Null(planner.NextGoal(plan, P(10, half + 0.2f), P(8, 0)));
        Assert.Equal(CartSteeringStatus.LeftCorridor, planner.Follow(plan, P(10, 0), P(8, half + 0.2f)).Status);
        Assert.Equal(CartSteeringStatus.Following, planner.Follow(plan, P(10, half - 0.2f), P(8, 0)).Status);
        Assert.Equal(CartSteeringStatus.LeftCorridor, planner.Follow(plan, new WorkPoint(float.NaN, 0f, 0f), P(8, 0)).Status);
    }

    [Fact]
    public void ARefusedPlanHasNothingToFollow()
    {
        var (planner, _, _) = Planner();
        CartRoutePlan refused = CartRoutePlan.Refused(CartRouteVerdict.TooNarrow, 2);

        Assert.Equal(CartSteeringStatus.NotSuitable, planner.Follow(refused, P(0, 0), P(0, 0)).Status);
        Assert.Null(planner.NextGoal(refused, P(0, 0), P(0, 0)));
        Assert.False(planner.NeedsRefresh(refused, 100f));
        Assert.Same(refused, planner.Refresh(refused, P(0, 0), P(0, 0), 100f));
    }

    [Fact]
    public void ARefreshRechecksWhatIsLeftAgainstTheWorldWithoutANavmeshQuery()
    {
        var (planner, world, paths) = Planner();
        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(30, 0)), 0f);
        Assert.True(plan.IsSuitable);
        Assert.False(planner.NeedsRefresh(plan, HaulLimits.Default.PlanRefreshSeconds - 0.1f));
        Assert.True(planner.NeedsRefresh(plan, HaulLimits.Default.PlanRefreshSeconds));

        CartRoutePlan fresh = planner.Refresh(plan, P(6, 0), P(3.79f, 0), 5f);
        Assert.True(fresh.IsSuitable, planner.LastAssessment!.Describe());
        Assert.NotSame(plan, fresh);
        Assert.Equal(P(6, 0), fresh.Waypoints[0]);
        Assert.Equal(P(30, 0), fresh.Waypoints[fresh.Waypoints.Count - 1]);

        world.Obstacles.Add(FakeObstacle.Box("new wall", 19.5f, 20.5f, -6f, 6f));
        CartRoutePlan blocked = planner.Refresh(fresh, P(8, 0), P(5.79f, 0), 10f);
        Assert.Equal(CartRouteVerdict.TooNarrow, blocked.Verdict);
        Assert.Equal(1, paths.Calls);

        Assert.Equal(CartRouteVerdict.NoPath, planner.Refresh(fresh, P(8, 9), P(5.79f, 0), 15f).Verdict);
        Assert.Equal(CartRouteFinding.LeftCorridor, planner.LastAssessment!.Finding);
    }

    // -- motion monitor ----------------------------------------------------------

    private static HaulLimits MotionLimits => new HaulLimits { StallWindowSeconds = 2f, StallCartDisplacementMetres = 0.5f };

    [Fact]
    public void NotCommandedIsIdleAndStartsTheWindowOver()
    {
        var monitor = new HaulMotionMonitor(MotionLimits);
        Assert.Equal(HaulMotion.Idle, monitor.Current);

        for (float t = 0f; t <= 3f; t += 0.05f)
        {
            monitor.Sample(t, P(0, 0), P(-2, 0), true);
        }

        Assert.Equal(HaulMotion.Stalled, monitor.Current);
        monitor.Sample(3.05f, P(0, 0), P(-2, 0), false);
        Assert.Equal(HaulMotion.Idle, monitor.Current);
        monitor.Sample(3.1f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Progressing, monitor.Current);
    }

    [Fact]
    public void APullIsGivenAWholeWindowBeforeItIsJudged()
    {
        var monitor = new HaulMotionMonitor(MotionLimits);
        monitor.Sample(0f, P(0, 0), P(-2, 0), true);
        monitor.Sample(1.99f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Progressing, monitor.Current);

        monitor.Sample(2f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Stalled, monitor.Current);
        Assert.Equal(2f, monitor.CommandedSeconds, 3);
    }

    [Fact]
    public void TheCartMovingAtLeastTheThresholdIsProgressAndJustLessIsNot()
    {
        var progressing = new HaulMotionMonitor(MotionLimits);
        progressing.Sample(0f, P(0, 0), P(-2, 0), true);
        progressing.Sample(2f, P(0.5f, 0), P(-1.5f, 0), true);
        Assert.Equal(HaulMotion.Progressing, progressing.Current);
        Assert.Equal(0.5f, progressing.CartDisplacementMetres, 3);

        var stalled = new HaulMotionMonitor(MotionLimits);
        stalled.Sample(0f, P(0, 0), P(-2, 0), true);
        stalled.Sample(2f, P(0.49f, 0), P(-1.51f, 0), true);
        Assert.Equal(HaulMotion.Stalled, stalled.Current);
    }

    [Fact]
    public void GunnarGettingSomewhereWhileTheCartStaysPutIsWedged()
    {
        var moved = new HaulMotionMonitor(MotionLimits);
        moved.Sample(0f, P(0, 0), P(-2, 0), true);
        moved.Sample(2f, P(0.6f, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Wedged, moved.Current);

        // Straining back and forth: no net movement, but Gunnar got that far.
        var strained = new HaulMotionMonitor(MotionLimits);
        strained.Sample(0f, P(0, 0), P(-2, 0), true);
        strained.Sample(1f, P(0.7f, 0), P(-2, 0), true);
        strained.Sample(2f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Wedged, strained.Current);
        Assert.Equal(0f, strained.PullerDisplacementMetres, 3);
        Assert.Equal(0.7f, strained.PullerExcursionMetres, 3);
    }

    [Fact]
    public void ACartRockingBackAndForthIsNotProgress()
    {
        var monitor = new HaulMotionMonitor(MotionLimits);
        for (int step = 0; step <= 40; step++)
        {
            float t = step * 0.1f;
            float sway = step % 2 == 0 ? 0f : 0.8f;
            monitor.Sample(t, P(0, 0), P(-2 + sway, 0), true);
        }

        Assert.NotEqual(HaulMotion.Progressing, monitor.Current);
    }

    [Fact]
    public void UnusableSamplesAreIgnoredAndABackwardsClockStartsOver()
    {
        var monitor = new HaulMotionMonitor(MotionLimits);
        monitor.Sample(0f, P(0, 0), P(-2, 0), true);
        monitor.Sample(float.NaN, P(0, 0), P(-2, 0), true);
        monitor.Sample(1f, new WorkPoint(float.PositiveInfinity, 0f, 0f), P(-2, 0), true);
        monitor.Sample(2f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Stalled, monitor.Current);

        monitor.Sample(1f, P(0, 0), P(-2, 0), true);
        Assert.Equal(HaulMotion.Progressing, monitor.Current);
        monitor.Reset();
        Assert.Equal(HaulMotion.Idle, monitor.Current);
    }

    [Fact]
    public void TheMonitorKeepsJudgingOverLongPullsWithoutGrowing()
    {
        var monitor = new HaulMotionMonitor(MotionLimits);
        for (int step = 0; step <= 10_000; step++)
        {
            float t = step * 0.02f;
            monitor.Sample(t, P(t, 0), P(t - 2f, 0), true);
        }

        Assert.Equal(HaulMotion.Progressing, monitor.Current);
        Assert.InRange(monitor.CartDisplacementMetres, 2f, 4.5f);
    }

    // -- failure cache ---------------------------------------------------------

    [Fact]
    public void AStuckPlaceRefusesRoutesThroughItTheSameWayOnly()
    {
        var cache = new CartRouteFailureCache(1.36f, 3.25f);
        cache.Remember(P(30, 0), P(10, 0), P(12, 0), HaulAttentionReason.Wedged, 0f);

        Assert.True(cache.RefusesRoute(new[] { P(0, 0), P(20, 0) }, 1f));
        Assert.True(cache.RefusesRoute(new[] { P(0, 1), P(20, 1) }, 1f));
        Assert.False(cache.RefusesRoute(new[] { P(20, 0), P(0, 0) }, 1f));
        Assert.False(cache.RefusesRoute(new[] { P(0, 3), P(20, 3) }, 1f));
        Assert.False(cache.RefusesRoute(new[] { P(10, -10), P(10, 10) }, 1f));
        Assert.True(cache.RefusesSpot(P(31, 0), 1f));
        Assert.False(cache.RefusesSpot(P(40, 0), 1f));

        Assert.False(cache.RefusesRoute(new[] { P(0, 0), P(20, 0) }, CartRouteFailureCache.FirstPauseSeconds));
    }

    [Fact]
    public void TheSameTroubleAgainIsAvoidedLongerUpToACap()
    {
        var cache = new CartRouteFailureCache(1.36f, 3.25f);
        float now = 0f;
        float[] pauses = new float[5];
        for (int strike = 0; strike < 5; strike++)
        {
            CartRouteSetback setback = cache.Remember(P(30, 0), P(10, 0), P(12, 0), HaulAttentionReason.Wedged, now);
            pauses[strike] = setback.PauseSeconds;
            Assert.Equal(strike + 1, setback.Strikes);
            now = setback.Until + 1f;
        }

        Assert.Equal(new[] { 60f, 120f, 240f, 300f, 300f }, pauses);
        Assert.Single(cache.Remembered);

        CartRouteSetback fresh = cache.Remember(P(30, 0), P(10, 0), P(12, 0), HaulAttentionReason.Wedged,
            cache.Remembered[0].Until + CartRouteFailureCache.RecallSeconds + 1f);
        Assert.Equal(1, fresh.Strikes);
    }

    [Fact]
    public void TheCacheHoldsABoundedNumberOfSetbacks()
    {
        var cache = new CartRouteFailureCache(1.36f, 3.25f);
        for (int index = 0; index < CartRouteFailureCache.Capacity + 5; index++)
        {
            cache.Remember(P(index * 20f, 50f), HaulAttentionReason.NoRoute, index);
        }

        Assert.Equal(CartRouteFailureCache.Capacity, cache.Remembered.Count);
        cache.Clear();
        Assert.Empty(cache.Remembered);
    }

    // -- recovery ----------------------------------------------------------------

    [Fact]
    public void RecoveryBacksOffOnceThenReplansThenGivesUpNeverMore()
    {
        HaulLimits limits = HaulLimits.Default;
        var failures = CartRouteFailureCache.For(VanillaCart, limits);
        var (planner, _, _) = Planner(limits, failures);
        var policy = new HaulRecoveryPolicy(limits);
        WorkPoint target = P(30, 0);
        WorkPoint puller = P(10, 0);
        WorkPoint cart = P(7.79f, 0);

        HaulRecoveryDecision first = policy.OnMotion(HaulMotion.Wedged, target, puller, cart, VanillaCart, planner, 100f);
        Assert.Equal(HaulRecoveryStep.BackOff, first.Step);
        Assert.Equal(100f + limits.RecoveryBackoffSeconds, first.NotBefore, 3);
        Assert.True(first.BackOffGoal!.Value.IsFinalStop);
        Assert.True(first.BackOffGoal.Value.Target.X < puller.X);

        // Stuck again at the same place: no second back-off there.
        HaulRecoveryDecision second = policy.OnMotion(HaulMotion.Stalled, target, puller, cart, VanillaCart, planner, 110f);
        Assert.Equal(HaulRecoveryStep.Replan, second.Step);
        Assert.Equal(110f + (2f * limits.RecoveryBackoffSeconds), second.NotBefore, 3);

        HaulRecoveryDecision third = policy.OnMotion(HaulMotion.Wedged, target, puller, cart, VanillaCart, planner, 130f);
        Assert.Equal(HaulRecoveryStep.GiveUp, third.Step);
        Assert.Equal(HaulAttentionReason.Wedged, third.Reason);
        Assert.Equal(limits.MaxRecoveryAttempts, third.AttemptsUsed);
        Assert.True(policy.HasGivenUp);

        Assert.Equal(HaulRecoveryStep.GiveUp, policy.OnMotion(HaulMotion.Progressing, target, puller, cart, VanillaCart, planner, 140f).Step);
        policy.Reset();
        Assert.Equal(HaulRecoveryStep.Continue, policy.OnMotion(HaulMotion.Progressing, target, puller, cart, VanillaCart, planner, 150f).Step);
    }

    [Fact]
    public void AReplanIntoTheStuckPlaceTheSameWayIsRefusedSoNothingOscillates()
    {
        HaulLimits limits = HaulLimits.Default;
        var failures = CartRouteFailureCache.For(VanillaCart, limits);
        var (planner, _, paths) = Planner(limits, failures);
        var policy = new HaulRecoveryPolicy(limits);

        policy.OnMotion(HaulMotion.Wedged, P(40, 0), P(12, 0), P(9.79f, 0), VanillaCart, planner, 0f);

        CartRoutePlan again = planner.Plan(Request(P(4, 0), P(40, 0)), 5f);
        Assert.Equal(CartRouteVerdict.NoPath, again.Verdict);
        Assert.Equal(CartRouteFinding.RecentFailure, planner.LastAssessment!.Finding);
        Assert.Equal(0, paths.Calls);

        HaulRecoveryDecision afterRefusal = policy.OnReplanRefused(again.Verdict, 0f, 5f);
        Assert.Equal(HaulRecoveryStep.Replan, afterRefusal.Step);
        HaulRecoveryDecision last = policy.OnReplanRefused(again.Verdict, 0f, 20f);
        Assert.Equal(HaulRecoveryStep.GiveUp, last.Step);
        Assert.Equal(HaulAttentionReason.NoRoute, last.Reason);
    }

    [Fact]
    public void ASpentQueryBudgetWaitsWithoutSpendingARecoveryAttempt()
    {
        var policy = new HaulRecoveryPolicy(HaulLimits.Default);
        for (int round = 0; round < 10; round++)
        {
            HaulRecoveryDecision decision = policy.OnReplanRefused(CartRouteVerdict.BudgetExhausted, 60f, 10f);
            Assert.Equal(HaulRecoveryStep.Replan, decision.Step);
            Assert.Equal(60f, decision.NotBefore);
        }

        Assert.Equal(0, policy.AttemptsUsed);
    }

    [Fact]
    public void ABackOffIsOnlyOfferedWhereTheGroundBehindIsProvenClear()
    {
        HaulLimits limits = HaulLimits.Default;
        var (planner, world, _) = Planner(limits, CartRouteFailureCache.For(VanillaCart, limits));
        // The back-off moves the cart one sample spacing: its tail sweeps from
        // x = 6.75 back to 5.25, into this rock.
        world.Obstacles.Add(FakeObstacle.Box("rock behind", 4.8f, 5.6f, -3f, 3f));

        Assert.False(planner.TryPlanBackOff(P(10, 0), P(7.79f, 0), VanillaCart, 0f, out _));
        Assert.Equal(CartRouteFinding.Obstructed, planner.LastAssessment!.Finding);

        var policy = new HaulRecoveryPolicy(limits);
        Assert.Equal(
            HaulRecoveryStep.Replan,
            policy.OnMotion(HaulMotion.Wedged, P(30, 0), P(10, 0), P(7.79f, 0), VanillaCart, planner, 0f).Step);

        var (waterPlanner, waterWorld, _) = Planner(limits);
        waterWorld.Water.Add((new FakeArea(4f, 7f, -3f, 3f), 0.2f));
        Assert.False(waterPlanner.TryPlanBackOff(P(10, 0), P(7.79f, 0), VanillaCart, 0f, out _));
        Assert.Equal(CartRouteFinding.Water, waterPlanner.LastAssessment!.Finding);
    }

    // -- staging -----------------------------------------------------------------

    [Fact]
    public void CandidatesStartAtTheDesiredPointThenFaceGunnarAndAlternate()
    {
        var candidates = new List<WorkPoint>();
        CartStagingSelector.BuildCandidates(P(0, 0), P(-10, 0), 3f, 6f, 100, candidates);

        Assert.Equal(P(0, 0), candidates[0]);
        Assert.Equal(-3f, candidates[1].X, 3);
        Assert.Equal(0f, candidates[1].Z, 3);
        Assert.True(candidates[2].Z < 0f && candidates[3].Z > 0f);
        Assert.Equal(1 + 6 + 12, candidates.Count);

        CartStagingSelector.BuildCandidates(P(0, 0), P(-10, 0), 3f, 60f, 10, candidates);
        Assert.Equal(10, candidates.Count);
    }

    [Fact]
    public void StagingChoosesTheNearestSafeReachableSpotTheSameWayEveryTime()
    {
        WorkPoint? chosen = null;
        for (int run = 0; run < 2; run++)
        {
            var (planner, world, _) = Planner();
            world.Water.Add((new FakeArea(17f, 23f, -3f, 3f), 0.4f));
            var selector = new CartStagingSelector(planner, world);

            CartStagingChoice choice = selector.Select(
                new CartStagingRequest(P(20, 0), P(0, 0), P(-2.21f, 0), VanillaCart, 70f, 12f, 3), 0f);

            Assert.Equal(CartStagingOutcome.Chosen, choice.Outcome);
            Assert.True(choice.Plan!.IsSuitable);
            Assert.Equal(choice.Spot, choice.Plan.StopPoint);
            Assert.True(choice.Spot!.Value.HorizontalDistanceTo(P(20, 0)) > 3f);
            chosen ??= choice.Spot;
            Assert.Equal(chosen, choice.Spot);
        }
    }

    [Fact]
    public void StagingSkipsSpotsThatRecentlyFailedAndIsBoundedWhenNothingFits()
    {
        HaulLimits limits = HaulLimits.Default;
        var failures = CartRouteFailureCache.For(VanillaCart, limits);
        var (planner, world, _) = Planner(limits, failures);
        var selector = new CartStagingSelector(planner, world);
        failures.Remember(P(20, 0), HaulAttentionReason.NoRoute, 0f);

        CartStagingChoice choice = selector.Select(
            new CartStagingRequest(P(20, 0), P(0, 0), null, VanillaCart, 70f, 6f, 1), 1f);
        Assert.Equal(CartStagingOutcome.Chosen, choice.Outcome);
        Assert.NotEqual(P(20, 0), choice.Spot);

        var (steepPlanner, steepWorld, _) = Planner(limits);
        steepWorld.Height = (x, z) => x * 0.08f;
        var steep = new CartStagingSelector(steepPlanner, steepWorld).Select(
            new CartStagingRequest(P(20, 0), P(0, 0), null, VanillaCart, 70f, 64f, 1), 0f);
        Assert.Equal(CartStagingOutcome.NoSafeSpot, steep.Outcome);
        Assert.InRange(steep.CandidatesChecked, 1, limits.ClearanceProbesPerPlan);
        Assert.Equal(0, steep.RoutesPlanned);

        var invalid = new CartStagingSelector(steepPlanner, steepWorld).Select(
            new CartStagingRequest(new WorkPoint(float.NaN, 0f, 0f), P(0, 0), null, VanillaCart, 70f, 6f, 1), 0f);
        Assert.Equal(CartStagingOutcome.Invalid, invalid.Outcome);
    }

    [Fact]
    public void StagingStopsWhenTheQueryBudgetIsSpent()
    {
        var limits = new HaulLimits { PathQueriesPerMinute = 1 };
        var (planner, world, _) = Planner(limits);
        world.Obstacles.Add(FakeObstacle.Box("wall", 9.5f, 10.5f, -40f, 40f));
        var selector = new CartStagingSelector(planner, world);

        CartStagingChoice choice = selector.Select(
            new CartStagingRequest(P(20, 0), P(0, 0), null, VanillaCart, 70f, 6f, 1), 0f);

        Assert.Equal(CartStagingOutcome.BudgetExhausted, choice.Outcome);
        Assert.Equal(2, choice.RoutesPlanned);
    }
}
