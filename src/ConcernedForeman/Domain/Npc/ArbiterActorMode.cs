using System;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.Workers;
using Library = TheConcernedCat.ConcernedNPC.Bodies;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Thorstein's mode, taken from Concerned NPC's one arbiter instead of
/// from an owner this product built for itself.
///
/// <b>What this replaces, and why the replacement is stronger.</b> Until the
/// adoption <c>CollectionRuntime</c> constructed its own <c>ActorModeOwner</c> for
/// <c>WorkerKey.Thorstein</c>, out of shared source compiled into this assembly.
/// (The constructor call is not written out here, or anywhere in this product: the
/// validator reads these files as text and a quoted example would be
/// indistinguishable from the real thing.) That owner was correct and
/// it was also unreachable: Concerned Teamster and Concerned Steward each
/// compiled their own copy of the same file, so "one mode owner per identity" was
/// true three times over, once per assembly, and nothing could see across. The
/// never-coexist rule — one identity never has a presentation body and a worker
/// body at once — was therefore enforced only by everybody agreeing to it. The
/// arbiter inside the library is one object for the whole process, so a refusal
/// here is a refusal every product can see, and the validator refuses the
/// constructor line outright for a consumer
/// (<c>check_library_consumers_do_not_bypass_the_arbiter</c>).
///
/// <b>Every mutation goes through the registry, never through the owner.</b>
/// <c>NpcRoleRegistry.ModeOf</c> hands back a read-only view: the owner's
/// <c>Enter</c> and <c>Release</c> are internal to the library precisely so that
/// the one arbiter sees every change. So this type reads through the owner and
/// writes through <c>EnterMode</c> and <c>ReleaseMode</c>, which is not an
/// inconsistency — it is the shape the library was built in.
///
/// <b>It fails closed, in both of the ways that matter.</b>
///
/// <i>A mode it cannot take is a refusal.</i> <c>EnterMode</c> answers
/// <c>Unspecified</c> for an identity the registry does not track — a
/// registration that was refused at load, say — and <c>Unspecified</c> is
/// reported as exactly that. It is not translated into a grant and it is not
/// translated into <c>RefusedBusy</c>, because a caller that only looks for
/// <c>RefusedBusy</c> is the defect this whole seam exists to make impossible;
/// <c>ActorModeGrants.IsGranted</c> is what a caller asks.
///
/// <i>An identity it cannot find may not have its body retired.</i> With no
/// owner there is no evidence that nothing holds him, so
/// <see cref="MayRetireBody"/> and <see cref="MayRelocateHome"/> are false.
/// Reading them as true would be reading "I could not tell" as "nobody is in
/// there", which is how a body somebody's job is standing in gets despawned.
/// That is the worker-authority half of the program's rule, not the
/// feature-access half: ambiguous evidence grants feature access and refuses
/// authority.
///
/// <b>Two enums, one vocabulary, translated by hand.</b> The library's
/// <c>ActorMode</c> and the shared <c>ActorMode</c> are two distinct CLR types
/// with identical members, by design: shared-source types are <c>internal</c> to
/// each assembly and cannot appear on a library's public surface. They are
/// mapped member by member rather than cast, so a value either side gains that
/// the other does not know becomes <c>Unspecified</c> — a refusal — instead of a
/// silently wrong mode. <c>ForemanNpcAdoptionTests</c> pins the mapping in both
/// directions across every member.</summary>
internal sealed class ArbiterActorMode : IActorModeHold
{
    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;

    /// <param name="registry">The process-wide registry in a game
    /// (<c>NpcRoleRegistry.Shared</c>), a private one in a test. Passed in rather
    /// than reached for, so a test is not one global away from the next
    /// test.</param>
    /// <param name="worker">Whose mode this is, in this product's own
    /// vocabulary. Kept so the game-free loops can keep checking that the hold
    /// they were handed belongs to the worker they drive.</param>
    /// <param name="identity">The same worker, in the library's vocabulary. Both
    /// are taken rather than one converted, because the conversion belongs at the
    /// boundary that owns both names — <see cref="ForemanNpcAdoption"/> — and not
    /// in a type whose job is to answer questions.</param>
    internal ArbiterActorMode(NpcRoleRegistry registry, WorkerKey worker, NpcIdentity identity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (worker.IsEmpty)
        {
            throw new ArgumentException("An actor-mode hold needs a worker.", nameof(worker));
        }

        if (identity.IsEmpty)
        {
            throw new ArgumentException("An actor-mode hold needs an identity.", nameof(identity));
        }

        Worker = worker;
        _identity = identity;
    }

    /// <inheritdoc />
    public WorkerKey Worker { get; }

    /// <summary>True while the arbiter actually holds a mode owner for this
    /// identity. False means the registration was refused, and every reader below
    /// gives its closed answer - and the refusals a player sees say <i>that</i>
    /// rather than blaming a busy job or a missing body.</summary>
    public bool IsIdentityKnown => _registry.ModeOf(_identity) != null;

    /// <inheritdoc />
    public ActorMode Mode
    {
        get
        {
            Library.ActorModeOwner? owner = _registry.ModeOf(_identity);
            return owner == null ? ActorMode.Unspecified : FromLibrary(owner.Mode);
        }
    }

    /// <inheritdoc />
    public string? JobId => _registry.ModeOf(_identity)?.JobId;

    /// <inheritdoc />
    public bool MayRelocateHome => _registry.ModeOf(_identity)?.MayRelocateHome ?? false;

    /// <inheritdoc />
    public bool MayRetireBody => _registry.ModeOf(_identity)?.MayRetireBody ?? false;

    /// <inheritdoc />
    public bool IsHeldBy(string? jobId) => _registry.ModeOf(_identity)?.IsHeldBy(jobId) ?? false;

    /// <inheritdoc />
    public ActorModeOutcome Enter(ActorMode mode, string jobId) =>
        FromLibrary(_registry.EnterMode(_identity, ToLibrary(mode), jobId));

    /// <inheritdoc />
    public ActorModeOutcome Release(string jobId) =>
        FromLibrary(_registry.ReleaseMode(_identity, jobId));

    /// <summary>This product's mode, in the library's vocabulary. An unmapped
    /// value becomes <c>Unspecified</c>, which the registry refuses outright
    /// rather than guessing at.</summary>
    internal static Library.ActorMode ToLibrary(ActorMode mode)
    {
        switch (mode)
        {
            case ActorMode.Resting: return Library.ActorMode.Resting;
            case ActorMode.Surveying: return Library.ActorMode.Surveying;
            case ActorMode.Working: return Library.ActorMode.Working;
            case ActorMode.Paused: return Library.ActorMode.Paused;
            case ActorMode.Recovering: return Library.ActorMode.Recovering;
            default: return Library.ActorMode.Unspecified;
        }
    }

    internal static ActorMode FromLibrary(Library.ActorMode mode)
    {
        switch (mode)
        {
            case Library.ActorMode.Resting: return ActorMode.Resting;
            case Library.ActorMode.Surveying: return ActorMode.Surveying;
            case Library.ActorMode.Working: return ActorMode.Working;
            case Library.ActorMode.Paused: return ActorMode.Paused;
            case Library.ActorMode.Recovering: return ActorMode.Recovering;
            default: return ActorMode.Unspecified;
        }
    }

    /// <summary>The library's answer, in this product's vocabulary.
    ///
    /// <c>Unspecified</c> stays <c>Unspecified</c>. It is tempting to turn it
    /// into <c>RefusedBusy</c> so that a caller written before the adoption keeps
    /// working; that would be a lie about why the hold was refused, and the
    /// honest fix — a caller that asks whether the outcome was a <i>grant</i> —
    /// is the one that also survives the next outcome anybody adds.</summary>
    internal static ActorModeOutcome FromLibrary(Library.ActorModeOutcome outcome)
    {
        switch (outcome)
        {
            case Library.ActorModeOutcome.Entered: return ActorModeOutcome.Entered;
            case Library.ActorModeOutcome.AlreadyInMode: return ActorModeOutcome.AlreadyInMode;
            case Library.ActorModeOutcome.RefusedBusy: return ActorModeOutcome.RefusedBusy;
            case Library.ActorModeOutcome.Released: return ActorModeOutcome.Released;
            case Library.ActorModeOutcome.NotHeld: return ActorModeOutcome.NotHeld;
            default: return ActorModeOutcome.Unspecified;
        }
    }
}
