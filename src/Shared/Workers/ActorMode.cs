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

/// <summary>One worker identity's mode, as the code that drives a job sees it:
/// what he is doing, which job holds him, and the two verbs that take and give
/// back the hold.
///
/// <b>Why this is an interface and not just <see cref="ActorModeOwner"/>.</b>
/// Because who owns the mode is no longer the same answer in every product. A
/// product that has not adopted the Concerned NPC library still holds its
/// identity in an <see cref="ActorModeOwner"/> compiled into its own assembly;
/// a product that HAS adopted it must take the mode from the library's one
/// arbiter instead, or one identity has two mode owners and the never-coexist
/// rule is advice rather than enforcement
/// (<c>tools/validate_repo.py check_library_consumers_do_not_bypass_the_arbiter</c>).
/// This is the seam that lets the game-free job loops in
/// <c>src/Shared/Settlement</c> be written once against either.
///
/// <b>Every member here is one an existing caller already used</b> — nothing was
/// added for symmetry. Widening it later is a deliberate edit, because each
/// member is a thing an arbiter-backed implementation has to be able to answer
/// honestly without inventing a fact.
///
/// <b>Reading a mode and changing one are different questions.</b> The readers
/// are total: they answer for an identity nobody has registered, and the answer
/// is the closed one — an unknown mode is not <see cref="ActorMode.Resting"/>,
/// so <see cref="MayRetireBody"/> and <see cref="MayRelocateHome"/> are false
/// rather than true. Failing open here would let a runtime that could not find
/// its identity retire a body somebody's job is standing in.</summary>
internal interface IActorModeHold
{
    /// <summary>Whose mode this is. A loop checks it against the worker it was
    /// built for, so a mis-wiring is a construction-time failure rather than a
    /// worker walking on somebody else's hold.</summary>
    WorkerKey Worker { get; }

    ActorMode Mode { get; }

    /// <summary>The job holding the identity, or null when nothing does.</summary>
    string? JobId { get; }

    bool MayRelocateHome { get; }

    bool MayRetireBody { get; }

    bool IsHeldBy(string? jobId);

    /// <summary>Takes the identity for <paramref name="jobId"/>, or says why
    /// not. <see cref="ActorModeOutcome.Entered"/> and
    /// <see cref="ActorModeOutcome.AlreadyInMode"/> are the only two grants;
    /// <b>every other outcome, including
    /// <see cref="ActorModeOutcome.Unspecified"/>, is a refusal</b> and a caller
    /// that treats one of them as a grant runs a job with no hold on the body it
    /// is moving.</summary>
    ActorModeOutcome Enter(ActorMode mode, string jobId);

    /// <summary>Gives the identity back. Releasing one this job does not hold
    /// changes nothing and is not an error, so cleanup after a death, a reload
    /// or a countermand can be unconditional.</summary>
    ActorModeOutcome Release(string jobId);
}

/// <summary>Whether an <see cref="IActorModeHold.Enter"/> outcome actually
/// granted the hold.
///
/// <b>Stated once, here, because getting it wrong is silent.</b> A caller that
/// asks "is this RefusedBusy?" treats every other refusal - most importantly
/// <see cref="ActorModeOutcome.Unspecified"/>, which is what an arbiter answers
/// for an identity it does not track - as permission to proceed, and then drives
/// a body it has no hold on.</summary>
internal static class ActorModeGrants
{
    public static bool IsGranted(ActorModeOutcome outcome) =>
        outcome == ActorModeOutcome.Entered || outcome == ActorModeOutcome.AlreadyInMode;
}

/// <summary>The single actor-mode owner for one worker identity.
///
/// One job at a time holds the identity; while it does, nothing else - a bed
/// change, hiding presentation, a home relocation - may move, retire or replace
/// the body, because the job's actual position (and the cart or cargo attached
/// to it) wins over home. A job moves freely between Surveying, Working, Paused
/// and Recovering; only releasing it returns the identity to Resting.
///
/// <b>This is the pre-adoption implementation of
/// <see cref="IActorModeHold"/>.</b> A product that consumes the Concerned NPC
/// library may not construct one - the validator refuses the line - and takes
/// the same seam over the library's arbiter instead. Products that have not
/// adopted the library still use this, and it is still correct for them.</summary>
internal sealed class ActorModeOwner : IActorModeHold
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
