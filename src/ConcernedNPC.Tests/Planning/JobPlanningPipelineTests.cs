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
        Assert.Equal(0, mine.NewlyTaken);

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

        // And it is counted as unreadable rather than as ground nobody has
        // walked to. Both are unknown and neither may ever add up to "there is
        // nothing here" - but walking over there fixes one of them and nothing
        // at all about the other, and the player is told which.
        Assert.Equal(1, snapshot.Report.Unreadable);
        Assert.Equal(0, snapshot.Report.NotLoaded);
        Assert.True(snapshot.Report.CountsAgree);
    }

    /// <summary>The two kinds of "I did not see" are counted apart, in both the
    /// places a snapshot can meet one.
    ///
    /// <b>The regression this is written against</b> is a role being told to
    /// walk the player over to a zone that is already loaded, because a probe
    /// that fell over was filed under ground that had not streamed in. The
    /// conclusion is the same either way - neither may add up to a finished job -
    /// and the diagnosis is not.</summary>
    [Fact]
    public void An_unanswerable_probe_is_not_ground_nobody_has_reached()
    {
        var area = new FakeArea();

        JobSnapshot unloaded = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f) },
            null,
            new PlanningStopObserver(),
            new FakeProbe { Otherwise = AreaSampleVerdict.NotLoaded },
            PlanningBudget.Unlimited());

        Assert.Equal(1, unloaded.Report.NotLoaded);
        Assert.Equal(0, unloaded.Report.Unreadable);

        JobSnapshot blind = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f) },
            null,
            new PlanningStopObserver(),
            new FakeProbe { Otherwise = AreaSampleVerdict.Unreadable },
            PlanningBudget.Unlimited());

        Assert.Equal(0, blind.Report.NotLoaded);
        Assert.Equal(1, blind.Report.Unreadable);

        // Same conclusion from both: unknown is never empty, and the outcome a
        // role acts on is the same one.
        Assert.False(unloaded.IsConclusive);
        Assert.False(blind.IsConclusive);
        Assert.Equal(AreaScanOutcome.NotLoaded, unloaded.Report.Outcome);
        Assert.Equal(AreaScanOutcome.NotLoaded, blind.Report.Outcome);
        Assert.True(unloaded.Report.CountsAgree);
        Assert.True(blind.Report.CountsAgree);
    }

    /// <summary>A job that needs more trips than one pass of planning can work
    /// out is <b>not</b> a plan. It is an ask-again.
    ///
    /// <b>The failure this is written against, in the reviewer's own numbers.</b>
    /// Twelve targets needing one wood each, a capacity of two, a budget of
    /// three. The partitioner builds two trips covering four targets and says it
    /// ran out. The planner used to read that outcome only when <i>no</i> trips
    /// came back, so with two it discarded the answer, returned
    /// <c>Planned</c> carrying the whole job's twelve-wood manifest over steps
    /// for four targets, and reconciled to complete with eight wall sections
    /// never built. Nothing anywhere said so, which is what makes it worse than
    /// failing loudly.</summary>
    [Fact]
    public void A_job_the_budget_could_not_finish_planning_is_never_a_plan()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(Jobs.Target("t" + index, index, 0f, 0, ("wood", 1)));
        }

        JobTourPlan plan = PlanWith(
            targets, new[] { Jobs.Stock(chest, ("wood", 100)) }, new NpcCarryCapacity(2), allowance: 3);

        Assert.Equal(JobPlanVerdict.BudgetExhausted, plan.Plan.Verdict);
        Assert.Empty(plan.Plan.Steps);
        Assert.False(plan.Plan.IsActionable);

        // And with room to finish, the same job plans in full.
        JobTourPlan whole = PlanWith(
            targets, new[] { Jobs.Stock(chest, ("wood", 100)) }, new NpcCarryCapacity(2), allowance: 512);
        Assert.Equal(JobPlanVerdict.Planned, whole.Plan.Verdict);
        Assert.Equal(12, Services(whole));
        Assert.True(whole.CoversTheWholeJob);
    }

    /// <summary>A job needing more trips than one plan writes out is planned for
    /// the trips it covers, carries only the manifest it covers, and <b>does not
    /// reconcile to finished</b>.
    ///
    /// <b>Why this is not refused outright.</b> The trip cap is deterministic:
    /// asking again gives the identical answer, so refusing would leave an NPC
    /// permanently unable to build a long wall. The honest answer is a real plan
    /// for part of it, with the rest counted - so that every step coming off
    /// perfectly still reads as "more to do" rather than as a finished
    /// job.</summary>
    [Fact]
    public void A_job_of_more_trips_than_one_plan_writes_out_never_reports_finished()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        int targetsPerTour = 2;
        int tours = TourPartitioner.MostTours + 4;
        var targets = new List<JobTarget>();
        for (int index = 0; index < tours * targetsPerTour; index++)
        {
            targets.Add(Jobs.Target("t" + index.ToString("00"), index, 0f, 0, ("wood", 1)));
        }

        JobTourPlan plan = Plan(
            targets, new[] { Jobs.Stock(chest, ("wood", 1000)) }, new NpcCarryCapacity(targetsPerTour));

        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);
        Assert.Equal(TourPartitioner.MostTours, plan.Tours.Count);

        int serviced = Services(plan);
        Assert.Equal(TourPartitioner.MostTours * targetsPerTour, serviced);

        // The manifest is over what the steps reach, not over what the job asked
        // for. This is the assertion that fails the moment a plan claims a total
        // it did not cover.
        Assert.Equal(serviced, plan.Plan.Manifest.RequiredOf("wood"));
        Assert.Equal(targets.Count - serviced, plan.LeftForAnotherRound);
        Assert.False(plan.CoversTheWholeJob);

        // Every single step done, and the job is still not finished.
        var results = new List<StepResult>();
        foreach (JobStep step in plan.Plan.Steps)
        {
            results.Add(new StepResult(step.Index, StepOutcome.Done));
        }

        JobReconciliation books = JobReconciler.Reconcile(plan, results);
        Assert.Equal(plan.Plan.Steps.Count, books.Done);
        Assert.True(books.Outstanding.IsEmpty);
        Assert.True(books.LeftOver.IsEmpty);
        Assert.False(books.IsComplete);
        Assert.True(books.NeedsAnotherRound);
        Assert.Equal(targets.Count - serviced, books.LeftForAnotherRound);
    }

    /// <summary>A job whose material is spread across more chests than one
    /// provisioning phase opens is not a job the settlement is short of.
    ///
    /// <b>The failure this is written against.</b> Forty wood sitting evenly
    /// across twelve enabled chests. The whole-job check passes - the material is
    /// demonstrably there - and then the chest cap stops the selection at eight
    /// with wood outstanding, and the player used to be told the settlement was
    /// short of wood that is in chests nine to twelve. Planning is deterministic,
    /// so the identical wrong sentence recurred every tick and the NPC was stuck
    /// for good on a doable job, with the refusal naming the wrong fix.</summary>
    [Fact]
    public void Material_spread_over_more_chests_than_one_phase_opens_is_not_a_shortage()
    {
        var chests = new List<SourceStock>();
        for (int index = 0; index < SourceSelector.MostStops + 4; index++)
        {
            chests.Add(Jobs.Stock(
                new PlanningStockContainer("c" + index.ToString("00"), Jobs.World, index, 0f), ("wood", 4)));
        }

        var targets = new List<JobTarget>();
        for (int index = 0; index < SourceSelector.MostStops + 4; index++)
        {
            targets.Add(Jobs.Target("t" + index.ToString("00"), 40f + index, 0f, 0, ("wood", 4)));
        }

        JobTourPlan plan = Plan(targets, chests);

        Assert.Equal(JobPlanVerdict.Planned, plan.Plan.Verdict);
        Assert.DoesNotContain("short", plan.Plan.Reason);

        // Eight chests were opened and they paid for eight targets. The other
        // four wait for the next round rather than being reported as missing
        // material.
        Assert.Equal(SourceSelector.MostStops, plan.Provisioning[0].Draws.Count);
        Assert.Equal(SourceSelector.MostStops, Services(plan));
        Assert.Equal(4, plan.LeftForAnotherRound);
        Assert.False(plan.CoversTheWholeJob);

        // And the plan's manifest is what those eight chests actually give him.
        Assert.Equal(SourceSelector.MostStops * 4, plan.Plan.Manifest.RequiredOf("wood"));
        Assert.Equal(SourceSelector.MostStops * 4, Fetched(plan));
    }

    /// <summary>A selection that ran out of budget is an ask-again, and never a
    /// sentence about the world.</summary>
    [Fact]
    public void A_selection_that_ran_out_of_budget_is_never_a_shortage()
    {
        var chests = new List<SourceStock>();
        for (int index = 0; index < 4; index++)
        {
            chests.Add(Jobs.Stock(
                new PlanningStockContainer("c" + index, Jobs.World, index, 0f), ("wood", 4)));
        }

        var targets = new List<JobTarget> { Jobs.Target("t", 40f, 0f, 0, ("wood", 16)) };

        // One unit for the partition, then the selection runs dry mid-choice.
        JobTourPlan plan = PlanWith(targets, chests, NpcCarryCapacity.Unlimited, allowance: 3);

        Assert.Equal(JobPlanVerdict.BudgetExhausted, plan.Plan.Verdict);
        Assert.Empty(plan.Plan.Steps);
        Assert.True(plan.Shortfall.IsEmpty);
    }

    /// <summary>A plan that does cover the whole job still reconciles to
    /// finished - the guard added for the partial case must not make every
    /// finished job report itself unfinished.</summary>
    [Fact]
    public void A_plan_that_covers_the_job_still_reports_finished()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        JobTourPlan plan = Plan(
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 100)) });

        Assert.True(plan.CoversTheWholeJob);
        Assert.Equal(0, plan.LeftForAnotherRound);

        var results = new List<StepResult>();
        foreach (JobStep step in plan.Plan.Steps)
        {
            results.Add(new StepResult(step.Index, StepOutcome.Done));
        }

        Assert.True(JobReconciler.Reconcile(plan, results).IsComplete);
    }

    /// <summary>A partition that ran out of budget hands back nothing at all.
    ///
    /// <b>Why nothing rather than what it managed.</b> A trip built against half
    /// a budget has contents that depend on where the allowance happened to run
    /// out, and no caller can tell that from a trip that was worked out
    /// properly. The planner refuses on the outcome as well, and the selection
    /// refuses on its own spent budget after that - three guards over one
    /// mistake, because the mistake is a job that quietly becomes a smaller job
    /// and says it finished.</summary>
    [Fact]
    public void A_partition_that_ran_out_of_budget_hands_back_no_trips()
    {
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(Jobs.Target("t" + index.ToString("00"), index, 0f, 0, ("wood", 1)));
        }

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(2), Jobs.At(0f, 0f), new PlanningBudget(3));

        Assert.Equal(TourPartitionOutcome.BudgetExhausted, partition.Outcome);
        Assert.Empty(partition.Tours);
        Assert.False(partition.IsPlanned);
        Assert.True(partition.LeftOver > 0);
        Assert.False(partition.CoversTheWholeJob);

        // And a partition that did finish says so, with nothing left over.
        TourPartition whole = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(2), Jobs.At(0f, 0f), PlanningBudget.Unlimited());
        Assert.Equal(TourPartitionOutcome.Planned, whole.Outcome);
        Assert.Equal(0, whole.LeftOver);
        Assert.True(whole.CoversTheWholeJob);
        Assert.Equal(12, whole.Serviced);
    }

    /// <summary>The blocker's invariant, stated once and checked over every
    /// allowance from nothing to plenty: <b>a plan never claims a manifest its
    /// own steps do not cover, and every target it does not reach is
    /// counted.</b>
    ///
    /// Written as a sweep rather than as one case on purpose. The failure had
    /// two independent causes - a spent budget and the trip cap - and a third
    /// was reachable through provisioning; a test pinned to one of them would
    /// have gone green while the other two shipped. What is asserted here is the
    /// property all three have to preserve, so it holds whichever guard a later
    /// change removes.</summary>
    [Fact]
    public void A_plan_never_claims_a_manifest_its_steps_do_not_cover()
    {
        var chest = new PlanningStockContainer("supply", Jobs.World, 0f, 0f);
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(Jobs.Target("t" + index.ToString("00"), index, 0f, 0, ("wood", 1)));
        }

        var sources = new[] { Jobs.Stock(chest, ("wood", 100)) };
        int everPlanned = 0;
        int everPartial = 0;

        for (int allowance = 0; allowance <= 40; allowance++)
        {
            JobTourPlan plan = PlanWith(targets, sources, new NpcCarryCapacity(2), allowance);

            if (plan.Plan.Verdict != JobPlanVerdict.Planned)
            {
                // Every refusal on this input is an ask-again, never a sentence
                // about the world and never a finished job.
                Assert.Equal(JobPlanVerdict.BudgetExhausted, plan.Plan.Verdict);
                Assert.Empty(plan.Plan.Steps);
                continue;
            }

            everPlanned++;

            int serviced = 0;
            int servicedUnits = 0;
            foreach (PlannedStep step in plan.Steps)
            {
                if (step.IsCollect)
                {
                    continue;
                }

                serviced++;
                servicedUnits += step.Step.Units;
            }

            // The manifest is exactly what the steps reach.
            Assert.Equal(servicedUnits, plan.Plan.Manifest.TotalUnits);

            // And nothing the plan leaves out goes unaccounted for.
            Assert.Equal(targets.Count, serviced + plan.LeftForAnotherRound);

            var results = new List<StepResult>();
            foreach (JobStep step in plan.Plan.Steps)
            {
                results.Add(new StepResult(step.Index, StepOutcome.Done));
            }

            JobReconciliation books = JobReconciler.Reconcile(plan, results);
            Assert.Equal(plan.CoversTheWholeJob, books.IsComplete);

            if (!plan.CoversTheWholeJob)
            {
                everPartial++;
                Assert.True(books.NeedsAnotherRound);
            }
        }

        // The sweep has to have reached both shapes, or it proved nothing about
        // either.
        Assert.True(everPlanned > 0, "no allowance in the sweep produced a plan at all");
        Assert.True(everPartial == 0 || everPlanned > everPartial);
    }

    private static JobTourPlan PlanWith(
        IReadOnlyList<JobTarget> targets,
        IReadOnlyList<SourceStock> sources,
        NpcCarryCapacity capacity,
        int allowance)
    {
        var area = new FakeArea();
        JobSnapshot snapshot = Snapshot(targets, sources, area);
        var planner = new TourJobPlanner(
            snapshot, capacity, Jobs.Actions, setbacks: null, now: 0f, allowance: allowance);
        return planner.PlanTours(Jobs.Request(area, Jobs.At(0f, 0f)));
    }

    /// <summary>A plan refused one of the things it needs takes nothing at all.
    ///
    /// <b>The failure this is written against.</b> The attempt used to walk every
    /// step and keep going after a refusal, so a conflict at step five left steps
    /// nought to four held in the books - by a plan that is not walkable, since
    /// the runtime has no reason to walk a plan it was refused part of. A caller
    /// that read <c>AllHeld</c>, saw false and dropped the result would leak all
    /// four until the world unloaded, and dropping a result on the floor is the
    /// commonest thing any caller does.</summary>
    [Fact]
    public void A_plan_refused_one_subject_takes_nothing_at_all()
    {
        JobTourPlan plan = TwoTargetsTwoChests();
        var targetBook = new FakeBook<JobTarget>(Jobs.World);
        var chestBook = new FakeBook<INpcContainer>(Jobs.World);

        // Somebody else already has the first target the plan would service, so
        // the refusal lands with chests already taken out and steps still to go.
        JobTarget contested = default;
        foreach (PlannedStep step in plan.Steps)
        {
            if (!step.IsCollect)
            {
                contested = step.Target;
                break;
            }
        }

        Assert.Equal(
            ReservationOutcome.Reserved, targetBook.Reserve(contested, ReservationId.For("theirs", 0)));
        int theirs = targetBook.Count;

        var targets = new JobCommitment<JobTarget>("mine", targetBook);
        var chests = new JobCommitment<INpcContainer>("mine", chestBook);
        JobReservationResult result = PlanReservations.TakeOut(plan, targets, chests);

        Assert.False(result.AllHeld);
        Assert.True(result.RolledBack);

        // Nothing of ours is left in either book, and theirs is untouched.
        Assert.Equal(theirs, targetBook.Count);
        Assert.Equal(0, chestBook.Count);
        Assert.True(targets.IsCancelled);
        Assert.True(chests.IsCancelled);

        // And it stopped at the refusal rather than carrying on through the
        // rest of the plan.
        Assert.True(result.Attempts.Count < plan.Steps.Count);
    }

    /// <summary>Re-establishing a job's own holds after an interruption counts
    /// as nothing newly taken - and cancelling still gives every one of them
    /// back.
    ///
    /// <b>The trap this is written against</b> is a caller written as
    /// <c>if (c.NewlyTaken > 0) c.Cancel();</c>. On the recovery path every
    /// answer is "already satisfied", the count is nought, and that caller would
    /// leak the entire job's reservations on the one path recovery
    /// takes.</summary>
    [Fact]
    public void Re_establishing_holds_takes_nothing_new_and_cancelling_still_returns_them()
    {
        var book = new FakeBook<JobTarget>(Jobs.World);
        JobTarget target = Jobs.Target("a", 10f, 0f, 0, ("wood", 1));

        var before = new JobCommitment<JobTarget>("job", book);
        Assert.Equal(ReservationOutcome.Reserved, before.Reserve(target, 0));
        Assert.Equal(1, before.NewlyTaken);

        // The world reloaded, or the runtime was rebuilt: a fresh commitment
        // over the same book, walking the same plan.
        var after = new JobCommitment<JobTarget>("job", book);
        Assert.Equal(ReservationOutcome.AlreadySatisfied, after.Reserve(target, 0));
        Assert.Equal(0, after.NewlyTaken);

        // Nothing newly taken, and everything still given back.
        Assert.Equal(1, after.Cancel());
        Assert.Equal(0, book.Count);
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
