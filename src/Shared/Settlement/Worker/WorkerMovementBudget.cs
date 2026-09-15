using System;

namespace TheConcernedCat.Settlement.Worker;

/// <summary>The limits a worker plans inside.
///
/// Pathfinding is the expensive part of an NPC, and #273's gate 5 requires it
/// to be "local and budgeted". Every bound here is a number rather than a
/// convention, so "bounded" is something a test can prove instead of something
/// a comment claims.
///
/// <b>The tick these counts are per.</b> In the installed build the AI driver
/// is <c>MonoUpdaters.FixedUpdate</c>, which accumulates fixed delta time and
/// runs the whole of <c>BaseAI.Instances</c> once every <b>0.05 s</b>, passing a
/// constant <c>0.05f</c> rather than the real elapsed time. So a tick is 1/20 s,
/// and <see cref="MaxPathRequestsPerTick"/> of 1 means at most twenty path
/// requests per worker per second — not per frame.</summary>
internal readonly struct WorkerMovementBudget
{
    public WorkerMovementBudget(
        int maxPathRequestsPerTick,
        int maxPathAttemptsPerGoal,
        int retryBackoffTicks,
        float maxPlanningDistance)
    {
        if (maxPathRequestsPerTick < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPathRequestsPerTick), maxPathRequestsPerTick,
                "A worker that may make no path requests can never move.");
        }

        if (maxPathAttemptsPerGoal < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPathAttemptsPerGoal), maxPathAttemptsPerGoal,
                "A goal must be attempted at least once before it is called unreachable.");
        }

        if (retryBackoffTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryBackoffTicks), retryBackoffTicks,
                "Backoff cannot be negative.");
        }

        if (!(maxPlanningDistance > 0f) || float.IsInfinity(maxPlanningDistance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPlanningDistance), maxPlanningDistance,
                "Planning distance must be a positive, finite number of metres.");
        }

        MaxPathRequestsPerTick = maxPathRequestsPerTick;
        MaxPathAttemptsPerGoal = maxPathAttemptsPerGoal;
        RetryBackoffTicks = retryBackoffTicks;
        MaxPlanningDistance = maxPlanningDistance;
    }

    /// <summary>How many path requests this worker may spend in one 0.05 s
    /// tick. One is the deliberate default: a settlement worker has no reason
    /// to re-plan faster than twenty times a second.</summary>
    public int MaxPathRequestsPerTick { get; }

    /// <summary>How many failed attempts one goal gets before it is declared
    /// <see cref="WorkerDeferralReason.Unreachable"/>. This is the bound that
    /// turns "retry" into "give up and explain".</summary>
    public int MaxPathAttemptsPerGoal { get; }

    /// <summary>Ticks to wait after a failed request before spending another
    /// attempt. Without it, the attempt allowance would be burned in as many
    /// consecutive ticks, which is a spin with a low ceiling rather than a
    /// considered retry.</summary>
    public int RetryBackoffTicks { get; }

    /// <summary>The furthest a goal may be, in metres on the ground plane,
    /// before the worker refuses to plan for it at all. Checked before any
    /// attempt is spent, because measuring a distance is free and asking the
    /// pathfinder is not.</summary>
    public float MaxPlanningDistance { get; }

    /// <summary>One request per tick, five attempts, half a second of backoff
    /// between them, and a sixty-four metre planning horizon.
    ///
    /// Sixty-four metres is two Valheim zones' worth of half-width and
    /// comfortably inside loaded ground; the worst case for one goal is
    /// therefore five requests spread over about a fifth of a second of
    /// attempts plus two seconds of backoff, after which the worker stops
    /// asking entirely.</summary>
    public static WorkerMovementBudget Default => new(
        maxPathRequestsPerTick: 1,
        maxPathAttemptsPerGoal: 5,
        retryBackoffTicks: 10,
        maxPlanningDistance: 64f);
}
