using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Planning against what is free rather than against what is in the
/// chest.</summary>
public sealed class SourceAvailabilityTests
{
    /// <summary>Two jobs a tick apart do not both plan the same pile.
    ///
    /// <b>The failure this is written against.</b> Chest C holds a hundred wood.
    /// Job A plans a hundred-wood wall against it, and reserves. A tick later
    /// job B plans a sixty-wood fence from the same observation - which still
    /// says a hundred, because nothing asked the book - and comes out
    /// <c>Planned</c> and actionable. A hundred and sixty wood planned out of a
    /// hundred-wood chest. Custody's measured deltas stop the second job minting
    /// anything, so the cost is a stall and a wasted walk rather than duplicated
    /// material; and planning is deterministic, so the same wrong plan comes back
    /// every tick.</summary>
    [Fact]
    public void A_second_job_does_not_plan_on_what_the_first_has_set_aside()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 100)) };

        // Nobody has anything yet, so the whole hundred is plannable.
        JobTourPlan first = PlanFor(
            "a", new[] { Jobs.Target("wall", 10f, 0f, 0, ("wood", 100)) }, sources, book);
        Assert.Equal(JobPlanVerdict.Planned, first.Plan.Verdict);
        Assert.Equal(100, first.Plan.Manifest.RequiredOf("wood"));

        // Job A sets its hundred aside.
        Reserve(book, "a", 0, "chest", "wood", 100);

        // Job B now sees an empty chest, and is told so before it takes a step.
        JobTourPlan second = PlanFor(
            "b", new[] { Jobs.Target("fence", 20f, 0f, 0, ("wood", 60)) }, sources, book);
        Assert.Equal(JobPlanVerdict.ShortOfMaterial, second.Plan.Verdict);
        Assert.Equal(60, second.Shortfall.RequiredOf("wood"));
        Assert.False(second.Plan.IsActionable);

        // And with the hold given back, the same job plans perfectly.
        book.RefundAllFor("a");
        JobTourPlan afterRefund = PlanFor(
            "b", new[] { Jobs.Target("fence", 20f, 0f, 0, ("wood", 60)) }, sources, book);
        Assert.Equal(JobPlanVerdict.Planned, afterRefund.Plan.Verdict);
    }

    /// <summary>The refusal happens before anything starts, not four stops in.
    ///
    /// <b>Which of the two guards caught it is the whole assertion.</b> The
    /// whole-job shortfall check runs before the job is partitioned at all, so a
    /// plan it refuses has no trips; the per-trip selector refuses later and
    /// hands back the trips it had worked out. Both produce
    /// <c>ShortOfMaterial</c>, so a test that only read the verdict would go
    /// green with either one unwired - and the point of doing it up front is
    /// that the player is told before the NPC walks anywhere.</summary>
    [Fact]
    public void A_job_another_holds_the_material_for_is_refused_before_it_is_partitioned()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 100)) };
        Reserve(book, "a", 0, "chest", "wood", 100);

        JobTourPlan refused = PlanFor(
            "b", new[] { Jobs.Target("t", 10f, 0f, 0, ("wood", 60)) }, sources, book);

        Assert.Equal(JobPlanVerdict.ShortOfMaterial, refused.Plan.Verdict);
        Assert.Empty(refused.Tours);
        Assert.Empty(refused.Provisioning);
        Assert.Equal(60, refused.Shortfall.RequiredOf("wood"));
    }

    /// <summary>A chest whose contents are spoken for is not chosen, even when
    /// it is the nearest and would finish the job on its own.
    ///
    /// <b>This is the half of the wiring the whole-job check cannot cover.</b>
    /// Here the material genuinely is there in total - one chest's worth is
    /// reserved and another's is free - so the up-front check passes either way,
    /// and the only thing that stops the NPC walking to the wrong chest is the
    /// selector asking as well.</summary>
    [Fact]
    public void The_selector_does_not_choose_a_chest_whose_contents_are_spoken_for()
    {
        var book = new NpcMaterialReservationBook();
        var near = new PlanningStockContainer("near", Jobs.World, 1f, 0f);
        var far = new PlanningStockContainer("far", Jobs.World, 60f, 0f);
        var sources = new[]
        {
            Jobs.Stock(near, ("wood", 20)),
            Jobs.Stock(far, ("wood", 20)),
        };

        // Every unit in the near chest belongs to somebody else. In total there
        // is still exactly enough, so nothing is refused.
        Reserve(book, "a", 0, "near", "wood", 20);

        JobTourPlan plan = PlanFor(
            "b", new[] { Jobs.Target("t", 80f, 0f, 0, ("wood", 20)) }, sources, book);

        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);
        Assert.Single(plan.Provisioning[0].Draws);
        Assert.Equal("far", plan.Provisioning[0].Draws[0].Key);
        Assert.Equal(20, plan.Provisioning[0].Draws[0].Units);
    }

    /// <summary>Part of a pile being spoken for makes a plan smaller, not
    /// absent.</summary>
    [Fact]
    public void A_partly_reserved_chest_is_partly_available()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 100)) };
        Reserve(book, "a", 0, "chest", "wood", 70);

        var availability = new NpcReservedSourceAvailability(book);
        Assert.Equal(30, sources[0].UnitsOf("wood", availability));
        Assert.Equal(100, sources[0].UnitsOf("wood"));

        JobTourPlan fits = PlanFor(
            "b", new[] { Jobs.Target("t", 10f, 0f, 0, ("wood", 30)) }, sources, book);
        Assert.Equal(JobPlanVerdict.Planned, fits.Plan.Verdict);

        JobTourPlan doesNot = PlanFor(
            "b", new[] { Jobs.Target("t", 10f, 0f, 0, ("wood", 31)) }, sources, book);
        Assert.Equal(JobPlanVerdict.ShortOfMaterial, doesNot.Plan.Verdict);
        Assert.Equal(1, doesNot.Shortfall.RequiredOf("wood"));
    }

    /// <summary>The exclusion is one reservation, never one job.
    ///
    /// <b>The failure this is written against</b> is the obvious rule: ignore
    /// everything my own job holds. For an arithmetic of quantities that is a
    /// double count - one job's two steps each see forty free nails in a chest
    /// holding forty, and between them set aside eighty. So a job's own holds
    /// count against it like anybody else's, and the only thing looked past is
    /// the single name a re-plan is re-stating.</summary>
    [Fact]
    public void A_jobs_own_holds_count_against_it_and_only_one_name_is_looked_past()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 40)) };
        Reserve(book, "a", 0, "chest", "wood", 40);

        // Job A asking again, as a fresh plan, sees nothing free - its own hold
        // is a hold.
        Assert.Equal(0, sources[0].UnitsOf("wood", new NpcReservedSourceAvailability(book)));

        // And a re-plan re-stating that exact claim looks past that one name,
        // and nothing else.
        var restating = new NpcReservedSourceAvailability(book, ReservationId.For("a", 0));
        Assert.Equal(40, sources[0].UnitsOf("wood", restating));

        // Another step of the same job is not that name, so it still sees
        // nothing - which is the whole point.
        var otherStep = new NpcReservedSourceAvailability(book, ReservationId.For("a", 1));
        Assert.Equal(0, sources[0].UnitsOf("wood", otherStep));
    }

    /// <summary>A settled reservation is not a hold. Only what is still held is
    /// subtracted, so a refunded or committed claim frees its units.</summary>
    [Fact]
    public void Only_what_is_still_held_is_subtracted()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 40)) };
        ReservationId name = Reserve(book, "a", 0, "chest", "wood", 40);
        var availability = new NpcReservedSourceAvailability(book);

        Assert.Equal(0, sources[0].UnitsOf("wood", availability));

        book.Refund(name);
        Assert.Equal(40, sources[0].UnitsOf("wood", availability));
    }

    /// <summary>A hold on another chest, or on another item, is not this
    /// chest's or this item's.</summary>
    [Fact]
    public void A_hold_somewhere_else_is_not_a_hold_here()
    {
        var book = new NpcMaterialReservationBook();
        var here = new PlanningStockContainer("here", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(here, ("wood", 40), ("nails", 40));
        Reserve(book, "a", 0, "elsewhere", "wood", 40);
        Reserve(book, "a", 1, "here", "nails", 40);

        var availability = new NpcReservedSourceAvailability(book);
        Assert.Equal(40, stock.UnitsOf("wood", availability));
        Assert.Equal(0, stock.UnitsOf("nails", availability));
    }

    /// <summary>Without an availability, planning goes back to raw counts - and
    /// that is a decision the caller makes rather than one that happens.
    /// </summary>
    [Fact]
    public void No_availability_is_the_raw_count_and_is_the_callers_choice()
    {
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 100));

        Assert.Equal(100, stock.UnitsOf("wood", null));
        Assert.Equal(100, stock.UnitsOf("wood"));
    }

    /// <summary>An implementation that answers with more than was seen is
    /// answering about a chest nobody looked in, and what was seen is the
    /// ceiling.</summary>
    [Fact]
    public void What_was_seen_is_the_ceiling()
    {
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 10));

        Assert.Equal(10, stock.UnitsOf("wood", new GenerousAvailability(1000)));
        Assert.Equal(0, stock.UnitsOf("wood", new GenerousAvailability(-5)));
    }

    /// <summary>When the two vocabularies meet, the seam says so.
    ///
    /// The three tests below are one finding between them: this seam compares
    /// <c>INpcContainer.Key</c> against <c>NpcCustodyLocation.Key</c>, and
    /// <c>StockLine.Item</c> against <c>NpcMaterial.ItemName</c>, ordinally, and
    /// <b>both pairs are role-owned on both sides with nothing checking
    /// them</b>. If a role writes "Wood" into the ledger and "wood" into its
    /// stock lines, or keys a chest by a prefab and a place while keying custody
    /// by a ZDOID's text, then nothing matches, nothing is subtracted, and every
    /// answer is the raw observed count: the exact behaviour the seam was built
    /// to remove, in the unsafe direction, with a seam in place saying it is
    /// handled. No exception, no wrong-looking plan.</summary>
    [Fact]
    public void When_the_two_vocabularies_meet_the_seam_says_so()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 40));
        Reserve(book, "a", 0, "chest", "wood", 25);

        var availability = new NpcReservedSourceAvailability(book);
        Assert.Equal(15, stock.UnitsOf("wood", availability));

        NpcSourceAvailabilityAgreement met = availability.Agreement;
        Assert.Equal(NpcSourceVocabularies.Agree, met.Vocabularies);
        Assert.Equal(1, met.Asks);
        Assert.Equal(1, met.HeldRowsSeen);
        Assert.Equal(1, met.ContainerNameMatches);
        Assert.Equal(1, met.ItemNameMatches);
        Assert.Equal(25, met.UnitsSubtracted);
    }

    /// <summary>A role that keys chests one way and custody another gets the raw
    /// count back, and the seam records that not one container name met.
    /// </summary>
    [Fact]
    public void A_container_vocabulary_that_does_not_agree_is_visible_rather_than_silent()
    {
        var book = new NpcMaterialReservationBook();

        // The planner's chest is keyed by the role's container name; the ledger
        // kept the same chest under a ZDOID's text. Both are the role's, and
        // nothing in this library mints either.
        var chest = new PlanningStockContainer("chest-by-prefab-and-place", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 40));
        Reserve(book, "a", 0, "-8123456789:12", "wood", 40);

        var availability = new NpcReservedSourceAvailability(book);

        // Forty wood is spoken for, and the planner is told forty is free.
        Assert.Equal(40, stock.UnitsOf("wood", availability));

        NpcSourceAvailabilityAgreement seen = availability.Agreement;
        Assert.Equal(NpcSourceVocabularies.NoContainerNameMatched, seen.Vocabularies);
        Assert.Equal(1, seen.HeldRowsSeen);
        Assert.Equal(0, seen.ContainerNameMatches);
        Assert.Equal(0, seen.UnitsSubtracted);
    }

    /// <summary>And the half a matching container name hides: the chest is
    /// found and every item name inside it misses.</summary>
    [Fact]
    public void An_item_vocabulary_that_does_not_agree_is_visible_rather_than_silent()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 40));

        // The right chest, and the role spelled the item differently on the two
        // sides.
        Reserve(book, "a", 0, "chest", "Wood", 40);

        var availability = new NpcReservedSourceAvailability(book);
        Assert.Equal(40, stock.UnitsOf("wood", availability));

        NpcSourceAvailabilityAgreement seen = availability.Agreement;
        Assert.Equal(NpcSourceVocabularies.NoItemNameMatchedInAMatchedContainer, seen.Vocabularies);
        Assert.Equal(1, seen.ContainerNameMatches);
        Assert.Equal(1, seen.StacksInMatchedContainers);
        Assert.Equal(0, seen.ItemNameMatches);
        Assert.Equal(0, seen.UnitsSubtracted);
    }

    /// <summary>An empty book is not a mismatch, and neither is nobody having
    /// asked. The counts distinguish "there was nothing to disagree about" from
    /// "there was, and it did".</summary>
    [Fact]
    public void Nothing_to_compare_is_not_a_disagreement()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        SourceStock stock = Jobs.Stock(chest, ("wood", 40));

        var neverAsked = new NpcReservedSourceAvailability(book);
        Assert.Equal(NpcSourceVocabularies.NothingToCompare, neverAsked.Agreement.Vocabularies);

        var asked = new NpcReservedSourceAvailability(book);
        Assert.Equal(40, stock.UnitsOf("wood", asked));
        Assert.Equal(NpcSourceVocabularies.NothingToCompare, asked.Agreement.Vocabularies);
        Assert.Equal(1, asked.Agreement.Asks);
        Assert.Equal(0, asked.Agreement.HeldRowsSeen);
    }

    /// <summary>And a whole plan's worth of asking is observable the same way,
    /// which is how a role's own test finds out that its two name spaces never
    /// met.</summary>
    [Fact]
    public void A_whole_plan_reports_whether_the_names_ever_met()
    {
        var book = new NpcMaterialReservationBook();
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var sources = new[] { Jobs.Stock(chest, ("wood", 100)) };
        Reserve(book, "somebody-else", 0, "CHEST", "wood", 100);

        var availability = new NpcReservedSourceAvailability(book);
        var area = new FakeArea();
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("wall", 10f, 0f, 0, ("wood", 100)) },
            sources,
            new PlanningStopObserver(),
            new FakeProbe(),
            PlanningBudget.Unlimited());
        var planner = new TourJobPlanner(
            snapshot,
            NpcCarryCapacity.Unlimited,
            Jobs.Actions,
            setbacks: null,
            now: 0f,
            allowance: TourJobPlanner.DefaultAllowance,
            availability: availability);

        // The plan comes out perfectly formed, on a hundred wood another job
        // holds, because the chest was never found.
        JobTourPlan plan = planner.PlanTours(
            new JobPlanRequest(
                new Roles.NpcIdentity("product", "worker"),
                "mine",
                area,
                Jobs.World,
                JobManifest.Empty,
                Jobs.At(0f, 0f)));
        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);

        // And this is the one thing that says why.
        Assert.Equal(
            NpcSourceVocabularies.NoContainerNameMatched, availability.Agreement.Vocabularies);
        Assert.True(availability.Agreement.Asks > 0);
        Assert.Equal(0, availability.Agreement.UnitsSubtracted);
    }

    private static ReservationId Reserve(
        NpcMaterialReservationBook book, string jobId, int step, string containerKey, string item, int units)
    {
        ReservationId name = ReservationId.For(jobId, step);
        var where = new NpcCustodyLocation(NpcCustodyPlace.Stored, containerKey, Jobs.World);
        var stacks = new List<NpcMaterialStack> { new NpcMaterialStack(NpcMaterial.Of(item), units) };
        Assert.Equal(
            NpcCustodyOutcome.Applied, book.Reserve(new NpcMaterialReservation(name, where, stacks)));
        return name;
    }

    private static JobTourPlan PlanFor(
        string jobId,
        IReadOnlyList<JobTarget> targets,
        IReadOnlyList<SourceStock> sources,
        NpcMaterialReservationBook book)
    {
        var area = new FakeArea();
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            targets,
            sources,
            new PlanningStopObserver(),
            new FakeProbe(),
            PlanningBudget.Unlimited());

        var planner = new TourJobPlanner(
            snapshot,
            NpcCarryCapacity.Unlimited,
            Jobs.Actions,
            setbacks: null,
            now: 0f,
            allowance: TourJobPlanner.DefaultAllowance,
            availability: new NpcReservedSourceAvailability(book));

        return planner.PlanTours(
            new JobPlanRequest(
                new Roles.NpcIdentity("product", "worker"),
                jobId,
                area,
                Jobs.World,
                JobManifest.Empty,
                Jobs.At(0f, 0f)));
    }

    /// <summary>An availability that answers whatever it likes, to prove the
    /// caller does not simply believe it.</summary>
    private sealed class GenerousAvailability : INpcSourceAvailability
    {
        private readonly int _answer;

        internal GenerousAvailability(int answer)
        {
            _answer = answer;
        }

        public int AvailableIn(string containerKey, string item, int observed) => _answer;
    }
}
