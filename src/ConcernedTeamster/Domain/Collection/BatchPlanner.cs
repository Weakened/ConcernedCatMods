using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a batch is the size it is. Zero means nobody planned.</summary>
internal enum BatchOutcome
{
    Unspecified = 0,

    /// <summary>Everything the survey offered is in this batch.</summary>
    CoversEverythingSeen = 1,

    /// <summary>He cannot carry any more, so the rest wait for the next round.
    /// A real batch with real work left, never a refusal.</summary>
    CapacityReached = 2,

    /// <summary>The per-batch ceiling was reached before capacity was.</summary>
    BatchCeilingReached = 3,

    /// <summary>The survey offered nothing takeable.</summary>
    NothingToDo = 4,

    /// <summary>The survey could not say what is in the area, so no batch is
    /// planned at all. Distinct from <see cref="NothingToDo"/> on purpose: one
    /// means the area is clear, the other means nobody knows.</summary>
    Unknown = 5,
}

/// <summary>One thing to walk to and take, in the order it will be walked.
/// </summary>
internal sealed class CollectionStop
{
    public CollectionStop(CollectionCandidate candidate, int order)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Order = order;
    }

    public CollectionCandidate Candidate { get; }

    /// <summary>Zero-based position in the route.</summary>
    public int Order { get; }

    public override string ToString() => Order + ": " + Candidate.Key;
}

/// <summary>One planned batch: which targets, in what order, and what is left
/// over.</summary>
internal sealed class CollectionBatchPlan
{
    internal CollectionBatchPlan(
        BatchOutcome outcome,
        IReadOnlyList<CollectionStop> stops,
        int leftForAnotherRound,
        float kilograms,
        IReadOnlyDictionary<string, int> manifest)
    {
        Outcome = outcome;
        Stops = stops ?? Array.Empty<CollectionStop>();
        LeftForAnotherRound = leftForAnotherRound > 0 ? leftForAnotherRound : 0;
        Kilograms = kilograms > 0f ? kilograms : 0f;
        Manifest = manifest ?? new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public BatchOutcome Outcome { get; }

    /// <summary>The route, already ordered.</summary>
    public IReadOnlyList<CollectionStop> Stops { get; }

    /// <summary>How many takeable targets this plan does <b>not</b> reach.
    ///
    /// <b>This is the number the round loop exists to read.</b> A plan that
    /// covers part of a job has to say so, or a job of nine trips silently
    /// becomes a job of eight and reports that it finished. Nothing else in this
    /// product may compute "is the job done" from the steps alone.</summary>
    public int LeftForAnotherRound { get; }

    /// <summary>What this batch weighs if every stop comes off.</summary>
    public float Kilograms { get; }

    /// <summary>What this batch would yield, per item prefab.</summary>
    public IReadOnlyDictionary<string, int> Manifest { get; }

    /// <summary>Whether this plan reaches everything the survey offered. The
    /// only honest source of "the job is finished after this batch".</summary>
    public bool CoversEverythingSeen => LeftForAnotherRound == 0;

    /// <summary>Whether it is worth walking at all.</summary>
    public bool HasWork => Stops.Count > 0;
}

/// <summary>Chooses an efficient batch out of what the survey found, and puts it
/// in the order it will be walked (#381).
///
/// <b>Never one branch at a time, and never a trip per item.</b> The batch is
/// filled against the real carry budget (and the cart's room when one is
/// assigned) before a single step is taken, and the route is planned over the
/// whole batch. What does not fit is reported as left over rather than dropped,
/// which is what lets the round loop ask again instead of declaring the job
/// finished.
///
/// <b>What it deliberately is not.</b> Not a travelling-salesman solver: nearest
/// neighbour from where he stands, with an ordinal tie-break, and no improvement
/// pass. An NPC who walks a few metres further is better than one who stops to
/// think every few steps, and a deterministic order is what makes the same base
/// produce the same route twice - which is the property a player needs to
/// believe what they are watching.
///
/// <b>Where this is going.</b> The shared NPC runtime already contains a tour
/// partitioner, a stop sequencer and a reconciler that do this and more, with
/// priorities, reservations and source selection. They are <c>internal</c> to
/// that assembly today and a product cannot call them. When they become
/// reachable, this type deletes and the round loop keeps its shape: the two use
/// the same words for the same things - <see cref="CollectionBatchPlan.LeftForAnotherRound"/>
/// and <see cref="RoundReport.NeedsAnotherRound"/> - precisely so the swap is a
/// rename and not a redesign.</summary>
internal static class BatchPlanner
{
    public static CollectionBatchPlan Plan(
        CollectionSurvey survey,
        CarryBudget carry,
        CartCapacity cart,
        CollectionLimits limits,
        CollectionPoint from)
    {
        if (survey == null)
        {
            throw new ArgumentNullException(nameof(survey));
        }

        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (survey.Outcome == SurveyOutcome.AreaUnavailable || survey.Outcome == SurveyOutcome.Unspecified)
        {
            return Empty(BatchOutcome.Unknown);
        }

        var takeable = new List<CollectionCandidate>();
        for (int index = 0; index < survey.Candidates.Count; index++)
        {
            CollectionCandidate candidate = survey.Candidates[index];
            if (candidate.IsTakeable)
            {
                takeable.Add(candidate);
            }
        }

        if (takeable.Count == 0)
        {
            // An unfinished look that found nothing takeable is NOT an empty
            // area. Saying so is the difference between "he has cleared it" and
            // "he could not see", and only one of those means stop.
            return Empty(survey.Outcome == SurveyOutcome.Finished
                ? BatchOutcome.NothingToDo
                : BatchOutcome.Unknown);
        }

        // Fill nearest-first from where he is standing, each candidate measured
        // against what the earlier ones already put on his back. The cart's room
        // is counted per item prefab, because a cart slot holds one kind.
        var chosen = new List<CollectionCandidate>();
        var remaining = new List<CollectionCandidate>(takeable);
        var manifest = new Dictionary<string, int>(StringComparer.Ordinal);
        CarryBudget budget = carry;
        CollectionPoint at = from;
        float kilograms = 0f;
        BatchOutcome outcome = BatchOutcome.CoversEverythingSeen;
        bool capacityStopped = false;

        while (remaining.Count > 0)
        {
            if (chosen.Count >= limits.MostTargetsPerBatch)
            {
                outcome = BatchOutcome.BatchCeilingReached;
                break;
            }

            int pick = -1;
            float best = float.MaxValue;
            for (int index = 0; index < remaining.Count; index++)
            {
                CollectionCandidate candidate = remaining[index];
                float distance = at.FlatDistanceSquaredTo(candidate.Where);
                if (pick < 0 || distance < best ||
                    (distance == best && string.CompareOrdinal(candidate.Key, remaining[pick].Key) < 0))
                {
                    pick = index;
                    best = distance;
                }
            }

            CollectionCandidate next = remaining[pick];
            remaining.RemoveAt(pick);

            if (!Fits(next, budget, cart, manifest))
            {
                // He cannot take this one. Keep looking at the rest: a heavy
                // stone he has no room for does not mean the light branch
                // beside it has to wait for another round.
                capacityStopped = true;
                continue;
            }

            chosen.Add(next);
            budget = budget.After(next.Kilograms);
            kilograms += next.Kilograms;
            manifest.TryGetValue(next.ItemPrefab, out int already);
            manifest[next.ItemPrefab] = already + next.Units;
            at = next.Where;
        }

        int leftOver = takeable.Count - chosen.Count;
        if (chosen.Count == 0)
        {
            // Everything he can see is heavier than the room he has. That is a
            // real answer with real work left, not an empty area.
            return new CollectionBatchPlan(
                BatchOutcome.CapacityReached, Array.Empty<CollectionStop>(), leftOver, 0f,
                new Dictionary<string, int>(StringComparer.Ordinal));
        }

        if (outcome != BatchOutcome.BatchCeilingReached && capacityStopped)
        {
            outcome = BatchOutcome.CapacityReached;
        }

        if (leftOver == 0 && survey.Outcome != SurveyOutcome.Finished)
        {
            // Every candidate he was shown is in the batch, but the looking did
            // not finish, so the area may hold more. Reported as at least one
            // more round rather than as a covered job: the alternative is a
            // truncated survey reading as a finished one.
            leftOver = 1;
            if (outcome == BatchOutcome.CoversEverythingSeen)
            {
                outcome = BatchOutcome.BatchCeilingReached;
            }
        }

        var stops = new List<CollectionStop>(chosen.Count);
        for (int index = 0; index < chosen.Count; index++)
        {
            stops.Add(new CollectionStop(chosen[index], index));
        }

        return new CollectionBatchPlan(outcome, stops, leftOver, kilograms, manifest);
    }

    private static bool Fits(
        CollectionCandidate candidate,
        CarryBudget budget,
        CartCapacity cart,
        IReadOnlyDictionary<string, int> manifest)
    {
        if (budget.HowManyFit(candidate.UnitKilograms, candidate.Units) >= candidate.Units)
        {
            return true;
        }

        if (!cart.Assigned)
        {
            return false;
        }

        // The cart's room is the fallback, counted per item so the same slots
        // are not promised twice within one batch.
        manifest.TryGetValue(candidate.ItemPrefab, out int already);
        return already + candidate.Units <= cart.Units;
    }

    private static CollectionBatchPlan Empty(BatchOutcome outcome) =>
        new CollectionBatchPlan(
            outcome, Array.Empty<CollectionStop>(), 0, 0f,
            new Dictionary<string, int>(StringComparer.Ordinal));
}
