using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;

namespace ConcernedForeman.Tests;

/// <summary>The stand-ins for the four things only the game can do for a build
/// order, and a world that adds up.
///
/// <b>The conservation invariant lives in <see cref="BuildWorld.Total"/> and
/// nowhere else.</b> Every unit of material in this world is in the chest, in
/// Thorstein's hands, or inside a piece that is standing - so one number per item
/// is the whole of "no free material, no double-spend, no silent loss", and a
/// test asserts it after every tick rather than inspecting the loop's
/// intentions.</summary>
internal sealed class FakeWalk : IBuildWalk
{
    internal List<SitePoint> Goals { get; } = new List<SitePoint>();

    internal SitePoint Position { get; set; }

    /// <summary>Whether a walk arrives at once. True for tests that are about
    /// what he does when he gets there; false for tests about walking.</summary>
    internal bool Arrives { get; set; } = true;

    internal bool Present { get; set; } = true;

    internal bool Refuses { get; set; }

    internal BuildWalkStatus Walked { get; set; } = BuildWalkStatus.Idle;

    internal string Why { get; set; } = string.Empty;

    internal int Stops { get; private set; }

    bool IBuildWalk.IsPresent => Present;

    SitePoint IBuildWalk.Position => Position;

    BuildWalkStatus IBuildWalk.Status => Walked;

    string IBuildWalk.Deferral => Why;

    public bool WalkTo(SitePoint point, float arrivalTolerance)
    {
        if (Refuses)
        {
            return false;
        }

        Goals.Add(point);
        if (Arrives)
        {
            Position = point;
            Walked = BuildWalkStatus.Arrived;
        }
        else if (Walked != BuildWalkStatus.Deferred)
        {
            Walked = BuildWalkStatus.Walking;
        }

        return true;
    }

    public void Stop() => Stops++;
}

internal sealed class FakePose : IBuildPose
{
    internal List<bool> Calls { get; } = new List<bool>();

    internal bool On { get; private set; }

    internal int TimesStarted { get; private set; }

    public void Working(bool on)
    {
        Calls.Add(on);
        On = on;
        if (on)
        {
            TimesStarted++;
        }
    }
}

/// <summary>The placer, over a world that remembers what is standing.
///
/// A placement makes the piece <b>standing</b> in the sight the loop reads, which
/// is the whole point of reading progress from the world: the loop is never told
/// that a piece went up, it looks.</summary>
internal sealed class FakePlacer : IPiecePlacer
{
    private readonly BuildWorld _world;

    internal FakePlacer(BuildWorld world)
    {
        _world = world;
    }

    internal List<string> Placed { get; } = new List<string>();

    internal List<bool> Authority { get; } = new List<bool>();

    internal List<MaterialTally> Offered { get; } = new List<MaterialTally>();

    /// <summary>What the next placement does. Keyed by piece, then the default.
    /// </summary>
    internal Dictionary<string, PiecePlaced> Verdicts { get; } =
        new Dictionary<string, PiecePlaced>(StringComparer.Ordinal);

    internal PiecePlaced Default { get; set; } = PiecePlaced.Placed;

    internal string Reason { get; set; } = "something is already there";

    public PiecePlaced Place(CostedPiece piece, MaterialTally carried, bool authorised, out string reason)
    {
        Authority.Add(authorised);
        Offered.Add(carried.Copy());
        PiecePlaced verdict = Verdicts.TryGetValue(piece.Key, out PiecePlaced set) ? set : Default;

        // The gate's own cost check, kept here too: a fake that placed a piece
        // the carried material does not cover would hide a loop that walks to a
        // wall with nothing in its hands.
        if (verdict == PiecePlaced.Placed &&
            !new MaterialTally().Add(piece.Recipe).Missing(carried).IsEmpty)
        {
            reason = "it costs more than he is carrying";
            return PiecePlaced.Refused;
        }

        if (verdict != PiecePlaced.Placed)
        {
            reason = Reason;
            return verdict;
        }

        Placed.Add(piece.Key);
        _world.Stand(piece);
        reason = string.Empty;
        return PiecePlaced.Placed;
    }
}

/// <summary>The chest, his hands, and the two movements between them.</summary>
internal sealed class FakeMaterials : IBuildMaterials
{
    private readonly BuildWorld _world;

    internal FakeMaterials(BuildWorld world)
    {
        _world = world;
    }

    internal int Draws { get; private set; }

    internal int Spends { get; private set; }

    internal int PutBacks { get; private set; }

    internal SitePoint At { get; set; }

    internal string? NoSupply { get; set; }

    internal string? RefuseDraw { get; set; }

    internal string? RefuseSpend { get; set; }

    internal string? RefusePutBack { get; set; }

    /// <summary>Caps what one draw can move, so a short draw can be arranged
    /// without emptying the chest.</summary>
    internal int? Cap { get; set; }

    public bool TrySupply(out SitePoint at, out string refusal)
    {
        at = At;
        refusal = NoSupply ?? string.Empty;
        return NoSupply == null;
    }

    public MaterialTally Carried
    {
        get
        {
            var tally = new MaterialTally();
            foreach (KeyValuePair<string, int> held in _world.Carried)
            {
                tally.Add(held.Key, held.Value);
            }

            return tally;
        }
    }

    public BuildDraw Draw(MaterialTally wanted)
    {
        Draws++;
        if (RefuseDraw != null)
        {
            return BuildDraw.Refused(RefuseDraw);
        }

        var drawn = new MaterialTally();
        var shortBy = new MaterialTally();
        foreach (PieceCost line in wanted.Lines)
        {
            int allowed = Cap.HasValue ? Math.Min(Cap.Value, line.Amount) : line.Amount;
            int moved = Math.Min(allowed, _world.Chest.TryGetValue(line.Item, out int had) ? had : 0);
            if (moved > 0)
            {
                _world.Chest[line.Item] = had - moved;
                _world.Carried[line.Item] =
                    (_world.Carried.TryGetValue(line.Item, out int carried) ? carried : 0) + moved;
            }

            drawn.Add(line.Item, moved);
            shortBy.Add(line.Item, line.Amount - moved);
        }

        return BuildDraw.Moved(drawn, shortBy);
    }

    public bool Spend(PieceRecipe recipe, out MaterialTally spent, out string failure)
    {
        Spends++;
        spent = new MaterialTally();
        if (RefuseSpend != null)
        {
            failure = RefuseSpend;
            return false;
        }

        foreach (PieceCost cost in recipe.Costs)
        {
            int had = _world.Carried.TryGetValue(cost.Item, out int held) ? held : 0;
            int take = Math.Min(had, cost.Amount);
            _world.Carried[cost.Item] = had - take;
            spent.Add(cost.Item, take);
            if (take < cost.Amount)
            {
                failure = "he is carrying only " + had + " " + cost.Item;
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    public MaterialTally PutBack(out string failure)
    {
        PutBacks++;
        var back = new MaterialTally();
        if (RefusePutBack != null)
        {
            failure = RefusePutBack;
            return back;
        }

        foreach (string item in new List<string>(_world.Carried.Keys))
        {
            int held = _world.Carried[item];
            if (held < 1)
            {
                continue;
            }

            _world.Carried[item] = 0;
            _world.Chest[item] = (_world.Chest.TryGetValue(item, out int had) ? had : 0) + held;
            back.Add(item, held);
        }

        failure = string.Empty;
        return back;
    }
}

/// <summary>One world a build order runs in: the chest, his hands, what is
/// standing, and one number per item that must never change.</summary>
internal sealed class BuildWorld
{
    internal BuildWorld(int wood = 200, int hide = 40, float yaw = 0f)
    {
        Recipes = new FakeRecipes();
        Recipes.Set("wood_floor", ("Wood", 2));
        Recipes.Set("wood_wall", ("Wood", 2));
        Recipes.Set("wood_door", ("Wood", 4));
        Recipes.Set("bed", ("Wood", 8), ("DeerHide", 4));

        Marker = ConstructionRig.Confirmed(yaw: yaw);
        Plan = ShelterPlan.For(Marker, Recipes);
        Sight = new FakeSight { Default = PieceSighting.Missing };
        Chest["Wood"] = wood;
        Chest["DeerHide"] = hide;
        Materials = new FakeMaterials(this) { At = new SitePoint(30f, 0f, 0f) };
        Placer = new FakePlacer(this);
        Walk = new FakeWalk();
        Pose = new FakePose();
    }

    internal FakeRecipes Recipes { get; }

    internal BuildOrderMarker Marker { get; }

    internal ShelterPlan Plan { get; }

    internal FakeSight Sight { get; }

    internal FakeMaterials Materials { get; }

    internal FakePlacer Placer { get; }

    internal FakeWalk Walk { get; }

    internal FakePose Pose { get; }

    internal Dictionary<string, int> Chest { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    internal Dictionary<string, int> Carried { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>What the pieces that are standing are made of.</summary>
    internal Dictionary<string, int> Embodied { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    internal List<string> Said { get; } = new List<string>();

    internal bool Authorised { get; set; } = true;

    /// <summary>Marks a piece standing and records what it is made of, which is
    /// what makes the total conserved rather than merely non-negative.</summary>
    internal void Stand(CostedPiece piece)
    {
        Sight.Set(piece.Key, PieceSighting.Standing);
        foreach (PieceCost cost in piece.Recipe.Costs)
        {
            Embodied[cost.Item] = (Embodied.TryGetValue(cost.Item, out int had) ? had : 0) + cost.Amount;
        }
    }

    /// <summary>Pretends somebody else - the player, or an earlier session -
    /// already built a whole phase, paying for it out of the same world.</summary>
    internal void AlreadyBuilt(BuildPhase phase)
    {
        foreach (CostedPiece piece in Plan.Pieces)
        {
            if (piece.Phase != phase)
            {
                continue;
            }

            foreach (PieceCost cost in piece.Recipe.Costs)
            {
                Chest[cost.Item] = (Chest.TryGetValue(cost.Item, out int had) ? had : 0) - cost.Amount;
            }

            Stand(piece);
        }
    }

    /// <summary>Every unit of one item in this world. <b>The invariant.</b>
    /// </summary>
    internal int Total(string item) =>
        Count(Chest, item) + Count(Carried, item) + Count(Embodied, item);

    internal int Carrying(string item) => Count(Carried, item);

    internal int InChest(string item) => Count(Chest, item);

    /// <summary>A loop over this world, freshly built. Calling it twice is what
    /// a reload looks like from the loop's side: the same chest, the same hands,
    /// the same standing pieces, and no memory at all.</summary>
    internal ShelterBuildLoop Loop() => new ShelterBuildLoop(
        () => ShelterPlan.For(Authorised ? Marker : Marker.Withdraw(), Recipes),
        () => Authorised,
        Sight,
        Walk,
        Materials,
        Placer,
        Pose,
        Said.Add);

    private static int Count(Dictionary<string, int> where, string item) =>
        where.TryGetValue(item, out int had) ? had : 0;
}
