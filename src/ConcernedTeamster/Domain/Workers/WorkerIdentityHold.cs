using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Workers;

/// <summary>What an attempt to take or give back an identity did, as the
/// authority behind it answered.</summary>
internal enum WorkerHoldOutcome
{
    /// <summary>Nobody asked, or the authority could not answer. Fails closed:
    /// treated as a refusal everywhere.</summary>
    Unspecified = 0,

    /// <summary>This job now holds the identity.</summary>
    Taken = 1,

    /// <summary>This job already held it.</summary>
    AlreadyHeld = 2,

    /// <summary>Somebody else holds it.</summary>
    RefusedBusy = 3,

    /// <summary>Given back.</summary>
    Given = 4,

    /// <summary>This job did not hold it, so there was nothing to give
    /// back.</summary>
    NotHeld = 5,

    /// <summary>The authority is not in a position to answer - no world, no
    /// registration. A refusal, never a grant.</summary>
    Unavailable = 6,
}

/// <summary>Who may say that one job holds Gunnar's identity.
///
/// <b>Why this is a seam and not a field.</b> Until this product consumed the
/// shared NPC runtime, Teamster answered this question itself, out of its own
/// compiled copy of the shared worker source - and so did Concerned Foreman, and
/// so did Concerned Steward, each of them certain it was the only one. Three
/// arbiters for three identities is fine right up to the moment two of them are
/// asked about the same one. The shared runtime exists to end that, and the
/// repository now fails the build if a product that consumes it builds its own
/// mode owner.
///
/// So the hold comes from the runtime's own arbiter, through
/// <c>GunnarIdentityAuthority</c>, and this interface is the line between the
/// game-free executor and that. A test supplies its own, which is the other
/// reason it is here: the arbiter is a process-wide singleton, and a suite that
/// shared one would have its tests holding each other's identities.</summary>
internal interface IWorkerIdentityAuthority
{
    /// <summary>The job holding the identity right now, or null. Asked rather
    /// than remembered, because a hold can end without this product noticing -
    /// a world unload ends every hold the arbiter has.</summary>
    string? Holder { get; }

    /// <summary>Asks for the identity on behalf of one job.</summary>
    WorkerHoldOutcome Take(string jobId);

    /// <summary>Gives it back. Giving back one this job does not hold says so
    /// and changes nothing, so tidying up after a failure can be
    /// unconditional.</summary>
    WorkerHoldOutcome Give(string jobId);
}

/// <summary>What Gunnar's identity is doing, and who is entitled to say so.
///
/// <b>What changed, and what did not.</b> The vocabulary is unchanged - the same
/// five modes, the same outcomes, the same rule that only releasing a job
/// returns the identity to <see cref="ActorMode.Resting"/> - because every one
/// of those is a shipped behaviour with tests on it. What changed is where the
/// <i>hold</i> lives: this type no longer owns it, it asks
/// <see cref="IWorkerIdentityAuthority"/>, and in a running game that is the
/// shared runtime's arbiter. Two jobs in this product - a haul and a collection
/// round - therefore cannot both hold Gunnar, and neither can a job in another
/// product that ever registers the same identity.
///
/// <b>The residue, stated rather than discovered.</b> The <i>mode word</i> is
/// still kept here. The shared runtime has its own mode owner per identity and
/// it is the right home for this, but its accessor is <c>internal</c> to that
/// assembly, so a product cannot reach it. When it opens, this type keeps its
/// shape and its <see cref="Mode"/> becomes a read of the arbiter's - a rename,
/// not a redesign. Until then, one fact about Gunnar lives in two assemblies:
/// the hold, which is the arbiter's and is enforced, and the word for what he is
/// doing, which is this product's and is reported.</summary>
internal sealed class WorkerIdentityHold
{
    private readonly IWorkerIdentityAuthority _authority;

    public WorkerIdentityHold(WorkerKey worker, IWorkerIdentityAuthority authority)
    {
        if (worker.IsEmpty)
        {
            throw new ArgumentException("An identity hold needs a worker.", nameof(worker));
        }

        Worker = worker;
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public WorkerKey Worker { get; }

    /// <summary>What he is doing. <see cref="ActorMode.Resting"/> whenever no
    /// job holds him, whatever the authority thinks.</summary>
    public ActorMode Mode { get; private set; } = ActorMode.Resting;

    /// <summary>The job holding the identity, or null while resting. Read
    /// through to the authority every time.</summary>
    public string? JobId => _authority.Holder;

    /// <summary>Increments on every change, so an observer can tell a mode it
    /// read is stale.</summary>
    public int Revision { get; private set; }

    public bool MayRelocateHome => Mode == ActorMode.Resting;

    public bool MayRetireBody => Mode == ActorMode.Resting;

    public bool IsHeldBy(string? jobId)
    {
        string? holder = JobId;
        return holder != null && jobId != null && string.Equals(holder, jobId, StringComparison.Ordinal);
    }

    /// <summary>Takes, or keeps, the identity for one job and records what it is
    /// doing.</summary>
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

        WorkerHoldOutcome held = _authority.Take(jobId);
        if (held != WorkerHoldOutcome.Taken && held != WorkerHoldOutcome.AlreadyHeld)
        {
            // RefusedBusy, Unavailable and an authority that answered nothing
            // all mean the same thing to a job about to move a body: not yours.
            return ActorModeOutcome.RefusedBusy;
        }

        if (held == WorkerHoldOutcome.AlreadyHeld && Mode == mode)
        {
            return ActorModeOutcome.AlreadyInMode;
        }

        Mode = mode;
        Revision++;
        return ActorModeOutcome.Entered;
    }

    /// <summary>Gives the identity back. Only the holder may.</summary>
    public ActorModeOutcome Release(string jobId)
    {
        if (!IsHeldBy(jobId))
        {
            return ActorModeOutcome.NotHeld;
        }

        _authority.Give(jobId);
        Mode = ActorMode.Resting;
        Revision++;
        return ActorModeOutcome.Released;
    }

    /// <summary>Forgets the mode word because the world it described has gone.
    /// Does not give the hold back: the authority ends its own holds when a
    /// world ends, and a product reaching in to release somebody else's is the
    /// failure the arbiter exists to prevent.</summary>
    public void ForgetWorld()
    {
        if (Mode == ActorMode.Resting)
        {
            return;
        }

        Mode = ActorMode.Resting;
        Revision++;
    }
}

/// <summary>An authority for a process with no shared runtime behind it: it
/// remembers one holder and nothing else.
///
/// <b>Not used in a running game.</b> Teamster declares a hard dependency on the
/// shared runtime package, so the real authority is always available there. This
/// exists so the game-free executor and its suite can be exercised without a
/// process-wide singleton that every test would be sharing, and so that the
/// behaviour of the hold itself can be tested apart from the arbiter's.</summary>
internal sealed class LocalWorkerIdentityAuthority : IWorkerIdentityAuthority
{
    public string? Holder { get; private set; }

    public WorkerHoldOutcome Take(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return WorkerHoldOutcome.Unspecified;
        }

        if (Holder == null)
        {
            Holder = jobId;
            return WorkerHoldOutcome.Taken;
        }

        return string.Equals(Holder, jobId, StringComparison.Ordinal)
            ? WorkerHoldOutcome.AlreadyHeld
            : WorkerHoldOutcome.RefusedBusy;
    }

    public WorkerHoldOutcome Give(string jobId)
    {
        if (Holder == null || !string.Equals(Holder, jobId, StringComparison.Ordinal))
        {
            return WorkerHoldOutcome.NotHeld;
        }

        Holder = null;
        return WorkerHoldOutcome.Given;
    }
}
