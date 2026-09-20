using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>One line of a vanilla piece's real build cost: an item and how many
/// of it.
///
/// <b>The item is a prefab name, deliberately.</b> Foreman's custody ledger
/// already identifies material by the item's prefab name - <c>MaterialItem</c>
/// carries <c>PrefabName</c>, and <c>EngineInventoryPorts</c> matches a stack by
/// <c>m_dropPrefab.name</c>. Using the same token here is what makes "the
/// shelter needs forty Wood" and "that chest holds forty Wood" the same string,
/// with no translation table in between. A display name would be a second
/// vocabulary that agrees with the first until somebody plays in German.
/// </summary>
internal readonly struct PieceCost : IEquatable<PieceCost>
{
    internal PieceCost(string item, int amount)
    {
        Item = item ?? string.Empty;
        Amount = amount;
    }

    /// <summary>The item's prefab name, as custody spells it.</summary>
    internal string Item { get; }

    /// <summary>How many. A line that is not a positive count is not a cost, and
    /// <see cref="PieceRecipe"/> refuses one rather than rounding it up.
    /// </summary>
    internal int Amount { get; }

    internal bool IsValid => !string.IsNullOrEmpty(Item) && Amount > 0;

    public bool Equals(PieceCost other) =>
        string.Equals(Item, other.Item, StringComparison.Ordinal) && Amount == other.Amount;

    public override bool Equals(object? obj) => obj is PieceCost other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(Item) * 397) ^ Amount);

    public override string ToString() =>
        Amount.ToString(CultureInfo.InvariantCulture) + " " + Item;
}

/// <summary>What one vanilla piece really costs, as the game says.
///
/// <b>There is no default and no fallback.</b> A recipe that could not be read
/// is <see cref="IsKnown"/> false and carries the reason, and every caller turns
/// that into a refusal. The alternative - a guessed cost, or zero, or the last
/// recipe that worked - produces an NPC that takes the wrong amount of a
/// player's material out of a chest and calls it correct, which is the one
/// failure that cannot be undone by cancelling the order.</summary>
internal readonly struct PieceRecipe
{
    private readonly PieceCost[]? _costs;

    private PieceRecipe(string prefab, PieceCost[]? costs, bool known, string refusal)
    {
        Prefab = prefab ?? string.Empty;
        _costs = costs;
        IsKnown = known;
        Refusal = refusal ?? string.Empty;
    }

    /// <summary>The piece this is the cost of.</summary>
    internal string Prefab { get; }

    /// <summary>Whether the game actually answered.</summary>
    internal bool IsKnown { get; }

    /// <summary>Why not, when it did not. Empty when it did.</summary>
    internal string Refusal { get; }

    /// <summary>The lines, in the order the game lists them.</summary>
    internal IReadOnlyList<PieceCost> Costs => _costs ?? Array.Empty<PieceCost>();

    /// <summary>A piece whose cost the game gave us.</summary>
    /// <remarks>A piece that genuinely costs nothing is legal and answers
    /// <see cref="IsKnown"/> true with no lines. That is not the same as a piece
    /// nobody could read, and folding the two together is how a missing prefab
    /// becomes a free cottage.</remarks>
    internal static PieceRecipe Known(string prefab, IReadOnlyList<PieceCost>? costs)
    {
        if (string.IsNullOrEmpty(prefab))
        {
            return Unknown(string.Empty, "a piece with no name has no recipe");
        }

        var kept = new List<PieceCost>();
        if (costs != null)
        {
            foreach (PieceCost cost in costs)
            {
                if (!cost.IsValid)
                {
                    return Unknown(
                        prefab,
                        "its recipe lists " + cost + ", which is not an amount of anything");
                }

                kept.Add(cost);
            }
        }

        return new PieceRecipe(prefab, kept.ToArray(), known: true, refusal: string.Empty);
    }

    /// <summary>A piece whose cost could not be read, and why.</summary>
    internal static PieceRecipe Unknown(string prefab, string refusal) => new PieceRecipe(
        prefab ?? string.Empty,
        costs: null,
        known: false,
        refusal: string.IsNullOrEmpty(refusal) ? "the game did not answer" : refusal);

    public override string ToString() => IsKnown
        ? Prefab + ": " + MaterialTally.Of(Costs)
        : Prefab + ": unknown (" + Refusal + ")";
}

/// <summary>Where a piece's real cost comes from.
///
/// <b>One method, and it can say no.</b> The implementation that matters reads
/// <c>Piece.m_resources</c> off the vanilla prefab, which is the only honest
/// source of a build cost in this game; the implementation in the tests hands
/// back whatever the test wants to prove. Both may refuse, and a refusal is an
/// answer rather than an exception, because this is asked while an order is
/// being accepted and a throw there is an order that vanishes with no
/// sentence.</summary>
internal interface IPieceRecipes
{
    /// <summary>What this piece costs, or why that could not be established.
    /// </summary>
    PieceRecipe Read(string prefab);
}

/// <summary>A bag of (item, count), totalled.
///
/// <b>Why this exists when the shared runtime has a manifest of its own.</b>
/// This is the arithmetic that turns seventeen real recipes into one list, and
/// it is the role's own work - what a shelter is made of is not something a
/// general NPC runtime may know. What crosses into that runtime is the total,
/// converted once at the seam. This type deliberately does no planning: it adds
/// up, it subtracts, and it can say what is missing.</summary>
internal sealed class MaterialTally
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

    private readonly List<string> _order = new List<string>();

    /// <summary>An empty tally.</summary>
    internal MaterialTally()
    {
    }

    /// <summary>How many distinct items are in it.</summary>
    internal int Kinds => _order.Count;

    /// <summary>Every unit of every kind added together.</summary>
    internal int TotalUnits
    {
        get
        {
            int total = 0;
            foreach (string item in _order)
            {
                total += _counts[item];
            }

            return total;
        }
    }

    /// <summary>Nothing is wanted.</summary>
    internal bool IsEmpty => _order.Count == 0;

    /// <summary>The items, in the order they were first added. Stable, because
    /// a missing-material list a player reads twice must read the same way
    /// twice.</summary>
    internal IReadOnlyList<string> Items => _order;

    /// <summary>The lines, in that same order.</summary>
    internal IReadOnlyList<PieceCost> Lines
    {
        get
        {
            var lines = new List<PieceCost>(_order.Count);
            foreach (string item in _order)
            {
                lines.Add(new PieceCost(item, _counts[item]));
            }

            return lines;
        }
    }

    /// <summary>How many of one item.</summary>
    internal int UnitsOf(string? item) =>
        item != null && _counts.TryGetValue(item, out int count) ? count : 0;

    /// <summary>Adds a cost line. A non-positive amount is ignored rather than
    /// subtracted: this is a requirement, and a negative requirement is a
    /// defect upstream, not a refund.</summary>
    internal MaterialTally Add(string? item, int amount)
    {
        if (string.IsNullOrEmpty(item) || amount <= 0)
        {
            return this;
        }

        if (_counts.TryGetValue(item!, out int already))
        {
            _counts[item!] = already + amount;
        }
        else
        {
            _counts[item!] = amount;
            _order.Add(item!);
        }

        return this;
    }

    /// <summary>Adds every line of a recipe.</summary>
    internal MaterialTally Add(PieceRecipe recipe)
    {
        foreach (PieceCost cost in recipe.Costs)
        {
            Add(cost.Item, cost.Amount);
        }

        return this;
    }

    /// <summary>Adds another tally.</summary>
    internal MaterialTally Add(MaterialTally? other)
    {
        if (other != null)
        {
            foreach (string item in other._order)
            {
                Add(item, other._counts[item]);
            }
        }

        return this;
    }

    /// <summary>What this tally asks for that <paramref name="available"/> does
    /// not cover, item by item. Empty when everything is covered.
    ///
    /// <b>Per item, never in units.</b> Forty of one thing and forty of another
    /// are the same number and not the same shortfall, and a player told "you
    /// are eight short" without being told of what has been told
    /// nothing.</summary>
    internal MaterialTally Missing(MaterialTally? available)
    {
        var missing = new MaterialTally();
        foreach (string item in _order)
        {
            int owed = _counts[item] - (available?.UnitsOf(item) ?? 0);
            if (owed > 0)
            {
                missing.Add(item, owed);
            }
        }

        return missing;
    }

    /// <summary>A copy, so a caller cannot edit somebody else's total.</summary>
    internal MaterialTally Copy() => new MaterialTally().Add(this);

    /// <summary>A player-readable list: "40 Wood, 8 Stone". Empty tallies read
    /// as "nothing".</summary>
    internal string Describe() => Of(Lines);

    internal static string Of(IReadOnlyList<PieceCost> lines)
    {
        if (lines.Count == 0)
        {
            return "nothing";
        }

        var text = new System.Text.StringBuilder();
        for (int index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                text.Append(index == lines.Count - 1 ? " and " : ", ");
            }

            text.Append(lines[index].ToString());
        }

        return text.ToString();
    }

    public override string ToString() => Describe();
}
