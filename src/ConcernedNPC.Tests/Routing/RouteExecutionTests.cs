using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Walking a round that the world keeps changing underneath.
///
/// The behaviour under test is the one difference between this package and the
/// loops it replaces: a stop that stops being worth doing is skipped, and the
/// round carries on. It is not a reason to work the job out again.</summary>
public sealed class RouteExecutionTests
{
    /// <summary>Targets somebody else finished, and targets that are not there
    /// any more, are skipped where they stand - mid-round, one at a time, with
    /// the round carrying on around them.</summary>
    [Fact]
    public void Completed_and_destroyed_stops_are_skipped_mid_route()
    {
        var observer = new PlanningStopObserver();
        RouteExecution round = Round(observer, "a", "b", "c", "d");

        Assert.Equal("a", Go(round, observer));
        round.Arrived();

        // Between the first stop and the second, the player chops "b" himself
        // and something knocks "c" over.
        observer.Say("b", StopStatus.AlreadyDone).Say("c", StopStatus.Gone);

        Assert.Equal("d", Go(round, observer));
        round.Arrived();

        RouteAdvance end = round.Next(observer);
        Assert.Equal(RouteProgress.Finished, end.Progress);
        Assert.Equal(0, round.Replans);
        Assert.Equal(2, round.Serviced.Count);
        Assert.Equal(2, round.Skipped.Count);
    }

    /// <summary>A stop that moved, and a stop nobody could read, are deferred
    /// rather than skipped: both are still wanted, and next round is when they
    /// are asked about again.</summary>
    [Fact]
    public void A_moved_or_unreadable_stop_is_kept_for_next_round()
    {
        var observer = new PlanningStopObserver()
            .Say("b", StopStatus.Moved)
            .Say("c", StopStatus.Unreadable);
        RouteExecution round = Round(observer, "a", "b", "c");

        Assert.Equal("a", Go(round, observer));
        round.Arrived();
        Assert.Equal(RouteProgress.Finished, round.Next(observer).Progress);

        Assert.Empty(round.Skipped);
        Assert.Equal(2, round.Deferred.Count);
        Assert.Equal(0, round.Replans);
    }

    /// <summary>New targets turning up while the round is being walked never
    /// cause a replan.
    ///
    /// <b>The failure this is written against</b> is the loop that re-decides
    /// everything from what is in front of it. In a forest where trees keep
    /// streaming in as the player walks towards the NPC, that loop never takes a
    /// step: every tick brings a new target and every new target restarts the
    /// thinking. Here a thousand of them are noted and the round finishes
    /// exactly as planned.</summary>
    [Fact]
    public void New_targets_do_not_cause_endless_replanning()
    {
        var observer = new PlanningStopObserver();
        RouteExecution round = Round(observer, "a", "b", "c");

        int serviced = 0;
        while (true)
        {
            RouteAdvance advance = round.Next(observer);
            round.NoteNewTargets(400);
            if (advance.Progress != RouteProgress.Go)
            {
                Assert.Equal(RouteProgress.Finished, advance.Progress);
                break;
            }

            round.Arrived();
            serviced++;
            Assert.True(serviced <= 3, "the round did not end");
        }

        Assert.Equal(3, serviced);
        Assert.Equal(0, round.Replans);
        Assert.Equal(1600, round.PendingNewTargets);
    }

    /// <summary>A round whose every stop turned out worthless asks for a new
    /// one - once - and then finishes rather than asking forever.
    ///
    /// <b>Why the cap is not a fudge.</b> The world can hold "every stop is
    /// gone" true indefinitely, and an NPC that answered by planning again would
    /// stand still, thinking, for as long as the player watched.</summary>
    [Fact]
    public void A_round_that_was_all_skipped_asks_once_and_then_stops()
    {
        var observer = new PlanningStopObserver()
            .Say("a", StopStatus.Gone)
            .Say("b", StopStatus.Gone);
        var round = new RouteExecution(Sequence("a", "b"), new RouteExecutionLimits(1));

        Assert.Equal(RouteProgress.Replan, round.Next(observer).Progress);
        Assert.Equal(1, round.Replans);
        Assert.Equal(RouteProgress.Finished, round.Next(observer).Progress);
        Assert.Equal(1, round.Replans);
        Assert.False(round.RequestReplan());
    }

    /// <summary>The replan cap belongs to the job, not to the instance.
    ///
    /// <b>The failure this is written against.</b> The only sane response to a
    /// replan is to plan again and start a new round, and a new round starting
    /// at zero can be told to replan again immediately - so an observer
    /// answering "gone" for every stop loops forever over a world that never
    /// changes, which is the precise thing the cap documents itself as the whole
    /// defence against. Carrying the count forward is what makes it a cap.
    /// </summary>
    [Fact]
    public void The_replan_cap_is_carried_forward_between_rounds()
    {
        var observer = new PlanningStopObserver()
            .Say("a", StopStatus.Gone)
            .Say("b", StopStatus.Gone);
        var limits = new RouteExecutionLimits(2);

        int replans = 0;
        for (int round = 0; round < 6; round++)
        {
            var execution = new RouteExecution(Sequence("a", "b"), limits, replans);
            RouteAdvance advance = execution.Next(observer);
            replans = execution.Replans;

            if (advance.Progress == RouteProgress.Finished)
            {
                break;
            }

            Assert.Equal(RouteProgress.Replan, advance.Progress);
        }

        Assert.Equal(limits.MostReplans, replans);

        // And a fresh round handed the spent count does not start again.
        var last = new RouteExecution(Sequence("a", "b"), limits, replans);
        Assert.Equal(RouteProgress.Finished, last.Next(observer).Progress);
        Assert.False(last.RequestReplan());
    }

    /// <summary>One stop going wrong is not a replan. A round that serviced
    /// anything at all is a round that finished.</summary>
    [Fact]
    public void One_stop_going_wrong_is_not_a_replan()
    {
        var observer = new PlanningStopObserver().Say("b", StopStatus.Gone);
        RouteExecution round = Round(observer, "a", "b");

        Assert.Equal("a", Go(round, observer));
        round.Arrived();

        Assert.Equal(RouteProgress.Finished, round.Next(observer).Progress);
        Assert.Equal(0, round.Replans);
    }

    /// <summary>A stop reached that could not be done is skipped like any other,
    /// and never a replan.</summary>
    [Fact]
    public void A_stop_that_failed_on_arrival_is_skipped_not_replanned()
    {
        var observer = new PlanningStopObserver();
        RouteExecution round = Round(observer, "a", "b");

        Assert.Equal("a", Go(round, observer));
        round.Abandoned();

        Assert.Equal("b", Go(round, observer));
        round.Arrived();

        Assert.Equal(RouteProgress.Finished, round.Next(observer).Progress);
        Assert.Equal(0, round.Replans);
        Assert.Single(round.Skipped);
        Assert.Single(round.Serviced);
    }

    /// <summary>Asked twice without having got anywhere, the round answers the
    /// same stop. One body cannot be given two destinations.</summary>
    [Fact]
    public void Asking_twice_while_standing_still_gives_the_same_stop()
    {
        var observer = new PlanningStopObserver();
        RouteExecution round = Round(observer, "a", "b");

        Assert.Equal("a", Go(round, observer));
        int looks = observer.Looks;
        Assert.Equal("a", Go(round, observer));

        // And the role is not asked again while nothing has changed, because the
        // answer could not be acted on differently anyway.
        Assert.Equal(looks, observer.Looks);
    }

    /// <summary>A role whose completion condition throws does not take the NPC
    /// out. It is treated as having said nothing, which leaves the stop for next
    /// round - and a round nobody could read anything in does <b>not</b> ask for
    /// a new route, because a new route over the same unreadable stops is the
    /// same route.</summary>
    [Fact]
    public void A_throwing_observer_is_unreadable_rather_than_fatal()
    {
        var observer = new PlanningStopObserver { Throws = true };
        var round = new RouteExecution(Sequence("a", "b"), RouteExecutionLimits.Default);

        RouteAdvance advance = round.Next(observer);

        Assert.Equal(RouteProgress.Finished, advance.Progress);
        Assert.Equal(2, round.Deferred.Count);
        Assert.Empty(round.Skipped);
        Assert.Equal(0, round.Replans);
    }

    /// <summary>No observer at all means nothing is known, which is never a
    /// licence to walk to something.</summary>
    [Fact]
    public void No_observer_means_nothing_is_known()
    {
        var round = new RouteExecution(Sequence("a"), RouteExecutionLimits.Default);

        Assert.NotEqual(RouteProgress.Go, round.Next(null).Progress);
        Assert.Single(round.Deferred);
    }

    /// <summary>Every status has exactly one decision, and the two that keep the
    /// material are not the two that give up on the stop.</summary>
    // The enums are internal, so a public theory takes their numbers. Written
    // out rather than derived, because the point is that somebody chose each
    // one: adding a status without deciding what it means fails here.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    [InlineData(4, 3)]
    [InlineData(0, 3)]
    [InlineData(99, 3)]
    public void Each_status_has_one_decision(int status, int disposition)
    {
        Assert.Equal((StopDisposition)disposition, RouteExecution.Decide((StopStatus)status));
    }

    private static RouteExecution Round(PlanningStopObserver observer, params string[] keys)
    {
        _ = observer;
        return new RouteExecution(Sequence(keys), RouteExecutionLimits.Default);
    }

    private static string Go(RouteExecution round, PlanningStopObserver observer)
    {
        RouteAdvance advance = round.Next(observer);
        return advance.Progress == RouteProgress.Go ? advance.Stop.Key : advance.Progress.ToString();
    }

    private static StopSequence Sequence(params string[] keys)
    {
        var stops = new List<RouteStop>(keys.Length);
        for (int index = 0; index < keys.Length; index++)
        {
            stops.Add(new RouteStop(keys[index], new NpcPoint(index * 10f, 0f, 0f), 0, true));
        }

        return new StopSequence(StopSequenceOutcome.Ordered, stops, null, 0f, false, false);
    }
}
