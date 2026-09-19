using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The behaviour the whole package exists for, tested the way it would
/// fail if it regressed.
///
/// Every test in here is written against something a player would see going
/// wrong, not against the shape of the code that happens to be there. The one
/// the rest hang off is
/// <see cref="Many_targets_are_one_plan_with_one_provisioning_phase"/>: if that
/// ever passes while the NPC walks to a chest per target, the test is
/// worthless.</summary>
public sealed class JobPlanningPipelineTests
{
    /// <summary>Six wall sections, two materials, one chest that has both.
    ///
    /// <b>The failure this is written against</b> is the shipped loop: see one
    /// target, walk to a chest, fetch one item, service one target, walk back,
    /// repeat. That loop over these inputs produces six collect steps. This
    /// asserts one.</summary>
    [Fact]
    public void Many_targets_are_one_plan_with_one_provisioning_phase()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>();
        for (int index = 0; index < 6; index++)
        {
            targets.Add(Jobs.Target("wall" + index, 10f + index, 0f, 0, ("wood", 5), ("nails", 2)));
        }

        JobTourPlan plan = Plan(targets, new[] { Jobs.Stock(chest, ("wood", 100), ("nails", 100)) });

        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);
        Assert.Single(plan.Tours);
        Assert.Equal(1, Collects(plan));
        Assert.Equal(6, Services(plan));

        // And the one collect step comes before every service step, because a
        // provisioning phase that happened halfway through would be the same
        // walk to the chest under another name.
        Assert.Equal(0, FirstCollect(plan));
        Assert.True(FirstService(plan) > LastCollect(plan));
    }

    /// <summary>The total exists before a single step does, and it is the sum of
    /// the targets rather than anything the planner discovered on the way.
    /// </summary>
    [Fact]
    public void The_manifest_is_calculated_before_execution()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>
        {
            Jobs.Target("a", 10f, 0f, 0, ("wood", 5), ("nails", 2)),
            Jobs.Target("b", 12f, 0f, 0, ("wood", 7)),
            Jobs.Target("c", 14f, 0f, 0, ("nails", 3)),
        };

        JobTourPlan plan = Plan(targets, new[] { Jobs.Stock(chest, ("wood", 100), ("nails", 100)) });

        Assert.Equal(12, plan.Plan.Manifest.RequiredOf("wood"));
        Assert.Equal(5, plan.Plan.Manifest.RequiredOf("nails"));
        Assert.Equal(17, plan.Plan.Manifest.TotalUnits);

        // The manifest is on the plan, which means it existed before the first
        // step was written - and the draw matches it exactly, which means
        // nothing was fetched that the job does not need.
        Assert.Equal(17, plan.Provisioning[0].Draws[0].Units);
    }

    /// <summary>One chest that has everything beats two that have half each,
    /// even when the two are nearer. The owner's own worked example.</summary>
    [Fact]
    public void One_sufficient_chest_beats_two_nearer_ones()
    {
        var both = new PlanningStockContainer("both", Jobs.World, 40f, 0f);
        var wood = new PlanningStockContainer("wood", Jobs.World, 1f, 0f);
        var nails = new PlanningStockContainer("nails", Jobs.World, 2f, 0f);

        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 60f, 0f, 0, ("wood", 10), ("nails", 10)) },
            new[]
            {
                Jobs.Stock(wood, ("wood", 100)),
                Jobs.Stock(nails, ("nails", 100)),
                Jobs.Stock(both, ("wood", 100), ("nails", 100)),
            });

        Assert.Equal(1, plan.Provisioning[0].Stops);
        Assert.Equal("both", plan.Provisioning[0].Draws[0].Key);
    }

    /// <summary>And when no single chest can do it, several are used - and only
    /// as many as it genuinely takes.</summary>
    [Fact]
    public void Several_chests_are_used_when_genuinely_needed()
    {
        var wood = new PlanningStockContainer("wood", Jobs.World, 1f, 0f);
        var nails = new PlanningStockContainer("nails", Jobs.World, 2f, 0f);
        var spare = new PlanningStockContainer("spare", Jobs.World, 3f, 0f);

        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 60f, 0f, 0, ("wood", 10), ("nails", 10)) },
            new[]
            {
                Jobs.Stock(wood, ("wood", 100)),
                Jobs.Stock(nails, ("nails", 100)),
                Jobs.Stock(spare, ("wood", 100), ("nails", 100)),
            });

        // "spare" holds both, so it wins outright and the answer is one stop -
        // the interesting case is the one where nothing holds both.
        Assert.Equal(1, plan.Provisioning[0].Stops);

        JobTourPlan split = Plan(
            new[] { Jobs.Target("a", 60f, 0f, 0, ("wood", 10), ("nails", 10)) },
            new[] { Jobs.Stock(wood, ("wood", 100)), Jobs.Stock(nails, ("nails", 100)) });

        Assert.Equal(2, split.Provisioning[0].Stops);
        Assert.Equal(10, split.Provisioning[0].Draws[0].Units);
        Assert.Equal(10, split.Provisioning[0].Draws[1].Units);
    }

    /// <summary>A job bigger than one trip becomes the fewest trips it fits in,
    /// each one filled - never one target at a time.
    ///
    /// <b>The regression this catches</b> is the easy wrong answer: a
    /// partitioner that starts a new trip whenever the next target does not fit
    /// the <i>remaining</i> need rather than the remaining capacity ends up with
    /// one target a trip, which looks like batching in the code and looks like
    /// the old loop in the game.</summary>
    [Fact]
    public void Overflow_creates_planned_batches_and_never_one_target_a_trip()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>();
        for (int index = 0; index < 9; index++)
        {
            targets.Add(Jobs.Target("t" + index, 10f + index, 0f, 0, ("wood", 10)));
        }

        JobTourPlan plan = Plan(
            targets, new[] { Jobs.Stock(chest, ("wood", 1000)) }, new NpcCarryCapacity(30));

        Assert.Equal(3, plan.Tours.Count);
        Assert.Equal(new NpcCarryCapacity(30).FewestToursFor(90), plan.Tours.Count);
        foreach (JobTour tour in plan.Tours)
        {
            Assert.Equal(3, tour.Targets.Count);
            Assert.Equal(30, tour.Units);
        }

        // One provisioning phase per trip, not one per target.
        Assert.Equal(3, Collects(plan));
        Assert.Equal(9, Services(plan));
    }

    /// <summary>The trips are geographically coherent: he clears one end and
    /// then the other, rather than crossing the camp on every trip.</summary>
    [Fact]
    public void Batches_are_geographically_coherent()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>
        {
            Jobs.Target("near1", 10f, 0f, 0, ("wood", 10)),
            Jobs.Target("far1", 80f, 0f, 0, ("wood", 10)),
            Jobs.Target("near2", 11f, 0f, 0, ("wood", 10)),
            Jobs.Target("far2", 81f, 0f, 0, ("wood", 10)),
        };

        JobTourPlan plan = Plan(
            targets, new[] { Jobs.Stock(chest, ("wood", 1000)) }, new NpcCarryCapacity(20));

        Assert.Equal(2, plan.Tours.Count);
        Assert.Equal(new[] { "near1", "near2" }, Keys(plan.Tours[0]));
        Assert.Equal(new[] { "far1", "far2" }, Keys(plan.Tours[1]));
    }

    /// <summary>Every step of a plan reserves under its own name, and no two
    /// steps ask under the same one. A collision would have one step release
    /// another's hold.</summary>
    [Fact]
    public void Every_step_reserves_under_a_name_of_its_own()
    {
        JobTourPlan plan = TwoTargetsTwoChests();
        var targets = new JobCommitment<JobTarget>("job", new FakeBook<JobTarget>(Jobs.World));
        var chests = new JobCommitment<INpcContainer>("job", new FakeBook<INpcContainer>(Jobs.World));

        JobReservationResult result = PlanReservations.TakeOut(plan, targets, chests);

        Assert.True(result.NamesAreDistinct);
        Assert.True(result.AllHeld);
        Assert.Equal(plan.Plan.Steps.Count, result.Attempts.Count);
        Assert.Equal(0, result.Conflicts);
    }

    /// <summary>Another job's hold is refused, never taken over, and the
    /// refusal names it. Two NPCs withdrawing the same forty nails is a
    /// player's chest emptied twice over.</summary>
    [Fact]
    public void A_subject_another_job_holds_is_refused()
    {
        var book = new FakeBook<JobTarget>(Jobs.World);
        JobTarget contested = Jobs.Target("a", 10f, 0f, 0, ("wood", 1));
        Assert.Equal(ReservationOutcome.Reserved, book.Reserve(contested, ReservationId.For("theirs", 0)));

        var mine = new JobCommitment<JobTarget>("mine", book);
        Assert.Equal(ReservationOutcome.HeldByAnother, mine.Reserve(contested, 4));
        Assert.Equal(0, mine.Held);

        Assert.True(book.TryGetHolder(contested, out ReservationId holder));
        Assert.Equal("theirs", holder.JobId);
    }

    /// <summary>Re-establishing a job's own holds after an interruption is
    /// satisfied, not refused - including when the re-plan moved the subject to
    /// a different step.</summary>
    [Fact]
    public void A_job_re_reserving_its_own_subject_at_a_new_step_is_satisfied()
    {
        var book = new FakeBook<JobTarget>(Jobs.World);
        var commitment = new JobCommitment<JobTarget>("job", book);
        JobTarget target = Jobs.Target("a", 10f, 0f, 0, ("wood", 1));

        Assert.Equal(ReservationOutcome.Reserved, commitment.Reserve(target, 3));
        Assert.Equal(ReservationOutcome.AlreadySatisfied, commitment.Reserve(target, 2));
        Assert.Equal(1, book.Count);
    }

    /// <summary>A cancelled plan gives everything back, once, however many
    /// callers cancel it.
    ///
    /// <b>The failure this is written against</b> is a refund that happens twice
    /// because the player countermanded the order in the same frame the NPC
    /// died. Material refunded twice came from nowhere.</summary>
    [Fact]
    public void A_cancelled_plan_refunds_exactly_once()
    {
        var targetBook = new FakeBook<JobTarget>(Jobs.World);
        var chestBook = new FakeBook<INpcContainer>(Jobs.World);
        var targets = new JobCommitment<JobTarget>("job", targetBook);
        var chests = new JobCommitment<INpcContainer>("job", chestBook);
        PlanReservations.TakeOut(TwoTargetsTwoChests(), targets, chests);

        // Each half latches on its own first, because a caller with a reason to
        // cancel one book directly must not be able to refund it twice either -
        // and a latch that lived only in the composite would look right here
        // while leaving that door open.
        int fromTargets = targets.Cancel();
        Assert.True(fromTargets > 0);
        Assert.Equal(0, targets.Cancel());
        Assert.Equal(0, targets.Cancel());
        Assert.Equal(1, targetBook.ReleaseAllCalls);
        Assert.True(targets.IsCancelled);

        var both = new JobCommitments("job", targets, chests);
        int first = both.Cancel();

        Assert.True(first > 0);
        Assert.Equal(0, both.Cancel());
        Assert.Equal(0, both.Cancel());
        Assert.Equal(1, targetBook.ReleaseAllCalls);
        Assert.Equal(1, chestBook.ReleaseAllCalls);
        Assert.Equal(0, targetBook.Count);
        Assert.Equal(0, chestBook.Count);

        // And a cancelled commitment asks for nothing more, so a late step
        // cannot leave an orphan nobody will ever release.
        Assert.Equal(
            ReservationOutcome.Unspecified,
            targets.Reserve(Jobs.Target("late", 1f, 1f, 0, ("wood", 1)), 99));
        Assert.Equal(0, targetBook.Count);
    }

    /// <summary>A job nothing reachable can provision is refused before a step
    /// is taken, and the refusal says what is missing.</summary>
    [Fact]
    public void A_job_nothing_can_provision_is_refused_before_it_starts()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 50)) },
            new[] { Jobs.Stock(chest, ("wood", 10)) });

        Assert.Equal(JobPlanVerdict.ShortOfMaterial, plan.Plan.Verdict);
        Assert.Empty(plan.Plan.Steps);
        Assert.Equal(40, plan.Shortfall.RequiredOf("wood"));
        Assert.Contains("40 wood", plan.Plan.Reason);
    }

    /// <summary>A chest the player has not enabled is not material. Counting it
    /// would start a job that cannot be provisioned and discover it four stops
    /// in.</summary>
    [Fact]
    public void A_chest_the_player_has_not_enabled_is_not_material()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f) { Allowed = NpcContainerUse.Off };

        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 500)) });

        Assert.Equal(JobPlanVerdict.ShortOfMaterial, plan.Plan.Verdict);

        // A deposit-only chest is not a source either: take and deposit are
        // different trusts.
        chest.Allowed = NpcContainerUse.Deposit;
        Assert.Equal(JobPlanVerdict.ShortOfMaterial, Replan(chest).Plan.Verdict);

        chest.Allowed = NpcContainerUse.Take;
        Assert.Equal(JobPlanVerdict.Planned, Replan(chest).Plan.Verdict);

        // And a ward refuses an enabled chest just as hard.
        chest.Refusal = NpcContainerRefusal.WardDenied;
        Assert.Equal(JobPlanVerdict.ShortOfMaterial, Replan(chest).Plan.Verdict);
    }

    /// <summary>Nothing found is only a finished job when the looking finished.
    ///
    /// <b>The single most expensive thing an NPC can be wrong about.</b> Telling
    /// a player his settlement is done when the truth is that he walked away
    /// from it is how an NPC loses trust it does not get back.</summary>
    [Fact]
    public void Nothing_found_is_only_a_finished_job_when_the_looking_finished()
    {
        var area = new FakeArea();
        var probe = new FakeProbe();
        var observer = new PlanningStopObserver().Say("a", StopStatus.AlreadyDone);

        JobSnapshot conclusive = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f) },
            null,
            observer,
            probe,
            PlanningBudget.Unlimited());
        Assert.True(conclusive.IsConclusive);
        Assert.Equal(JobPlanVerdict.NothingToDo, Planner(conclusive).Plan(Ask(area)).Verdict);

        // The same empty result, with one candidate the probe could not see.
        var blind = new FakeProbe { Otherwise = AreaSampleVerdict.NotLoaded };
        JobSnapshot inconclusive = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f) },
            null,
            new PlanningStopObserver(),
            blind,
            PlanningBudget.Unlimited());
        Assert.False(inconclusive.IsConclusive);
        Assert.Equal(JobPlanVerdict.BudgetExhausted, Planner(inconclusive).Plan(Ask(area)).Verdict);

        // And a budget that ran out is never an empty area either.
        JobSnapshot truncated = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f), Jobs.Target("b", 11f, 0f) },
            null,
            new PlanningStopObserver().Say("a", StopStatus.AlreadyDone),
            probe,
            new PlanningBudget(1));
        Assert.True(truncated.Report.TruncatedByBudget);
        Assert.False(truncated.IsConclusive);
        Assert.Equal(JobPlanVerdict.BudgetExhausted, Planner(truncated).Plan(Ask(area)).Verdict);
    }

    /// <summary>A snapshot that needs more than the role said the job was for is
    /// refused rather than quietly provisioned. Taking a player's material for
    /// something nobody asked for is not a plan.</summary>
    [Fact]
    public void A_job_that_needs_more_than_it_was_asked_for_is_refused()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 50)) },
            new[] { Jobs.Stock(chest, ("wood", 500)) },
            NpcCarryCapacity.Unlimited,
            Jobs.Needs(("wood", 10)));

        Assert.Equal(JobPlanVerdict.Refused, plan.Plan.Verdict);
        Assert.Contains("40 wood", plan.Plan.Reason);
    }

    /// <summary>The same snapshot and the same request give the same plan, twice
    /// over - which is what lets an interruption tell "the world moved" from
    /// "the planner felt different about it".</summary>
    [Fact]
    public void The_same_inputs_give_the_same_plan()
    {
        JobTourPlan first = TwoTargetsTwoChests();
        JobTourPlan second = TwoTargetsTwoChests();

        Assert.Equal(Describe(first), Describe(second));
        Assert.Equal(first.BudgetSpent, second.BudgetSpent);
    }

    /// <summary>Calling the same planner twice does not give two answers, which
    /// is a real risk when the budget is an object the phases spend.</summary>
    [Fact]
    public void One_planner_asked_twice_answers_the_same()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var area = new FakeArea();
        JobSnapshot snapshot = Snapshot(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)), Jobs.Target("b", 20f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 100)) },
            area);

        var planner = new TourJobPlanner(snapshot, NpcCarryCapacity.Unlimited, Jobs.Actions);
        JobTourPlan first = planner.PlanTours(Ask(area));
        JobTourPlan second = planner.PlanTours(Ask(area));

        Assert.Equal(Describe(first), Describe(second));
        Assert.Equal(first.BudgetSpent, second.BudgetSpent);
    }

    /// <summary>A plan knows which world and which area it was computed
    /// against, and says so when either moves.</summary>
    [Fact]
    public void A_plan_goes_stale_when_the_area_or_the_world_moves()
    {
        JobTourPlan plan = TwoTargetsTwoChests();

        Assert.False(plan.Plan.IsStale(7, Jobs.World));
        Assert.True(plan.Plan.IsStale(8, Jobs.World));
        Assert.True(plan.Plan.IsStale(7, NpcWorldEpoch.Mint()));
        Assert.True(plan.Plan.IsStale(7, NpcWorldEpoch.Unknown));
    }

    /// <summary>A snapshot from another loading of the world plans nothing. Its
    /// names point at whatever now answers to them.</summary>
    [Fact]
    public void A_snapshot_from_another_world_plans_nothing()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var area = new FakeArea();
        JobSnapshot snapshot = Snapshot(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 100)) },
            area);

        var elsewhere = new JobPlanRequest(
            new Roles.NpcIdentity("product", "worker"),
            "job",
            area,
            NpcWorldEpoch.Mint(),
            JobManifest.Empty,
            Jobs.At(0f, 0f));

        Assert.Equal(JobPlanVerdict.Refused, Planner(snapshot).Plan(elsewhere).Verdict);
    }

    /// <summary>No work area is refused, never widened to anywhere.</summary>
    [Fact]
    public void No_work_area_is_refused_rather_than_widened()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobSnapshot snapshot = Snapshot(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 100)) },
            new FakeArea());

        Assert.Equal(
            JobPlanVerdict.AreaInvalid,
            Planner(snapshot).Plan(Jobs.Request(null, Jobs.At(0f, 0f))).Verdict);

        Assert.Equal(
            AreaScanOutcome.AreaInvalid,
            JobSnapshotBuilder.Take(
                null,
                Jobs.World,
                new[] { Jobs.Target("a", 10f, 0f) },
                null,
                new PlanningStopObserver(),
                new FakeProbe(),
                PlanningBudget.Unlimited()).Report.Outcome);
    }

    /// <summary>One target needing more than a whole trip is refused rather than
    /// split. What half a target means is the role's question.</summary>
    [Fact]
    public void One_target_bigger_than_a_trip_is_refused_rather_than_split()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 50)) },
            new[] { Jobs.Stock(chest, ("wood", 500)) },
            new NpcCarryCapacity(20));

        Assert.Equal(JobPlanVerdict.Refused, plan.Plan.Verdict);
        Assert.Empty(plan.Plan.Steps);
    }

    /// <summary>Two trips drawing on one chest do not both plan against its full
    /// contents.
    ///
    /// <b>The failure this is written against</b> is the obvious one: each trip
    /// chooses its chests against the snapshot as it was read, so the near chest
    /// looks full to the second trip as well, and the plan promises thirty units
    /// out of a chest that holds thirty and was already emptied of twenty. The
    /// NPC opens it on trip two and finds ten. Here the second trip sees what the
    /// first one left, so it walks to the far chest instead - the longer walk is
    /// the right answer, and getting it wrong is invisible until the game.
    /// </summary>
    [Fact]
    public void A_later_trip_plans_against_what_the_earlier_ones_left()
    {
        var near = new PlanningStockContainer("near", Jobs.World, 0f, 0f);
        var far = new PlanningStockContainer("far", Jobs.World, 50f, 0f);
        var targets = new List<JobTarget>
        {
            Jobs.Target("a", 5f, 0f, 0, ("wood", 10)),
            Jobs.Target("b", 6f, 0f, 0, ("wood", 10)),
            Jobs.Target("c", 7f, 0f, 0, ("wood", 10)),
            Jobs.Target("d", 8f, 0f, 0, ("wood", 10)),
        };

        JobTourPlan plan = Plan(
            targets,
            new[] { Jobs.Stock(near, ("wood", 30)), Jobs.Stock(far, ("wood", 100)) },
            new NpcCarryCapacity(20));

        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);
        Assert.Equal(2, plan.Tours.Count);
        Assert.Equal(40, Fetched(plan));

        // Trip one empties twenty of the near chest's thirty; trip two cannot be
        // served from the ten that are left, so it goes to the far one.
        Assert.Equal(20, DrawnFrom(plan.Provisioning[0], "near"));
        Assert.Equal(0, DrawnFrom(plan.Provisioning[0], "far"));
        Assert.Equal(0, DrawnFrom(plan.Provisioning[1], "near"));
        Assert.Equal(20, DrawnFrom(plan.Provisioning[1], "far"));

        // And no chest is ever promised more than it was seen to hold, which is
        // the invariant the assertions above are one instance of.
        Assert.True(DrawnFrom(plan.Provisioning[0], "near") + DrawnFrom(plan.Provisioning[1], "near") <= 30);
    }

    private static int DrawnFrom(SourcePlan supply, string key)
    {
        int units = 0;
        foreach (SourceDraw draw in supply.Draws)
        {
            if (draw.Key == key)
            {
                units += draw.Units;
            }
        }

        return units;
    }

    /// <summary>Nothing is ever drawn that the job does not need. An NPC that
    /// took a chest's whole stack because it was there has moved a player's
    /// material for nothing.</summary>
    [Fact]
    public void Nothing_is_drawn_that_the_job_does_not_need()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 3), ("nails", 1)) },
            new[] { Jobs.Stock(chest, ("wood", 999), ("nails", 999), ("stone", 999)) });

        Assert.Equal(4, Fetched(plan));
        foreach (SourceDraw draw in plan.Provisioning[0].Draws)
        {
            foreach (StockLine line in draw.Take)
            {
                Assert.NotEqual("stone", line.Item);
                Assert.True(line.Units <= plan.Plan.Manifest.RequiredOf(line.Item));
            }
        }
    }

    /// <summary>A target outside the work area is never planned for, whatever
    /// the role offered.</summary>
    [Fact]
    public void A_target_outside_the_work_area_is_never_planned_for()
    {
        var area = new FakeArea(radius: 20f);
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("inside", 10f, 0f), Jobs.Target("outside", 90f, 0f) },
            null,
            new PlanningStopObserver(),
            new FakeProbe(),
            PlanningBudget.Unlimited());

        Assert.Single(snapshot.Targets);
        Assert.Equal("inside", snapshot.Targets[0].Key);
        Assert.Equal(1, snapshot.Report.Rejected);
    }

    /// <summary>A role whose completion condition throws is a broken role, not a
    /// broken NPC in somebody else's product.</summary>
    [Fact]
    public void A_throwing_completion_condition_does_not_escape()
    {
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            new FakeArea(),
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f) },
            null,
            new PlanningStopObserver { Throws = true },
            new FakeProbe(),
            PlanningBudget.Unlimited());

        Assert.Empty(snapshot.Targets);
        Assert.False(snapshot.IsConclusive);
        Assert.Equal(1, snapshot.Report.NotLoaded);
    }

    private static JobTourPlan TwoTargetsTwoChests()
    {
        var wood = new PlanningStockContainer("wood", Jobs.World, 1f, 0f);
        var nails = new PlanningStockContainer("nails", Jobs.World, 2f, 0f);
        return Plan(
            new[]
            {
                Jobs.Target("a", 10f, 0f, 0, ("wood", 5), ("nails", 2)),
                Jobs.Target("b", 20f, 0f, 0, ("wood", 5)),
            },
            new[] { Jobs.Stock(wood, ("wood", 100)), Jobs.Stock(nails, ("nails", 100)) });
    }

    private static JobTourPlan Replan(PlanningStockContainer chest) =>
        Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 500)) });

    private static JobTourPlan Plan(
        IReadOnlyList<JobTarget> targets,
        IReadOnlyList<SourceStock> sources,
        NpcCarryCapacity capacity = default,
        JobManifest wanted = default)
    {
        var area = new FakeArea();
        JobSnapshot snapshot = Snapshot(targets, sources, area);
        NpcCarryCapacity room = capacity.UnitsPerTour == 0 ? NpcCarryCapacity.Unlimited : capacity;
        var planner = new TourJobPlanner(snapshot, room, Jobs.Actions);
        return planner.PlanTours(Jobs.Request(area, Jobs.At(0f, 0f), wanted));
    }

    private static JobSnapshot Snapshot(
        IReadOnlyList<JobTarget> targets, IReadOnlyList<SourceStock> sources, FakeArea area) =>
        JobSnapshotBuilder.Take(
            area, Jobs.World, targets, sources, new PlanningStopObserver(), new FakeProbe(), PlanningBudget.Unlimited());

    private static TourJobPlanner Planner(JobSnapshot snapshot) =>
        new TourJobPlanner(snapshot, NpcCarryCapacity.Unlimited, Jobs.Actions);

    private static JobPlanRequest Ask(FakeArea area) => Jobs.Request(area, Jobs.At(0f, 0f));

    private static int Collects(JobTourPlan plan)
    {
        int count = 0;
        foreach (PlannedStep step in plan.Steps)
        {
            if (step.IsCollect)
            {
                count++;
            }
        }

        return count;
    }

    private static int Services(JobTourPlan plan) => plan.Steps.Count - Collects(plan);

    private static int Fetched(JobTourPlan plan)
    {
        int units = 0;
        foreach (PlannedStep step in plan.Steps)
        {
            if (step.IsCollect)
            {
                units += step.Moves.TotalUnits;
            }
        }

        return units;
    }

    private static int FirstCollect(JobTourPlan plan)
    {
        foreach (PlannedStep step in plan.Steps)
        {
            if (step.IsCollect)
            {
                return step.Step.Index;
            }
        }

        return -1;
    }

    private static int LastCollect(JobTourPlan plan)
    {
        int last = -1;
        foreach (PlannedStep step in plan.Steps)
        {
            if (step.IsCollect)
            {
                last = step.Step.Index;
            }
        }

        return last;
    }

    private static int FirstService(JobTourPlan plan)
    {
        foreach (PlannedStep step in plan.Steps)
        {
            if (!step.IsCollect)
            {
                return step.Step.Index;
            }
        }

        return -1;
    }

    private static string[] Keys(JobTour tour)
    {
        var keys = new string[tour.Targets.Count];
        for (int index = 0; index < tour.Targets.Count; index++)
        {
            keys[index] = tour.Targets[index].Key;
        }

        return keys;
    }

    /// <summary>A plan written out, so two of them can be compared as one
    /// value. Comparing the steps field by field is what "the same plan"
    /// actually means.</summary>
    private static string Describe(JobTourPlan plan)
    {
        var text = new System.Text.StringBuilder();
        text.Append(plan.Plan.Verdict).Append('|').Append(plan.Plan.Reason).Append('|')
            .Append(ManifestArithmetic.Describe(plan.Plan.Manifest)).Append('|')
            .Append(plan.Tours.Count).Append('\n');
        foreach (JobStep step in plan.Plan.Steps)
        {
            text.Append(step.Index).Append(' ').Append(step.Action).Append(' ').Append(step.Subject)
                .Append(' ').Append(step.At).Append(' ').Append(step.Units).Append('\n');
        }

        return text.ToString();
    }
}
