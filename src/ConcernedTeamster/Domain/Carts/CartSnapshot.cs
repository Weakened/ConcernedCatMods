namespace TheConcernedCat.ConcernedTeamster.Domain.Carts;

/// <summary>Immutable, game-free observation of one cart at one instant
/// (CT-002). Every value is copied out of the game by the cart adapter; the
/// only derived value is <see cref="TotalMass"/>, computed here with the
/// vanilla formula so panels can explain every number they show. Raw values
/// pass through unchanged — vanilla truth first, even when it is odd — and
/// the semantics of each source member are recorded in CART_INTERNALS.md.</summary>
public sealed class CartSnapshot
{
    private CartSnapshot(
        string cartId,
        float baseMass,
        float cargoWeight,
        bool cargoDataAvailable,
        float itemWeightMassFactor,
        float totalMass,
        bool isAttached,
        bool isPulledByLocalPlayer)
    {
        CartId = cartId;
        BaseMass = baseMass;
        CargoWeight = cargoWeight;
        CargoDataAvailable = cargoDataAvailable;
        ItemWeightMassFactor = itemWeightMassFactor;
        TotalMass = totalMass;
        IsAttached = isAttached;
        IsPulledByLocalPlayer = isPulledByLocalPlayer;
    }

    /// <summary>Network-stable cart identity, "&lt;ownerUserId&gt;:&lt;objectId&gt;".
    /// Never null; an unresolvable id becomes the empty string.</summary>
    public string CartId { get; }

    /// <summary>The empty cart's physics mass before cargo.</summary>
    public float BaseMass { get; }

    /// <summary>Total weight of the cargo container's inventory — the exact
    /// number the game feeds into cart mass. Meaningful only when
    /// <see cref="CargoDataAvailable"/>.</summary>
    public float CargoWeight { get; }

    /// <summary>False when the cart exposed no cargo container (CT-003:
    /// unobtainable is flagged, never silently defaulted). Then
    /// <see cref="CargoWeight"/> is 0 and <see cref="TotalMass"/> equals
    /// <see cref="BaseMass"/> by construction, not by measurement.</summary>
    public bool CargoDataAvailable { get; }

    /// <summary>The game's cargo-weight-to-mass multiplier.</summary>
    public float ItemWeightMassFactor { get; }

    /// <summary>BaseMass + CargoWeight * ItemWeightMassFactor — the vanilla
    /// cart-mass formula recomputed from live values. May be fresher than the
    /// physics engine's own number, which the game refreshes only every five
    /// seconds and only on the owning client (see CART_INTERNALS.md).</summary>
    public float TotalMass { get; }

    /// <summary>Whether anything is attached to the pull handle. Replicated
    /// through the game's network state, so observers see remote pullers.</summary>
    public bool IsAttached { get; }

    /// <summary>Whether the local player holds the pull handle. Pulling is
    /// client-local physics, so this is only ever true on the pulling
    /// client.</summary>
    public bool IsPulledByLocalPlayer { get; }

    public static CartSnapshot Create(
        string? cartId,
        float baseMass,
        float cargoWeight,
        bool cargoDataAvailable,
        float itemWeightMassFactor,
        bool isAttached,
        bool isPulledByLocalPlayer)
    {
        float effectiveCargoWeight = cargoDataAvailable ? cargoWeight : 0f;
        return new CartSnapshot(
            cartId ?? string.Empty,
            baseMass,
            effectiveCargoWeight,
            cargoDataAvailable,
            itemWeightMassFactor,
            baseMass + effectiveCargoWeight * itemWeightMassFactor,
            isAttached,
            isPulledByLocalPlayer);
    }
}
