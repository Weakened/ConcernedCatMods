using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>CF-SET-002: the game-free half of the worker actor spike.
///
/// Two of the issue's acceptance criteria are claims about cost and about
/// giving up — "pathfinding cost per tick is bounded and measured" and "an
/// unreachable goal produces a deferral with a reason, not a spin". Neither is
/// observable by watching a worker walk across a field, and neither is provable
/// by a comment. They are properties of the decision loop, so they are proved
/// here, by driving that loop for more ticks than a person would ever watch and
/// counting what it spent.
///
/// What these tests deliberately do <b>not</b> prove: that the worker actually
/// moves in Valheim. That is live evidence, it is still owed, and no count in
/// this file may be offered in its place.</summary>
public sealed class WorkerMovementPlannerTests
{
    private static readonly SitePoint Origin = new(0f, 0f, 0f);

    /// <summary>Runs a planner for <paramref name="ticks"/> ticks against a
    /// fixed observation, returning every action in order. The pathfinder is
    /// modelled by <paramref name="pathFound"/>: null means the adapter never
    /// reports back at all, which is the lost-report case the planner is
    /// designed to survive.</summary>
    private static List<WorkerAction> Run(
        WorkerMovementPlanner planner,
        WorkerObservation observation,
        int ticks,
        bool? pathFound = false)
    {
        List<WorkerAction> actions = new(ticks);
        for (int i = 0; i < ticks; i++)
        {
            planner.BeginTick();
            WorkerAction action = planner.Decide(observation);
            actions.Add(action);
            if (action.Kind == WorkerActionKind.RequestPath && pathFound.HasValue)
            {
                planner.ReportPathOutcome(pathFound.Value);
            }
        }

        return actions;
    }

    // ---- no order means nothing at all -------------------------------------

    [Fact]
    public void WithNoGoal_DoesNothing_ForeverAndFree()
    {
        WorkerMovementPlanner planner = new();

        List<WorkerAction> actions = Run(planner, WorkerObservation.Ready(Origin), ticks: 2000);

        Assert.All(actions, a => Assert.Equal(WorkerActionKind.Idle, a.Kind));
        Assert.Equal(0, planner.TotalPathRequests);
        Assert.False(planner.HasGoal);
        Assert.False(planner.IsDeferred);
    }

    [Fact]
    public void ClearingTheGoal_ReturnsTheWorkerToInert()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(20f, 0f, 0f)));
        Run(planner, WorkerObservation.Ready(Origin), ticks: 5);

        planner.ClearGoal();
        long spentBefore = planner.TotalPathRequests;
        List<WorkerAction> after = Run(planner, WorkerObservation.Ready(Origin), ticks: 500);

        Assert.All(after, a => Assert.Equal(WorkerActionKind.Idle, a.Kind));
        Assert.Equal(spentBefore, planner.TotalPathRequests);
    }

    // ---- bounded, and measured ---------------------------------------------

    [Fact]
    public void PathRequests_NeverExceedThePerTickBudget()
    {
        WorkerMovementBudget budget = new(
            maxPathRequestsPerTick: 2,
            maxPathAttemptsPerGoal: 100,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f);
        WorkerMovementPlanner planner = new(budget);
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));
        WorkerObservation observation = WorkerObservation.Ready(Origin);

        // Decide repeatedly *within* one tick: the per-tick allowance must hold
        // even against a caller that asks far more often than the driver does.
        planner.BeginTick();
        List<WorkerAction> withinOneTick = new();
        for (int i = 0; i < 50; i++)
        {
            withinOneTick.Add(planner.Decide(observation));
        }

        Assert.Equal(2, withinOneTick.Count(a => a.Kind == WorkerActionKind.RequestPath));
        Assert.Equal(2, planner.PathRequestsThisTick);
        Assert.All(
            withinOneTick.Skip(2),
            a => Assert.Equal(WorkerActionKind.WaitForBudget, a.Kind));
    }

    [Fact]
    public void RetryBackoff_SpacesAttemptsOutInsteadOfBurningThemInConsecutiveTicks()
    {
        WorkerMovementBudget budget = new(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 3,
            retryBackoffTicks: 10,
            maxPlanningDistance: 64f);
        WorkerMovementPlanner planner = new(budget);
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));

        List<WorkerAction> actions = Run(planner, WorkerObservation.Ready(Origin), ticks: 25);

        List<int> requestTicks = actions
            .Select((a, i) => (a, i))
            .Where(p => p.a.Kind == WorkerActionKind.RequestPath)
            .Select(p => p.i)
            .ToList();

        Assert.Equal(3, requestTicks.Count);
        // First is immediate; each later attempt waits out the backoff.
        Assert.Equal(0, requestTicks[0]);
        Assert.Equal(10, requestTicks[1]);
        Assert.Equal(20, requestTicks[2]);
    }

    [Fact]
    public void TotalPathRequests_CountsEveryAuthorisedRequest()
    {
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 4,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f));

        planner.AssignGoal(WorkerGoal.At(new SitePoint(10f, 0f, 0f)));
        Run(planner, WorkerObservation.Ready(Origin), ticks: 50);
        planner.AssignGoal(WorkerGoal.At(new SitePoint(12f, 0f, 0f)));
        Run(planner, WorkerObservation.Ready(Origin), ticks: 50);

        // Four attempts per goal, two goals, and nothing beyond that however
        // long the loop runs.
        Assert.Equal(8, planner.TotalPathRequests);
    }

    // ---- an unreachable goal defers with a reason, and stays stopped --------

    [Fact]
    public void UnreachableGoal_DefersWithAReason_AndNeverSpinsAgain()
    {
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 5,
            retryBackoffTicks: 2,
            maxPlanningDistance: 64f));
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));

        Run(planner, WorkerObservation.Ready(Origin), ticks: 40);

        Assert.True(planner.IsDeferred);
        Assert.Equal(WorkerDeferralReason.Unreachable, planner.DeferredReason);
        Assert.Equal(5, planner.TotalPathRequests);

        // The anti-spin proof: thousands of further ticks cost nothing.
        long spent = planner.TotalPathRequests;
        List<WorkerAction> later = Run(planner, WorkerObservation.Ready(Origin), ticks: 5000);

        Assert.Equal(spent, planner.TotalPathRequests);
        Assert.All(later, a =>
        {
            Assert.Equal(WorkerActionKind.Defer, a.Kind);
            Assert.Equal(WorkerDeferralReason.Unreachable, a.Reason);
        });
    }

    [Fact]
    public void ALostAdapterReport_StillSpendsTheAttempt_SoTheWorkerCannotAskForever()
    {
        // The adapter asks the pathfinder and never reports back — a crash, an
        // unload, a bug. The allowance must still drain.
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 5,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f));
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));

        Run(planner, WorkerObservation.Ready(Origin), ticks: 1000, pathFound: null);

        Assert.Equal(WorkerDeferralReason.Unreachable, planner.DeferredReason);
        Assert.Equal(5, planner.TotalPathRequests);
    }

    // The expected reason travels as its underlying int: xUnit only discovers
    // public test methods, and the settlement layer is entirely internal, so a
    // public signature cannot name the enum directly.
    [Theory]
    [InlineData(false, true, false, (int)WorkerDeferralReason.NoAuthority)]
    [InlineData(true, false, false, (int)WorkerDeferralReason.OutsideLoadedGround)]
    [InlineData(true, true, true, (int)WorkerDeferralReason.Hazardous)]
    public void RefusalsHappenBeforeAnyPathRequestIsSpent(
        bool hasAuthority,
        bool inLoadedGround,
        bool hazardous,
        int expectedReason)
    {
        WorkerDeferralReason expected = (WorkerDeferralReason)expectedReason;
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));
        WorkerObservation observation = new(
            Origin, hasPath: false, hasAuthority, inLoadedGround, hazardous);

        List<WorkerAction> actions = Run(planner, observation, ticks: 500);

        Assert.Equal(expected, planner.DeferredReason);
        Assert.Equal(0, planner.TotalPathRequests);
        Assert.All(actions, a => Assert.Equal(WorkerActionKind.Defer, a.Kind));
    }

    [Fact]
    public void MissingAuthority_IsRefused_NotAssumed()
    {
        // The authority ADR's rule in one test: a check that cannot be
        // established refuses. The worker is otherwise perfectly able to walk
        // there, and still does not.
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(5f, 0f, 0f)));
        WorkerObservation unowned = new(
            Origin,
            hasPath: true,
            hasAuthority: false,
            goalInLoadedGround: true,
            goalIsHazardous: false);

        planner.BeginTick();
        WorkerAction action = planner.Decide(unowned);

        Assert.Equal(WorkerActionKind.Defer, action.Kind);
        Assert.Equal(WorkerDeferralReason.NoAuthority, action.Reason);
    }

    [Fact]
    public void AGoalBeyondThePlanningHorizon_IsRefusedWithoutAskingThePathfinder()
    {
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 5,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f));
        planner.AssignGoal(WorkerGoal.At(new SitePoint(64.1f, 0f, 0f)));

        planner.BeginTick();
        WorkerAction action = planner.Decide(WorkerObservation.Ready(Origin));

        Assert.Equal(WorkerDeferralReason.TooFar, action.Reason);
        Assert.Equal(0, planner.TotalPathRequests);
    }

    [Fact]
    public void ExactlyAtThePlanningHorizon_IsStillAllowed()
    {
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 5,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f));
        planner.AssignGoal(WorkerGoal.At(new SitePoint(64f, 0f, 0f)));

        planner.BeginTick();
        WorkerAction action = planner.Decide(WorkerObservation.Ready(Origin));

        Assert.Equal(WorkerActionKind.RequestPath, action.Kind);
    }

    // ---- move, then stop ----------------------------------------------------

    [Fact]
    public void WithAPath_TheWorkerMoves()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));

        planner.BeginTick();
        WorkerAction action = planner.Decide(WorkerObservation.Ready(Origin, hasPath: true));

        Assert.Equal(WorkerActionKind.Move, action.Kind);
        Assert.Equal(0, planner.TotalPathRequests);
    }

    [Fact]
    public void WithinTolerance_TheWorkerArrivesAndStaysArrived()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(new WorkerGoal(new SitePoint(1f, 0f, 0f), arrivalTolerance: 2f));

        List<WorkerAction> actions = Run(
            planner, WorkerObservation.Ready(Origin, hasPath: true), ticks: 100);

        Assert.All(actions, a => Assert.Equal(WorkerActionKind.Arrive, a.Kind));
        Assert.Equal(0, planner.TotalPathRequests);
    }

    [Fact]
    public void ArrivalIgnoresHeight_BecauseTheWorkerWalksOnTheGround()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(new WorkerGoal(new SitePoint(0f, 40f, 0f), arrivalTolerance: 2f));

        planner.BeginTick();
        WorkerAction action = planner.Decide(WorkerObservation.Ready(Origin, hasPath: true));

        // Directly below a point forty metres up is "arrived" on the ground
        // plane. Whether that is the right goal is the caller's problem; the
        // planner must at least be consistent about which distance it means.
        Assert.Equal(WorkerActionKind.Arrive, action.Kind);
    }

    [Fact]
    public void ASuccessfulPath_RestoresTheFullAttemptAllowance()
    {
        WorkerMovementPlanner planner = new(new WorkerMovementBudget(
            maxPathRequestsPerTick: 1,
            maxPathAttemptsPerGoal: 3,
            retryBackoffTicks: 0,
            maxPlanningDistance: 64f));
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));
        WorkerObservation searching = WorkerObservation.Ready(Origin);

        planner.BeginTick();
        planner.Decide(searching);
        planner.ReportPathOutcome(false);
        planner.BeginTick();
        planner.Decide(searching);
        planner.ReportPathOutcome(true);

        Assert.Equal(0, planner.AttemptsForCurrentGoal);

        // A path that later goes stale gets the whole allowance again rather
        // than the one attempt left over from before.
        List<WorkerAction> actions = Run(planner, searching, ticks: 20);
        Assert.Equal(3, actions.Count(a => a.Kind == WorkerActionKind.RequestPath));
    }

    // ---- assignment clears the past ----------------------------------------

    [Fact]
    public void ANewGoal_ClearsAPreviousDeferral()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));
        Run(planner, WorkerObservation.Ready(Origin), ticks: 200);
        Assert.True(planner.IsDeferred);

        planner.AssignGoal(WorkerGoal.At(new SitePoint(10f, 0f, 0f)));

        Assert.False(planner.IsDeferred);
        Assert.Equal(WorkerDeferralReason.None, planner.DeferredReason);
        Assert.Equal(0, planner.AttemptsForCurrentGoal);
    }

    [Fact]
    public void AnExternalDeferral_HoldsTheFirstReasonAndStopsTheWorker()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(10f, 0f, 0f)));

        planner.DeferExternally(WorkerDeferralReason.Hazardous);
        planner.DeferExternally(WorkerDeferralReason.Unreachable);

        Assert.Equal(WorkerDeferralReason.Hazardous, planner.DeferredReason);

        List<WorkerAction> actions = Run(planner, WorkerObservation.Ready(Origin), ticks: 100);
        Assert.All(actions, a => Assert.Equal(WorkerDeferralReason.Hazardous, a.Reason));
        Assert.Equal(0, planner.TotalPathRequests);
    }

    [Fact]
    public void AnExternalDeferralWithoutAGoal_IsIgnored()
    {
        WorkerMovementPlanner planner = new();

        planner.DeferExternally(WorkerDeferralReason.Hazardous);

        Assert.False(planner.IsDeferred);
    }

    [Fact]
    public void DeferringWithNoReason_IsNotADeferral()
    {
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(10f, 0f, 0f)));

        planner.DeferExternally(WorkerDeferralReason.None);

        Assert.False(planner.IsDeferred);
    }

    // ---- the budget and goal types refuse nonsense --------------------------

    [Theory]
    [InlineData(0, 5, 10, 64f)]
    [InlineData(1, 0, 10, 64f)]
    [InlineData(1, 5, -1, 64f)]
    [InlineData(1, 5, 10, 0f)]
    [InlineData(1, 5, 10, -3f)]
    [InlineData(1, 5, 10, float.PositiveInfinity)]
    public void AnImpossibleBudget_IsRejectedAtConstruction(
        int perTick, int perGoal, int backoff, float distance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkerMovementBudget(perTick, perGoal, backoff, distance));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.PositiveInfinity)]
    public void AnImpossibleArrivalTolerance_IsRejectedAtConstruction(float tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkerGoal(Origin, tolerance));
    }

    [Fact]
    public void TheDefaultBudget_IsTheOneTheSpikeDocuments()
    {
        WorkerMovementBudget budget = WorkerMovementBudget.Default;

        Assert.Equal(1, budget.MaxPathRequestsPerTick);
        Assert.Equal(5, budget.MaxPathAttemptsPerGoal);
        Assert.Equal(10, budget.RetryBackoffTicks);
        Assert.Equal(64f, budget.MaxPlanningDistance);
    }

    [Fact]
    public void AWorstCaseGoal_CostsAtMostFiveRequestsAndUnderThreeSecondsOfTicks()
    {
        // The documented worst case, stated as a test so the number in
        // WORKER_ACTOR_SPIKE.md cannot drift away from the code. The driver
        // ticks at 20 Hz, so 45 ticks is 2.25 seconds.
        WorkerMovementPlanner planner = new();
        planner.AssignGoal(WorkerGoal.At(new SitePoint(30f, 0f, 0f)));

        List<WorkerAction> actions = Run(planner, WorkerObservation.Ready(Origin), ticks: 45);

        Assert.Equal(5, planner.TotalPathRequests);
        Assert.Equal(WorkerDeferralReason.Unreachable, planner.DeferredReason);
        Assert.Equal(WorkerActionKind.Defer, actions[^1].Kind);
    }

    // ---- value semantics ----------------------------------------------------

    [Fact]
    public void SitePointDistances_SeparateGroundFromHeight()
    {
        SitePoint a = new(0f, 0f, 0f);
        SitePoint b = new(3f, 4f, 4f);

        Assert.Equal(5f, a.HorizontalDistanceTo(b), 4);
        Assert.Equal(4f, a.VerticalDistanceTo(b), 4);
    }

    [Fact]
    public void SitePointAndGoal_HaveValueEquality()
    {
        Assert.Equal(new SitePoint(1f, 2f, 3f), new SitePoint(1f, 2f, 3f));
        Assert.NotEqual(new SitePoint(1f, 2f, 3f), new SitePoint(1f, 2f, 4f));
        Assert.Equal(
            new SitePoint(1f, 2f, 3f).GetHashCode(),
            new SitePoint(1f, 2f, 3f).GetHashCode());
        Assert.Equal(WorkerGoal.At(Origin), WorkerGoal.At(Origin));
        Assert.NotEqual(WorkerGoal.At(Origin), new WorkerGoal(Origin, 3f));
    }

    [Fact]
    public void ANonDeferralCarriesNoReason()
    {
        Assert.Equal(WorkerDeferralReason.None, WorkerAction.Idle.Reason);
        Assert.Equal(WorkerDeferralReason.None, WorkerAction.Move.Reason);
        Assert.Equal(WorkerDeferralReason.None, WorkerAction.Arrive.Reason);
        Assert.Equal(WorkerDeferralReason.None, WorkerAction.RequestPath.Reason);
        Assert.Equal(WorkerDeferralReason.None, WorkerAction.WaitForBudget.Reason);
        Assert.Equal(
            WorkerDeferralReason.Hazardous,
            WorkerAction.Defer(WorkerDeferralReason.Hazardous).Reason);
    }
}
