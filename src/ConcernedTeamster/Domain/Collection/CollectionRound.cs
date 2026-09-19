using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why a collection job stopped. Zero means it has not stopped.
/// </summary>
internal enum CollectionStopReason
{
    /// <summary>Still running.</summary>
    Unspecified = 0,

    /// <summary>The work area is clear and the looking finished. The only value
    /// that means the job is done.</summary>
    AreaCleared = 1,

    /// <summary>The round ceiling was reached with work still outstanding. Not
    /// a failure: a very large job, worked as far as one order goes.</summary>
    RoundCeilingReached = 2,

    /// <summary>The work area could not be resolved - no provider, an unreadable
    /// shape, a deleted designation. Refused; never replaced by a radius nobody
    /// asked for.</summary>
    AreaUnavailable = 3,

    /// <summary>Nothing in the area could be reached on foot after bounded
    /// local recovery.
    ///
    /// <b>This is where a portal would go, and does not.</b> He does not
    /// teleport, with or without a cart, and never to recover a stuck route. He
    /// tries what is local, and then he stands still and says so.</summary>
    NoRouteOnFoot = 4,

    /// <summary>The destination container refused, or could not be reached, so
    /// he is holding what he gathered rather than putting it somewhere else.
    /// </summary>
    DestinationUnavailable = 5,

    /// <summary>He has no room and cannot make any, because the destination is
    /// not reachable either.</summary>
    CannotCarryAnyMore = 6,

    /// <summary>The assigned cart was destroyed. The haul stops where it is,
    /// the accounting is preserved, and nothing replaces what it held.</summary>
    CartDestroyed = 7,

    /// <summary>Authority, the world or the body went away under him.</summary>
    Interrupted = 8,

    /// <summary>The job was cancelled by the player.</summary>
    Cancelled = 9,
}

/// <summary>What happened at one stop.</summary>
internal enum StopOutcome
{
    /// <summary>Nobody said. Treated as an interruption, because a step that
    /// cannot say what it did is not a step that can be assumed to have worked.
    /// </summary>
    Unspecified = 0,

    /// <summary>He took it, and the material is on his back.</summary>
    Taken = 1,

    /// <summary>It is not there any more. Skipped, and it does not count
    /// against him.</summary>
    Gone = 2,

    /// <summary>He could not get to it after bounded local recovery. Skipped,
    /// counted, and the round reports it.</summary>
    Unreachable = 3,

    /// <summary>The place refused when he got there - a ward, an owner, a
    /// location. Skipped and counted.</summary>
    Refused = 4,

    /// <summary>Something ended the round: authority, the body, the world, the
    /// player.</summary>
    Interrupted = 5,
}

/// <summary>What one stop did, and where what he took ended up.</summary>
internal readonly struct StopResult
{
    public StopResult(StopOutcome outcome, CargoPlace landed = CargoPlace.Carried)
    {
        Outcome = outcome;
        Landed = landed;
    }

    public StopOutcome Outcome { get; }

    /// <summary>Where the material physically is now: on his back, or stowed in
    /// the assigned cart. Asked rather than assumed, because a cart is the only
    /// reason a batch can be bigger than he is, and a ledger that recorded
    /// everything as carried would have nothing to account for when the cart is
    /// destroyed.</summary>
    public CargoPlace Landed { get; }

    public static StopResult Took(CargoPlace landed = CargoPlace.Carried) =>
        new StopResult(StopOutcome.Taken, landed);

    public static StopResult Did(StopOutcome outcome) => new StopResult(outcome);
}

/// <summary>What a deposit did.</summary>
internal enum DepositOutcome
{
    Unspecified = 0,

    /// <summary>Everything he carried went in.</summary>
    Deposited = 1,

    /// <summary>Some of it went in and the rest stays with him. Never deleted.
    /// </summary>
    PartlyDeposited = 2,

    /// <summary>The container refused, or he could not get to it. He keeps what
    /// he has.</summary>
    Refused = 3,

    /// <summary>Whether it went in could not be established. Nothing is written
    /// off and nothing is compensated; the job stops and a person looks.
    /// </summary>
    Uncertain = 4,
}

/// <summary>What one deposit actually moved, measured on both sides.</summary>
internal readonly struct DepositResult
{
    public DepositResult(DepositOutcome outcome, IReadOnlyList<KeyValuePair<string, int>>? moved, string detail = "")
    {
        Outcome = outcome;
        Moved = moved ?? Array.Empty<KeyValuePair<string, int>>();
        Detail = detail ?? string.Empty;
    }

    public DepositOutcome Outcome { get; }

    /// <summary>Per item, how many units were <b>observed</b> to arrive - not
    /// how many were intended to.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Moved { get; }

    public string Detail { get; }
}

/// <summary>Everything the round loop needs the world to do for it. Every method
/// is one question or one action, and none of them plans anything.</summary>
internal interface ICollectionWorker
{
    /// <summary>Where he is standing right now.</summary>
    CollectionPoint Where { get; }

    /// <summary>Look at the work area. Bounded, and it says how the looking
    /// went.</summary>
    CollectionSurvey Survey();

    /// <summary>What the game says he can carry, read fresh.</summary>
    CarryFacts ReadCarry();

    /// <summary>How much room the assigned cart has, or
    /// <see cref="CartCapacity.None"/>.</summary>
    CartCapacity ReadCart();

    /// <summary>Walk to one stop and take it, saying where what he took ended
    /// up. Bounded local recovery lives here; a route that cannot be found
    /// answers <see cref="StopOutcome.Unreachable"/> rather than reaching for
    /// anything that would move him instantly.</summary>
    StopResult Go(CollectionStop stop);

    /// <summary>Travel once to the destination and put everything in - what he
    /// is carrying and what the cart holds. Called once per batch, never once
    /// per item.</summary>
    DepositResult DepositAll(IReadOnlyList<KeyValuePair<string, int>> carried);

    /// <summary>Whether the assigned cart still exists. Asked every round, so a
    /// cart destroyed between batches ends the job rather than being discovered
    /// at the next attach.</summary>
    bool CartStillExists();
}

/// <summary>What one job did.</summary>
internal sealed class RoundReport
{
    internal RoundReport(
        int roundsRun,
        int stopsServiced,
        int stopsSkipped,
        int leftForAnotherRound,
        CollectionStopReason stopped,
        string detail,
        CargoLedger ledger)
    {
        RoundsRun = roundsRun;
        StopsServiced = stopsServiced;
        StopsSkipped = stopsSkipped;
        LeftForAnotherRound = leftForAnotherRound > 0 ? leftForAnotherRound : 0;
        Stopped = stopped;
        Detail = detail ?? string.Empty;
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public int RoundsRun { get; }

    public int StopsServiced { get; }

    /// <summary>Stops he walked to and did not service - gone, refused or out
    /// of reach. Counted, because a round of nothing but skips is not a round
    /// that cleared anything.</summary>
    public int StopsSkipped { get; }

    /// <summary>How much of the job the last plan did not cover.</summary>
    public int LeftForAnotherRound { get; }

    /// <summary>Whether the job wants another order. <b>True unless the area was
    /// actually cleared and the looking finished.</b> Derived here, once, so
    /// nothing else has to remember that "every step came off" and "the job is
    /// finished" are different questions.</summary>
    public bool NeedsAnotherRound => Stopped != CollectionStopReason.AreaCleared;

    public CollectionStopReason Stopped { get; }

    public string Detail { get; }

    /// <summary>The accounting. Survives every stop reason, including a
    /// destroyed cart.</summary>
    public CargoLedger Ledger { get; }
}

/// <summary>The round loop: survey, plan a batch, collect along the route,
/// travel once, deposit, and then ask again (#381).
///
/// <b>This is the thing that reads what a plan did not cover.</b> A batch
/// planner that honestly reports the work it left behind is only half a fix -
/// the other half is somebody acting on it, and without that a large job stops
/// after one round and looks, from the player's side, exactly like the bug the
/// honest report was supposed to fix. So this loop's continuation condition is
/// <see cref="CollectionBatchPlan.LeftForAnotherRound"/> and the survey's own
/// outcome, and the only way out with
/// <see cref="CollectionStopReason.AreaCleared"/> is a finished look that found
/// nothing left to take.
///
/// <b>Bounded, and it ends.</b> Every round must make progress or the loop stops
/// and says so: a round that services nothing, twice running, is a world holding
/// "everything is gone" true, and an NPC who answered that by planning again
/// would stand there thinking for as long as the player watched.
///
/// <b>No portal, anywhere, for any reason.</b> There is no branch in this type
/// that moves him other than by asking the worker to walk. A stop he cannot
/// reach is skipped; a job where nothing can be reached ends in
/// <see cref="CollectionStopReason.NoRouteOnFoot"/> with a reason, and stays
/// there.</summary>
internal static class CollectionRound
{
    /// <summary>How many rounds in a row may service nothing before the loop
    /// gives up. Two: one is a normal race (the player took the last stone
    /// while he walked), and a second in a row is a world that is not going to
    /// change by being asked again.</summary>
    internal const int BarrenRoundsBeforeStopping = 2;

    public static RoundReport Run(ICollectionWorker worker, CollectionLimits limits, Func<bool>? cancelled = null)
    {
        if (worker == null)
        {
            throw new ArgumentNullException(nameof(worker));
        }

        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        limits.Validate();

        var ledger = new CargoLedger();
        int rounds = 0;
        int serviced = 0;
        int skipped = 0;
        int left = 0;
        int barren = 0;

        while (rounds < limits.MostRoundsPerJob)
        {
            if (cancelled != null && cancelled())
            {
                return Report(rounds, serviced, skipped, left, CollectionStopReason.Cancelled,
                    "the player cancelled the order", ledger);
            }

            rounds++;

            CollectionSurvey survey = worker.Survey();
            if (survey.Outcome == SurveyOutcome.AreaUnavailable || survey.Outcome == SurveyOutcome.Unspecified)
            {
                return Report(rounds, serviced, skipped, left, CollectionStopReason.AreaUnavailable,
                    survey.Detail.Length > 0 ? survey.Detail : "the work area could not be read", ledger);
            }

            CarryBudget budget = CarryBudget.From(worker.ReadCarry(), limits);
            CartCapacity cart = worker.ReadCart();
            CollectionBatchPlan plan = BatchPlanner.Plan(survey, budget, cart, limits, worker.Where);
            left = plan.LeftForAnotherRound;

            if (!plan.HasWork)
            {
                switch (plan.Outcome)
                {
                    case BatchOutcome.NothingToDo:
                        return Report(rounds, serviced, skipped, 0, CollectionStopReason.AreaCleared,
                            "the work area is clear", ledger);
                    case BatchOutcome.CapacityReached:
                        // He can see work and cannot lift any of it. Going to
                        // the chest is the only thing that changes that, so it
                        // is tried once and the answer decides.
                        DepositResult relief = Unload(worker, ledger, rounds);
                        if (relief.Outcome == DepositOutcome.Deposited ||
                            relief.Outcome == DepositOutcome.PartlyDeposited)
                        {
                            continue;
                        }

                        return Report(rounds, serviced, skipped, left,
                            CollectionStopReason.CannotCarryAnyMore,
                            "he cannot carry any more and the destination did not take it: " + relief.Detail,
                            ledger);
                    default:
                        return Report(rounds, serviced, skipped, left, CollectionStopReason.AreaUnavailable,
                            "the area could not be established well enough to plan a batch", ledger);
                }
            }

            int servicedThisRound = 0;
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                CollectionStop stop = plan.Stops[index];
                if (cancelled != null && cancelled())
                {
                    Unload(worker, ledger, rounds);
                    return Report(rounds, serviced, skipped, left, CollectionStopReason.Cancelled,
                        "the player cancelled the order", ledger);
                }

                StopResult result = worker.Go(stop);
                switch (result.Outcome)
                {
                    case StopOutcome.Taken:
                        string step = StepName(rounds, stop.Order, "take");
                        ledger.Take(step, stop.Candidate.ItemPrefab, stop.Candidate.Units);
                        if (result.Landed == CargoPlace.InCart)
                        {
                            // It went straight into the cart. Recorded as a move
                            // rather than as a different kind of take, so the
                            // total never depends on where it landed.
                            ledger.Move(
                                StepName(rounds, stop.Order, "stow"),
                                stop.Candidate.ItemPrefab,
                                stop.Candidate.Units,
                                CargoPlace.Carried,
                                CargoPlace.InCart);
                        }

                        serviced++;
                        servicedThisRound++;
                        break;
                    case StopOutcome.Gone:
                    case StopOutcome.Refused:
                    case StopOutcome.Unreachable:
                        skipped++;
                        break;
                    default:
                        Unload(worker, ledger, rounds);
                        return Report(rounds, serviced, skipped, left, CollectionStopReason.Interrupted,
                            "the round was interrupted at stop " + stop.Candidate.Key, ledger);
                }

                if (cart.Assigned && !worker.CartStillExists())
                {
                    ledger.CartDestroyed(StepName(rounds, stop.Order, "cart-destroyed"));
                    return Report(rounds, serviced, skipped, left, CollectionStopReason.CartDestroyed,
                        "the assigned cart was destroyed; what it held is on the ground where it stood", ledger);
                }
            }

            DepositResult deposit = Unload(worker, ledger, rounds);
            if (deposit.Outcome == DepositOutcome.Uncertain)
            {
                return Report(rounds, serviced, skipped, left, CollectionStopReason.DestinationUnavailable,
                    "whether the last load went into the container could not be established: " + deposit.Detail,
                    ledger);
            }

            if (servicedThisRound == 0)
            {
                barren++;
                if (barren >= BarrenRoundsBeforeStopping)
                {
                    return Report(rounds, serviced, skipped, left,
                        plan.Stops.Count > 0 ? CollectionStopReason.NoRouteOnFoot : CollectionStopReason.AreaUnavailable,
                        "two rounds in a row reached nothing; he walks and does not teleport, so he stops here",
                        ledger);
                }
            }
            else
            {
                barren = 0;
            }

            if (deposit.Outcome == DepositOutcome.Refused)
            {
                return Report(rounds, serviced, skipped, left, CollectionStopReason.DestinationUnavailable,
                    "the destination would not take it, so he is holding it: " + deposit.Detail, ledger);
            }

            if (left == 0 && survey.Outcome == SurveyOutcome.Finished)
            {
                // Every takeable thing the finished look found is now serviced
                // or accounted for. One more look decides whether that is the
                // end, because a take can uncover something the first look
                // could not see past.
                continue;
            }
        }

        return Report(rounds, serviced, skipped, left, CollectionStopReason.RoundCeilingReached,
            "he worked " + rounds + " rounds and there is still more to do", ledger);
    }

    private static DepositResult Unload(ICollectionWorker worker, CargoLedger ledger, int round)
    {
        IReadOnlyList<KeyValuePair<string, int>> load = Combine(
            ledger.Holding(CargoPlace.Carried), ledger.Holding(CargoPlace.InCart));
        if (load.Count == 0)
        {
            return new DepositResult(DepositOutcome.Deposited, load);
        }

        DepositResult result = worker.DepositAll(load);
        if (result.Outcome == DepositOutcome.Refused || result.Outcome == DepositOutcome.Uncertain)
        {
            // Nothing moves in the ledger. Refused means he still has it;
            // uncertain means nobody knows, and writing either side of that
            // down as fact is how material is lost or invented.
            return result;
        }

        for (int index = 0; index < result.Moved.Count; index++)
        {
            KeyValuePair<string, int> entry = result.Moved[index];
            if (entry.Value <= 0)
            {
                continue;
            }

            // Off his back first, then out of the cart. A deposit that measured
            // more than either place holds moves what it can and leaves the
            // discrepancy visible, because inventing the difference is the one
            // thing this ledger exists to make impossible.
            int fromBack = Least(entry.Value, ledger.At(entry.Key, CargoPlace.Carried));
            if (fromBack > 0)
            {
                ledger.Move(
                    StepName(round, index, "deposit"), entry.Key, fromBack,
                    CargoPlace.Carried, CargoPlace.Delivered);
            }

            int fromCart = Least(entry.Value - fromBack, ledger.At(entry.Key, CargoPlace.InCart));
            if (fromCart > 0)
            {
                ledger.Move(
                    StepName(round, index, "unload"), entry.Key, fromCart,
                    CargoPlace.InCart, CargoPlace.Delivered);
            }
        }

        return result;
    }

    private static int Least(int left, int right) => left < right ? left : right;

    private static IReadOnlyList<KeyValuePair<string, int>> Combine(
        IReadOnlyList<KeyValuePair<string, int>> back,
        IReadOnlyList<KeyValuePair<string, int>> cart)
    {
        if (cart.Count == 0)
        {
            return back;
        }

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < back.Count; index++)
        {
            totals[back[index].Key] = back[index].Value;
        }

        for (int index = 0; index < cart.Count; index++)
        {
            totals.TryGetValue(cart[index].Key, out int already);
            totals[cart[index].Key] = already + cart[index].Value;
        }

        var combined = new List<KeyValuePair<string, int>>(totals.Count);
        foreach (KeyValuePair<string, int> entry in totals)
        {
            combined.Add(entry);
        }

        combined.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return combined;
    }

    /// <summary>A step's name: the round, the position within it and what it
    /// was. Stable for a retry within one world load, and meaningless outside
    /// it, which is the point - nothing here is ever written down.</summary>
    private static string StepName(int round, int order, string what) =>
        "r" + round + "#" + order + ":" + what;

    private static RoundReport Report(
        int rounds, int serviced, int skipped, int left, CollectionStopReason stopped, string detail,
        CargoLedger ledger) =>
        new RoundReport(rounds, serviced, skipped, left, stopped, detail, ledger);
}
