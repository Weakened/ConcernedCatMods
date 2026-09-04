namespace TheConcernedCat.ConcernedTeamster.Domain.Terrain;

/// <summary>Ground surface under the cart (CT-004), mirroring the terrain
/// paint kinds the game encodes in its paint mask channels (verified:
/// dirt = red, cultivated = green, paved = blue; see CART_INTERNALS.md).
/// <see cref="Unavailable"/> means the ground could not be verified — never
/// guessed.</summary>
public enum TerrainSurfaceKind
{
    Unavailable,
    Untouched,
    Dirt,
    Cultivated,
    Paved,
}
