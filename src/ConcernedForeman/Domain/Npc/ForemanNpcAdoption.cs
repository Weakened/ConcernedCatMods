using System;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>How Concerned Foreman holds Thorstein's identity, now that Concerned
/// NPC is the thing that answers who may have a body.
///
/// <b>Why the mode and not a body claim.</b> Foreman does not build Thorstein's
/// body — it finds the one the world already saved, by prefab name — so there is
/// no construction of its own for a lease to gate. What Foreman does own is the
/// job, and the job is what the arbiter has to be able to see:
/// <c>NpcBodyArbiter.TryClaim</c> refuses a body to any holder but the mode's
/// holder while a job holds the identity, naming the job in the refusal, and
/// refuses a presentation body to an identity that is not resting. Before this,
/// that mode lived in an <c>ActorModeOwner</c> compiled into this assembly alone,
/// so the arbiter had no way to know Thorstein was busy and both of those
/// refusals were unreachable for him. Now they are not.
///
/// <b>Exactly how much the never-coexist rule gains, stated precisely.</b>
/// Thorstein is registered with a <i>worker</i> body contract, and an identity has
/// one contract; a presentation-body claim for him is therefore refused as
/// <c>RefusedKindNotContracted</c> whatever his mode is, and it would be refused
/// that way with or without this adoption. What the adoption actually adds is the
/// refusal that was missing: while a collection order holds him, <b>nothing else
/// in the process may take his body</b> — not another runtime, not another
/// product, not a second copy of this one — because the arbiter can now see the
/// hold. And if a presentation contract for him is ever written, the mode gate is
/// already the thing that will stop the camp figure appearing while he works,
/// without anybody having to remember to add it. That is the difference between a
/// rule enforced and a rule agreed to.
///
/// <b>What is deliberately NOT here.</b> No body claim and no prefab handover.
/// Foreman still registers its own prefab, from its own plugin start, under its
/// own name — a prefab registered late, or under a different name, deletes every
/// existing saved worker on the next load. Moving Thorstein's body onto
/// <c>NpcWorkerPrefabFactory</c> is a separate adoption.
///
/// <b>Registration is per process, not per world.</b> The library says so, and it
/// matters: a role re-registered on every world load is refused as a duplicate
/// from the second load onwards and the log fills with it.
///
/// <b>A mode belongs to one world load.</b> <see cref="NoteWorldLoaded"/> mints
/// the epoch and <see cref="NoteWorldUnloaded"/> ends it, and ending it is what
/// abandons a hold whose job id nothing still has. Without that call an
/// identity held by a job that died with its world stays busy for the life of the
/// process, and no ordinary release can help, because a release needs the
/// holder's own string.
///
/// <b>Everything here is total.</b> Registration is offered once and its outcome
/// is reported rather than thrown, because four products register into one
/// process at load and one of them failing must not take the others down. Every
/// other call is idempotent, so a runtime tidying up after a reload or a failure
/// can call them unconditionally.</summary>
internal sealed class ForemanNpcAdoption
{
    private readonly NpcRoleRegistry _registry;
    private readonly Action<string>? _log;

    private bool _offered;
    private bool _registered;

    /// <param name="registry">The process-wide registry in a game
    /// (<c>NpcRoleRegistry.Shared</c>), a private one in a test. Passed in rather
    /// than read from the static, so a test is not one global away from the next
    /// test.</param>
    /// <param name="role">Foreman's declaration of its own durable facts.</param>
    /// <param name="log">Where a refusal is reported. A refusal is always
    /// reported: a Thorstein who quietly declines to exist is the hardest kind of
    /// bug to be told about.</param>
    internal ForemanNpcAdoption(NpcRoleRegistry registry, ForemanNpcRole role, Action<string>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Role = role ?? throw new ArgumentNullException(nameof(role));
        _log = log;
        Modes = new ArbiterActorMode(registry, ForemanRole.Worker, ForemanNpcRole.Id);
    }

    internal ForemanNpcRole Role { get; }

    /// <summary>The registry Thorstein is registered in. Handed out rather than
    /// reached for, so the one place that registered him is the one place a job
    /// is started from, in the product and in a test alike.</summary>
    internal NpcRoleRegistry Registry => _registry;

    internal NpcIdentity Identity => ForemanNpcRole.Id;

    internal WorkerKey Worker => ForemanRole.Worker;

    /// <summary>Thorstein's mode, over the arbiter. Built in the constructor and
    /// handed to the collection runtime, which hands it to the loop and to the
    /// motion port: <b>one</b> hold, for the life of the plugin, and not a
    /// private mode owner anywhere.
    ///
    /// Valid before <see cref="Register"/> and after a refused registration; it
    /// simply answers closed — no job may take the mode, and the body may not be
    /// retired, because nothing can establish that nobody is standing in
    /// it.</summary>
    internal IActorModeHold Modes { get; }

    /// <summary>True once the library has accepted Foreman's declaration.</summary>
    internal bool IsRegistered => _registered;

    /// <summary>The world load the library is currently in, or unknown.</summary>
    internal NpcWorldEpoch World => _registry.CurrentWorld;

    /// <summary>Offers Thorstein to the library. Called once, from plugin start,
    /// and safe to call again.</summary>
    internal RoleRegistration Register()
    {
        if (_offered)
        {
            return new RoleRegistration();
        }

        _offered = true;
        RoleRegistration outcome = _registry.Register(Role);
        _registered = outcome.IsRegistered;
        if (!_registered)
        {
            _log?.Invoke(
                "Thorstein could not register with Concerned NPC (" + outcome.Status + ": " +
                outcome.Reason + "). He will take no collection order this session.");
        }

        return outcome;
    }

    /// <summary>Tells the library a world has been loaded and takes the epoch it
    /// mints. Idempotent while the same world is up, and safe when another
    /// product's runtime got there first.</summary>
    internal NpcWorldEpoch NoteWorldLoaded()
    {
        NpcWorldEpoch world = _registry.BeginWorldLoad(out int forgotten);
        if (forgotten > 0)
        {
            _log?.Invoke(
                "Concerned NPC forgot " + forgotten + " body hold(s) belonging to a world that has gone.");
        }

        return world;
    }

    /// <summary>Tells the library the world has gone. Every mode hold and every
    /// body hold ends with it, including Thorstein's.
    ///
    /// Called <b>after</b> the collection runtime has dropped its own world
    /// state, so the job releases its own hold by name first and this only has to
    /// catch what a fault or a crash left behind.</summary>
    internal void NoteWorldUnloaded() => _registry.EndWorldLoad();
}
