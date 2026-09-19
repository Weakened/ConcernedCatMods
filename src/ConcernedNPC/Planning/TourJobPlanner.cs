using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One step of a plan, with the thing it is about still attached.
///
/// <b>Why this exists beside <see cref="JobStep"/>.</b> A <c>JobStep</c> is the
/// contract's view: an index, two opaque words, a place, a count. That is
/// everything a role needs to carry a step out and deliberately nothing more.
/// But the pipeline itself has to reserve the container a collect step opens and
/// the target a service step is for, and a name is not a subject - a reservation
/// book holds the thing, not a string about it. So the tour plan keeps the
/// subjects alongside, and the <c>JobPlan</c> stays exactly as narrow as it was
/// written to be.</summary>
internal readonly struct PlannedStep
{
    internal PlannedStep(
        JobStep step, int tour, bool isCollect, SourceStock source, JobTarget target, JobManifest moves)
    {
        Step = step;
        Tour = tour;
        IsCollect = isCollect;
        Source = source;
        Target = target;
        Moves = moves;
    }

    /// <summary>The step as the plan carries it.</summary>
    internal JobStep Step { get; }

    /// <summary>Which trip it belongs to.</summary>
    internal int Tour { get; }

    /// <summary>Whether this is a chest being opened rather than a target being
    /// serviced.</summary>
    internal bool IsCollect { get; }

    /// <summary>The container, for a collect step.</summary>
    internal SourceStock Source { get; }

    /// <summary>The target, for a service step.</summary>
    internal JobTarget Target { get; }

    /// <summary>What this step moves, item by item: out of the chest for a
    /// collect step, into the work for a service step. <b>The only thing that
    /// makes reconciliation arithmetic rather than guesswork</b> - what was
    /// fetched minus what was used is what is still being carried at the end of
    /// the round, and a total in units cannot answer that because forty of one
    /// thing and forty of another are the same number.</summary>
    internal JobManifest Moves { get; }

    /// <summary>The name this step's reservation would be taken under.</summary>
    internal ReservationId ReservationFor(string? jobId) => ReservationId.For(jobId, Step.Index);
}

/// <summary>A plan, plus everything the pipeline worked out on the way to it.
///
/// <b>The plan is the contract; this is the working.</b> A role that only needs
/// the ordered steps takes <see cref="Plan"/> and never sees the rest. A runtime
/// that has to provision once per trip, reserve what it is about to touch and
/// reconcile at the end needs the trips, the draws and the subjects, and
/// <see cref="JobPlan"/> deliberately carries none of those - it is a list of
/// steps and refuses to become a second source of truth about anything
/// else.</summary>
internal readonly struct JobTourPlan
{
    private readonly JobTour[]? _tours;
    private readonly SourcePlan[]? _provisioning;
    private readonly PlannedStep[]? _steps;

    internal JobTourPlan(
        JobPlan plan,
        IReadOnlyList<JobTour>? tours,
        IReadOnlyList<SourcePlan>? provisioning,
        IReadOnlyList<PlannedStep>? steps,
        JobManifest shortfall,
        int budgetSpent,
        int leftForAnotherRound = 0)
    {
        Plan = plan;
        Shortfall = shortfall;
        BudgetSpent = budgetSpent;
        LeftForAnotherRound = leftForAnotherRound < 0 ? 0 : leftForAnotherRound;
        _tours = Copy(tours);
        _provisioning = Copy(provisioning);
        _steps = Copy(steps);
    }

    /// <summary>The plan itself, exactly as <see cref="IJobPlanner"/> hands it
    /// over.</summary>
    internal JobPlan Plan { get; }

    /// <summary>The trips, in the order they are made.</summary>
    internal IReadOnlyList<JobTour> Tours => _tours ?? Array.Empty<JobTour>();

    /// <summary>What to fetch before each trip, index-aligned with
    /// <see cref="Tours"/>. <b>One provisioning phase per trip</b>, which is the
    /// whole shape of the behaviour.</summary>
    internal IReadOnlyList<SourcePlan> Provisioning => _provisioning ?? Array.Empty<SourcePlan>();

    /// <summary>Every step with its subject still attached, index-aligned with
    /// <c>Plan.Steps</c>.</summary>
    internal IReadOnlyList<PlannedStep> Steps => _steps ?? Array.Empty<PlannedStep>();

    /// <summary>What the job is short of, if anything. Empty for a plan.
    /// </summary>
    internal JobManifest Shortfall { get; }

    /// <summary>What working this out cost.</summary>
    internal int BudgetSpent { get; }

    /// <summary>How many targets of the job this plan does not reach, because
    /// the job needs more trips than one plan writes out or more chests than one
    /// provisioning phase opens.
    ///
    /// <b>Zero is the claim, not the default.</b> A plan that covers eight trips
    /// of twelve is a perfectly good plan for eight trips; what would be a
    /// defect is one that said nothing about the other four, because then every
    /// step finishing reads as the job finishing. This number is what
    /// <see cref="JobReconciliation.IsComplete"/> refuses to ignore.</summary>
    internal int LeftForAnotherRound { get; }

    /// <summary>Whether this plan reaches every target the job was for.</summary>
    internal bool CoversTheWholeJob => LeftForAnotherRound == 0;

    internal bool IsActionable => Plan.IsActionable;

    private static T[]? Copy<T>(IReadOnlyList<T>? source)
    {
        if (source == null || source.Count == 0)
        {
            return null;
        }

        var copy = new T[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }
}

/// <summary>The pipeline: a snapshot in, a whole job out.
///
/// <b>What it does, in the order it does it.</b> Total the targets into a
/// manifest. Refuse a job the role and the snapshot disagree about. Refuse a job
/// nothing reachable can provision, <i>before</i> a step is taken. Split what is
/// left into the fewest trips capacity allows. For each trip in turn: choose the
/// chests, write the collect steps, order the targets into a walk, write the
/// service steps. Hand back a plan and the working.
///
/// <b>What it must never look like, said once in full because it is the reason
/// the package exists.</b> See one target, walk to a chest, fetch one item,
/// service one target, walk back, repeat. Every step of that loop is a decision
/// taken with the whole job unknown, and the visible result is an NPC that
/// crosses the camp eight times for a wall it could have provisioned in one
/// trip. Nothing in here decides anything about a target without the total in
/// front of it.
///
/// <b>Pure, and pure for a reason.</b> The same snapshot and the same request
/// give the same plan, every time, in this process and in a test with no game
/// installed. Each call works from a fresh budget so that calling twice does not
/// give two different answers, and nothing here reads a clock it was not handed.
/// That is not tidiness: an interruption decides between carrying on and
/// planning again by comparing a fresh plan against the one it was following,
/// and a planner whose second answer differed for no reason would replan
/// forever.
///
/// <b>What stays the role's.</b> Which targets are eligible, what they cost,
/// what priority they have, what the two action words are, what "done" means,
/// and what any of the item tokens are. Nothing in this file knows what a cart
/// is, what resin is for, or why anybody wants a shelter.
///
/// <b>Cost.</b> Bounded by <see cref="TourPartitioner.MostTours"/> trips, each
/// costing one source selection (<c>O(chests * items)</c> per stop, at most
/// <see cref="SourceSelector.MostStops"/> stops) and one ordering
/// (<c>O(stops^2)</c>, capped by
/// <see cref="StopSequencer.MostStopsPerRound"/>). No probe, no navigation
/// query, no clock, no allocation per candidate.</summary>
internal sealed class TourJobPlanner : IJobPlanner
{
    /// <summary>What one call is allowed to spend if the caller does not say. A
    /// number rather than no limit, because "no limit" is how a pathological
    /// snapshot becomes a frame the player feels.</summary>
    internal const int DefaultAllowance = 512;

    private readonly JobSnapshot _snapshot;
    private readonly NpcCarryCapacity _capacity;
    private readonly JobStepActions _actions;
    private readonly INpcSourceAvailability? _availability;
    private readonly NpcWalkSetbacks? _setbacks;
    private readonly int _allowance;
    private readonly float _now;

    /// <summary>Builds a planner over one snapshot.</summary>
    /// <param name="snapshot">What was found. Frozen: the planner reads nothing
    /// else about the world.</param>
    /// <param name="capacity">How much goes in one trip.</param>
    /// <param name="actions">The role's two words for the two kinds of step this
    /// planner writes.</param>
    /// <param name="setbacks">What walks have failed lately, so a target behind a
    /// wall the ground lies about is left out of the round. Shared across NPCs,
    /// and optional.</param>
    /// <param name="now">The caller's clock, for judging which setbacks are
    /// still active. Passed in rather than read.</param>
    /// <param name="allowance">What one call may spend.</param>
    /// <param name="availability">What other jobs have already set aside, or
    /// null to plan against the raw observations. Null is a decision, not a
    /// default that happens to be safe: without it two jobs a tick apart plan
    /// the same pile and both stall.</param>
    internal TourJobPlanner(
        JobSnapshot snapshot,
        NpcCarryCapacity capacity,
        JobStepActions actions,
        NpcWalkSetbacks? setbacks = null,
        float now = 0f,
        int allowance = DefaultAllowance,
        INpcSourceAvailability? availability = null)
    {
        _snapshot = snapshot;
        _capacity = capacity;
        _actions = actions;
        _availability = availability;
        _setbacks = setbacks;
        _now = now;
        _allowance = allowance < 0 ? 0 : allowance;
    }

    /// <inheritdoc />
    public JobPlan Plan(in JobPlanRequest request) => PlanTours(request).Plan;

    /// <summary>The plan and the working behind it.</summary>
    internal JobTourPlan PlanTours(in JobPlanRequest request)
    {
        var budget = new PlanningBudget(_allowance);
        NpcWorldEpoch epoch = request.Epoch;

        if (request.Identity.IsEmpty || !_actions.IsValid ||
            ReservationId.For(request.JobId, 0).IsEmpty)
        {
            // A job whose steps cannot be named cannot reserve anything, and a
            // plan whose steps nobody can carry out is not a plan.
            return Refuse(
                JobPlanVerdict.Refused,
                "the job was asked for without an identity, a usable job name, or the two words its steps are written in",
                epoch,
                budget);
        }

        if (request.Area == null)
        {
            return Refuse(
                JobPlanVerdict.AreaInvalid,
                "there is no work area to plan against, and nothing here widens to anywhere",
                epoch,
                budget);
        }

        if (epoch.IsUnknown || !epoch.Matches(_snapshot.Epoch))
        {
            return Refuse(
                JobPlanVerdict.Refused,
                "what was seen belongs to a different loading of the world, so none of its names mean anything now",
                epoch,
                budget);
        }

        if (_snapshot.Report.Outcome == AreaScanOutcome.AreaInvalid)
        {
            return Refuse(
                JobPlanVerdict.AreaInvalid, "the work area could not be read at all", epoch, budget);
        }

        IReadOnlyList<JobTarget> targets = _snapshot.Targets;
        if (targets.Count == 0)
        {
            // The one place a job may be reported finished without doing
            // anything - and only on evidence that it really is finished.
            return _snapshot.IsConclusive
                ? Refuse(JobPlanVerdict.NothingToDo, string.Empty, epoch, budget)
                : Refuse(
                    JobPlanVerdict.BudgetExhausted,
                    "nothing was found and the looking was not finished, so there is nothing to say yet",
                    epoch,
                    budget);
        }

        JobManifest wanted = ManifestArithmetic.Total(targets);
        if (ManifestArithmetic.Exceeds(wanted, request.Wanted, out JobManifestLine over))
        {
            return Refuse(
                JobPlanVerdict.Refused,
                "what was found needs " + over.ToString() +
                " more than the job was said to be for, and taking a player's material for something nobody asked for is not a plan",
                epoch,
                budget);
        }

        JobManifest missing = ManifestArithmetic.Shortfall(wanted, _snapshot.Sources, _availability);
        if (!missing.IsEmpty)
        {
            // Refused before anything starts, which is the entire benefit of
            // working the whole job out first: the player is told what is
            // missing instead of watching a third of a wall go up.
            return new JobTourPlan(
                JobPlan.Refused(
                    JobPlanVerdict.ShortOfMaterial,
                    "nothing he may use holds " + ManifestArithmetic.Describe(missing),
                    epoch),
                null,
                null,
                null,
                missing,
                budget.Spent);
        }

        TourPartition partition = TourPartitioner.Partition(targets, _capacity, request.StartingFrom, budget);
        if (partition.Outcome == TourPartitionOutcome.TargetTooLarge)
        {
            return Refuse(
                JobPlanVerdict.Refused,
                "one thing on its own needs more than he can carry in a single trip, and this runtime does not decide what half of it would mean",
                epoch,
                budget);
        }

        if (partition.Outcome == TourPartitionOutcome.BudgetExhausted)
        {
            // Asked whatever the tour count is. Reading this outcome only when
            // no trips came back is how a job of twelve trips quietly became a
            // job of the first two and then reported itself finished.
            //
            // Honestly: this branch is currently unreachable, because the
            // partitioner answers this outcome with no trips at all and the
            // selection below refuses on the same spent budget a moment later.
            // Three guards over one mistake, and deleting this one alone would
            // not fail a test. It is here because the rule it backs up - a
            // partition that did not finish is not a partition - is the one a
            // later change is most likely to relax, and on the day it is
            // relaxed this is what stops the blocker coming back.
            return Refuse(
                JobPlanVerdict.BudgetExhausted,
                "working out the trips was not finished, so there is nothing to walk yet",
                epoch,
                budget);
        }

        if (partition.Tours.Count == 0)
        {
            return Refuse(JobPlanVerdict.NothingToDo, string.Empty, epoch, budget);
        }

        return Write(request, partition, budget);
    }

    /// <summary>Writes the steps, and carries what was left out.
    ///
    /// <b>The manifest a plan carries is computed here, over the targets the
    /// steps actually reach - never over the targets the job started with.</b>
    /// A plan that claimed the whole job's total while covering eight trips of
    /// twelve is a plan that reconciles to finished with a third of the wall
    /// missing, and nothing anywhere would say so.</summary>
    private JobTourPlan Write(in JobPlanRequest request, TourPartition partition, PlanningBudget budget)
    {
        var steps = new List<JobStep>();
        var planned = new List<PlannedStep>();
        var provisioning = new List<SourcePlan>();
        var covered = new List<JobTarget>();
        List<SourceStock> stock = new List<SourceStock>(_snapshot.Sources);
        NpcPoint cursor = request.StartingFrom;
        int leftOver = partition.LeftOver;

        foreach (JobTour tour in partition.Tours)
        {
            SourcePlan supply = SourceSelector.Select(
                tour.Provision, stock, cursor, budget, _availability);

            if (supply.Truncation == SourceTruncation.BudgetSpent)
            {
                // Not a fact about the world at all: the next call gets a fresh
                // budget and may finish. Answering with the shortfall here would
                // tell a player the settlement is short of material that is
                // sitting in the next chest along.
                return Refuse(
                    JobPlanVerdict.BudgetExhausted,
                    "choosing which chests to open was not finished, so there is nothing to walk yet",
                    request.Epoch,
                    budget);
            }

            JobTour tourBeingWalked = tour;
            if (supply.Truncation == SourceTruncation.ChestCap)
            {
                // This trip needs more chests than one provisioning phase opens.
                // Asking again is useless - the choice is deterministic - so the
                // trip gets smaller instead: he takes what those chests give him,
                // services as many targets as that covers, and the rest wait for
                // the next round. That is the planned-batch answer capacity
                // already gets, applied to the other axis.
                tourBeingWalked = Shrink(tour, supply, out int deferred);
                leftOver += deferred;
                if (tourBeingWalked.Targets.Count == 0)
                {
                    leftOver += Remaining(partition, tour.Index);
                    break;
                }
            }
            else if (!supply.IsComplete)
            {
                // The whole-job check above passed, so reaching here means the
                // earlier trips have already committed the material this one
                // wanted. Saying so is better than planning a trip whose chest
                // will be empty when he opens it.
                return new JobTourPlan(
                    JobPlan.Refused(
                        JobPlanVerdict.ShortOfMaterial,
                        "after the earlier trips there is no longer enough for " +
                        ManifestArithmetic.Describe(supply.Shortfall),
                        request.Epoch),
                    partition.Tours,
                    provisioning,
                    null,
                    supply.Shortfall,
                    budget.Spent,
                    leftOver);
            }

            provisioning.Add(supply);

            foreach (SourceDraw draw in supply.Draws)
            {
                var step = new JobStep(
                    steps.Count, _actions.Collect, draw.Key, draw.Position, true, draw.Units);
                steps.Add(step);
                planned.Add(new PlannedStep(
                    step, tour.Index, true, draw.Source, default, Fetched(draw)));
                cursor = draw.Position;
            }

            stock = Deplete(stock, supply.Draws);

            StopSequence round = Order(tourBeingWalked, cursor);
            foreach (RouteStop stop in round.Stops)
            {
                if (!TryFind(tourBeingWalked.Targets, stop.Key, out JobTarget target))
                {
                    continue;
                }

                string action = target.Action.Length != 0 ? target.Action : _actions.Service;
                var step = new JobStep(steps.Count, action, target.Key, target.At, true, target.Units);
                steps.Add(step);
                planned.Add(new PlannedStep(step, tourBeingWalked.Index, false, default, target, target.Needs));
                covered.Add(target);
                cursor = target.At;
            }

            if (supply.Truncation == SourceTruncation.ChestCap)
            {
                // A shrunken trip is as far as this plan goes: the trips after it
                // were worked out against chests this one has now emptied, and
                // re-deriving them here would be planning the job twice.
                leftOver += Remaining(partition, tour.Index);
                break;
            }
        }

        if (steps.Count == 0)
        {
            return Refuse(
                JobPlanVerdict.NothingToDo, string.Empty, request.Epoch, budget);
        }

        var plan = new JobPlan(
            JobPlanVerdict.Planned,
            ManifestArithmetic.Total(covered),
            steps,
            _snapshot.AreaRevision,
            request.Epoch,
            string.Empty);
        return new JobTourPlan(
            plan, partition.Tours, provisioning, planned, JobManifest.Empty, budget.Spent, leftOver);
    }

    /// <summary>The largest prefix of a trip's targets that what was actually
    /// drawn can pay for, in the trip's own order.
    ///
    /// Greedy and in order rather than best fit, because "he serviced the three
    /// nearest and came back for the rest" is something a player watches and
    /// understands, and "he serviced the first, the fourth and the fifth" is
    /// not.</summary>
    private static JobTour Shrink(JobTour tour, SourcePlan supply, out int deferred)
    {
        var fetched = new List<JobManifest>();
        foreach (SourceDraw draw in supply.Draws)
        {
            fetched.Add(Fetched(draw));
        }

        JobManifest have = ManifestArithmetic.Merge(fetched);
        var kept = new List<JobTarget>();
        foreach (JobTarget target in tour.Targets)
        {
            JobManifest after = ManifestArithmetic.Subtract(have, target.Needs);
            if (after.TotalUnits != have.TotalUnits - target.Needs.TotalUnits)
            {
                // Subtraction clamps at nothing, so a total that did not fall by
                // the whole of what this target needs means some of it was not
                // there. Stop: the targets after it wait for the next round.
                break;
            }

            have = after;
            kept.Add(target);
        }

        deferred = tour.Targets.Count - kept.Count;
        return new JobTour(tour.Index, kept);
    }

    /// <summary>How many targets live in the trips after this one.</summary>
    private static int Remaining(TourPartition partition, int afterTour)
    {
        int total = 0;
        foreach (JobTour tour in partition.Tours)
        {
            if (tour.Index > afterTour)
            {
                total += tour.Targets.Count;
            }
        }

        return total;
    }

    private StopSequence Order(JobTour tour, NpcPoint from)
    {
        var stops = new List<RouteStop>(tour.Targets.Count);
        foreach (JobTarget target in tour.Targets)
        {
            stops.Add(target.AsStop());
        }

        var request = new StopSequenceRequest(from, stops, false, default);
        return StopSequencer.Order(request, _setbacks, _now);
    }

    /// <summary>What the chests hold once the earlier trips have had their
    /// share. Without this, two trips drawing from one chest would each plan
    /// against the full count and the second would open an empty
    /// chest.</summary>
    private static List<SourceStock> Deplete(IReadOnlyList<SourceStock> stock, IReadOnlyList<SourceDraw> draws)
    {
        if (draws.Count == 0)
        {
            return new List<SourceStock>(stock);
        }

        var left = new List<SourceStock>(stock.Count);
        foreach (SourceStock source in stock)
        {
            JobManifest taken = TakenFrom(draws, source.Key);
            if (taken.IsEmpty)
            {
                left.Add(source);
                continue;
            }

            var lines = new List<StockLine>();
            foreach (StockLine line in source.Lines)
            {
                int units = line.Units - taken.RequiredOf(line.Item);
                if (units > 0)
                {
                    lines.Add(new StockLine(line.Item, units));
                }
            }

            left.Add(new SourceStock(source.Container, lines));
        }

        return left;
    }

    /// <summary>What one draw takes out, as a requirement - the one place stock
    /// becomes requirement, so that reconciliation can subtract what was used
    /// from what was fetched.</summary>
    private static JobManifest Fetched(SourceDraw draw)
    {
        var lines = new List<JobManifestLine>(draw.Take.Count);
        foreach (StockLine line in draw.Take)
        {
            lines.Add(new JobManifestLine(line.Item, line.Units));
        }

        return new JobManifest(lines);
    }

    private static JobManifest TakenFrom(IReadOnlyList<SourceDraw> draws, string key)
    {
        var lines = new List<JobManifestLine>();
        foreach (SourceDraw draw in draws)
        {
            if (!string.Equals(draw.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (StockLine line in draw.Take)
            {
                lines.Add(new JobManifestLine(line.Item, line.Units));
            }
        }

        return new JobManifest(lines);
    }

    private static bool TryFind(IReadOnlyList<JobTarget> targets, string key, out JobTarget found)
    {
        foreach (JobTarget target in targets)
        {
            if (string.Equals(target.Key, key, StringComparison.Ordinal))
            {
                found = target;
                return true;
            }
        }

        found = default;
        return false;
    }

    private static JobTourPlan Refuse(
        JobPlanVerdict verdict, string reason, NpcWorldEpoch epoch, PlanningBudget budget) =>
        new JobTourPlan(
            JobPlan.Refused(verdict, reason, epoch), null, null, null, JobManifest.Empty, budget.Spent);
}
