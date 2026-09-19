namespace TheConcernedCat.ConcernedNPC.Bodies;

/// <summary>What happened when a role asked to construct or give up a body.
/// Zero is unspecified, so a claim nobody made is never mistaken for one that
/// was granted.</summary>
public enum BodyClaimStatus
{
    /// <summary>Nobody asked. Never a grant.</summary>
    Unspecified = 0,

    /// <summary>The claim is granted. This holder may now construct the body,
    /// and must release it through the same holder id.</summary>
    Claimed = 1,

    /// <summary>This holder already has this body. Idempotent, so a runtime that
    /// re-asks on every tick or after a reload is not punished for it.</summary>
    AlreadyHeld = 2,

    /// <summary><b>The never-coexist rule, refusing.</b> A body of the other
    /// kind exists for this identity. One logical NPC is one world entity: a
    /// presentation figure and a saved worker body standing in the same world
    /// are two of him, and whichever one the player then talks to, feeds or
    /// takes tools from, the other is wrong. The holder of the existing body
    /// releases it first; nothing here tears one down on somebody's behalf.
    /// </summary>
    RefusedOtherKindExists = 3,

    /// <summary>Another holder has this identity - either it already holds a
    /// body of the same kind, or a job holds the identity's mode. Two holders of
    /// one identity is how one NPC ends up with two bodies, so the second asker
    /// is refused rather than joined.</summary>
    RefusedHolderConflict = 4,

    /// <summary>This identity is not registered. A body may only be built for a
    /// role that declared its durable facts, because those facts are what find
    /// the body again after a reload.</summary>
    RefusedNotRegistered = 5,

    /// <summary>This role's contract does not describe a body of that kind, or
    /// no kind was stated. A role that contracted for a presentation figure has
    /// no prefab name to build a saved body under, and building one anyway is
    /// how a body gets registered under a name nothing will find again.</summary>
    RefusedKindNotContracted = 6,

    /// <summary>No holder id was given. An unowned body is one nobody can be
    /// required to release.</summary>
    RefusedNoHolder = 7,

    /// <summary>The body is given up and the identity is free. If the holder
    /// also held the identity's mode, that is released with it.</summary>
    Released = 8,

    /// <summary>Nothing to release: no body of that kind, or it belongs to a
    /// different holder. Not an error - a runtime tidying up after a reload asks
    /// about bodies it may not have - and never a reason to clear somebody
    /// else's claim.</summary>
    NotHeld = 9,
}
