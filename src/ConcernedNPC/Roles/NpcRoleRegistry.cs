using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>The roles registered in one process, and the one door through which
/// any of them may take a body.
///
/// <b>What it guarantees.</b> Three things.
///
/// <i>Roles are isolated.</i> Identities are keyed by product, so two products
/// may ship an NPC with the same slug without colliding, and nothing one product
/// registered is reachable through another product's identity.
///
/// <i>A rejected registration leaves nothing behind.</i> The whole role is
/// validated before anything is written, so a role that is refused is refused
/// entirely - no half-registered identity, no orphaned mode owner, no prefab
/// name reserved by a role that does not exist.
///
/// <i>There is one arbiter.</i> This is the only thing that constructs an
/// <see cref="NpcBodyArbiter"/> and, through it, the only thing that constructs
/// an <c>ActorModeOwner</c>. A role reaches the never-coexist rule through
/// <see cref="TryClaimBody"/> and has nowhere to construct a second arbiter that
/// would not know about the first. That is the difference between the rule being
/// enforced and the rule being agreed to.
///
/// <b>Registration never throws.</b> Four products register into one process at
/// load, and one of them getting it wrong must not take down the other three. A
/// null role, a malformed identity, a contradictory body contract, a duplicate,
/// even a role whose own property getter throws: each is an outcome the caller
/// can log and carry on from. The outcome names the offending fact, because
/// "inconsistent" with no further detail is a debugger session.
///
/// <b>Shaped from <c>CompanionRegistry</c>, with its defects fixed.</b> That
/// type - <c>src/Shared/Companions/Definitions/CompanionRegistry.cs</c> - was
/// designed for exactly this and has never had a caller. This is its first real
/// use, so it is corrected rather than copied: <c>Registered</c> no longer sits
/// at zero where a defaulted value reads as success; a null argument is an
/// outcome rather than an <c>ArgumentNullException</c> thrown out of the middle
/// of a validation routine that promises not to throw; and every refusal carries
/// which definition caused it.
///
/// <b>Threading.</b> Not thread safe. Roles register from their plugin's
/// <c>Awake</c> and claim bodies from the game loop, both on the main
/// thread.</summary>
public sealed class NpcRoleRegistry
{
    private readonly Dictionary<string, INpcRole> _roles = new Dictionary<string, INpcRole>(StringComparer.Ordinal);

    /// <summary>Prefab name to the identity that registered it. Ordinal, not
    /// case-insensitive: the game's prefab lookup is exact, so two names that
    /// differ only in case are two prefabs and treating them as one here would
    /// refuse a legal registration.</summary>
    private readonly Dictionary<string, NpcIdentity> _prefabNames =
        new Dictionary<string, NpcIdentity>(StringComparer.Ordinal);

    private readonly List<NpcIdentity> _identities = new List<NpcIdentity>();

    /// <summary>A view over <c>_identities</c>, not the list itself. Handing the
    /// list out as <c>IReadOnlyList</c> lets any caller cast it back and clear
    /// it - on a public surface, on a process-wide singleton, in a library four
    /// products load - leaving the registry's three collections permanently
    /// disagreeing. A doc comment asking callers not to is a request; this is
    /// the guarantee.</summary>
    private readonly ReadOnlyCollection<NpcIdentity> _identitiesView;

    private readonly NpcBodyArbiter _arbiter = new NpcBodyArbiter();

    /// <summary>Only this class creates one. A role gets <see cref="Shared"/>;
    /// a test gets its own through this constructor, which is why it is not
    /// public - a second registry in a running game would be a second arbiter,
    /// and the whole point is that there is one.</summary>
    internal NpcRoleRegistry()
    {
        _identitiesView = new ReadOnlyCollection<NpcIdentity>(_identities);
    }

    /// <summary>The process-wide registry. Every role in every product uses this
    /// one, which is what makes "one mode owner per identity" a fact about the
    /// process rather than about a single mod.</summary>
    public static NpcRoleRegistry Shared { get; } = new NpcRoleRegistry();

    /// <summary>How many roles are registered.</summary>
    public int RoleCount => _roles.Count;

    /// <summary>Every registered identity, in registration order. A read-only
    /// view that cannot be cast back to the registry's own list.</summary>
    public IReadOnlyList<NpcIdentity> Identities => _identitiesView;

    /// <summary>Which world load this process is in, or
    /// <see cref="NpcWorldEpoch.Unknown"/> between worlds.
    ///
    /// <b>The one process-wide fact with one owner.</b> An epoch is what decides
    /// whether an in-session name still means anything, and before this existed
    /// every role minted its own and handed it in - so two roles in one process
    /// could hold two different values for the same world load, and the moment
    /// anything was shared between them (a reservation over a container they
    /// both want, a census, a journal row) the two would never match. Every seam
    /// in this package that takes an <see cref="NpcWorldEpoch"/> takes
    /// <b>this</b> one.</summary>
    public NpcWorldEpoch CurrentWorld { get; private set; }

    /// <summary>Says a world has been loaded, and returns how many bodies were
    /// forgotten because they belonged to the previous one.
    ///
    /// Called once per world load, by whichever role's runtime notices first.
    /// Calling it again with the same epoch does nothing, so several roles
    /// racing to be first is fine and none of them needs to know whether
    /// another got there already. Registration is untouched - a role registers
    /// once per process - but every body hold and every job from the previous
    /// world ends here, because both belonged to that world's scene and neither
    /// survived it.</summary>
    public int BeginWorldLoad(NpcWorldEpoch world)
    {
        if (world.IsUnknown)
        {
            // Not a world. Treated as the unload it actually is, rather than
            // adopted as an epoch that would then match nothing.
            return EndWorldLoad();
        }

        if (CurrentWorld.Matches(world))
        {
            return 0;
        }

        int forgotten = _arbiter.ForgetWorld();
        CurrentWorld = world;
        return forgotten;
    }

    /// <summary>Says the world has gone: back to the main menu, a disconnect, a
    /// switch to another world. Every body hold and every job ends, and
    /// <see cref="CurrentWorld"/> becomes unknown, so nothing may claim a body
    /// until a world is loaded again.</summary>
    public int EndWorldLoad()
    {
        int forgotten = _arbiter.ForgetWorld();
        CurrentWorld = NpcWorldEpoch.Unknown;
        return forgotten;
    }

    /// <summary>Offers a role. Validates all of it, then registers all of it, or
    /// registers none of it and says which fact was wrong.</summary>
    public RoleRegistration Register(INpcRole? role)
    {
        if (role == null)
        {
            return new RoleRegistration(RoleRegistrationStatus.NoRole, default,
                "no role was offered");
        }

        NpcIdentity identity;
        NpcBodyContract body;
        INpcDataPaths? paths;
        try
        {
            identity = role.Identity;
            body = role.Body;
            paths = role.Paths;
        }
        catch (Exception exception)
        {
            // A role whose own property getter throws is a broken role, not a
            // broken process. Three other products are registering into this
            // same load.
            return new RoleRegistration(RoleRegistrationStatus.NoRole, default,
                "reading the role threw " + exception.GetType().Name + ": " + exception.Message);
        }

        if (identity.IsEmpty)
        {
            return new RoleRegistration(RoleRegistrationStatus.InvalidIdentity, identity,
                "a role must declare an identity of the form product/role");
        }

        if (!body.Validate(out string bodyReason))
        {
            return new RoleRegistration(RoleRegistrationStatus.InvalidBodyContract, identity, bodyReason);
        }

        if (!ValidatePaths(paths, out string pathsReason))
        {
            return new RoleRegistration(RoleRegistrationStatus.InvalidDataPaths, identity, pathsReason);
        }

        if (_roles.ContainsKey(identity.Value))
        {
            return new RoleRegistration(RoleRegistrationStatus.DuplicateIdentity, identity,
                "this identity is already registered");
        }

        if (body.PrefabName.Length != 0 &&
            _prefabNames.TryGetValue(body.PrefabName, out NpcIdentity owner))
        {
            return new RoleRegistration(RoleRegistrationStatus.DuplicatePrefabName, identity,
                "'" + body.PrefabName + "' is already registered by " + owner.Value +
                "; two roles sharing a prefab find each other's bodies");
        }

        // Nothing above this line has written anything, and the arbiter is the
        // FIRST write rather than the last: it is the only one of the four that
        // can refuse, so doing it here means its refusal cannot leave three
        // collections disagreeing. Both its failure conditions - an empty
        // identity and a duplicate - are refused above, which makes this
        // unreachable today and is exactly why the result is checked rather
        // than discarded: it stops being unreachable the moment somebody
        // reorders the guards.
        if (!_arbiter.Track(identity, body.Kind))
        {
            return new RoleRegistration(RoleRegistrationStatus.DuplicateIdentity, identity,
                "the arbiter is already tracking this identity");
        }

        _roles.Add(identity.Value, role);
        _identities.Add(identity);
        if (body.PrefabName.Length != 0)
        {
            _prefabNames.Add(body.PrefabName, identity);
        }

        return new RoleRegistration(RoleRegistrationStatus.Registered, identity, string.Empty);
    }

    /// <summary>Looks a role up by its identity. Another product's identity
    /// never finds it: that is the isolation guarantee in its smallest
    /// form.</summary>
    public bool TryGetRole(NpcIdentity identity, out INpcRole role)
    {
        if (identity.IsEmpty)
        {
            role = null!;
            return false;
        }

        return _roles.TryGetValue(identity.Value, out role!);
    }

    /// <summary>Which kind of body exists for this identity <b>in the current
    /// world</b> right now, or <see cref="NpcBodyKind.Unspecified"/> for none -
    /// including for a hold taken in a world that has since been unloaded, and
    /// for no world at all.</summary>
    public NpcBodyKind CurrentBodyKind(NpcIdentity identity) =>
        _arbiter.CurrentBodyKind(identity, CurrentWorld);

    /// <summary>May <paramref name="holder"/> construct a body of
    /// <paramref name="kind"/> for <paramref name="identity"/> right now? The
    /// only route to the arbiter, and therefore the only route to a body.
    ///
    /// <paramref name="holder"/> is whoever will be responsible for releasing
    /// it: a job id for a worker body, the runtime that shows the figure for a
    /// presentation one. It is required for both, so that every body has
    /// somebody who can be asked to give it up.
    ///
    /// The claim is stamped with <see cref="CurrentWorld"/> and is refused
    /// outright when no world is loaded, because a body exists in a world. A
    /// grant carries a <see cref="BodyLease"/>, which the body factory requires
    /// as a parameter - so a role that was refused has nothing to build with,
    /// rather than a return value it is asked to respect.</summary>
    public BodyClaim TryClaimBody(NpcIdentity identity, NpcBodyKind kind, string holder) =>
        _arbiter.TryClaim(identity, kind, holder, CurrentWorld);

    /// <summary>Gives a body up. Releasing one this holder does not have says so
    /// and changes nothing, so cleanup after a reload, a death or a failure can
    /// be unconditional.</summary>
    public BodyClaim ReleaseBody(NpcIdentity identity, NpcBodyKind kind, string holder) =>
        _arbiter.Release(identity, kind, holder, CurrentWorld);

    /// <summary>The identity's single mode owner. Internal: what a job may do
    /// with an identity's mode is the job pipeline's question, and widening this
    /// library's public surface is a breaking change for every installed
    /// consumer, so it waits for the leaf that needs it.</summary>
    internal Bodies.ActorModeOwner? ModeOf(NpcIdentity identity) => _arbiter.ModeOf(identity);

    private static bool ValidatePaths(INpcDataPaths? paths, out string reason)
    {
        if (paths == null)
        {
            reason = "a role must say where its data lives, even if it stores nothing";
            return false;
        }

        string root;
        try
        {
            root = paths.Root;
        }
        catch (Exception exception)
        {
            reason = "reading the role's data root threw " + exception.GetType().Name + ": " + exception.Message;
            return false;
        }

        if (string.IsNullOrEmpty(root))
        {
            reason = "a role's data root must be an absolute path; this library never composes one";
            return false;
        }

        try
        {
            if (!Path.IsPathRooted(root))
            {
                reason = "a role's data root must be absolute, not '" + root + "'";
                return false;
            }
        }
        catch (ArgumentException exception)
        {
            // .NET Framework's Path.IsPathRooted throws on invalid path
            // characters; .NET Core's does not. The shipping compile is net48
            // and the test run is net10, so this branch is real in a game
            // session and UNREACHABLE in the suite. It stays, because a role
            // passing a path with a '|' in it must get an outcome rather than
            // an exception out of a method that promises not to throw - and it
            // is called out here because a branch no test can reach is one a
            // reader should be told about rather than left to assume is
            // covered.
            reason = "a role's data root is not a usable path: " + exception.Message;
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
