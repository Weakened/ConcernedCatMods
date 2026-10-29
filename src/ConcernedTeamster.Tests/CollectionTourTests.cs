using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's collection tour (#381): a load is walked whole, then taken
/// to the chest once, then the next load. These tests count trips to the chest,
/// because "never one branch, chest, one stone, chest" is a count.</summary>
public sealed class CollectionTourTests
{
    private static readonly CollectionPoint Chest = new CollectionPoint(0f, 0f, 0f);
    private static readonly CollectionTourLimits Limits = new CollectionTourLimits(2f, 10f, 100);

    /// <summary>A scripted driver: hands out the given steps in order, then
    /// Finished (or the given end).</summary>
    private sealed class ScriptPump : ITourPump
    {
        private readonly Queue<TourStep> _steps;
        private readonly TourProgress _end;
        private readonly string _endReason;
        private bool _outstanding;

        public ScriptPump(IEnumerable<TourStep> steps, TourProgress end = TourProgress.Finished, string endReason = "")
        {
            _steps = new Queue<TourStep>(steps);
            _end = end;
            _endReason = endReason;
        }

        public List<string> Log { get; } = new List<string>();

        public int Abandoned { get; private set; }

        public TourAdvance Next(CollectionPoint standingAt)
        {
            if (_outstanding)
            {
                // One body has one destination: asking again before reporting
                // answers with the same step, exactly as the real driver does.
                return new TourAdvance(TourProgress.Do, _steps.Peek(), true, string.Empty);
            }

            if (_steps.Count == 0)
            {
                return new TourAdvance(_end, default, false, _endReason);
            }

            _outstanding = true;
            return new TourAdvance(TourProgress.Do, _steps.Peek(), true, string.Empty);
        }

        public void Done() => Report("done");

        public void Skipped() => Report("skipped");

        public void Failed() => Report("failed");

        public void Abandon() => Abandoned++;

        private void Report(string what)
        {
            Assert.True(_outstanding, "reported a step that was never handed out");
            Log.Add(what + ":" + _steps.Dequeue().Key);
            _outstanding = false;
        }
    }

    private static TourStep Stop(string key, int load, float x, int round = 1) =>
        new TourStep(key, round, load, new CollectionPoint(x, 0f, 0f));

    /// <summary>Runs a tour to its end with Gunnar teleported - in the test
    /// only - to wherever he is told to walk, every pick taking one unit and
    /// every deposit emptying him. Returns the sequence of orders.</summary>
    private static List<string> Run(CollectionTour tour, Func<string, TourPickEnd>? pick = null, Func<TourDepositEnd>? deposit = null)
    {
        var orders = new List<string>();
        CollectionPoint at = new CollectionPoint(0f, 0f, 0f);
        float now = 0f;
        for (int tick = 0; tick < 1000 && !tour.IsOver; tick++)
        {
            now += 0.05f;
            TourInstruction instruction = tour.Tick(at, now);
            switch (instruction.Order)
            {
                case TourOrder.Walk:
                    at = instruction.Target;
                    break;
                case TourOrder.Pick:
                    orders.Add("pick " + instruction.Key);
                    TourPickEnd end = pick?.Invoke(instruction.Key) ?? TourPickEnd.Took;
                    tour.PickEnded(end, end == TourPickEnd.Took ? 1 : 0);
                    break;
                case TourOrder.Deposit:
                    orders.Add("deposit");
                    tour.DepositEnded(deposit?.Invoke() ?? TourDepositEnd.Emptied, string.Empty);
                    break;
            }
        }

        return orders;
    }

    [Fact]
    public void A_load_is_walked_whole_and_taken_to_the_chest_once()
    {
        var pump = new ScriptPump(new[]
        {
            Stop("a", 0, 10f), Stop("b", 0, 12f), Stop("c", 0, 14f),
            Stop("d", 1, 30f), Stop("e", 1, 32f), Stop("f", 1, 34f),
        });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        List<string> orders = Run(tour);

        Assert.Equal(
            new[] { "pick a", "pick b", "pick c", "deposit", "pick d", "pick e", "pick f", "deposit" },
            orders.ToArray());
        Assert.Equal(TourPhase.Finished, tour.Phase);
        Assert.Equal(2, tour.Deposits);
        Assert.Equal(2, tour.Loads);
        Assert.Equal(6, tour.Picks);
    }

    [Fact]
    public void There_are_never_more_trips_to_the_chest_than_loads()
    {
        // The property, over many shapes of job: trips to the chest never
        // exceed the number of loads, and never approach one per source when a
        // load holds more than one.
        for (int loads = 1; loads <= 4; loads++)
        for (int perLoad = 1; perLoad <= 5; perLoad++)
        {
            var steps = new List<TourStep>();
            for (int load = 0; load < loads; load++)
            for (int index = 0; index < perLoad; index++)
            {
                steps.Add(Stop("s" + load + "-" + index, load, 5f + (load * 10f) + index));
            }

            var tour = new CollectionTour(new ScriptPump(steps), Limits, Chest, startWithDeposit: false);
            List<string> orders = Run(tour);

            Assert.Equal(TourPhase.Finished, tour.Phase);
            Assert.Equal(loads, tour.Deposits);
            Assert.Equal(loads * perLoad, tour.Picks);
            Assert.Equal(loads, orders.FindAll(o => o == "deposit").Count);
        }
    }

    [Fact]
    public void A_new_round_starting_again_at_load_zero_is_still_a_new_load()
    {
        var pump = new ScriptPump(new[]
        {
            Stop("a", 0, 10f, round: 1), Stop("b", 0, 12f, round: 1),
            Stop("c", 0, 30f, round: 2), Stop("d", 0, 32f, round: 2),
        });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        List<string> orders = Run(tour);

        Assert.Equal(new[] { "pick a", "pick b", "deposit", "pick c", "pick d", "deposit" }, orders.ToArray());
    }

    [Fact]
    public void A_load_that_collected_nothing_costs_no_walk_to_the_chest()
    {
        var pump = new ScriptPump(new[] { Stop("gone1", 0, 10f), Stop("gone2", 0, 12f), Stop("x", 1, 20f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        List<string> orders = Run(tour, key => key.StartsWith("gone", StringComparison.Ordinal) ? TourPickEnd.Refused : TourPickEnd.Took);

        Assert.Equal(new[] { "pick gone1", "pick gone2", "pick x", "deposit" }, orders.ToArray());
        Assert.Equal(new[] { "skipped:gone1", "skipped:gone2", "done:x" }, pump.Log.ToArray());
    }

    [Fact]
    public void A_job_that_finishes_with_nothing_collected_never_walks_to_the_chest()
    {
        var tour = new CollectionTour(new ScriptPump(Array.Empty<TourStep>()), Limits, Chest, startWithDeposit: false);
        Assert.Empty(Run(tour));
        Assert.Equal(TourPhase.Finished, tour.Phase);
        Assert.Equal(0, tour.Deposits);
    }

    [Fact]
    public void What_he_already_carries_is_emptied_before_the_first_load()
    {
        var tour = new CollectionTour(new ScriptPump(new[] { Stop("a", 0, 10f) }), Limits, Chest, startWithDeposit: true);
        Assert.Equal(new[] { "deposit", "pick a", "deposit" }, Run(tour).ToArray());
    }

    [Fact]
    public void A_pick_that_measured_nothing_is_owed_rather_than_done()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        Run(tour, _ => TourPickEnd.NothingReached);
        Assert.Equal(new[] { "failed:a" }, pump.Log.ToArray());
        Assert.Equal(0, tour.Deposits);
    }

    [Fact]
    public void A_full_chest_stops_the_tour_with_the_rest_on_him_and_does_not_keep_walking_loads()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f), Stop("b", 1, 20f), Stop("c", 2, 30f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        List<string> orders = Run(tour, deposit: () => TourDepositEnd.ChestFull);

        Assert.Equal(new[] { "pick a", "deposit" }, orders.ToArray());
        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains("full", tour.Reason, StringComparison.Ordinal);
        Assert.Equal(1, pump.Abandoned);
    }

    [Fact]
    public void A_refused_deposit_stops_and_is_never_retried() =>
        AssertStopsOnce(TourDepositEnd.Refused, "refused");

    [Fact]
    public void An_uncertain_deposit_stops_and_is_never_retried() =>
        AssertStopsOnce(TourDepositEnd.Uncertain, "could not be established");

    private static void AssertStopsOnce(TourDepositEnd how, string said)
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f), Stop("b", 1, 20f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        List<string> orders = Run(tour, deposit: () => how);

        Assert.Equal(new[] { "pick a", "deposit" }, orders.ToArray());
        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains(said, tour.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_driver_that_stops_mid_job_still_gets_the_load_to_the_chest_and_says_why()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f) }, TourProgress.Stopped, "the round cap");
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        Assert.Equal(new[] { "pick a", "deposit" }, Run(tour).ToArray());
        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Equal("the round cap", tour.Reason);
    }

    [Fact]
    public void A_source_he_cannot_reach_is_owed_after_the_stall_window_and_he_never_jumps()
    {
        var pump = new ScriptPump(new[] { Stop("far", 0, 100f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        var standing = new CollectionPoint(0f, 0f, 0f);

        // He never gets closer. Every instruction is a walk or a hold - there is
        // no order that puts him anywhere.
        float now = 0f;
        for (int tick = 0; tick < 400 && pump.Log.Count == 0; tick++)
        {
            now += 0.05f;
            TourInstruction instruction = tour.Tick(standing, now);
            Assert.True(instruction.Order == TourOrder.Walk || instruction.Order == TourOrder.Hold ||
                instruction.Order == TourOrder.None);
        }

        Assert.Equal(new[] { "failed:far" }, pump.Log.ToArray());
        Assert.True(now >= Limits.StallSeconds);
    }

    [Fact]
    public void A_chest_he_cannot_reach_stops_the_tour_with_the_load_on_him()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        // He picks at the source, then is stuck there.
        var at = new CollectionPoint(10f, 0f, 0f);
        float now = 0f;
        for (int tick = 0; tick < 400 && !tour.IsOver; tick++)
        {
            now += 0.05f;
            TourInstruction instruction = tour.Tick(at, now);
            if (instruction.Order == TourOrder.Pick)
            {
                tour.PickEnded(TourPickEnd.Took, 1);
            }

            Assert.NotEqual(TourOrder.Deposit, instruction.Order);
        }

        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains("could not get to the chest", tour.Reason, StringComparison.Ordinal);
        Assert.True(tour.CollectedSinceDeposit);
    }

    [Fact]
    public void A_pick_or_deposit_is_ordered_once_while_it_is_in_flight()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 1f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        var at = new CollectionPoint(1f, 0f, 0f);

        int picks = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            if (tour.Tick(at, tick * 0.05f).Order == TourOrder.Pick)
            {
                picks++;
            }
        }

        Assert.Equal(1, picks);
        Assert.Equal(TourPhase.Picking, tour.Phase);
    }

    [Fact]
    public void Cancelling_abandons_the_job_once_and_leaves_what_he_holds_on_him()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f), Stop("b", 0, 12f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        var at = new CollectionPoint(10f, 0f, 0f);
        tour.Tick(at, 0f);
        tour.Tick(at, 0.05f);
        tour.PickEnded(TourPickEnd.Took, 1);

        tour.Cancel("a peer connected");
        tour.Cancel("again");

        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Equal("a peer connected", tour.Reason);
        Assert.Equal(1, pump.Abandoned);
        Assert.True(tour.CollectedSinceDeposit);
        Assert.Equal(TourOrder.None, tour.Tick(at, 1f).Order);
    }

    [Fact]
    public void A_report_that_arrives_in_the_wrong_phase_changes_nothing()
    {
        var pump = new ScriptPump(new[] { Stop("a", 0, 10f) });
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        tour.PickEnded(TourPickEnd.Took, 5);
        tour.DepositEnded(TourDepositEnd.Emptied, string.Empty);

        Assert.Empty(pump.Log);
        Assert.Equal(0, tour.Picks);
        Assert.Equal(0, tour.Deposits);
        Assert.False(tour.CollectedSinceDeposit);
    }

    /// <summary>A driver that keeps saying "wait".</summary>
    private sealed class WaitingPump : ITourPump
    {
        public int Asked { get; private set; }

        public int Abandoned { get; private set; }

        public TourAdvance Next(CollectionPoint standingAt)
        {
            Asked++;
            return new TourAdvance(TourProgress.Waiting, default, false, "held by another job");
        }

        public void Done() { }

        public void Skipped() { }

        public void Failed() { }

        public void Abandon() => Abandoned++;
    }

    [Fact]
    public void Waiting_is_asked_about_at_most_once_a_second_not_every_tick()
    {
        // Every ask is a whole new look at the area; twenty a second is a frame
        // the player feels.
        var pump = new WaitingPump();
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        var at = new CollectionPoint(0f, 0f, 0f);
        for (int tick = 0; tick < 200; tick++)
        {
            Assert.Equal(TourOrder.Hold, tour.Tick(at, tick * 0.05f).Order);
        }

        Assert.InRange(pump.Asked, 1, 11);
    }

    [Fact]
    public void Waiting_for_ever_stops_the_tour_and_says_why()
    {
        var pump = new WaitingPump();
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);
        var at = new CollectionPoint(0f, 0f, 0f);
        float now = 0f;
        while (!tour.IsOver && now < 1000f)
        {
            tour.Tick(at, now);
            now += 0.5f;
        }

        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains("waited too long", tour.Reason, StringComparison.Ordinal);
        Assert.Contains("held by another job", tour.Reason, StringComparison.Ordinal);
        Assert.True(now <= Limits.MostWaitingSeconds + 2f);
        Assert.Equal(1, pump.Abandoned);
    }

    [Fact]
    public void A_tour_that_waits_too_long_still_takes_its_load_to_the_chest()
    {
        var pump = new ThenWaitPump(Stop("a", 0, 10f));
        var tour = new CollectionTour(pump, Limits, Chest, startWithDeposit: false);

        var orders = new List<string>();
        CollectionPoint at = new CollectionPoint(0f, 0f, 0f);
        for (float now = 0f; now < 400f && !tour.IsOver; now += 0.5f)
        {
            TourInstruction instruction = tour.Tick(at, now);
            if (instruction.Order == TourOrder.Walk) { at = instruction.Target; }
            if (instruction.Order == TourOrder.Pick) { orders.Add("pick"); tour.PickEnded(TourPickEnd.Took, 1); }
            if (instruction.Order == TourOrder.Deposit) { orders.Add("deposit"); tour.DepositEnded(TourDepositEnd.Emptied, ""); }
        }

        Assert.Equal(new[] { "pick", "deposit" }, orders.ToArray());
        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains("waited too long", tour.Reason, StringComparison.Ordinal);
    }

    /// <summary>Hands out one step, then says "wait" for ever.</summary>
    private sealed class ThenWaitPump : ITourPump
    {
        private readonly TourStep _step;
        private bool _done;

        public ThenWaitPump(TourStep step) => _step = step;

        public TourAdvance Next(CollectionPoint standingAt) =>
            !_done ? new TourAdvance(TourProgress.Do, _step, true, string.Empty)
                   : new TourAdvance(TourProgress.Waiting, default, false, "nothing yet");

        public void Done() => _done = true;

        public void Skipped() => _done = true;

        public void Failed() => _done = true;

        public void Abandon() { }
    }

    [Fact]
    public void The_step_cap_stops_a_tour_that_never_ends()
    {
        var steps = new List<TourStep>();
        for (int index = 0; index < 50; index++)
        {
            steps.Add(Stop("s" + index, index / 5, 1f));
        }

        var tour = new CollectionTour(new ScriptPump(steps), new CollectionTourLimits(2f, 10f, 7), Chest, startWithDeposit: false);
        Run(tour);

        Assert.Equal(TourPhase.Stopped, tour.Phase);
        Assert.Contains("more steps", tour.Reason, StringComparison.Ordinal);
    }
}
