namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>Which of the two ways an NPC can have a body. Zero is unspecified,
/// so a kind nobody computed is never mistaken for a body that exists.
///
/// There are exactly two, they are built by opposite methods, and one identity
/// never has both at once (<see cref="NpcRoleRegistry.TryClaimBody"/> is where
/// that stops being prose).</summary>
public enum NpcBodyKind
{
    /// <summary>No kind was stated. Never a body, never a grant.</summary>
    Unspecified = 0,

    /// <summary>A local-only figure, assembled dark from visual parts and never
    /// saved. It has no prefab registered with the game and writes nothing into
    /// the world, so it carries no durable facts at all: a presentation contract
    /// with a prefab name or a ZDO key prefix is refused at registration.
    ///
    /// Built by <b>extraction</b>: parts are assembled while inactive, a
    /// forbidden component is a refusal rather than something to strip
    /// afterwards, and only then is the object activated. Never by waking a live
    /// character and removing what should not be there.</summary>
    Presentation = 1,

    /// <summary>A real creature the world saves, built from an inactive clone of
    /// a vanilla prefab and moved only through the vanilla motor. It carries the
    /// two durable facts that make it findable again after a reload - the name
    /// its prefab is registered under, and the prefix of the keys it stores its
    /// identity and inventory beneath - and a worker contract without both is
    /// refused at registration.</summary>
    Worker = 2,
}
