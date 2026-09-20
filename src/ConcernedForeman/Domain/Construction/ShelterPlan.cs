using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>One placement with the real cost of the piece that goes there.
/// </summary>
internal readonly struct CostedPiece
{
    internal CostedPiece(PiecePlacement placement, PieceRecipe recipe)
    {
        Placement = placement;
        Recipe = recipe;
    }

    internal PiecePlacement Placement { get; }

    /// <summary>What the game says this piece costs. Always
    /// <see cref="PieceRecipe.IsKnown"/> inside a plan - a plan is only made
    /// when every recipe was read.</summary>
    internal PieceRecipe Recipe { get; }

    internal BuildPhase Phase => Placement.Piece.Phase;

    internal string Key => Placement.Key;

    public override string ToString() => Placement + " for " + MaterialTally.Of(Recipe.Costs);
}

/// <summary>A confirmed build order turned into seventeen real placements with
/// seventeen real costs, or a refusal saying which piece stopped it.
///
/// <b>Everything is resolved up front, before any material moves.</b> #380 asks
/// for the manifest to be calculated before execution, and that is not only a
/// planning convenience: a build order that discovers on its eleventh piece that
/// <c>wood_door</c> does not resolve has already taken a player's wood out of a
/// chest for a cottage that cannot be finished. So the whole blueprint is priced
/// in one pass and an order that cannot be priced is never accepted.
///
/// <b>No cost is ever invented.</b> Every number here came out of
/// <see cref="IPieceRecipes"/>, which in the game reads <c>Piece.m_resources</c>.
/// There is no table of costs in this product and there must never be one: a
/// hard-coded cost is a number that is right until the next game patch and wrong
/// silently afterwards.</summary>
internal readonly struct ShelterPlan
{
    private readonly CostedPiece[]? _pieces;

    private ShelterPlan(
        BuildOrderMarker marker, CostedPiece[]? pieces, MaterialTally? total, string refusal)
    {
        Marker = marker;
        _pieces = pieces;
        Total = total ?? new MaterialTally();
        Refusal = refusal ?? string.Empty;
    }

    /// <summary>The order this is a plan for.</summary>
    internal BuildOrderMarker Marker { get; }

    /// <summary>Every piece, in build order.</summary>
    internal IReadOnlyList<CostedPiece> Pieces => _pieces ?? Array.Empty<CostedPiece>();

    /// <summary>What the whole shelter costs.</summary>
    internal MaterialTally Total { get; }

    /// <summary>Why there is no plan. Empty when there is one.</summary>
    internal string Refusal { get; }

    /// <summary>Whether this is a plan at all.</summary>
    internal bool IsPlanned => string.IsNullOrEmpty(Refusal) && Pieces.Count > 0;

    /// <summary>Prices one confirmed order.</summary>
    /// <param name="marker">What the player confirmed. An unconfirmed marker is
    /// refused: #280's authority is the confirmation, not the position.</param>
    /// <param name="recipes">Where the real costs come from.</param>
    internal static ShelterPlan For(BuildOrderMarker marker, IPieceRecipes? recipes)
    {
        if (marker.Kind != BuildOrderKind.Shelter)
        {
            return Refused(marker, "there is no build order to plan");
        }

        if (!marker.IsConfirmed)
        {
            return Refused(marker, "the build order has not been confirmed, so nothing may be placed");
        }

        if (recipes == null)
        {
            return Refused(marker, "there is no way to read what the pieces cost");
        }

        IReadOnlyList<PiecePlacement> placements = ShelterBlueprint.PlaceAt(marker);
        if (placements.Count == 0)
        {
            return Refused(marker, "the build order produced no pieces");
        }

        // One read per distinct prefab rather than one per piece: seventeen
        // pieces are four prefabs, and the game-side reader walks a prefab table.
        var read = new Dictionary<string, PieceRecipe>(StringComparer.Ordinal);
        var costed = new CostedPiece[placements.Count];
        var total = new MaterialTally();

        for (int index = 0; index < placements.Count; index++)
        {
            PiecePlacement placement = placements[index];
            string prefab = placement.Piece.Prefab;
            if (!read.TryGetValue(prefab, out PieceRecipe recipe))
            {
                recipe = recipes.Read(prefab);
                read[prefab] = recipe;
            }

            if (!recipe.IsKnown)
            {
                // Named, because "a piece could not be read" sends a player
                // looking through seventeen of them.
                return Refused(
                    marker,
                    "the game would not say what " + prefab + " costs (" + recipe.Refusal +
                    "), so the order cannot be priced and nothing will be taken out of a chest for it");
            }

            costed[index] = new CostedPiece(placement, recipe);
            total.Add(recipe);
        }

        return new ShelterPlan(marker, costed, total, refusal: string.Empty);
    }

    /// <summary>What one phase of the shelter costs.</summary>
    internal MaterialTally TotalFor(BuildPhase phase)
    {
        var tally = new MaterialTally();
        foreach (CostedPiece piece in Pieces)
        {
            if (piece.Phase == phase)
            {
                tally.Add(piece.Recipe);
            }
        }

        return tally;
    }

    /// <summary>The piece with this key, if the plan has one.</summary>
    internal bool TryFind(string? key, out CostedPiece piece)
    {
        foreach (CostedPiece candidate in Pieces)
        {
            if (string.Equals(candidate.Key, key, StringComparison.Ordinal))
            {
                piece = candidate;
                return true;
            }
        }

        piece = default;
        return false;
    }

    private static ShelterPlan Refused(BuildOrderMarker marker, string why) =>
        new ShelterPlan(marker, pieces: null, total: null, refusal: why);

    public override string ToString() => IsPlanned
        ? Marker + ": " + Pieces.Count + " pieces for " + Total.Describe()
        : "no plan (" + Refusal + ")";
}
