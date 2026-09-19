using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Batching and the route: never one branch at a time, and a plan that
/// covers part of a job never says it covered all of it (#381).</summary>
public sealed class GunnarBatchPlanningTests
{
    private static readonly CollectionLimits Limits = CollectionLimits.Default;
    private static readonly CollectionPoint Origin = new CollectionPoint(0f, 0f, 0f);

    private static CollectionCandidate Takeable(
        string key, float x, float z, string item = "Stone", int units = 1, float unitKilograms = 2f) =>
        new CollectionCandidate(
            key,
            new CollectionPoint(x, 0f, z),
            CollectableVerdict.Eligible(CollectableKind.LooseStone),
            CollectionSiteClause.Unspecified,
            item,
            units,
            unitKilograms);

    private static CollectionCandidate Refused(string key, float x, float z) =>
        new CollectionCandidate(
            key,
            new CollectionPoint(x, 0f, z),
            CollectableVerdict.Eligible(CollectableKind.LooseStone),
            CollectionSiteClause.WardDenied,
            "Stone",
            1,
            2f);

    private static CollectionSurvey Finished(params CollectionCandidate[] candidates) =>
        new CollectionSurvey(SurveyOutcome.Finished, candidates);

    private static CarryBudget Room(float kilograms) =>
        CarryBudget.From(new CarryFacts(kilograms, 0f, 0f, beltEquipped: true), Limits);

    [Fact]
    public void Many_targets_are_one_batch_with_one_route()
    {
        CollectionSurvey survey = Finished(
            Takeable("a", 10f, 0f), Takeable("b", 20f, 0f), Takeable("c", 30f, 0f));

        CollectionBatchPlan plan = BatchPlanner.Plan(survey, Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(3, plan.Stops.Count);
        Assert.Equal(BatchOutcome.CoversEverythingSeen, plan.Outcome);
        Assert.Equal(0, plan.LeftForAnotherRound);
        Assert.True(plan.CoversEverythingSeen);
    }

    [Fact]
    public void The_route_is_walked_nearest_first_from_where_he_stands()
    {
        CollectionSurvey survey = Finished(
            Takeable("far", 40f, 0f), Takeable("near", 3f, 0f), Takeable("middle", 15f, 0f));

        CollectionBatchPlan plan = BatchPlanner.Plan(survey, Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(
            new[] { "near", "middle", "far" },
            Keys(plan));
    }

    [Fact]
    public void Two_candidates_the_same_distance_away_are_ordered_the_same_way_every_time()
    {
        // Determinism is what lets a player believe what they are watching, and
        // it is the only property a nearest-neighbour route can offer that an
        // optimal one cannot.
        CollectionSurvey forwards = Finished(Takeable("bravo", 5f, 0f), Takeable("alpha", -5f, 0f));
        CollectionSurvey backwards = Finished(Takeable("alpha", -5f, 0f), Takeable("bravo", 5f, 0f));

        Assert.Equal(
            Keys(BatchPlanner.Plan(forwards, Room(100f), CartCapacity.None, Limits, Origin)),
            Keys(BatchPlanner.Plan(backwards, Room(100f), CartCapacity.None, Limits, Origin)));
    }

    [Fact]
    public void What_he_cannot_carry_is_left_for_another_round_and_never_dropped()
    {
        // Ten stones at two kilograms each, and room for four.
        var candidates = new List<CollectionCandidate>();
        for (int index = 0; index < 10; index++)
        {
            candidates.Add(Takeable("s" + index, index + 1, 0f));
        }

        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(candidates.ToArray()), Room(8f), CartCapacity.None, Limits, Origin);

        Assert.Equal(4, plan.Stops.Count);
        Assert.Equal(6, plan.LeftForAnotherRound);
        Assert.Equal(BatchOutcome.CapacityReached, plan.Outcome);
        Assert.False(plan.CoversEverythingSeen);
        Assert.Equal(10, plan.Stops.Count + plan.LeftForAnotherRound);
    }

    [Fact]
    public void A_heavy_thing_he_cannot_lift_does_not_make_the_light_one_beside_it_wait()
    {
        CollectionSurvey survey = Finished(
            Takeable("boulder", 1f, 0f, unitKilograms: 100f),
            Takeable("pebble", 2f, 0f, unitKilograms: 1f));

        CollectionBatchPlan plan = BatchPlanner.Plan(survey, Room(10f), CartCapacity.None, Limits, Origin);

        Assert.Equal(new[] { "pebble" }, Keys(plan));
        Assert.Equal(1, plan.LeftForAnotherRound);
    }

    [Fact]
    public void A_cart_makes_room_his_back_does_not_have()
    {
        var candidates = new List<CollectionCandidate>();
        for (int index = 0; index < 10; index++)
        {
            candidates.Add(Takeable("s" + index, index + 1, 0f));
        }

        CollectionBatchPlan onFoot = BatchPlanner.Plan(
            Finished(candidates.ToArray()), Room(4f), CartCapacity.None, Limits, Origin);
        CollectionBatchPlan withCart = BatchPlanner.Plan(
            Finished(candidates.ToArray()), Room(4f),
            new CartCapacity(assigned: true, freeSlots: 1, slotStackSize: 50), Limits, Origin);

        Assert.Equal(2, onFoot.Stops.Count);
        Assert.Equal(10, withCart.Stops.Count);
        Assert.Equal(0, withCart.LeftForAnotherRound);
    }

    [Fact]
    public void The_carts_slots_are_not_promised_twice_inside_one_batch()
    {
        var candidates = new List<CollectionCandidate>();
        for (int index = 0; index < 6; index++)
        {
            candidates.Add(Takeable("s" + index, index + 1, 0f, units: 1, unitKilograms: 50f));
        }

        // No room on his back at all, and a cart that holds three units.
        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(candidates.ToArray()), Room(0f),
            new CartCapacity(assigned: true, freeSlots: 3, slotStackSize: 1), Limits, Origin);

        Assert.Equal(3, plan.Stops.Count);
        Assert.Equal(3, plan.LeftForAnotherRound);
    }

    [Fact]
    public void Nothing_he_can_lift_is_still_a_real_answer_with_real_work_left()
    {
        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(Takeable("boulder", 1f, 0f, unitKilograms: 100f)),
            Room(5f), CartCapacity.None, Limits, Origin);

        Assert.Empty(plan.Stops);
        Assert.Equal(BatchOutcome.CapacityReached, plan.Outcome);
        Assert.Equal(1, plan.LeftForAnotherRound);
    }

    [Fact]
    public void A_cleared_area_is_nothing_to_do_and_an_unfinished_look_never_is()
    {
        CollectionBatchPlan cleared = BatchPlanner.Plan(
            new CollectionSurvey(SurveyOutcome.Finished, new CollectionCandidate[0]),
            Room(100f), CartCapacity.None, Limits, Origin);
        CollectionBatchPlan unloaded = BatchPlanner.Plan(
            new CollectionSurvey(SurveyOutcome.NotLoaded, new CollectionCandidate[0]),
            Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(BatchOutcome.NothingToDo, cleared.Outcome);
        Assert.Equal(BatchOutcome.Unknown, unloaded.Outcome);
    }

    [Fact]
    public void A_truncated_look_that_fits_in_one_batch_still_asks_for_another_round()
    {
        // The nastiest shape: everything he was shown fits, so the batch looks
        // complete - but the looking did not finish, and the area may hold
        // more. Reporting that as covered is how a job of nine trips becomes a
        // job of one.
        CollectionBatchPlan plan = BatchPlanner.Plan(
            new CollectionSurvey(SurveyOutcome.Truncated, new[] { Takeable("a", 1f, 0f) }),
            Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Single(plan.Stops);
        Assert.False(plan.CoversEverythingSeen);
        Assert.Equal(1, plan.LeftForAnotherRound);
    }

    [Fact]
    public void An_unavailable_area_plans_nothing_rather_than_planning_a_default_one()
    {
        CollectionBatchPlan plan = BatchPlanner.Plan(
            CollectionSurvey.Unavailable("the designation was deleted"),
            Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(BatchOutcome.Unknown, plan.Outcome);
        Assert.Empty(plan.Stops);
    }

    [Fact]
    public void Candidates_the_place_refuses_are_never_planned_for()
    {
        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(Refused("warded", 1f, 0f), Takeable("free", 9f, 0f)),
            Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(new[] { "free" }, Keys(plan));
        Assert.Equal(0, plan.LeftForAnotherRound);
    }

    [Fact]
    public void The_batch_ceiling_caps_a_route_nobody_could_watch()
    {
        var limits = Limits.With(mostTargetsPerBatch: 3);
        var candidates = new List<CollectionCandidate>();
        for (int index = 0; index < 12; index++)
        {
            candidates.Add(Takeable("s" + index, index + 1, 0f, unitKilograms: 0.1f));
        }

        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(candidates.ToArray()), Room(1000f), CartCapacity.None, limits, Origin);

        Assert.Equal(3, plan.Stops.Count);
        Assert.Equal(BatchOutcome.BatchCeilingReached, plan.Outcome);
        Assert.Equal(9, plan.LeftForAnotherRound);
    }

    [Fact]
    public void The_manifest_says_what_the_batch_would_yield()
    {
        CollectionBatchPlan plan = BatchPlanner.Plan(
            Finished(
                Takeable("a", 1f, 0f, item: "Stone", units: 2),
                Takeable("b", 2f, 0f, item: "Wood", units: 3, unitKilograms: 1f),
                Takeable("c", 3f, 0f, item: "Stone", units: 1)),
            Room(100f), CartCapacity.None, Limits, Origin);

        Assert.Equal(3, plan.Manifest["Stone"]);
        Assert.Equal(3, plan.Manifest["Wood"]);
        Assert.Equal((2 * 2f) + (3 * 1f) + (1 * 2f), plan.Kilograms);
    }

    [Fact]
    public void An_unfinished_look_with_nothing_takeable_is_never_an_empty_area()
    {
        var survey = new CollectionSurvey(SurveyOutcome.NotLoaded, new[] { Refused("warded", 1f, 0f) });

        Assert.False(survey.IsExhaustedArea);
        Assert.True(new CollectionSurvey(SurveyOutcome.Finished, new[] { Refused("warded", 1f, 0f) })
            .IsExhaustedArea);
    }

    private static string[] Keys(CollectionBatchPlan plan)
    {
        var keys = new string[plan.Stops.Count];
        for (int index = 0; index < plan.Stops.Count; index++)
        {
            keys[index] = plan.Stops[index].Candidate.Key;
            Assert.Equal(index, plan.Stops[index].Order);
        }

        return keys;
    }
}
