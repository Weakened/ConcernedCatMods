namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>What happened when a role was offered to the registry.
///
/// <b>Zero is not success.</b> A status nobody computed - a defaulted struct, a
/// field never assigned, a value read before the call returned - must never read
/// as a registered role, because a caller that believes it registered goes on to
/// build a body. The repository's own outcome enums all take this shape
/// (<c>WorkAuthorityVerdict</c>, <c>ActorModeOutcome</c>, <c>PickupOutcome</c>);
/// the one designed-but-never-exercised exception, <c>CompanionRegistry</c>'s
/// <c>RegistrationOutcome</c>, put <c>Registered</c> at zero, and this is its
/// first real use, so the defect is fixed here rather than inherited.</summary>
public enum RoleRegistrationStatus
{
    /// <summary>Nobody computed a status. Never a grant.</summary>
    Unspecified = 0,

    /// <summary>The role is registered. Its identity is now held, its mode owner
    /// exists, and it may claim a body of its contracted kind.</summary>
    Registered = 1,

    /// <summary>No role was offered. A null argument is an outcome here rather
    /// than an exception: one role's mistake must not take down a process two
    /// other roles are also running in.</summary>
    NoRole = 2,

    /// <summary>The role declared no identity, or one whose halves are not valid
    /// slugs.</summary>
    InvalidIdentity = 3,

    /// <summary>The body contract contradicts itself: no kind, a worker without
    /// its prefab name or key prefix, or a presentation body carrying durable
    /// facts it can never use.</summary>
    InvalidBodyContract = 4,

    /// <summary>The role supplied no paths, or a root that is not an absolute
    /// path.</summary>
    InvalidDataPaths = 5,

    /// <summary>Another role already holds this identity. Refused rather than
    /// replaced: the second registration would be a second mode owner for one
    /// NPC, and two owners is how one identity ends up with two bodies.</summary>
    DuplicateIdentity = 6,

    /// <summary>Another identity already registered this prefab name. Two roles
    /// on one prefab contaminate each other's census - the prefab is the first
    /// filter and the identity key only the second - and the visible failure is
    /// a second Thorstein standing beside the first with the player's axe inside
    /// it. Sharing a key <i>prefix</i> is fine and two shipped roles already do;
    /// sharing the prefab name is not.</summary>
    DuplicatePrefabName = 7,
}
