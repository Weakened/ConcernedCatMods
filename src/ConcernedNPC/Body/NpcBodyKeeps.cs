namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>What a role's body stores in its own network object.
///
/// <b>Why this is a choice and not a capability everybody gets.</b> Two of the
/// three shipped worker bodies persist an inventory and one - the one that
/// pulls a cart and carries nothing - stores only its identity. Switching that
/// third one on as a side effect of unifying the code would give an existing
/// player a Gunnar who keeps items across a relog, which is a gameplay change
/// riding inside a refactor, and the architecture lists exactly that class of
/// thing as needing its own issue and its own proof in game. So the answer is
/// the role's, stated at registration, with no default: a role that has not
/// decided has not registered.
///
/// It is also not reversible for free in the other direction. Turning it off
/// for a role that had it leaves a stored inventory nothing reads, and the
/// items in it are gone as far as the player is concerned.</summary>
public enum NpcBodyKeeps
{
    /// <summary>Not stated. Refused at registration; never a default.</summary>
    Unspecified = 0,

    /// <summary>Its identity and nothing else. The body carries items in the
    /// session and loses them on a reload, exactly as a vanilla non-player
    /// humanoid does.</summary>
    IdentityOnly = 1,

    /// <summary>Its identity, everything it carries, and a revision counting
    /// the writes. What the two persisting products do today.</summary>
    IdentityAndInventory = 2,
}
