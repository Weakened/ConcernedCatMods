using System;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>What a world question answered.
///
/// <b>Three values, and the third is the whole of CF-SET-003's go/no-go.</b>
/// #280's rule is that every check which cannot be faithfully reimplemented
/// becomes a refusal rather than an assumption. A two-valued answer cannot
/// express "I could not make this check", so the adapter would have to pick
/// between a false yes and a false no - and a false yes places a piece inside
/// somebody's ward.</summary>
internal enum ProbeAnswer
{
    /// <summary>The check could not be made. Always a refusal.</summary>
    CouldNotTell = 0,

    /// <summary>The check passed.</summary>
    Yes = 1,

    /// <summary>The check failed.</summary>
    No = 2,
}

/// <summary>Which gate refused a placement. The player reads a different
/// sentence for each, because the thing they would go and change is different
/// for each.</summary>
internal enum PlacementRefusal
{
    /// <summary>Nothing refused it.</summary>
    None = 0,

    /// <summary>No player confirmed this build order. #280's authority is the
    /// confirmation, and this is the refusal that makes that true rather than
    /// intended.</summary>
    NotAuthorised = 1,

    /// <summary>Not the host, or not alone on it. A worker runtime does not run
    /// on a client or beside another peer.</summary>
    NotHost = 2,

    /// <summary>The ground is not loaded. There is no offscreen building, and
    /// this leaf reports that limit rather than hiding it.</summary>
    NotLoaded = 3,

    /// <summary>A ward refuses it - <c>PrivateArea.CheckAccess</c>.</summary>
    Ward = 4,

    /// <summary>The piece needs a crafting station in range and there is not one
    /// - <c>CraftingStation.HaveBuildStationInRange</c>.</summary>
    Station = 5,

    /// <summary>The ground itself refuses it: the biome, the slope, water,
    /// unlevelled terrain.</summary>
    Ground = 6,

    /// <summary>Something is already there.</summary>
    Space = 7,

    /// <summary>The cost is not covered by what was reserved for this piece.
    /// <b>Never satisfied from a nearby chest</b>: material comes out of the
    /// reservation or the piece is not placed.</summary>
    Cost = 8,

    /// <summary>A check could not be made at all. The fail-closed answer, and
    /// the reason <see cref="ProbeAnswer.CouldNotTell"/> exists.</summary>
    Unchecked = 9,

    /// <summary>The blueprint asked for a piece the game does not have under
    /// that name. Never substituted.</summary>
    NoSuchPiece = 10,

    /// <summary>The site is inside a location the game forbids building in -
    /// <c>Location.IsInsideNoBuildLocation</c>.</summary>
    NoBuildZone = 11,

    /// <summary>The piece itself declares a placement constraint - only in this
    /// biome, not in a dungeon, only on cultivated ground, not on a tilting
    /// surface - that either fails here or that this runtime cannot judge.
    ///
    /// <b>The second case is the point.</b> Vanilla's placement ghost weighs a
    /// dozen of these and an NPC cannot drive the ghost, so each one this
    /// runtime does not reimplement has to be a refusal naming the constraint.
    /// The alternative is a containment that lives in a data file - "the pieces
    /// we happen to use declare none of them" - which evaporates the day
    /// somebody adds a piece, with no test going red.</summary>
    Constraint = 12,
}

/// <summary>The verdict on one placement.</summary>
internal readonly struct PlacementVerdict
{
    private PlacementVerdict(PlacementRefusal refusal, string check)
    {
        Refusal = refusal;
        Check = check ?? string.Empty;
    }

    internal PlacementRefusal Refusal { get; }

    /// <summary>The named check, in the player's words.</summary>
    internal string Check { get; }

    internal bool MayPlace => Refusal == PlacementRefusal.None;

    internal static PlacementVerdict Allowed { get; } =
        new PlacementVerdict(PlacementRefusal.None, string.Empty);

    internal static PlacementVerdict Refused(PlacementRefusal refusal, string check) =>
        new PlacementVerdict(refusal, check);

    public override string ToString() => MayPlace ? "may place" : Refusal + ": " + Check;
}

/// <summary>Every question the world has to answer before a piece is placed.
///
/// <b>One method per check, by name.</b> #280 names four gates
/// (<c>PrivateArea.CheckAccess</c>, <c>CraftingStation.HaveBuildStationInRange</c>,
/// the ground queries, and the real cost) and the whole point of the leaf is
/// that each is made explicitly rather than inherited from a placement path that
/// does not make it. One method per gate is what makes "which check refused
/// this" answerable, and what makes each one individually provable against a
/// stub.
///
/// <b>Every one of them may answer <see cref="ProbeAnswer.CouldNotTell"/>.</b>
/// That is not defensiveness: three of these reach into engine state that can be
/// absent (no zone system yet, no player instance, a prefab with no piece
/// component), and the honest answer there is not "yes".</summary>
internal interface IPlacementProbe
{
    /// <summary>Whether this runtime may change the world at all right now:
    /// host, not dedicated, no other peers.</summary>
    ProbeAnswer MayActAsHost();

    /// <summary>Whether the game knows a piece by this name.</summary>
    ProbeAnswer PieceExists(string prefab);

    /// <summary>Whether the ground at this point is loaded.</summary>
    ProbeAnswer IsLoaded(in PiecePlacement placement);

    /// <summary>Whether a ward allows building here -
    /// <c>PrivateArea.CheckAccess(point, radius, flash: false, wardCheck: true)</c>.
    /// </summary>
    ProbeAnswer WardAllows(in PiecePlacement placement);

    /// <summary>Whether the station this piece requires is in range -
    /// <c>CraftingStation.HaveBuildStationInRange(name, point)</c>. A piece that
    /// requires no station answers <see cref="ProbeAnswer.Yes"/>.</summary>
    ProbeAnswer StationInRange(in PiecePlacement placement);

    /// <summary>Whether the site is outside every location the game forbids
    /// building in - <c>Location.IsInsideNoBuildLocation</c>.</summary>
    ProbeAnswer OutsideNoBuildZone(in PiecePlacement placement);

    /// <summary>Whether every placement constraint the piece itself declares is
    /// satisfied here, <b>and understood at all</b>.</summary>
    /// <param name="constraint">Which constraint failed, or which one this
    /// runtime does not implement. Named, because it is the only way a player or
    /// a later reader learns why a wall will not go up.</param>
    /// <returns><see cref="ProbeAnswer.Yes"/> only when every constraint the
    /// piece declares was both understood and satisfied;
    /// <see cref="ProbeAnswer.No"/> when one is understood and fails; and
    /// <see cref="ProbeAnswer.CouldNotTell"/> when the piece declares one this
    /// runtime does not judge.</returns>
    ProbeAnswer ConstraintsAllow(in PiecePlacement placement, out string constraint);

    /// <summary>Whether the ground and terrain accept this piece.</summary>
    ProbeAnswer GroundAllows(in PiecePlacement placement);

    /// <summary>Whether the space is clear.</summary>
    ProbeAnswer SpaceIsClear(in PiecePlacement placement);
}

/// <summary>The placement authority: eight checks in a fixed order, and any one
/// of them that cannot be made is a refusal.
///
/// <b>Why the order is fixed and written down.</b> The refusal a player is shown
/// is the first one that fired, so the order decides which sentence they read.
/// It runs from the cheapest and most general to the most specific: being
/// allowed to act at all, then the piece existing, then the ground being there,
/// then the two permission gates, then the physical ones, and the cost last -
/// because the cost is the only check whose answer depends on what was reserved
/// rather than on the world, and asking it first would report "not enough wood"
/// for a wall that a ward was never going to allow anyway.
///
/// <b>The cost check is against the reservation and nothing else.</b> This is
/// the difference between a settlement worker and a cheat: material comes out of
/// what was set aside for this piece, never out of whatever chest happens to be
/// near the site.</summary>
internal static class PlacementGate
{
    /// <summary>Whether this piece may be placed.</summary>
    /// <param name="placement">Where and what.</param>
    /// <param name="recipe">Its real cost, as the game gave it.</param>
    /// <param name="probe">The world.</param>
    /// <param name="reserved">What is in hand for this piece, out of custody.
    /// </param>
    /// <param name="authorised">Whether a player confirmed the order this piece
    /// belongs to.</param>
    internal static PlacementVerdict May(
        PiecePlacement placement,
        PieceRecipe recipe,
        IPlacementProbe? probe,
        MaterialTally? reserved,
        bool authorised)
    {
        if (!authorised)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.NotAuthorised,
                "no confirmed build order authorises building here");
        }

        if (!placement.IsValid)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.NoSuchPiece, "the blueprint piece is not a piece");
        }

        if (probe == null)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.Unchecked, "there is no way to ask the world anything");
        }

        PiecePlacement at = placement;
        PlacementVerdict verdict = Ask(
            () => probe.MayActAsHost(),
            PlacementRefusal.NotHost,
            "this is not a world this runtime may build in: it builds only as the host, alone");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.PieceExists(placement.Piece.Prefab),
            PlacementRefusal.NoSuchPiece,
            "the game has no piece called " + placement.Piece.Prefab +
            ", and nothing will be put there instead");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.IsLoaded(in at),
            PlacementRefusal.NotLoaded,
            "the ground there is not loaded, so nothing is built there until somebody is near it");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.WardAllows(in at),
            PlacementRefusal.Ward,
            "a ward refuses building there");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.OutsideNoBuildZone(in at),
            PlacementRefusal.NoBuildZone,
            "the game forbids building at that place");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        // The piece's own constraints, and the one check whose refusal has to
        // carry a name from the probe rather than a sentence from here: a piece
        // that declares something this runtime does not judge must say WHICH
        // thing, or the next person to add a piece to a blueprint has a wall
        // that will not go up and nothing to read.
        verdict = AskAboutConstraints(probe, at);
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.StationInRange(in at),
            PlacementRefusal.Station,
            "the crafting station this piece needs is not in range");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.GroundAllows(in at),
            PlacementRefusal.Ground,
            "the ground there will not take this piece");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        verdict = Ask(
            () => probe.SpaceIsClear(in at),
            PlacementRefusal.Space,
            "something is already there");
        if (!verdict.MayPlace)
        {
            return verdict;
        }

        if (!recipe.IsKnown)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.Cost,
                "what this piece costs was never established, so nothing is spent on it");
        }

        MaterialTally wanted = new MaterialTally().Add(recipe);
        MaterialTally missing = wanted.Missing(reserved);
        if (!missing.IsEmpty)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.Cost,
                "it costs " + wanted.Describe() + " and " + missing.Describe() +
                " of that was not reserved for it");
        }

        return PlacementVerdict.Allowed;
    }

    private static PlacementVerdict AskAboutConstraints(IPlacementProbe probe, PiecePlacement at)
    {
        ProbeAnswer answer;
        string constraint;
        try
        {
            answer = probe.ConstraintsAllow(in at, out constraint);
        }
        catch (Exception exception)
        {
            return PlacementVerdict.Refused(
                PlacementRefusal.Unchecked,
                "the piece's own constraints could not be read (" + SafeFailure.Brief(exception) +
                "), so nothing is placed");
        }

        if (answer == ProbeAnswer.Yes)
        {
            return PlacementVerdict.Allowed;
        }

        string named = string.IsNullOrEmpty(constraint) ? "a constraint it did not name" : constraint;
        return answer == ProbeAnswer.No
            ? PlacementVerdict.Refused(
                PlacementRefusal.Constraint, "the piece may not be built there: " + named)
            : PlacementVerdict.Refused(
                PlacementRefusal.Constraint,
                "the piece declares " + named +
                ", which this runtime does not judge, so nothing is placed rather than being " +
                "placed where the game would have refused it");
    }

    private static PlacementVerdict Ask(
        Func<ProbeAnswer> question, PlacementRefusal refusal, string check)
    {
        ProbeAnswer answer;
        try
        {
            answer = question();
        }
        catch (Exception exception)
        {
            // A check that threw is a check that was not made. The fail-closed
            // reading is the only one #280 permits.
            return PlacementVerdict.Refused(
                PlacementRefusal.Unchecked,
                "a check could not be made (" + SafeFailure.Brief(exception) + "), so nothing is placed");
        }

        switch (answer)
        {
            case ProbeAnswer.Yes:
                return PlacementVerdict.Allowed;
            case ProbeAnswer.No:
                return PlacementVerdict.Refused(refusal, check);
            default:
                return PlacementVerdict.Refused(
                    PlacementRefusal.Unchecked,
                    "the check for whether " + check + " could not be made, so nothing is placed");
        }
    }
}
