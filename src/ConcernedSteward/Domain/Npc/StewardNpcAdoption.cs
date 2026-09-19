using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedSteward.Domain.Npc;

/// <summary>How the Steward holds his own identity, now that Concerned NPC is
/// the thing that answers who may have a body.
///
/// <b>What this replaces, and why the replacement is stronger.</b> Until the
/// adoption the Steward built his own <c>ActorModeOwner</c> out of shared source
/// compiled into this assembly. That owner was correct and it was also
/// unreachable: Concerned Foreman and Concerned Teamster each compiled their own
/// copy of the same file, so "one mode owner per identity" was true three times
/// over, once per assembly, and nothing could see across. The validator forbids
/// a consumer building one for exactly that reason
/// (<c>check_library_consumers_do_not_bypass_the_arbiter</c>). The arbiter
/// inside the library is one object for the whole process, so a refusal here is
/// a refusal every product can see.
///
/// <b>Why the body claim and not a mode verb.</b> The lead's answer to that
/// question, recorded because the alternative will look tempting to the next
/// reader: <c>TryClaimBody</c> and <c>ReleaseBody</c> are already public, they
/// already refuse a second holder, and they are already what the arbiter exists
/// to be. A second public way to say "this identity is busy" would be a second
/// vocabulary for one fact.
///
/// <b>A claim belongs to one world load.</b> <see cref="NoteWorldLoaded"/> mints
/// the epoch every claim is stamped with and <see cref="NoteWorldUnloaded"/>
/// ends it. That is not bookkeeping: a body is destroyed with its world's scene,
/// and a hold that outlived the world would either report a destroyed body as
/// standing or leave the identity busy forever with no holder string left in
/// existence to release it.
///
/// <b>Everything here is total.</b> Registration is offered once and its outcome
/// is reported rather than thrown, because four products register into one
/// process at load and one of them failing must not take the others down. Every
/// other call is idempotent, so a runtime tidying up after a reload, a death or
/// a failure can call them unconditionally.</summary>
internal sealed class StewardNpcAdoption
{
    private readonly NpcRoleRegistry _registry;
    private readonly Action<string>? _log;

    private bool _offered;
    private bool _registered;
    private bool _holdsBody;

    /// <param name="registry">The process-wide registry in a game, and a
    /// private one in a test. Passed in rather than read from the static so
    /// that a test is not one global away from the next test.</param>
    /// <param name="role">The Steward's declaration of his own durable facts.
    /// </param>
    /// <param name="log">Where a refusal is reported. A refusal is always
    /// reported: a Steward who quietly declines to exist is the hardest kind of
    /// bug to be told about.</param>
    internal StewardNpcAdoption(NpcRoleRegistry registry, StewardNpcRole role, Action<string>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Role = role ?? throw new ArgumentNullException(nameof(role));
        _log = log;
    }

    internal StewardNpcRole Role { get; }

    internal NpcIdentity Identity => StewardNpcRole.Id;

    /// <summary>True once the library has accepted the Steward's declaration.
    /// Nothing else here does anything while it is false, because a claim for an
    /// unregistered identity is refused and would be refused every tick.
    /// </summary>
    internal bool IsRegistered => _registered;

    /// <summary>True while this runtime holds the Steward's one body in the
    /// current world.</summary>
    internal bool HoldsBody => _holdsBody;

    /// <summary>The world load the library is currently in, or unknown.</summary>
    internal NpcWorldEpoch World => _registry.CurrentWorld;

    /// <summary>Offers the Steward to the library. Called once, from plugin
    /// start, and safe to call again.
    ///
    /// <b>Registration is per process, not per world.</b> The library's own
    /// contract says so, and it matters: a role re-registered on every world
    /// load would be refused as a duplicate from the second load onwards and the
    /// log would fill with it.</summary>
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
                "The Steward could not register with Concerned NPC (" + outcome.Status +
                ": " + outcome.Reason + "). She will not take a body this session.");
        }

        return outcome;
    }

    /// <summary>Tells the library a world has been loaded and takes the epoch it
    /// mints. Idempotent while the same world is up.</summary>
    internal NpcWorldEpoch NoteWorldLoaded()
    {
        NpcWorldEpoch world = _registry.BeginWorldLoad(out int forgotten);
        if (forgotten > 0)
        {
            _log?.Invoke(
                "Concerned NPC forgot " + forgotten + " body hold(s) belonging to a world that " +
                "has gone.");
        }

        return world;
    }

    /// <summary>Tells the library the world has gone. Every hold ends with it,
    /// including this one.</summary>
    internal void NoteWorldUnloaded()
    {
        // Released explicitly first so that this runtime's own flag and the
        // arbiter's record agree even if some other role's runtime ended the
        // world before we noticed. ForgetWorld would have cleared the hold
        // anyway; what it would not have done is tell us.
        ReleaseBody();
        _registry.EndWorldLoad();
    }

    /// <summary>Takes the Steward's one body for this world, or says why not.
    ///
    /// Asked when a live Steward body binds and safe to re-ask every tick:
    /// re-asking as the same holder is <c>AlreadyHeld</c>, which is a grant.
    /// </summary>
    internal BodyClaim ClaimBody()
    {
        if (!_registered)
        {
            return new BodyClaim();
        }

        BodyClaim claim = _registry.TryClaimBody(
            Identity, NpcBodyKind.Worker, StewardRole.UpkeepJobId);
        bool held = claim.IsGranted;

        if (!held && _holdsBody)
        {
            // The arbiter no longer agrees that we hold it. Believing our own
            // flag over the arbiter is how two runtimes end up each certain they
            // have the body.
            _log?.Invoke(
                "The Steward's body is no longer this runtime's to hold (" + claim.Status +
                ": " + claim.Reason + ").");
        }

        if (!held && claim.Status != BodyClaimStatus.Unspecified && !_holdsBody)
        {
            _log?.Invoke(
                "The Steward may not take a body right now (" + claim.Status + ": " +
                claim.Reason + ").");
        }

        _holdsBody = held;
        return claim;
    }

    /// <summary>Gives the body back. Releasing one we do not hold changes
    /// nothing and is not an error, which is what lets every cleanup path call
    /// it without checking first.</summary>
    internal BodyClaim ReleaseBody()
    {
        _holdsBody = false;
        if (!_registered)
        {
            return new BodyClaim();
        }

        return _registry.ReleaseBody(Identity, NpcBodyKind.Worker, StewardRole.UpkeepJobId);
    }
}
