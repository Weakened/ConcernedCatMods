using System;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers.Felling;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>How much work a source costs him, cheapest first (the 2026-09-15
/// owner amendment on #282: "actual natural branch/loose-wood pickups, then
/// verified non-food woody brush/saplings, then small suitable trees"). A lower
/// tier is always chosen before a higher one, whatever the walk.</summary>
internal enum LabourTier
{
    Unspecified = 0,

    /// <summary>A loose stone or a fallen branch, picked by hand.</summary>
    LoosePickup = 1,

    /// <summary>Non-food woody brush. <b>Empty on purpose:</b> the installed
    /// game (1.0.21, D19's bundle read) has no natural sapling (every
    /// <c>*_Sapling</c> is player cultivation), its berry bushes are food, and
    /// D19 grants no bush, so no source is ever in this tier. It is named so the
    /// order of the tiers stays the owner's, not so anything can be put in it.
    /// </summary>
    Brush = 2,

    /// <summary>One of D19's four small trees, felled with his own axe.</summary>
    SmallTree = 3,
}

/// <summary>D19 inside a collection order (#282 leaf 2, #296's wood scope),
/// game-free: which tier a source is in, what a tree may be planned to give,
/// what a survey records for one, and the felling seam the loop drives.
/// </summary>
internal static class SmallTreeSources
{
    public static LabourTier TierOf(NaturalSourceKind kind) => kind switch
    {
        NaturalSourceKind.LooseStone => LabourTier.LoosePickup,
        NaturalSourceKind.Branch => LabourTier.LoosePickup,
        NaturalSourceKind.SmallTree => LabourTier.SmallTree,
        _ => LabourTier.Unspecified,
    };

    /// <summary>Is this source key one of the four small trees? Identity is the
    /// network prefab name the survey recorded.</summary>
    public static bool IsSmallTree(SourceKey key) => !key.IsEmpty && SmallTreeFelling.TryFind(key.PrefabName, out _);

    /// <summary>The most Wood one felled tree can drop: one Wood per drop pick
    /// at most (D19's table, read from the installed bundle: beech 2-3 picks,
    /// fir 4-5 picks, each pick Wood or a rarer Resin or FirCone). A plan only:
    /// what is credited is what was traced and taken.</summary>
    public static int MaxWood(SmallTreeKind kind) => kind == null ? 0 : Math.Max(0, kind.DropPicksMax);

    public static int MaxWood(string? prefab) =>
        SmallTreeFelling.TryFind(prefab, out SmallTreeKind? kind) ? MaxWood(kind!) : 0;

    /// <summary>What the selector plans a source to add. A stone or a branch
    /// gives its exact yield, so a pick that would give more than is still
    /// needed is never chosen. A tree's yield varies and only its own drops are
    /// ever taken, at most what is still needed and what fits him and the
    /// chest (<see cref="ISourceFellingPort.TryStrike"/>'s limit); the rest of
    /// what vanilla dropped stays on the ground as ordinary items. So a tree is
    /// planned at the smaller of its most and what is still needed.</summary>
    public static int PlannedYield(SourceObservation source, int remaining)
    {
        int yield = Math.Max(1, source.EstimatedYield);
        return source.Kind == NaturalSourceKind.SmallTree ? Math.Max(1, Math.Min(yield, remaining)) : yield;
    }

    /// <summary>What a survey records for a small tree: null when it is not one
    /// D19 allows (or already down), Inaccessible when a ward, a dungeon, a
    /// world location or a built piece within 15 m forbids it, Unknown when
    /// ownership, the ward or the location could not be established, and
    /// Available otherwise. Players move, so their clearance is a strike-time
    /// question; a building does not, so a tree beside one is never walked to.
    /// </summary>
    public static SourceAvailability? SurveyAvailability(
        SmallTreeFacts facts, SourceSiteFacts site, float builtPieceClearanceMetres)
    {
        if (!site.InScope || SmallTreeFelling.Classify(facts) != SmallTreeRefusal.Eligible)
        {
            return null;
        }

        if (site.InInterior || site.Location == LocationStanding.Inside || site.Ward == AreaAccess.Denied ||
            (builtPieceClearanceMetres < SmallTreeFelling.ClearanceMetres))
        {
            return SourceAvailability.Inaccessible;
        }

        if (!site.OwnedHere || site.Ward != AreaAccess.Granted || site.Location != LocationStanding.Outside ||
            !(builtPieceClearanceMetres >= SmallTreeFelling.ClearanceMetres))
        {
            // NaN lands here: a clearance nobody measured is not a clearance.
            return SourceAvailability.Unknown;
        }

        return SourceAvailability.Available;
    }
}

/// <summary>What one felling strike in an order did.</summary>
internal enum FellStrikeOutcome
{
    Unspecified = 0,

    /// <summary>The tree took the hit and still stands. Nothing moved; strike
    /// again after the swing cadence.</summary>
    Struck = 1,

    /// <summary>The tree is down. <see cref="FellStrikeResult.Pickup"/> holds
    /// the Wood drops his own hit produced that this order may take (possibly
    /// none: a beech can drop only resin).</summary>
    Felled = 2,

    /// <summary>A player is within 15 m of the tree. Nothing was struck; he
    /// waits, bounded, for them to step back.</summary>
    Waiting = 3,

    /// <summary>Refused before any hit, or the hit did nothing. Not tried again
    /// by this order.</summary>
    Refused = 4,

    /// <summary>What happened cannot be told. Never retried; the order needs
    /// attention with the evidence kept.</summary>
    Uncertain = 5,
}

internal sealed class FellStrikeResult
{
    public FellStrikeResult(FellStrikeOutcome outcome, PickupResult? pickup, string reason)
    {
        if (outcome == FellStrikeOutcome.Felled && (pickup == null || pickup.Outcome != PickupOutcome.Picked))
        {
            throw new ArgumentException("A fell carries the traced pickup of its own drops.", nameof(pickup));
        }

        Outcome = outcome;
        Pickup = pickup;
        Reason = reason ?? string.Empty;
    }

    public FellStrikeOutcome Outcome { get; }

    /// <summary>The journaled pickup of the drops a fell produced; null unless
    /// <see cref="Outcome"/> is Felled.</summary>
    public PickupResult? Pickup { get; }

    public string Reason { get; }
}

/// <summary>The felling seam of an order (D19). The Foreman adapter revalidates
/// every D19 gate in the same call as the hit, journals the strike as a pickup
/// before it, lands exactly one hit through the single pinned
/// <c>Destructible.Damage</c>, and on a fell traces only the Wood drops that hit
/// spawned, owned here and beside the tree, up to <paramref name="takeLimit"/>
/// units, for <see cref="ISourcePickupPort.TryTakeDrop"/> to take in the same
/// tick.</summary>
internal interface ISourceFellingPort
{
    FellStrikeResult TryStrike(SourceKey source, OrderId order, int takeLimit);
}
