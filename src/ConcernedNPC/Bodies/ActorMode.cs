using System;

namespace TheConcernedCat.ConcernedNPC.Bodies;

/// <summary>What an NPC identity is doing, owned by exactly one arbiter per
/// identity. Zero is unspecified.
///
/// <b>These values mirror <c>src/Shared/Workers/ActorMode.cs</c> exactly, and
/// the copy is the point of this whole package rather than an accident.</b>
/// That file is <c>internal</c> source compiled separately into Concerned
/// Foreman, Concerned Teamster and Concerned Steward, so each of those
/// assemblies has its own <c>ActorMode</c> type and its own
/// <c>ActorModeOwner</c> instances. This library cannot see any of them, and
/// they cannot see it. Until every role asks this arbiter instead of
/// constructing its own owner, one identity can still have two owners in two
/// assemblies, each believing it is the arbiter - which is exactly the condition
/// that lets one identity end up with two bodies. Closing that is the role
/// leaves' work; keeping the vocabulary identical is what makes it a namespace
/// change rather than a semantic one when they do.</summary>
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

/// <summary>What an attempt to take or release an identity's mode did.</summary>
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

/// <summary>The single actor-mode owner for one NPC identity.
///
/// One job at a time holds the identity; while it does, nothing else - a bed
/// change, hiding presentation, a home relocation, retiring the body - may move
/// or replace it, because the job's actual position (and anything attached to
/// it) wins over home. A job moves freely between Surveying, Working, Paused and
/// Recovering; only releasing it returns the identity to Resting.
///
/// Constructed only by <see cref="NpcBodyArbiter"/>, which holds one per
/// registered identity. That is deliberate and is the difference between this
/// and the three shipped copies it mirrors: a second owner for one identity is
/// not something a caller can reach for.</summary>
internal sealed class ActorModeOwner
{
    internal ActorModeOwner(Roles.NpcIdentity identity)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("An actor-mode owner needs an identity.", nameof(identity));
        }

        Identity = identity;
    }

    internal Roles.NpcIdentity Identity { get; }

    internal ActorMode Mode { get; private set; } = ActorMode.Resting;

    /// <summary>The job holding the identity, or null while resting.</summary>
    internal string? JobId { get; private set; }

    /// <summary>Increments on every change, so an observer can tell a mode it
    /// read is stale.</summary>
    internal int Revision { get; private set; }

    internal bool MayRelocateHome => Mode == ActorMode.Resting;

    internal bool MayRetireBody => Mode == ActorMode.Resting;

    internal bool IsHeldBy(string? jobId) =>
        JobId != null && jobId != null && string.Equals(JobId, jobId, StringComparison.Ordinal);

    internal ActorModeOutcome Enter(ActorMode mode, string jobId)
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

    internal ActorModeOutcome Release(string jobId)
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
