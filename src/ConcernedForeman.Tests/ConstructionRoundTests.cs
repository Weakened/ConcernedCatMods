using TheConcernedCat.ConcernedForeman.Domain.Construction;

namespace ConcernedForeman.Tests;

/// <summary>#380's round loop, and the acceptance criterion the shared
/// runtime's re-review asked for by name: <b>something must read
/// <c>LeftForAnotherRound</c></b>. Until this existed, a job bigger than one
/// plan stopped after one plan and looked exactly like the blocker that had
/// just been fixed.</summary>
public sealed class ConstructionRoundTests
{
    private static ConstructionProgress Fresh() =>
        ConstructionProgress.Read(ConstructionRig.Plan(), new FakeSight());

    private static ConstructionProgress Finished() => ConstructionProgress.Read(
        ConstructionRig.Plan(), new FakeSight { Default = PieceSighting.Standing });

    private static ConstructionProgress AfterFoundation()
    {
        ShelterPlan plan = ConstructionRig.Plan();
        var sight = new FakeSight();
        sight.Finish(plan, BuildPhase.Foundation);
        return ConstructionProgress.Read(plan, sight);
    }

    private static BuildRoundOutcome Round(
        RoundVerdict verdict = RoundVerdict.Planned,
        int serviced = 0,
        int left = 0,
        int deferred = 0,
        MaterialTally? missing = null,
        MaterialTally? carried = null,
        string reason = "") =>
        new BuildRoundOutcome(verdict, serviced, left, deferred, missing, carried, reason);

    [Fact]
    public void A_plan_that_did_not_cover_the_whole_job_asks_for_another_round()
    {
        var rounds = new ConstructionRounds();

        RoundDecision decision = rounds.Next(
            Round(serviced: 4, left: 13), AfterFoundation());

        Assert.Equal(RoundDecision.PlanAnother, decision);
        Assert.Contains("13 more than this plan covered", rounds.Reason);
    }

    [Fact]
    public void A_plan_that_covered_everything_and_left_pieces_deferred_asks_for_another_round()
    {
        var rounds = new ConstructionRounds();

        // The trip cap left nothing behind, but the roof was not its turn yet.
        RoundDecision decision = rounds.Next(Round(serviced: 4, deferred: 9), AfterFoundation());

        Assert.Equal(RoundDecision.PlanAnother, decision);
        Assert.Contains("9 left for the next round", rounds.Reason);
    }

    [Fact]
    public void A_round_that_covered_everything_and_finished_the_shelter_stops()
    {
        var rounds = new ConstructionRounds();

        Assert.Equal(
            RoundDecision.Finished, rounds.Next(Round(serviced: 17), Finished()));
        Assert.Contains("finished", rounds.Reason);
    }

    [Fact]
    public void The_world_decides_whether_it_is_finished_and_not_the_plan()
    {
        var rounds = new ConstructionRounds();

        // Every step of the plan came off, and four pieces of the cottage are
        // still missing. This is the exact shape of the blocker the shared
        // runtime's reviewer opened with, checked from the role's side.
        RoundDecision decision = rounds.Next(Round(serviced: 13), AfterFoundation());

        Assert.NotEqual(RoundDecision.Finished, decision);
        Assert.Equal(RoundDecision.PlanAnother, decision);
    }

    [Fact]
    public void A_round_that_says_there_is_nothing_to_do_while_the_shelter_stands_unfinished_stops_honestly()
    {
        var rounds = new ConstructionRounds();

        RoundDecision decision = rounds.Next(Round(RoundVerdict.NothingToDo), AfterFoundation());

        Assert.Equal(RoundDecision.Stop, decision);
        Assert.Contains("nothing left that can be built", rounds.Reason);
    }

    [Fact]
    public void Nothing_to_do_on_ground_nobody_finished_looking_at_waits_rather_than_concluding()
    {
        var rounds = new ConstructionRounds();
        ConstructionProgress unseen = ConstructionProgress.Read(
            ConstructionRig.Plan(), new FakeSight { Default = PieceSighting.Unknown });

        Assert.Equal(RoundDecision.Wait, rounds.Next(Round(RoundVerdict.NothingToDo), unseen));
        Assert.Contains("could be seen", rounds.Reason);
    }

    [Fact]
    public void Nothing_to_do_because_something_is_in_the_way_names_the_place()
    {
        ShelterPlan plan = ConstructionRig.Plan();
        var sight = new FakeSight();
        sight.Set(plan.Pieces[1].Key, PieceSighting.Blocked);
        ConstructionProgress blocked = ConstructionProgress.Read(plan, sight);

        var rounds = new ConstructionRounds();
        Assert.Equal(RoundDecision.Stop, rounds.Next(Round(RoundVerdict.NothingToDo), blocked));
        Assert.Contains("in the way of wood_floor", rounds.Reason);
        Assert.Contains("Clear it", rounds.Reason);
    }

    [Fact]
    public void A_missing_material_order_waits_and_says_what_is_missing()
    {
        var rounds = new ConstructionRounds();

        RoundDecision decision = rounds.Next(
            Round(RoundVerdict.ShortOfMaterial, missing: ConstructionRig.Tally(("Wood", 22))),
            Fresh());

        Assert.Equal(RoundDecision.Wait, decision);
        Assert.Contains("22 Wood", rounds.Reason);

        // Waiting, not failing: the player can fix it and the order goes on.
        Assert.Contains("goes on by itself", rounds.Reason);
    }

    [Fact]
    public void A_shortage_that_names_nothing_says_so_rather_than_pretending()
    {
        var rounds = new ConstructionRounds();
        rounds.Next(Round(RoundVerdict.ShortOfMaterial), Fresh());

        Assert.Contains("nothing was named as missing", rounds.Reason);
        Assert.Contains("defect", rounds.Reason);
    }

    [Fact]
    public void An_unavailable_container_makes_the_order_wait_rather_than_using_another()
    {
        var rounds = new ConstructionRounds();

        RoundDecision decision = rounds.Next(
            Round(RoundVerdict.ContainerUnavailable, reason: "somebody has it open"), Fresh());

        Assert.Equal(RoundDecision.Wait, decision);
        Assert.Contains("somebody has it open", rounds.Reason);
        Assert.Contains("Nothing else will be used instead", rounds.Reason);
    }

    [Fact]
    public void Rounds_that_build_nothing_stop_asking_rather_than_walking_in_circles()
    {
        var rounds = new ConstructionRounds();
        ConstructionProgress progress = Fresh();

        for (int round = 1; round < ConstructionRounds.FruitlessRoundsAllowed; round++)
        {
            Assert.Equal(
                RoundDecision.PlanAnother, rounds.Next(Round(serviced: 0, deferred: 17), progress));
        }

        // The budget runs out on the third, and the order waits for the world to
        // change rather than planning again forever.
        Assert.Equal(RoundDecision.Wait, rounds.Next(Round(serviced: 0, deferred: 17), progress));
        Assert.Contains("walking in circles", rounds.Reason);
    }

    [Fact]
    public void A_round_that_builds_something_clears_the_budget()
    {
        var rounds = new ConstructionRounds();
        ConstructionProgress progress = AfterFoundation();

        rounds.Next(Round(serviced: 0, deferred: 13), progress);
        rounds.Next(Round(serviced: 0, deferred: 13), progress);
        Assert.Equal(2, rounds.FruitlessRounds);

        rounds.Next(Round(serviced: 1, deferred: 12), progress);
        Assert.Equal(0, rounds.FruitlessRounds);

        // So a build that really is progressing never runs the budget down.
        Assert.Equal(
            RoundDecision.PlanAnother, rounds.Next(Round(serviced: 1, deferred: 11), progress));
    }

    [Fact]
    public void A_round_cap_ends_an_order_that_never_finishes()
    {
        var rounds = new ConstructionRounds();
        ConstructionProgress progress = AfterFoundation();

        RoundDecision decision = RoundDecision.PlanAnother;
        for (int round = 0; round <= ConstructionRounds.RoundsAllowed; round++)
        {
            // Every round builds one piece, so the fruitless budget never fires
            // and only the hard cap can end it.
            decision = rounds.Next(Round(serviced: 1, left: 1), progress);
            if (decision == RoundDecision.Stop)
            {
                break;
            }
        }

        Assert.Equal(RoundDecision.Stop, decision);
        Assert.Contains("rounds without finishing", rounds.Reason);
    }

    [Fact]
    public void A_round_that_did_not_say_how_it_ended_stops_rather_than_guessing()
    {
        var rounds = new ConstructionRounds();

        Assert.Equal(RoundDecision.Stop, rounds.Next(default, Fresh()));
        Assert.Contains("rather than guessing", rounds.Reason);
    }

    [Fact]
    public void An_area_that_is_gone_stops_and_a_site_half_seen_waits()
    {
        var one = new ConstructionRounds();
        Assert.Equal(
            RoundDecision.Stop,
            one.Next(Round(RoundVerdict.AreaInvalid, reason: "the site is gone"), Fresh()));
        Assert.Contains("the site is gone", one.Reason);

        var two = new ConstructionRounds();
        Assert.Equal(
            RoundDecision.Wait, two.Next(Round(RoundVerdict.NotFinishedLooking), Fresh()));
    }

    [Fact]
    public void An_interrupted_build_says_where_the_material_went()
    {
        string sentence = ConstructionSentences.Stopped(
            "the site was warded.",
            ConstructionRig.Tally(("Wood", 12)),
            ConstructionRig.Tally(("Wood", 4)));

        Assert.Contains("12 Wood went back where it came from", sentence);
        Assert.Contains("still carrying 4 Wood", sentence);
        Assert.Contains("nothing has been lost", sentence);
    }

    [Fact]
    public void A_build_that_stopped_before_it_opened_a_chest_says_that_too()
    {
        string sentence = ConstructionSentences.Stopped("the order was cancelled.", null, null);

        Assert.Contains("Nothing had been taken out of a container", sentence);
    }

    [Fact]
    public void What_is_still_carried_is_reported_so_the_next_round_can_plan_against_it()
    {
        var rounds = new ConstructionRounds();

        rounds.Next(
            Round(serviced: 3, left: 6, carried: ConstructionRig.Tally(("Wood", 8))),
            AfterFoundation());

        Assert.Contains("still carrying 8 Wood", rounds.Reason);
    }

    [Fact]
    public void The_round_count_is_what_the_loop_ran()
    {
        var rounds = new ConstructionRounds();
        ConstructionProgress progress = AfterFoundation();

        rounds.Next(Round(serviced: 1, left: 1), progress);
        rounds.Next(Round(serviced: 1, left: 1), progress);

        Assert.Equal(2, rounds.Rounds);
    }
}
