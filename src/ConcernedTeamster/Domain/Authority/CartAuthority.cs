namespace TheConcernedCat.ConcernedTeamster.Domain.Authority;

/// <summary>The local client's vanilla relationship to a specific cart
/// (CT-026). Derived only from the game's own network-view ownership surface
/// (the validity and owner checks the adapter reads, verified in
/// CART_INTERNALS.md) — Teamster never takes, requests, or infers authority
/// beyond what the game reports.</summary>
public enum CartAuthority
{
    /// <summary>Authority could not be established: capability off, no valid
    /// network view, or a probe failure. The fail-closed default (value 0) —
    /// any mutating feature is denied here.</summary>
    Unknown = 0,

    /// <summary>The local client owns the cart under vanilla rules right now.
    /// The only state in which a mutating feature may act.</summary>
    Local = 1,

    /// <summary>The cart's network view is valid but owned by another client.
    /// Observation is allowed (and labeled remote); mutation is denied.</summary>
    Remote = 2,
}

/// <summary>Resolves <see cref="CartAuthority"/> from the raw ownership facts
/// the adapter reads. Kept in the pure domain so the mapping is testable
/// without game assemblies; the adapter passes live values, tests pass
/// fakes.</summary>
public static class CartAuthorityResolver
{
    /// <summary>Fail-closed resolution: only a capability-verified, valid,
    /// owned view is <see cref="CartAuthority.Local"/>; a valid unowned view
    /// is <see cref="CartAuthority.Remote"/>; anything unverifiable is
    /// <see cref="CartAuthority.Unknown"/>.</summary>
    public static CartAuthority Resolve(bool capabilityOk, bool viewValid, bool isOwner)
    {
        if (!capabilityOk || !viewValid)
        {
            return CartAuthority.Unknown;
        }

        return isOwner ? CartAuthority.Local : CartAuthority.Remote;
    }
}
