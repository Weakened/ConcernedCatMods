namespace TheConcernedCat.Settlement.Worker;

/// <summary>Why a worker stopped trying to reach a goal.
///
/// Every value here is a sentence a player can be shown. #273's gate 5 requires
/// that an unreachable or hazardous goal "defers and explains why", and a
/// deferral without a reason is indistinguishable from a bug — so the reason is
/// part of the type rather than a log line the adapter may or may not write.
///
/// <see cref="None"/> exists so that a deferral reason is never "absent"; a
/// caller reading <see cref="WorkerAction.Reason"/> on a non-deferral gets an
/// explicit "not deferred" rather than a default that means something.</summary>
internal enum WorkerDeferralReason
{
    /// <summary>Not deferred.</summary>
    None = 0,

    /// <summary>Every permitted path request for this goal failed. The goal may
    /// be behind water, inside terrain, or on an island the agent type cannot
    /// path to. The worker stops and says so; it does not keep asking.</summary>
    Unreachable = 1,

    /// <summary>The goal is further away than this worker is allowed to plan.
    /// Checked before any path request is spent, because distance is cheap to
    /// measure and pathfinding is not.</summary>
    TooFar = 2,

    /// <summary>The adapter reported the goal as hazardous — fire, deep water,
    /// lava. Refusing is the whole point: a worker that walks into a fire to
    /// satisfy an order is worse than one that stops and explains.</summary>
    Hazardous = 3,

    /// <summary>The runtime could not establish that it was allowed to act:
    /// not the owner of this actor, or no authorised host. This is the
    /// fail-closed case the authority ADR requires — an unsupported authority
    /// check refuses, it does not assume permission.</summary>
    NoAuthority = 4,

    /// <summary>The goal is outside loaded ground. The first proof has no
    /// offscreen work and reports that limit rather than hiding it.</summary>
    OutsideLoadedGround = 5,
}

/// <summary>What the worker should do this tick.</summary>
internal enum WorkerActionKind
{
    /// <summary>Nothing at all. No order means no movement, no wandering and
    /// no path requests — the worker is inert, which is exactly what
    /// CF-SET-002 has to demonstrate.</summary>
    Idle = 0,

    /// <summary>Spend one path request from the budget.</summary>
    RequestPath = 1,

    /// <summary>A path is known; step along it.</summary>
    Move = 2,

    /// <summary>Close enough. Stop moving and report the goal reached.</summary>
    Arrive = 3,

    /// <summary>Stop, and hold the stated reason. Terminal for this goal.</summary>
    Defer = 4,

    /// <summary>The per-tick path budget is spent. Not a failure and not a
    /// deferral: the worker simply waits for the next tick. Kept distinct from
    /// <see cref="Idle"/> so that "doing nothing because there is no order" and
    /// "doing nothing because planning is rate-limited" are never confused in a
    /// log or a test.</summary>
    WaitForBudget = 5,
}

/// <summary>One tick's decision.</summary>
internal readonly struct WorkerAction
{
    private WorkerAction(WorkerActionKind kind, WorkerDeferralReason reason)
    {
        Kind = kind;
        Reason = reason;
    }

    public WorkerActionKind Kind { get; }

    /// <summary><see cref="WorkerDeferralReason.None"/> unless
    /// <see cref="Kind"/> is <see cref="WorkerActionKind.Defer"/>.</summary>
    public WorkerDeferralReason Reason { get; }

    public static WorkerAction Idle => new(WorkerActionKind.Idle, WorkerDeferralReason.None);
    public static WorkerAction RequestPath => new(WorkerActionKind.RequestPath, WorkerDeferralReason.None);
    public static WorkerAction Move => new(WorkerActionKind.Move, WorkerDeferralReason.None);
    public static WorkerAction Arrive => new(WorkerActionKind.Arrive, WorkerDeferralReason.None);
    public static WorkerAction WaitForBudget => new(WorkerActionKind.WaitForBudget, WorkerDeferralReason.None);

    public static WorkerAction Defer(WorkerDeferralReason reason)
    {
        return new WorkerAction(WorkerActionKind.Defer, reason);
    }

    public override string ToString()
    {
        return Kind == WorkerActionKind.Defer
            ? $"{Kind}({Reason})"
            : Kind.ToString();
    }
}
