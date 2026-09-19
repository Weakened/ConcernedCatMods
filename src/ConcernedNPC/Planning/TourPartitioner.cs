using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One trip: the targets it services, and what has to be fetched before
/// it starts.
///
/// <b>A tour is provisioned once and then walked.</b> That sentence is the whole
/// difference between this package and the loops it replaces. Fetch everything
/// this trip needs, then go round the trip. Not: fetch one thing, service one
/// target, come back.</summary>
internal readonly struct JobTour
{
    private readonly JobTarget[]? _targets;

    internal JobTour(int index, IReadOnlyList<JobTarget>? targets)
    {
        Index = index;

        if (targets == null || targets.Count == 0)
        {
            _targets = null;
        }
        else
        {
            var copy = new JobTarget[targets.Count];
            for (int at = 0; at < targets.Count; at++)
            {
                copy[at] = targets[at];
            }

            _targets = copy;
        }
    }

    /// <summary>Which trip this is, from zero.</summary>
    internal int Index { get; }

    /// <summary>What it services, in the order the partitioner grouped them. The
    /// order it is <i>walked</i> in is the route planner's answer, not
    /// this.</summary>
    internal IReadOnlyList<JobTarget> Targets => _targets ?? Array.Empty<JobTarget>();

    /// <summary>Everything this trip consumes, totalled. <b>What is fetched, in
    /// one provisioning phase, before the trip starts.</b></summary>
    internal JobManifest Provision => ManifestArithmetic.Total(Targets);

    /// <summary>How many units this trip carries out.</summary>
    internal int Units => Provision.TotalUnits;

    internal bool IsEmpty => _targets == null;
}

/// <summary>Why a partition is what it is.</summary>
internal enum TourPartitionOutcome
{
    /// <summary>Nobody partitioned.</summary>
    Unspecified = 0,

    /// <summary>The job is in trips, all of it.</summary>
    Planned = 1,

    /// <summary>There were no targets to put in one.</summary>
    NothingToDo = 2,

    /// <summary>One target on its own needs more than the NPC can carry in a
    /// single trip, so no partition can service it. Refused rather than split:
    /// whether servicing half a target means anything at all is the role's
    /// question, and a runtime that guessed would have an NPC deliver half a
    /// wall and call it done.</summary>
    TargetTooLarge = 3,

    /// <summary>The partitioner ran out of its budget before it had finished.
    /// <b>No trips come back with this</b>, and that is the point: a partition
    /// that did not finish is not a partition, and half of one is worse than
    /// none because the caller cannot tell which half. Incomplete, not
    /// impossible - the next call gets a fresh budget and may well finish.
    /// </summary>
    BudgetExhausted = 4,

    /// <summary>Trips were worked out, and there are targets left over that no
    /// trip in this plan reaches, because the job needs more trips than one
    /// plan writes out.
    ///
    /// <b>Not a failure, and deliberately not the same answer as a spent
    /// budget.</b> Asking again changes nothing here - partitioning is
    /// deterministic, so the same job would hit the same cap forever, and an
    /// NPC that refused a large job outright would never build a long wall.
    /// What comes back is a real plan for the trips it does cover, and
    /// <see cref="TourPartition.LeftOver"/> is how many targets are waiting for
    /// the round after it. Everything downstream must carry that number, or a
    /// plan for eight trips out of twelve reports the job finished.</summary>
    PartlyPlanned = 5,
}

/// <summary>The job, in trips.</summary>
internal readonly struct TourPartition
{
    private readonly JobTour[]? _tours;

    internal TourPartition(
        TourPartitionOutcome outcome, IReadOnlyList<JobTour>? tours, JobTarget oversized, int leftOver = 0)
    {
        Outcome = outcome;
        Oversized = oversized;
        LeftOver = leftOver < 0 ? 0 : leftOver;

        if (tours == null || tours.Count == 0)
        {
            _tours = null;
        }
        else
        {
            var copy = new JobTour[tours.Count];
            for (int index = 0; index < tours.Count; index++)
            {
                copy[index] = tours[index];
            }

            _tours = copy;
        }
    }

    internal TourPartitionOutcome Outcome { get; }

    /// <summary>The trips, in the order they should be made.</summary>
    internal IReadOnlyList<JobTour> Tours => _tours ?? Array.Empty<JobTour>();

    /// <summary>The target that does not fit in any trip. Meaningless unless the
    /// outcome is <see cref="TourPartitionOutcome.TargetTooLarge"/>.</summary>
    internal JobTarget Oversized { get; }

    /// <summary>How many targets no trip in this partition reaches. Zero for a
    /// partition that covers the job; the reason
    /// <see cref="TourPartitionOutcome.PartlyPlanned"/> is not a quiet
    /// truncation.</summary>
    internal int LeftOver { get; }

    /// <summary>Whether these trips cover the job. <b>The question everything
    /// downstream has to ask</b>, because a plan built from trips that do not
    /// must not carry the whole job's manifest and must not reconcile to
    /// finished.</summary>
    internal bool CoversTheWholeJob => LeftOver == 0;

    internal bool IsPlanned =>
        (Outcome == TourPartitionOutcome.Planned || Outcome == TourPartitionOutcome.PartlyPlanned)
        && Tours.Count > 0;

    /// <summary>Every target across every trip. What the whole job turns out to
    /// be, after capacity has had its say.</summary>
    internal int Serviced
    {
        get
        {
            int total = 0;
            foreach (JobTour tour in Tours)
            {
                total += tour.Targets.Count;
            }

            return total;
        }
    }
}

/// <summary>Splitting a job that does not fit into the fewest trips that do.
///
/// <b>The rule that matters is the one about what this must never become.</b>
/// A capacity limit creates planned batches. It must never degrade into
/// one-target-at-a-time, and the difference is not a matter of taste: a trip
/// that carries material for one target is a walk to the chest per target, which
/// is the shipped behaviour this package exists to replace. So a trip is filled
/// until the next target genuinely will not fit, and
/// <see cref="NpcCarryCapacity.FewestToursFor"/> is the number a test holds the
/// partition to.
///
/// <b>Coherent, and coherent means geographic.</b> Targets are grouped by where
/// they are, not by the order the role happened to list them: the trip starts at
/// the highest-priority target nearest where the NPC stands, and then keeps
/// taking the nearest target that still fits. The next trip starts from where
/// the last one ended. A player watching sees him clear one end of the camp and
/// then the other, rather than crossing it five times.
///
/// <b>Priority is respected across trips, not only within one.</b> The seed of
/// each trip is the highest priority left, and a trip prefers targets of its own
/// priority band before it fills up with lower ones. That is what keeps "this
/// lamp matters more" meaning something when the job takes three trips - without
/// it, priority would decide the order inside a trip and nothing about which
/// trip you are in.
///
/// <b>Cost.</b> <c>O(targets^2)</c> horizontal distances, with targets bounded
/// by what the snapshot kept, and one budget unit per target placed. No probe,
/// no query, one allocation per trip.
///
/// <b>What it does not do.</b> It does not order the targets inside a trip -
/// that is <see cref="Routing.StopSequencer"/>, because ordering a walk and
/// grouping a job are different questions and only one of them is about
/// capacity. And it does not split a single target across trips: see
/// <see cref="TourPartitionOutcome.TargetTooLarge"/>.</summary>
internal static class TourPartitioner
{
    /// <summary>The most trips one plan is allowed to be. A job needing more
    /// than this is a job to be re-planned after the first few trips, not one to
    /// be written out in full now - by the time trip twelve starts, everything
    /// the plan believed about the world is hours old.</summary>
    internal const int MostTours = 8;

    /// <summary>Splits the targets into trips.</summary>
    /// <param name="targets">What the job has to service. Already snapshotted,
    /// already inside the work area.</param>
    /// <param name="capacity">How much goes in one trip.</param>
    /// <param name="from">Where the NPC is standing when the first trip
    /// starts.</param>
    /// <param name="budget">One unit per target placed.</param>
    internal static TourPartition Partition(
        IReadOnlyList<JobTarget>? targets, NpcCarryCapacity capacity, NpcPoint from, PlanningBudget budget)
    {
        if (budget == null)
        {
            throw new ArgumentNullException(nameof(budget));
        }

        if (targets == null || targets.Count == 0)
        {
            return new TourPartition(TourPartitionOutcome.NothingToDo, null, default);
        }

        var left = new List<JobTarget>();
        foreach (JobTarget target in targets)
        {
            if (!target.IsValid)
            {
                continue;
            }

            if (!capacity.RoomFor(0, target.Units))
            {
                return new TourPartition(TourPartitionOutcome.TargetTooLarge, null, target);
            }

            left.Add(target);
        }

        if (left.Count == 0)
        {
            return new TourPartition(TourPartitionOutcome.NothingToDo, null, default);
        }

        var tours = new List<JobTour>();
        NpcPoint cursor = from;
        bool spent = false;
        bool capped = false;

        while (left.Count > 0)
        {
            if (tours.Count >= MostTours)
            {
                capped = true;
                break;
            }

            var carrying = new List<JobTarget>();
            int units = 0;

            // The seed: the highest priority left, nearest to where this trip
            // starts. Priority first, because a trip's band is what makes
            // priority mean something across trips and not only inside one.
            int seed = Seed(left, cursor);
            int band = left[seed].Priority;
            Move(left, seed, carrying, ref units, ref cursor);

            // Then fill, preferring the seed's own band, and only dropping to a
            // lower one when nothing in the band still fits. Filling is what
            // stops a capacity limit turning into one target a trip.
            while (true)
            {
                if (!budget.TrySpend())
                {
                    spent = true;
                    break;
                }

                int next = Next(left, cursor, capacity, units, band, true);
                if (next < 0)
                {
                    next = Next(left, cursor, capacity, units, band, false);
                }

                if (next < 0)
                {
                    break;
                }

                Move(left, next, carrying, ref units, ref cursor);
            }

            tours.Add(new JobTour(tours.Count, carrying));

            if (spent)
            {
                break;
            }
        }

        if (spent && left.Count > 0)
        {
            // Nothing comes back. A trip built against half a budget is a trip
            // whose own contents depend on where the allowance happened to run
            // out, and a caller cannot tell that from a trip that was worked
            // out properly. The next call gets a fresh budget.
            //
            // And the count is every target, not just the unplaced ones: the
            // trips built so far are being thrown away, so the targets already
            // moved into them are left over too. Counting only `left` said a
            // twelve-target job had four waiting when it had twelve.
            int placed = 0;
            foreach (JobTour built in tours)
            {
                placed += built.Targets.Count;
            }

            return new TourPartition(
                TourPartitionOutcome.BudgetExhausted, null, default, left.Count + placed);
        }

        // The trip cap is the other thing entirely: these trips are exactly
        // what they say they are, and there are targets after them. Saying so
        // is what keeps a nine-trip job from quietly becoming an eight-trip job
        // that reports itself finished.
        TourPartitionOutcome outcome = capped && left.Count > 0
            ? TourPartitionOutcome.PartlyPlanned
            : TourPartitionOutcome.Planned;

        return new TourPartition(outcome, tours, default, left.Count);
    }

    /// <summary>The highest priority left, nearest to where the trip starts;
    /// ties on the target's own name so the same targets in a different order
    /// give the same trips.</summary>
    private static int Seed(List<JobTarget> left, NpcPoint from)
    {
        int best = 0;
        for (int index = 1; index < left.Count; index++)
        {
            JobTarget candidate = left[index];
            JobTarget winner = left[best];
            if (candidate.Priority != winner.Priority)
            {
                if (candidate.Priority > winner.Priority)
                {
                    best = index;
                }

                continue;
            }

            float here = from.HorizontalDistanceTo(candidate.At);
            float there = from.HorizontalDistanceTo(winner.At);
            if (here < there || (here == there && string.CompareOrdinal(candidate.Key, winner.Key) < 0))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>The nearest target that still fits, optionally restricted to one
    /// priority band.</summary>
    private static int Next(
        List<JobTarget> left, NpcPoint cursor, NpcCarryCapacity capacity, int carried, int band, bool inBand)
    {
        int best = -1;
        float bestDistance = 0f;
        for (int index = 0; index < left.Count; index++)
        {
            JobTarget candidate = left[index];
            if (inBand && candidate.Priority != band)
            {
                continue;
            }

            if (!inBand && candidate.Priority > band)
            {
                // A higher band than this trip's seed cannot exist: the seed was
                // the highest left. Guarding anyway, because a partition that
                // quietly promoted a target into an earlier trip would make
                // priority mean something different from what it says.
                continue;
            }

            if (!capacity.RoomFor(carried, candidate.Units))
            {
                continue;
            }

            float distance = cursor.HorizontalDistanceTo(candidate.At);
            if (best < 0 || distance < bestDistance ||
                (distance == bestDistance && string.CompareOrdinal(candidate.Key, left[best].Key) < 0))
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static void Move(
        List<JobTarget> left, int index, List<JobTarget> carrying, ref int units, ref NpcPoint cursor)
    {
        JobTarget target = left[index];
        left.RemoveAt(index);
        carrying.Add(target);
        units += target.Units;
        cursor = target.At;
    }
}
