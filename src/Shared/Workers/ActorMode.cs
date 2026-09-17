using System;

namespace TheConcernedCat.Workers;

/// <summary>What a worker identity is doing, owned by exactly one arbiter per
/// identity (ARCH-02). Zero is unspecified.</summary>
internal enum ActorMode
{
    Unspecified = 0,

    /// <summary>At home or idle. The only mode in which the identity's home may
    /// move it, and the only mode in which its body may be retired.</summary>
    Resting = 1,

    Surveying = 2,

    Working = 3,

    /// <summary>A job is held but not progressing: waiting for the player, a
    /// peer to leave, a destination to free up.</summary>
    Paused = 4,

    /// <summary>Bringing a job back to a safe state after a failure: detaching,
    /// parking, reconciling custody. Nothing else may interrupt it.</summary>
    Recovering = 5,
}

internal enum ActorModeOutcome
{
    Unspecified = 0,
    Entered = 1,
    AlreadyInMode = 2,

    /// <summary>A different job holds this identity.</summary>
    RefusedBusy = 3,

    Released = 4,

    /// <summary>The job asking to release does not hold the identity.</summary>
    NotHeld = 5,
}

/// <summary>The single actor-mode owner for one worker identity.
///
/// One job at a time holds the identity; while it does, nothing else - a bed
/// change, hiding presentation, a home relocation - may move, retire or replace
/// the body, because the job's actual position (and the cart or cargo attached
/// to it) wins over home. A job moves freely between Surveying, Working, Paused
/// and Recovering; only releasing it returns the identity to Resting.</summary>
internal sealed class ActorModeOwner
{
    public ActorModeOwner(WorkerKey worker)
    {
        if (worker.IsEmpty)
        {
            throw new ArgumentException("An actor-mode owner needs a worker.", nameof(worker));
        }

        Worker = worker;
    }

    public WorkerKey Worker { get; }

    public ActorMode Mode { get; private set; } = ActorMode.Resting;

    /// <summary>The job holding the identity, or null while resting.</summary>
    public string? JobId { get; private set; }

    /// <summary>Increments on every change, so an observer can tell a mode it
    /// read is stale.</summary>
    public int Revision { get; private set; }

    public bool MayRelocateHome => Mode == ActorMode.Resting;

    public bool MayRetireBody => Mode == ActorMode.Resting;

    public bool IsHeldBy(string? jobId) =>
        JobId != null && jobId != null && string.Equals(JobId, jobId, StringComparison.Ordinal);

    public ActorModeOutcome Enter(ActorMode mode, string jobId)
    {
        if (mode == ActorMode.Unspecified || mode == ActorMode.Resting)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Resting is reached by releasing the job.");
        }

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentException("A job id is required.", nameof(jobId));
        }

        if (JobId != null && !IsHeldBy(jobId))
        {
            return ActorModeOutcome.RefusedBusy;
        }

        if (JobId != null && Mode == mode)
        {
            return ActorModeOutcome.AlreadyInMode;
        }

        JobId = jobId;
        Mode = mode;
        Revision++;
        return ActorModeOutcome.Entered;
    }

    public ActorModeOutcome Release(string jobId)
    {
        if (!IsHeldBy(jobId))
        {
            return ActorModeOutcome.NotHeld;
        }

        JobId = null;
        Mode = ActorMode.Resting;
        Revision++;
        return ActorModeOutcome.Released;
    }
}
