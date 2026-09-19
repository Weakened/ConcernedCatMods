using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Jobs;

/// <summary>One job, carried from looking to finished, with the role supplying
/// what and this supplying when.
///
/// <b>Why this type exists at all, said once and in full.</b> The pieces of the
/// sequence already existed and were tested: take a snapshot, total it into a
/// manifest, refuse a job nothing can provision, split it into trips, choose the
/// chests, order the walk, reserve everything before touching anything,
/// revalidate each stop on the way to it, close the books at the end. What did
/// not exist was anything that put them in that order. Three roles were about to
/// write that order three times, and the first thing three copies of a sequence
/// do is disagree about it. So the sequence is here, once, and a role hands in
/// providers and consumes steps.
///
/// <b>What a role does with one.</b> Construct it from an
/// <see cref="NpcJobOrder"/> and an <see cref="INpcJobRole"/>, then pump
/// <see cref="Next"/> on its own tick, carrying out each
/// <see cref="NpcJobProgress.Do"/> step and reporting what became of it through
/// <see cref="Done"/>, <see cref="Skipped"/> or <see cref="Failed"/>. Rounds,
/// re-planning, reservations, revalidation and reconciliation happen inside and
/// are never a role's to sequence.
///
/// <b>The verdict taxonomy, which is the thing three roles would each have got
/// wrong.</b> <c>Planned</c> is walk the steps. <c>NothingToDo</c> is finished,
/// and it is the only verdict a job may be reported finished on without doing
/// anything. <c>BudgetExhausted</c> is incomplete rather than impossible - ask
/// again next tick, and it is never a reason to stop a job, so it does not even
/// spend a round. <c>ShortOfMaterial</c>, <c>AreaInvalid</c> and <c>Refused</c>
/// stop the job, and the <see cref="Reason"/> is what a player is shown; the
/// verdict's own name is not, because a role that renders
/// <c>ShortOfMaterial</c> tells a player they are out of wood while they are
/// standing on it.
///
/// <b>The round loop keys on the verdict, not on the reconciliation.</b>
/// <see cref="JobReconciliation.HasUnfinishedWork"/> is a statement of fact and
/// is true whenever work is outstanding - including of a job that has just been
/// refused for good - so a loop written against it would plan again forever
/// against a world that is never going to answer differently. What ends a job
/// here is <see cref="JobReconciliation.IsComplete"/>, a terminal verdict, a
/// round that achieved nothing at all, or <see cref="NpcJobOrder.Rounds"/>.
///
/// <b>Both kinds of short stop the job, and only the reason tells them
/// apart.</b> <c>ShortOfMaterial</c> with a shortfall means the material is not
/// there; <c>ShortOfMaterial</c> with an empty shortfall means it is there and
/// not reachable within one round. Neither is retried unchanged, and neither is
/// a sentence a role should assemble from the verdict's name - the planner
/// already wrote one, and <see cref="Reason"/> is it.
///
/// <b>What it deliberately does not do.</b> It does not move the body: walking
/// between stops is the role's, through the motor its lease gave it. It does not
/// move items or keep custody: what was actually moved is a ledger's answer, and
/// a driver that also tracked it would be a second source of truth about custody
/// - which is why <see cref="JobReconciliation.LeftOver"/> is what the plan says
/// is surplus rather than what is measurably on his back. And it takes no
/// reservations a role can see: the books are the library's, so that two jobs
/// that want the same tree ask the same book.</summary>
public sealed class NpcJobDriver
{
    private readonly NpcJobOrder _order;
    private readonly INpcJobRole _role;
    private readonly NpcJobBooks _books;
    private readonly StepObserver _observer;
    private readonly List<StepResult> _results = new List<StepResult>();
    private readonly Dictionary<string, int> _stepOf = new Dictionary<string, int>(StringComparer.Ordinal);

    private JobTourPlan _plan;
    private RouteExecution? _route;
    private JobCommitment<JobTarget>? _targetHolds;
    private JobCommitment<INpcContainer>? _containerHolds;
    private PlannedStep _standing;
    private int _standingAt = -1;
    private bool _isStanding;
    private int _replansSpent;

    private NpcJobDriver(in NpcJobOrder order, INpcJobRole role)
    {
        _order = order;
        _role = role;
        _books = NpcJobBooks.ForWorld(order.World);
        _observer = new StepObserver(this);
        Reason = string.Empty;
    }

    /// <summary>Starts a job. Never throws for a world reason: an order that
    /// cannot be worked comes back already <see cref="NpcJobProgress.Stopped"/>
    /// with a reason, because a role that got its start-up order wrong should be
    /// told so rather than take an unrelated NPC out with it.</summary>
    public static NpcJobDriver For(in NpcJobOrder order, INpcJobRole? role)
    {
        if (role == null)
        {
            var refused = new NpcJobDriver(order, NullRole.Instance);
            refused.Stop(
                JobPlanVerdict.Refused, "a job needs a role to say what is worth doing and what doing it takes");
            return refused;
        }

        var driver = new NpcJobDriver(order, role);
        if (!order.IsValid)
        {
            driver.Stop(
                JobPlanVerdict.Refused,
                "the job was asked for without an identity, a usable job name, a work area, a loaded world, or the two words its steps are written in");
        }

        return driver;
    }

    /// <summary>What the job is called.</summary>
    public string JobId => _order.JobId;

    /// <summary>Which NPC it is for.</summary>
    public NpcIdentity Identity => _order.Identity;

    /// <summary>Where the job stands.</summary>
    public NpcJobProgress Progress { get; private set; }

    /// <summary>The planner's verdict on the most recent round.</summary>
    public JobPlanVerdict Verdict { get; private set; }

    /// <summary>Why, in words a role can render for a player. Never the name of
    /// an enum.</summary>
    public string Reason { get; private set; }

    /// <summary>How many rounds have been planned and walked. A round that never
    /// produced a plan does not count, so asking again after
    /// <see cref="NpcJobProgress.Waiting"/> costs nothing.</summary>
    public int Rounds { get; private set; }

    /// <summary>What the current round's plan was provisioned for.</summary>
    public JobManifest Manifest => _plan.Plan.Manifest;

    /// <summary>What the job is short of, if it was refused for being
    /// unprovisionable. Empty otherwise.</summary>
    public JobManifest Shortfall => _plan.Shortfall;

    /// <summary>What the last round fetched and did not use up, and what the
    /// next round may therefore spend without fetching it again.
    ///
    /// <b>Readable because a role has to put it somewhere.</b> The material is
    /// on the NPC's back and the job no longer needs it; whether it goes back in
    /// a chest, stays for the next round or is handed over is the role's
    /// decision and its custody layer's job. This says what it is.</summary>
    public JobManifest Carrying { get; private set; }

    /// <summary>How many targets of the job the current plan does not reach at
    /// all, because the job needs more trips than one plan writes out.
    ///
    /// <b>Readable because zero is a claim, not a default.</b> A count of
    /// perfectly executed steps says nothing about work the plan left out, and a
    /// job reported finished on the first is the most expensive kind of
    /// wrong.</summary>
    public int LeftForAnotherRound => _plan.LeftForAnotherRound;

    /// <summary>The steps of the current round, in order, with their subjects
    /// attached. Empty between rounds.</summary>
    public IReadOnlyList<PlannedStep> Steps => _plan.Steps;

    /// <summary>What the last round's books came to.</summary>
    public JobReconciliation LastRound { get; private set; }

    /// <summary>What the last look actually did, beside what it found. The
    /// evidence behind a job that reports nothing to do - and the only way to
    /// tell an area that is empty from one that is used up, which are different
    /// sentences for a player.</summary>
    public AreaScanReport LastLook { get; private set; }

    /// <summary>What to do next.
    ///
    /// Asking while a step is outstanding answers with that same step: one body
    /// has one destination, and a second answer would be a second one.</summary>
    /// <param name="standingAt">Where the body is now, so a round that has to be
    /// planned is ordered from where he actually is.</param>
    public NpcJobAdvance Next(NpcPoint standingAt)
    {
        if (Progress == NpcJobProgress.Finished || Progress == NpcJobProgress.Stopped)
        {
            return Answer();
        }

        if (_isStanding)
        {
            return new NpcJobAdvance(NpcJobProgress.Do, _standing, true, Verdict, string.Empty);
        }

        while (true)
        {
            if (_route == null && !StartRound(standingAt))
            {
                return Answer();
            }

            if (_plan.Plan.IsStale(_order.Area!.Revision, _order.World))
            {
                // The work area moved or the world reloaded under this plan.
                // Plan again rather than walk it - but capped, because the world
                // can hold "your plan is stale" true forever and an NPC that
                // answered by planning again would stand still, thinking, for
                // good. A stale plan is not held against the round the way a
                // round that achieved nothing is: nothing was wrong with it.
                bool mayPlanAgain = _route!.RequestReplan();
                _replansSpent = _route.Replans;
                EndRound();

                if (!mayPlanAgain)
                {
                    Stop(
                        Verdict,
                        "the work area kept changing under this job, so it stopped rather than plan against it again");
                    return Answer();
                }

                if (Rounds >= _order.Rounds)
                {
                    Stop(Verdict, "the job took more rounds than it is allowed and still has work left");
                    return Answer();
                }

                continue;
            }

            RouteAdvance advance = _route!.Next(_observer);
            if (advance.Progress == RouteProgress.Go)
            {
                _standingAt = _stepOf[advance.Stop.Key];
                _standing = _plan.Steps[_standingAt];
                _isStanding = true;
                return new NpcJobAdvance(NpcJobProgress.Do, _standing, true, Verdict, string.Empty);
            }

            _replansSpent = _route.Replans;
            EndRound();
            if (!ShouldPlanAgain(advance.Reason))
            {
                return Answer();
            }
        }
    }

    /// <summary>The step was carried out.</summary>
    public void Done() => Report(StepOutcome.Done, serviced: true);

    /// <summary>The step turned out not to be worth doing on arrival - somebody
    /// else had done it, or it was gone. <b>The material stays carried</b>, and
    /// nothing is owed for it: that is what makes this different from
    /// <see cref="Failed"/>.</summary>
    public void Skipped() => Report(StepOutcome.Skipped, serviced: false);

    /// <summary>The step was reached and did not work. Still owed, so the next
    /// round plans for it again.</summary>
    public void Failed() => Report(StepOutcome.Failed, serviced: false);

    /// <summary>Gives the job up: the player countermanded it, the NPC died, the
    /// world is going away. Every hold is given back, exactly once, so this is
    /// safe to call unconditionally and from more than one place.</summary>
    public void Abandon()
    {
        if (Progress == NpcJobProgress.Finished || Progress == NpcJobProgress.Stopped)
        {
            Release();
            return;
        }

        EndRound();
        Stop(Verdict, "the job was given up");
    }

    private bool StartRound(NpcPoint from)
    {
        if (!_order.IsValid)
        {
            Stop(JobPlanVerdict.Refused, "the job cannot be worked as it was ordered");
            return false;
        }

        _results.Clear();
        _stepOf.Clear();

        var budget = new PlanningBudget(_order.Allowance);
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            _order.Area,
            _order.World,
            _role.Candidates(_order.World),
            _role.Sources(_order.World),
            // The role's own observer, asked about the role's own targets under
            // the role's own names. The driver's dispatching one belongs to a
            // round and there is no round yet - and looking is where a role's
            // completion condition decides which candidates are worth planning
            // for at all.
            _role,
            _role.Probe,
            budget);
        LastLook = snapshot.Report;

        var planner = new TourJobPlanner(
            snapshot,
            _order.Capacity,
            _order.Actions,
            setbacks: null,
            now: 0f,
            allowance: _order.Allowance,
            availability: _role.Availability);

        // The carry is threaded here and nowhere else. A round that could not
        // fully provision a trip leaves the surplus on his back, and the round
        // after it would otherwise count chests only - fetching a second load
        // of what he is already holding, and able to answer ShortOfMaterial
        // about material he is visibly carrying. The driver is the only thing
        // that knows the answer, because it is the only thing that saw the last
        // round's books.
        var request = new JobPlanRequest(
            _order.Identity, _order.JobId, _order.Area, _order.World, _order.Ceiling, from, Carrying);

        _plan = planner.PlanTours(request);
        Verdict = _plan.Plan.Verdict;
        Reason = _plan.Plan.Reason;

        if (!_plan.IsActionable)
        {
            switch (Verdict)
            {
                case JobPlanVerdict.NothingToDo:
                    Finish();
                    return false;

                case JobPlanVerdict.BudgetExhausted:
                    // Incomplete, not impossible, and deliberately not counted
                    // as a round: a look that ran out of what it was allowed to
                    // spend is the ordinary cost of a big base, and a job that
                    // spent its rounds on them would stop for no reason at all.
                    Progress = NpcJobProgress.Waiting;
                    return false;

                default:
                    Stop(Verdict, Reason);
                    return false;
            }
        }

        _targetHolds = new JobCommitment<JobTarget>(_order.JobId, _books.Targets);
        _containerHolds = new JobCommitment<INpcContainer>(_order.JobId, _books.Containers);
        JobReservationResult holds = PlanReservations.TakeOut(_plan, _targetHolds, _containerHolds);
        if (!holds.AllHeld)
        {
            // Everything it had taken is already back - all of it or none of it
            // is the reservation contract - so there is nothing to clean up and
            // nothing to stop the job for. Somebody else has a tree this plan
            // wanted, and they may not have it next tick.
            Release();
            Progress = NpcJobProgress.Waiting;
            Reason = "something this round needed is held by another job";
            return false;
        }

        Rounds++;
        _route = new RouteExecution(Sequence(), RouteExecutionLimits.Default, _replansSpent);
        return true;
    }

    private void EndRound()
    {
        if (_route == null && _plan.Steps.Count == 0)
        {
            Release();
            return;
        }

        LastRound = JobReconciler.Reconcile(_plan, _results);
        Carrying = LastRound.LeftOver;
        Release();
        _route = null;
        _isStanding = false;
        _standingAt = -1;
    }

    private bool ShouldPlanAgain(string reason)
    {
        if (LastRound.IsComplete)
        {
            Finish();
            return false;
        }

        if (LastRound.Done == 0 && LastRound.Skipped == 0)
        {
            // Nothing was serviced and nothing was even given up on: this round
            // achieved literally nothing, and a round that achieves nothing
            // achieves nothing again.
            Stop(
                Verdict,
                reason.Length != 0
                    ? reason
                    : "a whole round went by without anything being done, so planning it again would give the same round");
            return false;
        }

        if (Rounds >= _order.Rounds)
        {
            Stop(Verdict, "the job took more rounds than it is allowed and still has work left");
            return false;
        }

        return true;
    }

    private void Report(StepOutcome outcome, bool serviced)
    {
        if (!_isStanding || _route == null)
        {
            return;
        }

        Record(_standingAt, outcome);
        _isStanding = false;
        _standingAt = -1;

        if (serviced)
        {
            _route.Arrived();
            return;
        }

        _route.Abandoned();
    }

    private StopSequence Sequence()
    {
        var stops = new List<RouteStop>(_plan.Steps.Count);
        for (int index = 0; index < _plan.Steps.Count; index++)
        {
            // The stop's name here is a position in this round and nothing else.
            // It is not the role's token for anything and never reaches the
            // role: what a role is handed is the PlannedStep, which carries its
            // own words. A position is used because two steps of one round can
            // legitimately be about the same chest, and stops are the same stop
            // when they have the same name.
            string key = index.ToString(CultureInfo.InvariantCulture);
            _stepOf[key] = index;
            stops.Add(new RouteStop(key, _plan.Steps[index].Step.At, _plan.Steps[index].Target.Priority, true));
        }

        return new StopSequence(StopSequenceOutcome.Ordered, stops, null, 0f, false, false);
    }

    /// <summary>Whether one stop of the round is still worth going to, asked
    /// again immediately before going there.
    ///
    /// <b>Two different questions, because a chest and a tree are not asked the
    /// same thing.</b> A target is asked the role's own completion condition,
    /// which is the one place a role's meaning enters. A container is not asked
    /// whether it is "done" - it is asked whether it may be used right now,
    /// which is what its own contract requires to be re-read at the moment of
    /// use rather than cached into a decision taken a minute ago. A chest that
    /// refuses is <see cref="StopStatus.Refused"/>, which is skipped like any
    /// other and named apart so the sentence a player reads can tell them what
    /// to change.</summary>
    private StopStatus Look(in RouteStop stop)
    {
        if (!_stepOf.TryGetValue(stop.Key, out int index) || index >= _plan.Steps.Count)
        {
            return StopStatus.Unreadable;
        }

        PlannedStep step = _plan.Steps[index];
        StopStatus status = step.IsCollect
            ? (step.Source.IsUsable ? StopStatus.Actionable : StopStatus.Refused)
            : _role.Observe(step.Target.AsStop());

        if (RouteExecution.Decide(status) == StopDisposition.Skip)
        {
            // Skipped, not owed: somebody else did it, or it is gone, or it
            // refuses. A deferred stop records nothing, which reconciliation
            // reads as never reached - and never reached is still owed, which is
            // exactly right for a stop that is still wanted.
            Record(index, StepOutcome.Skipped);
        }

        return status;
    }

    private void Record(int index, StepOutcome outcome)
    {
        if (index < 0 || index >= _plan.Steps.Count)
        {
            return;
        }

        _results.Add(new StepResult(_plan.Steps[index].Step.Index, outcome));
    }

    private void Release()
    {
        _targetHolds?.Cancel();
        _containerHolds?.Cancel();
        _targetHolds = null;
        _containerHolds = null;
    }

    private void Finish()
    {
        Release();
        Progress = NpcJobProgress.Finished;
        Reason = string.Empty;
        _route = null;
        _isStanding = false;
        _standingAt = -1;
    }

    private void Stop(JobPlanVerdict verdict, string reason)
    {
        Release();
        Verdict = verdict;
        Progress = NpcJobProgress.Stopped;
        Reason = reason ?? string.Empty;
        _route = null;
        _isStanding = false;
        _standingAt = -1;
    }

    private NpcJobAdvance Answer() =>
        new NpcJobAdvance(Progress, default, false, Verdict, Reason);

    /// <summary>The driver's own revalidation, handed to the round in place of
    /// the role's. It dispatches to the role for a target and to the container's
    /// own access for a chest, and records the skip as it goes so that the
    /// reconciliation at the end knows which steps were passed over and which
    /// were never reached.</summary>
    private sealed class StepObserver : IStopObserver
    {
        private readonly NpcJobDriver _driver;

        internal StepObserver(NpcJobDriver driver)
        {
            _driver = driver;
        }

        public StopStatus Observe(in RouteStop stop) => _driver.Look(stop);
    }

    /// <summary>Stands in for a role that was never supplied, so a refused
    /// driver is an ordinary object rather than one that throws on every
    /// member.</summary>
    private sealed class NullRole : INpcJobRole
    {
        internal static NullRole Instance { get; } = new NullRole();

        public INpcAreaProbe? Probe => null;

        public INpcSourceAvailability? Availability => null;

        public IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world) => Array.Empty<JobTarget>();

        public IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world) => Array.Empty<SourceStock>();

        public StopStatus Observe(in RouteStop stop) => StopStatus.Unreadable;
    }
}
