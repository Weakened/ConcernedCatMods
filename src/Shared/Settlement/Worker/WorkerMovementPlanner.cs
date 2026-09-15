namespace TheConcernedCat.Settlement.Worker;

/// <summary>Decides, once per tick, what a single worker should do about its
/// current goal — and is the only thing in the settlement runtime that may
/// authorise a path request.
///
/// It is engine-free on purpose. The game adapter converts positions, asks the
/// pathfinder and moves the body; every <i>decision</i> about whether to ask,
/// how often, and when to stop asking lives here, where it can be exercised
/// exhaustively without a running game. That split is what makes CF-SET-002's
/// "bounded and measured" and "defers with a reason, not a spin" provable
/// rather than asserted.
///
/// <b>The two properties everything else rests on.</b>
///
/// <list type="number">
/// <item><b>Bounded.</b> A goal can cost at most
/// <see cref="WorkerMovementBudget.MaxPathAttemptsPerGoal"/> path requests, ever,
/// and at most <see cref="WorkerMovementBudget.MaxPathRequestsPerTick"/> of them
/// in any one tick.</item>
/// <item><b>Terminal.</b> Once a goal is deferred, no later tick can spend
/// another request on it. Only assigning a new goal clears the deferral, and
/// only a person or a higher layer does that.</item>
/// </list>
///
/// <b>Why <see cref="Decide"/> mutates.</b> Deciding to request a path is what
/// spends the attempt — the counter moves inside <see cref="Decide"/>, not when
/// the adapter reports back. That means an adapter which asks the pathfinder and
/// then crashes, forgets to report, or is unloaded mid-request has still spent
/// the attempt. The alternative fails open: a lost report would leave the
/// allowance untouched and the worker would ask forever. Here the worst case of
/// a broken adapter is a goal that defers early and says
/// <see cref="WorkerDeferralReason.Unreachable"/>, which is the safe direction
/// to be wrong in.</summary>
internal sealed class WorkerMovementPlanner
{
    private readonly WorkerMovementBudget _budget;

    private WorkerGoal _goal;
    private bool _hasGoal;
    private WorkerDeferralReason _deferred;
    private int _attemptsForCurrentGoal;
    private int _requestsThisTick;
    private int _ticksSinceLastRequest;
    private bool _hasRequestedForThisGoal;
    private long _totalPathRequests;

    public WorkerMovementPlanner()
        : this(WorkerMovementBudget.Default)
    {
    }

    public WorkerMovementPlanner(WorkerMovementBudget budget)
    {
        _budget = budget;
    }

    public bool HasGoal => _hasGoal;

    /// <summary>The current goal. Meaningless when <see cref="HasGoal"/> is
    /// false.</summary>
    public WorkerGoal Goal => _goal;

    /// <summary><see cref="WorkerDeferralReason.None"/> while the goal is still
    /// being pursued.</summary>
    public WorkerDeferralReason DeferredReason => _deferred;

    public bool IsDeferred => _deferred != WorkerDeferralReason.None;

    /// <summary>Path requests authorised so far in the current tick. Reset by
    /// <see cref="BeginTick"/>.</summary>
    public int PathRequestsThisTick => _requestsThisTick;

    /// <summary>Path requests authorised for the current goal.</summary>
    public int AttemptsForCurrentGoal => _attemptsForCurrentGoal;

    /// <summary>Every path request this planner has ever authorised. This is the
    /// "measured" half of gate 5 — a number the adapter can log and a test can
    /// assert on, rather than a claim that the cost is small.</summary>
    public long TotalPathRequests => _totalPathRequests;

    /// <summary>Give the worker somewhere to be. Clears any deferral and
    /// restores the full attempt allowance, because a new goal is a new
    /// question — the fact that the last point was unreachable says nothing
    /// about this one.</summary>
    public void AssignGoal(WorkerGoal goal)
    {
        _goal = goal;
        _hasGoal = true;
        _deferred = WorkerDeferralReason.None;
        _attemptsForCurrentGoal = 0;
        _hasRequestedForThisGoal = false;
        _ticksSinceLastRequest = 0;
    }

    /// <summary>Take the order away. The worker becomes inert: no movement, no
    /// planning, no path requests.</summary>
    public void ClearGoal()
    {
        _hasGoal = false;
        _goal = default;
        _deferred = WorkerDeferralReason.None;
        _attemptsForCurrentGoal = 0;
        _hasRequestedForThisGoal = false;
        _ticksSinceLastRequest = 0;
    }

    /// <summary>Opens a tick. Must be called once per driver tick, before
    /// <see cref="Decide"/>, because it is what refills the per-tick request
    /// allowance and advances the retry backoff.</summary>
    public void BeginTick()
    {
        _requestsThisTick = 0;
        if (_ticksSinceLastRequest < int.MaxValue)
        {
            _ticksSinceLastRequest++;
        }
    }

    /// <summary>What to do about the goal, given what the adapter can see.</summary>
    public WorkerAction Decide(in WorkerObservation observation)
    {
        // No order means genuinely nothing: this is the branch that makes
        // "with no order it does nothing at all" true rather than hopeful.
        if (!_hasGoal)
        {
            return WorkerAction.Idle;
        }

        // A deferred goal is terminal. Re-reporting the same reason is
        // idempotent and, crucially, spends nothing — this is the branch that
        // makes the deferral a stop rather than a slow spin.
        if (_deferred != WorkerDeferralReason.None)
        {
            return WorkerAction.Defer(_deferred);
        }

        // Fail closed first. Authority is not a performance concern, so it is
        // checked before anything that could move a body or cost a request.
        if (!observation.HasAuthority)
        {
            return Defer(WorkerDeferralReason.NoAuthority);
        }

        if (!observation.GoalInLoadedGround)
        {
            return Defer(WorkerDeferralReason.OutsideLoadedGround);
        }

        if (observation.GoalIsHazardous)
        {
            return Defer(WorkerDeferralReason.Hazardous);
        }

        float distance = observation.Position.HorizontalDistanceTo(_goal.Point);

        // Distance is free to measure; a path request is not. Refusing a goal
        // beyond the horizon before spending an attempt is the whole reason
        // this check sits above the budget logic.
        if (distance > _budget.MaxPlanningDistance)
        {
            return Defer(WorkerDeferralReason.TooFar);
        }

        if (distance <= _goal.ArrivalTolerance)
        {
            return WorkerAction.Arrive;
        }

        if (observation.HasPath)
        {
            return WorkerAction.Move;
        }

        // From here the worker needs a path and does not have one.

        // The allowance is spent and the goal still is not reachable. Note this
        // is checked before the backoff: once there is nothing left to try,
        // waiting out a backoff would only delay an answer that is already
        // known.
        if (_attemptsForCurrentGoal >= _budget.MaxPathAttemptsPerGoal)
        {
            return Defer(WorkerDeferralReason.Unreachable);
        }

        // Space the attempts out. The first attempt for a goal is immediate;
        // every later one waits, so the allowance cannot be burned in as many
        // consecutive ticks.
        if (_hasRequestedForThisGoal && _ticksSinceLastRequest < _budget.RetryBackoffTicks)
        {
            return WorkerAction.WaitForBudget;
        }

        if (_requestsThisTick >= _budget.MaxPathRequestsPerTick)
        {
            return WorkerAction.WaitForBudget;
        }

        _requestsThisTick++;
        _attemptsForCurrentGoal++;
        _hasRequestedForThisGoal = true;
        _ticksSinceLastRequest = 0;
        if (_totalPathRequests < long.MaxValue)
        {
            _totalPathRequests++;
        }

        return WorkerAction.RequestPath;
    }

    /// <summary>Records what the pathfinder answered.
    ///
    /// A success resets the attempt allowance: the worker found its way, and a
    /// path that later goes stale — a tree falls, a piece is placed — deserves
    /// the full allowance again rather than the remainder of the last one. A
    /// failure records nothing, because <see cref="Decide"/> already charged the
    /// attempt.</summary>
    public void ReportPathOutcome(bool pathFound)
    {
        if (pathFound)
        {
            _attemptsForCurrentGoal = 0;
            _hasRequestedForThisGoal = false;
        }
    }

    /// <summary>Stop pursuing this goal for a stated reason, from outside the
    /// tick decision — the adapter's own refusal, for instance a check it could
    /// not faithfully make. Ignored when there is no goal, and never overwrites
    /// an existing deferral, so the first reason is the one a player sees.</summary>
    public void DeferExternally(WorkerDeferralReason reason)
    {
        if (!_hasGoal || reason == WorkerDeferralReason.None)
        {
            return;
        }

        if (_deferred == WorkerDeferralReason.None)
        {
            _deferred = reason;
        }
    }

    private WorkerAction Defer(WorkerDeferralReason reason)
    {
        _deferred = reason;
        return WorkerAction.Defer(reason);
    }
}
