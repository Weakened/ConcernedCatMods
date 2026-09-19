using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>The stand-ins the construction tests use for the three things only
/// the game can answer: what a piece costs, what is standing where, and whether
/// a placement is allowed.
///
/// Each is deliberately dumb. The point of the seams is that every decision is
/// in the domain, so a fake that made decisions would be proving itself.
/// </summary>
internal static class ConstructionRig
{
    /// <summary>A marker a player confirmed, at the origin, facing north.
    /// </summary>
    internal static BuildOrderMarker Confirmed(float x = 0f, float y = 0f, float z = 0f, float yaw = 0f) =>
        BuildOrderMarker.Proposed(BuildOrderKind.Shelter, new SitePoint(x, y, z), yaw).Confirm();

    /// <summary>The four real prefab names the blueprint uses, each with a cost
    /// that is a stand-in for the game's own. <b>These numbers are not vanilla's
    /// and are not claimed to be</b> - the product never carries a cost table,
    /// and the whole point of <see cref="IPieceRecipes"/> is that the real ones
    /// come out of <c>Piece.m_resources</c> at run time. They exist so the
    /// arithmetic can be proved.</summary>
    internal static FakeRecipes Recipes()
    {
        var recipes = new FakeRecipes();
        recipes.Set("wood_floor", ("Wood", 4));
        recipes.Set("wood_wall", ("Wood", 2));
        recipes.Set("wood_door", ("Wood", 10));
        recipes.Set("bed", ("Wood", 8));
        return recipes;
    }

    internal static MaterialTally Tally(params (string Item, int Amount)[] lines)
    {
        var tally = new MaterialTally();
        foreach ((string item, int amount) in lines)
        {
            tally.Add(item, amount);
        }

        return tally;
    }

    internal static ShelterPlan Plan(BuildOrderMarker? marker = null, IPieceRecipes? recipes = null) =>
        ShelterPlan.For(marker ?? Confirmed(), recipes ?? Recipes());
}

internal sealed class FakeRecipes : IPieceRecipes
{
    private readonly Dictionary<string, PieceRecipe> _recipes =
        new Dictionary<string, PieceRecipe>(StringComparer.Ordinal);

    internal List<string> Asked { get; } = new List<string>();

    internal void Set(string prefab, params (string Item, int Amount)[] costs)
    {
        var lines = new List<PieceCost>();
        foreach ((string item, int amount) in costs)
        {
            lines.Add(new PieceCost(item, amount));
        }

        _recipes[prefab] = PieceRecipe.Known(prefab, lines);
    }

    internal void SetFree(string prefab) => _recipes[prefab] = PieceRecipe.Known(prefab, null);

    internal void Forget(string prefab) => _recipes.Remove(prefab);

    public PieceRecipe Read(string prefab)
    {
        Asked.Add(prefab);
        return _recipes.TryGetValue(prefab, out PieceRecipe recipe)
            ? recipe
            : PieceRecipe.Unknown(prefab, "no such piece");
    }
}

internal sealed class FakeSight : IPieceSight
{
    private readonly Dictionary<string, PieceSighting> _byKey =
        new Dictionary<string, PieceSighting>(StringComparer.Ordinal);

    internal PieceSighting Default { get; set; } = PieceSighting.Missing;

    internal Exception? Throws { get; set; }

    internal void Set(string key, PieceSighting sighting) => _byKey[key] = sighting;

    /// <summary>Marks every piece of a phase as standing, the way it would be
    /// after Thorstein - or the player - finished that phase.</summary>
    internal void Finish(ShelterPlan plan, BuildPhase phase)
    {
        foreach (CostedPiece piece in plan.Pieces)
        {
            if (piece.Phase == phase)
            {
                _byKey[piece.Key] = PieceSighting.Standing;
            }
        }
    }

    public PieceSighting Look(in PiecePlacement placement)
    {
        if (Throws != null)
        {
            throw Throws;
        }

        return _byKey.TryGetValue(placement.Key, out PieceSighting sighting) ? sighting : Default;
    }
}

internal sealed class FakeProbe : IPlacementProbe
{
    internal List<string> Asked { get; } = new List<string>();

    internal ProbeAnswer Host { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Exists { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Loaded { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Ward { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Station { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Ground { get; set; } = ProbeAnswer.Yes;

    internal ProbeAnswer Space { get; set; } = ProbeAnswer.Yes;

    internal string? ThrowsFrom { get; set; }

    public ProbeAnswer MayActAsHost() => Answer("host", Host);

    public ProbeAnswer PieceExists(string prefab) => Answer("exists", Exists);

    public ProbeAnswer IsLoaded(in PiecePlacement placement) => Answer("loaded", Loaded);

    public ProbeAnswer WardAllows(in PiecePlacement placement) => Answer("ward", Ward);

    public ProbeAnswer StationInRange(in PiecePlacement placement) => Answer("station", Station);

    public ProbeAnswer GroundAllows(in PiecePlacement placement) => Answer("ground", Ground);

    public ProbeAnswer SpaceIsClear(in PiecePlacement placement) => Answer("space", Space);

    private ProbeAnswer Answer(string name, ProbeAnswer answer)
    {
        Asked.Add(name);
        if (string.Equals(ThrowsFrom, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("the world was not there");
        }

        return answer;
    }
}
