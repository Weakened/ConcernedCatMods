namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What kind of thing Gunnar may collect (#381). Zero is unspecified
/// and is never collectable, so a fact nobody filled in refuses.
///
/// <b>Why felling is not in this list.</b> #381 says eligible targets <i>may</i>
/// include permitted saplings and small trees, with real felling and real
/// vanilla drops. Felling one means calling the tree's own damage entry point,
/// which sends a network RPC, and Teamster's shipped worker-runtime scope audit
/// bans an RPC send from Gunnar's runtime outright
/// (<c>tools/validate_repo.py</c>, <c>TEAMSTER_WORKER_FORBIDDEN_TOKENS</c>).
/// Widening that audit moves a shipped safety property, which is a decision with
/// an owner, not an implementation detail - so this slice carries the two target
/// kinds that need no damage and no ownership claim, and the felling decision is
/// written up rather than taken quietly. See <c>GUNNAR_COLLECTION.md</c>
/// §"What this slice deliberately does not do".</summary>
internal enum CollectableKind
{
    /// <summary>Nobody said. Never collectable.</summary>
    Unspecified = 0,

    /// <summary>A natural loose stone lying on the ground.</summary>
    LooseStone = 1,

    /// <summary>A natural fallen branch lying on the ground.</summary>
    Branch = 2,

    /// <summary>Something the player marked for collection explicitly. The mark
    /// is the authority; it still has to pass every other clause, because a mark
    /// says "I want this" and not "ignore the rules".</summary>
    MarkedResource = 3,
}
