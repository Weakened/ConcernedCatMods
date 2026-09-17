using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>What one resource of the order still needs, and how much room there
/// is for it on the way: on the worker's back and at the destination.</summary>
internal readonly struct ResourceNeed
{
    /// <summary>No destination room limit applies (hold for the player, or a
    /// hauler whose cart is somebody else's question).</summary>
    public const int Unlimited = int.MaxValue;

    public ResourceNeed(CollectedResource resource, int remaining, int carryRoom, int returnRoom)
    {
        if (resource == CollectedResource.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(resource), "A need names a resource.");
        }

        Resource = resource;
        Remaining = Math.Max(0, remaining);
        CarryRoom = Math.Max(0, carryRoom);
        ReturnRoom = Math.Max(0, returnRoom);
    }

    public CollectedResource Resource { get; }

    /// <summary><see cref="ResourceProgress.StillToCollect"/>: requested minus
    /// everything already committed (on the ground, carried, in a cart,
    /// delivered or handed over). Never the surveyed amount.</summary>
    public int Remaining { get; }

    /// <summary>Units the worker may still take: his weight budget and his real
    /// inventory fit, whichever is smaller.</summary>
    public int CarryRoom { get; }

    /// <summary>Units the destination can take beyond what he already carries.
    /// </summary>
    public int ReturnRoom { get; }
}

internal enum SelectionOutcome
{
    Unspecified = 0,

    Chosen = 1,

    /// <summary>Every resource of the order has nothing left to collect.
    /// </summary>
    AllNeedsCovered = 2,

    /// <summary>Sources exist for a needed resource but he cannot carry one
    /// more pick of it: time to deliver.</summary>
    CarryFull = 3,

    /// <summary>Sources exist but the destination could not take what one more
    /// pick would add to what he carries: deliver first, or pause.</summary>
    NoReturnSpace = 4,

    /// <summary>No available, unexcluded source of a needed resource whose pick
    /// would not overcollect.</summary>
    NoCandidates = 5,
}

internal readonly struct SelectionResult
{
    private SelectionResult(
        SelectionOutcome outcome, SourceObservation? source, float score, bool overcollectionBlocked)
    {
        Outcome = outcome;
        Source = source;
        Score = score;
        OvercollectionBlocked = overcollectionBlocked;
    }

    public SelectionOutcome Outcome { get; }

    public SourceObservation? Source { get; }

    /// <summary>The estimated cost of the chosen source, in metres of walking.
    /// </summary>
    public float Score { get; }

    /// <summary>At least one source was skipped only because its pick would
    /// have given more than was still needed.</summary>
    public bool OvercollectionBlocked { get; }

    internal static SelectionResult Chose(SourceObservation source, float score) =>
        new SelectionResult(SelectionOutcome.Chosen, source, score, false);

    internal static SelectionResult None(SelectionOutcome outcome, bool overcollectionBlocked) =>
        new SelectionResult(outcome, null, float.NaN, overcollectionBlocked);
}

/// <summary>Chooses the next source to walk to (GATHER-04): a bounded,
/// deterministic, quantity-aware heuristic. It claims no optimality.
///
/// <b>Quantity-aware, precisely.</b> A resource whose remaining need is zero is
/// not collected at all, while another resource with a need continues. "Need"
/// is <see cref="ResourceProgress.StillToCollect"/>, so material already on the
/// worker, in a cart or delivered counts, and what the survey merely saw does
/// not. A source whose pick would give more than is still needed is skipped: no
/// overcollection. A pick that would not fit his back or the destination is
/// skipped too, and the outcome says which limit stopped him.
///
/// <b>Cost.</b> Estimated route cost from where he stands (horizontal distance
/// times a detour factor, plus a climb cost), plus the pick's own effort, plus a
/// weighted share of how much further from the destination the source takes
/// him. No path query is spent here; walking finds out the truth, and a source
/// he could not reach is excluded by the caller.
///
/// <b>Deterministic.</b> Ties break by the order's resource order, then by the
/// source's session id and prefab name, ordinally. The same inputs choose the
/// same source.
///
/// <b>Bounded.</b> One pass over the snapshot, which the survey caps.</summary>
internal static class CollectionTargetSelector
{
    public static SelectionResult Select(
        CollectionParameters parameters,
        IReadOnlyList<SourceObservation> sources,
        IReadOnlyList<ResourceNeed> needs,
        SitePoint worker,
        SitePoint? returnPoint,
        Func<SourceKey, bool>? isExcluded)
    {
        if (parameters == null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (sources == null || needs == null)
        {
            throw new ArgumentNullException(sources == null ? nameof(sources) : nameof(needs));
        }

        bool anyNeed = false;
        foreach (ResourceNeed need in needs)
        {
            anyNeed |= need.Remaining > 0;
        }

        if (!anyNeed)
        {
            return SelectionResult.None(SelectionOutcome.AllNeedsCovered, false);
        }

        SourceObservation? best = null;
        float bestScore = float.PositiveInfinity;
        int bestResourceOrder = int.MaxValue;
        bool carryBlocked = false;
        bool returnBlocked = false;
        bool overcollection = false;

        foreach (SourceObservation source in sources)
        {
            if (source == null || source.Availability != SourceAvailability.Available ||
                source.Reachability == SourceReachability.Unreachable)
            {
                continue;
            }

            int resourceOrder = IndexOf(needs, source.Yields);
            if (resourceOrder < 0)
            {
                // The order does not ask for this resource at all.
                continue;
            }

            ResourceNeed need = needs[resourceOrder];
            if (need.Remaining <= 0)
            {
                // Stop per satisfied resource.
                continue;
            }

            if (isExcluded != null && isExcluded(source.Key))
            {
                continue;
            }

            int yield = Math.Max(1, source.EstimatedYield);
            if (yield > need.Remaining)
            {
                overcollection = true;
                continue;
            }

            if (yield > need.CarryRoom)
            {
                carryBlocked = true;
                continue;
            }

            if (yield > need.ReturnRoom)
            {
                returnBlocked = true;
                continue;
            }

            float score = Score(parameters, worker, source.Key.Position, returnPoint);
            if (IsBetter(score, resourceOrder, source, bestScore, bestResourceOrder, best))
            {
                best = source;
                bestScore = score;
                bestResourceOrder = resourceOrder;
            }
        }

        if (best != null)
        {
            return SelectionResult.Chose(best, bestScore);
        }

        if (carryBlocked)
        {
            return SelectionResult.None(SelectionOutcome.CarryFull, overcollection);
        }

        return returnBlocked
            ? SelectionResult.None(SelectionOutcome.NoReturnSpace, overcollection)
            : SelectionResult.None(SelectionOutcome.NoCandidates, overcollection);
    }

    /// <summary>The estimated cost of fetching a source, in metres.</summary>
    public static float Score(CollectionParameters parameters, SitePoint worker, SitePoint source, SitePoint? returnPoint)
    {
        float cost = EstimatedRouteCost(parameters, worker, source) + parameters.PickupEffortMetres;
        if (returnPoint.HasValue)
        {
            float detour = source.HorizontalDistanceTo(returnPoint.Value) - worker.HorizontalDistanceTo(returnPoint.Value);
            if (detour > 0f)
            {
                cost += parameters.ReturnTripWeight * detour * parameters.RouteDetourFactor;
            }
        }

        return cost;
    }

    public static float EstimatedRouteCost(CollectionParameters parameters, SitePoint from, SitePoint to) =>
        (from.HorizontalDistanceTo(to) * parameters.RouteDetourFactor) + (from.VerticalDistanceTo(to) * parameters.ClimbCostFactor);

    private static bool IsBetter(
        float score, int resourceOrder, SourceObservation candidate, float bestScore, int bestResourceOrder,
        SourceObservation? best)
    {
        if (best == null)
        {
            return true;
        }

        if (score < bestScore)
        {
            return true;
        }

        if (score > bestScore)
        {
            return false;
        }

        if (resourceOrder != bestResourceOrder)
        {
            return resourceOrder < bestResourceOrder;
        }

        int bySession = string.CompareOrdinal(candidate.Key.SessionId, best.Key.SessionId);
        if (bySession != 0)
        {
            return bySession < 0;
        }

        return string.CompareOrdinal(candidate.Key.PrefabName, best.Key.PrefabName) < 0;
    }

    private static int IndexOf(IReadOnlyList<ResourceNeed> needs, CollectedResource resource)
    {
        for (int index = 0; index < needs.Count; index++)
        {
            if (needs[index].Resource == resource)
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>How much more fits: on the worker (his NPC weight budget and his
/// real inventory) and at the destination (GATHER-06). Game-free arithmetic
/// over counts the ports report.</summary>
internal static class CarryPlanner
{
    /// <summary>Units of one resource the worker may still take.</summary>
    /// <param name="weightBudget"><c>Collection/WorkerCarryWeight</c>.</param>
    /// <param name="carriedMaterialWeight">Weight of the collectable material
    /// he carries now, of every kind.</param>
    /// <param name="unitWeight">The game's weight of one unit; not positive
    /// means unknown, which fits nothing.</param>
    /// <param name="inventoryFit">What his real inventory reports it can take.
    /// </param>
    public static int CarryRoomUnits(float weightBudget, float carriedMaterialWeight, float unitWeight, int inventoryFit)
    {
        if (!(unitWeight > 0f) || float.IsInfinity(unitWeight) || inventoryFit <= 0)
        {
            return 0;
        }

        float room = weightBudget - Math.Max(0f, carriedMaterialWeight);
        if (!(room > 0f))
        {
            return 0;
        }

        // A hair of tolerance so 100 / 2.0 is 50 and not 49.
        int byWeight = (int)Math.Floor((room / unitWeight) + 0.0001f);
        return Math.Max(0, Math.Min(byWeight, inventoryFit));
    }

    /// <summary>The destination's room beyond what is already carried, given
    /// what it says it accepts of (carried + probe).</summary>
    public static int ReturnRoomUnits(int acceptsOfCarriedPlusProbe, int carried) =>
        Math.Max(0, acceptsOfCarriedPlusProbe - Math.Max(0, carried));

    /// <summary>The total weight of an order's quotas, for the hold-for-player
    /// limit: everything asked for must fit on his back at once.</summary>
    public static float QuotaWeight(IReadOnlyList<ResourceQuota> quotas, Func<CollectedResource, float> unitWeight)
    {
        float total = 0f;
        foreach (ResourceQuota quota in quotas)
        {
            float weight = unitWeight(quota.Resource);
            if (!(weight > 0f))
            {
                return float.PositiveInfinity;
            }

            total += quota.Requested * weight;
        }

        return total;
    }
}
