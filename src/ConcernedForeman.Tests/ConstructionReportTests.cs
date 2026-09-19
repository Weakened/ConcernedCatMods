using TheConcernedCat.ConcernedForeman.Domain.Construction;

namespace ConcernedForeman.Tests;

/// <summary>What a build order tells a player once the work has started, and the
/// one judgement this product keeps for itself: the world decides whether a
/// shelter is finished, not the plan.
///
/// <b>What is deliberately NOT here.</b> Rounds, re-planning, the anti-spin
/// budget and the verdict taxonomy. Those belong to the shared runtime's
/// <c>NpcJobDriver</c>, which owns the loop; a second one in this product would
/// be the duplication that package exists to end. An earlier version of this
/// file tested one, written when nothing anywhere read
/// <c>LeftForAnotherRound</c>. Something does now.</summary>
public sealed class ConstructionReportTests
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

    private static ShelterRound Round(
        RoundOutcome outcome = RoundOutcome.Working,
        int built = 0,
        int left = 0,
        MaterialTally? carrying = null,
        MaterialTally? missing = null,
        string reason = "") =>
        new ShelterRound(outcome, built, left, carrying, missing, reason);

    [Fact]
    public void The_world_decides_whether_the_shelter_is_finished_and_not_the_plan()
    {
        Assert.True(ConstructionReport.IsShelterFinished(Finished()));
        Assert.False(ConstructionReport.IsShelterFinished(AfterFoundation()));
        Assert.False(ConstructionReport.IsShelterFinished(null));
    }

    [Fact]
    public void A_round_that_reports_itself_finished_over_an_unfinished_cottage_says_so()
    {
        // A plan capped at eight trips can service every target it was for and
        // leave a hole in the wall. This is that failure, seen from the role.
        string said = ConstructionReport.Line(
            Round(RoundOutcome.Finished, built: 4), AfterFoundation());

        Assert.Contains("That round finished and the shelter is not", said);
        Assert.Contains("13 pieces are still to place", said);
    }

    [Fact]
    public void A_finished_shelter_reads_as_finished_whatever_the_round_said()
    {
        Assert.Equal(
            "The shelter is finished.",
            ConstructionReport.Line(Round(RoundOutcome.Stopped, reason: "anything"), Finished()));
    }

    [Fact]
    public void What_a_plan_did_not_cover_is_told_to_the_player_rather_than_only_to_the_driver()
    {
        string said = ConstructionReport.Line(
            Round(built: 4, left: 9), AfterFoundation());

        Assert.Contains("Built 4 pieces", said);
        Assert.Contains("9 of them were more than this plan covered", said);
        Assert.Contains("there will be another round", said);
    }

    [Fact]
    public void A_round_that_built_nothing_says_that_plainly()
    {
        Assert.Contains(
            "Nothing went up that round", ConstructionReport.Line(Round(built: 0), Fresh()));
    }

    [Fact]
    public void What_is_still_carried_is_reported_so_the_next_round_plans_against_it()
    {
        string said = ConstructionReport.Line(
            Round(built: 1, carrying: ConstructionRig.Tally(("Wood", 8))), AfterFoundation());

        Assert.Contains("still carrying 8 Wood", said);
        Assert.Contains("his own inventory", said);
    }

    [Fact]
    public void A_missing_material_order_waits_and_names_what_is_missing()
    {
        string said = ConstructionReport.Line(
            Round(RoundOutcome.Waiting, missing: ConstructionRig.Tally(("Wood", 22))), Fresh());

        Assert.Contains("22 Wood", said);
        Assert.Contains("goes on by itself", said);
    }

    [Fact]
    public void An_unavailable_container_makes_the_order_wait_on_the_runtimes_own_words()
    {
        string said = ConstructionReport.Line(
            Round(RoundOutcome.Waiting, reason: "a chest it needs is open."), Fresh());

        Assert.Contains("The order is waiting", said);
        Assert.Contains("a chest it needs is open.", said);
    }

    [Fact]
    public void A_stopped_order_says_why_and_where_the_material_went()
    {
        string said = ConstructionReport.Line(
            Round(
                RoundOutcome.Stopped,
                carrying: ConstructionRig.Tally(("Wood", 6)),
                reason: "the site was warded."),
            AfterFoundation());

        Assert.Contains("the site was warded.", said);
        Assert.Contains("still carrying 6 Wood", said);
        Assert.Contains("nothing has been lost", said);
    }

    [Fact]
    public void A_round_that_did_not_say_how_it_ended_claims_nothing()
    {
        Assert.Contains(
            "nothing is claimed about it", ConstructionReport.Line(default, Fresh()));
    }

    [Fact]
    public void Waiting_with_nothing_named_is_still_waiting_rather_than_a_claim()
    {
        Assert.Equal(
            "The order is waiting.",
            ConstructionReport.Line(Round(RoundOutcome.Waiting), Fresh()));
    }

    [Fact]
    public void A_negative_count_from_anywhere_is_read_as_none()
    {
        var round = new ShelterRound(RoundOutcome.Working, -3, -9, null, null, null);

        Assert.Equal(0, round.Built);
        Assert.Equal(0, round.LeftForAnotherRound);
        Assert.True(round.Carrying.IsEmpty);
    }
}
