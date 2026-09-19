using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The arithmetic underneath the pipeline, tested for the answers it is
/// relied on for rather than for the shape it happens to have.</summary>
public sealed class PlanningArithmeticTests
{
    /// <summary>Two targets needing the same thing are one line, and the line
    /// order is the order the items were first seen - so the same targets give
    /// the same manifest, line for line, and a plan can be compared against the
    /// manifest it was computed from.</summary>
    [Fact]
    public void A_total_merges_lines_and_keeps_the_order_they_were_first_seen()
    {
        JobManifest total = ManifestArithmetic.Total(new[]
        {
            Jobs.Target("a", 0f, 0f, 0, ("wood", 5), ("nails", 2)),
            Jobs.Target("b", 0f, 0f, 0, ("nails", 3), ("wood", 1)),
        });

        Assert.Equal(2, total.Lines.Count);
        Assert.Equal("wood", total.Lines[0].Item);
        Assert.Equal(6, total.Lines[0].Required);
        Assert.Equal("nails", total.Lines[1].Item);
        Assert.Equal(5, total.Lines[1].Required);
        Assert.Equal(11, total.TotalUnits);
    }

    /// <summary>Subtraction clamps at nothing and drops the line. A negative
    /// requirement would make a surplus look like a need on the next
    /// subtraction.</summary>
    [Fact]
    public void Subtraction_clamps_at_nothing_and_drops_the_line()
    {
        JobManifest left = ManifestArithmetic.Subtract(
            Jobs.Needs(("wood", 5), ("nails", 2)), Jobs.Needs(("wood", 9), ("nails", 1)));

        Assert.Single(left.Lines);
        Assert.Equal("nails", left.Lines[0].Item);
        Assert.Equal(1, left.Lines[0].Required);
        Assert.Equal(0, left.RequiredOf("wood"));
    }

    /// <summary>A shortfall counts only what may be used right now. A chest
    /// behind a ward is not material.</summary>
    [Fact]
    public void A_shortfall_counts_only_what_may_be_used_now()
    {
        var open = new PlanningStockContainer("open", Jobs.World, 0f, 0f);
        var warded = new PlanningStockContainer("warded", Jobs.World, 1f, 0f)
        {
            Refusal = Containers.NpcContainerRefusal.WardDenied,
        };

        var sources = new List<SourceStock>
        {
            Jobs.Stock(open, ("wood", 4)),
            Jobs.Stock(warded, ("wood", 100)),
        };

        JobManifest missing = ManifestArithmetic.Shortfall(Jobs.Needs(("wood", 10)), JobManifest.Empty, sources);
        Assert.Equal(6, missing.RequiredOf("wood"));

        warded.Refusal = Containers.NpcContainerRefusal.None;
        Assert.True(ManifestArithmetic.Shortfall(Jobs.Needs(("wood", 10)), JobManifest.Empty, sources).IsEmpty);
    }

    /// <summary>An empty allowance permits anything, because a role that did not
    /// state a total is not stating a limit of nothing.</summary>
    [Fact]
    public void An_unstated_allowance_is_not_a_limit_of_nothing()
    {
        Assert.False(ManifestArithmetic.Exceeds(Jobs.Needs(("wood", 5)), JobManifest.Empty, out _));
        Assert.True(ManifestArithmetic.Exceeds(Jobs.Needs(("wood", 5)), Jobs.Needs(("wood", 4)), out JobManifestLine over));
        Assert.Equal("wood", over.Item);
        Assert.Equal(1, over.Required);
    }

    /// <summary>A manifest reads as a sentence a person could act on.</summary>
    [Fact]
    public void A_manifest_reads_as_a_sentence()
    {
        Assert.Equal("40 wood, 12 nails", ManifestArithmetic.Describe(Jobs.Needs(("wood", 40), ("nails", 12))));
        Assert.Equal(string.Empty, ManifestArithmetic.Describe(JobManifest.Empty));
    }

    /// <summary>A defaulted capacity carries nothing. Unlimited is a value
    /// somebody chose, not one anybody arrives at by accident.</summary>
    [Fact]
    public void A_defaulted_capacity_carries_nothing()
    {
        NpcCarryCapacity none = default;

        Assert.Equal(0, none.UnitsPerTour);
        Assert.False(none.IsUnlimited);
        Assert.False(none.RoomFor(0, 1));
        Assert.True(none.RoomFor(0, 0));
        Assert.True(NpcCarryCapacity.Unlimited.RoomFor(int.MaxValue - 1, 5));
    }

    /// <summary>The fewest trips is the number a partition is held to.</summary>
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(90, 30, 3)]
    [InlineData(91, 30, 4)]
    public void The_fewest_trips_is_what_it_says(int units, int perTour, int tours)
    {
        Assert.Equal(tours, new NpcCarryCapacity(perTour).FewestToursFor(units));
    }

    /// <summary>A budget of nothing refuses everything, and a budget never goes
    /// below zero by half-spending.</summary>
    [Fact]
    public void A_budget_of_nothing_refuses_everything()
    {
        var nothing = new PlanningBudget(0);
        Assert.True(nothing.IsExhausted);
        Assert.False(nothing.TrySpend());

        var three = new PlanningBudget(3);
        Assert.True(three.TrySpend(2));
        Assert.False(three.TrySpend(2));
        Assert.Equal(1, three.Remaining);
        Assert.True(three.TrySpend(1));
        Assert.True(three.IsExhausted);

        Assert.Equal(0, new PlanningBudget(-5).Allowance);
    }

    /// <summary>A trip is filled before the next one starts. A partitioner that
    /// started a trip per target would pass every other test in the suite and
    /// produce the behaviour the whole package exists to replace.</summary>
    [Fact]
    public void Trips_are_filled_before_the_next_one_starts()
    {
        var targets = new List<JobTarget>();
        for (int index = 0; index < 7; index++)
        {
            targets.Add(Jobs.Target("t" + index, index, 0f, 0, ("wood", 4)));
        }

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(12), Jobs.At(0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(TourPartitionOutcome.Planned, partition.Outcome);
        Assert.Equal(3, partition.Tours.Count);
        Assert.Equal(7, partition.Serviced);
        Assert.Equal(3, partition.Tours[0].Targets.Count);
        Assert.Equal(3, partition.Tours[1].Targets.Count);
        Assert.Single(partition.Tours[2].Targets);
    }

    /// <summary>A job that consumes nothing is one trip, however many targets it
    /// has. Collecting is a job with targets and no manifest, and batching it by
    /// a capacity of nothing would be one target a trip forever.</summary>
    [Fact]
    public void A_job_that_consumes_nothing_is_one_trip()
    {
        var targets = new List<JobTarget>();
        for (int index = 0; index < 20; index++)
        {
            targets.Add(Jobs.Target("t" + index, index, 0f));
        }

        TourPartition partition = TourPartitioner.Partition(
            targets, NpcCarryCapacity.Unlimited, Jobs.At(0f, 0f), PlanningBudget.Unlimited());

        Assert.Single(partition.Tours);
        Assert.Equal(20, partition.Tours[0].Targets.Count);
        Assert.True(partition.Tours[0].Provision.IsEmpty);
    }

    /// <summary>Priority decides which trip a target is in, not only where it
    /// comes in one.</summary>
    [Fact]
    public void Priority_decides_which_trip_a_target_is_in()
    {
        var targets = new List<JobTarget>
        {
            Jobs.Target("ordinary far", 60f, 0f, 0, ("wood", 10)),
            Jobs.Target("ordinary near", 1f, 0f, 0, ("wood", 10)),
            Jobs.Target("urgent", 90f, 0f, 5, ("wood", 10)),
        };

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(20), Jobs.At(0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(2, partition.Tours.Count);
        Assert.Equal("urgent", partition.Tours[0].Targets[0].Key);
    }

    /// <summary>A partition that ran out of budget is incomplete, never a
    /// smaller job.</summary>
    [Fact]
    public void A_partition_that_ran_out_of_budget_says_so()
    {
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(Jobs.Target("t" + index, index, 0f, 0, ("wood", 1)));
        }

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(2), Jobs.At(0f, 0f), new PlanningBudget(3));

        Assert.Equal(TourPartitionOutcome.BudgetExhausted, partition.Outcome);
        Assert.True(partition.Serviced < 12);
    }

    /// <summary>Provisioning a job that needs nothing is not a failure.
    /// </summary>
    [Fact]
    public void Needing_nothing_is_not_a_failure()
    {
        SourcePlan plan = SourceSelector.Select(
            JobManifest.Empty, null, Jobs.At(0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(SourceSelectionOutcome.NothingNeeded, plan.Outcome);
        Assert.True(plan.IsComplete);
        Assert.Equal(0, plan.Stops);
    }

    /// <summary>Nothing to draw from is its own answer, distinct from having
    /// drawn some of it.</summary>
    [Fact]
    public void Nothing_to_draw_from_is_its_own_answer()
    {
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);

        SourcePlan none = SourceSelector.Select(
            Jobs.Needs(("wood", 5)),
            new[] { Jobs.Stock(chest, ("stone", 50)) },
            Jobs.At(0f, 0f),
            PlanningBudget.Unlimited());
        Assert.Equal(SourceSelectionOutcome.NoUsableSource, none.Outcome);
        Assert.Equal(5, none.Shortfall.RequiredOf("wood"));

        SourcePlan some = SourceSelector.Select(
            Jobs.Needs(("wood", 5)),
            new[] { Jobs.Stock(chest, ("wood", 2)) },
            Jobs.At(0f, 0f),
            PlanningBudget.Unlimited());
        Assert.Equal(SourceSelectionOutcome.Partial, some.Outcome);
        Assert.Equal(3, some.Shortfall.RequiredOf("wood"));
        Assert.Equal(1, some.Stops);
    }

    /// <summary>Among chests that are equally good, the nearest wins - measured
    /// from where the walk will be, and tie-broken by name so the answer does not
    /// depend on which was read first.</summary>
    [Fact]
    public void Equally_good_chests_are_decided_by_distance_then_by_name()
    {
        var far = new PlanningStockContainer("a far", Jobs.World, 50f, 0f);
        var near = new PlanningStockContainer("z near", Jobs.World, 5f, 0f);

        SourcePlan plan = SourceSelector.Select(
            Jobs.Needs(("wood", 5)),
            new[] { Jobs.Stock(far, ("wood", 50)), Jobs.Stock(near, ("wood", 50)) },
            Jobs.At(0f, 0f),
            PlanningBudget.Unlimited());
        Assert.Equal("z near", plan.Draws[0].Key);

        var left = new PlanningStockContainer("b", Jobs.World, 5f, 0f);
        var right = new PlanningStockContainer("a", Jobs.World, -5f, 0f);
        SourcePlan tied = SourceSelector.Select(
            Jobs.Needs(("wood", 5)),
            new[] { Jobs.Stock(left, ("wood", 50)), Jobs.Stock(right, ("wood", 50)) },
            Jobs.At(0f, 0f),
            PlanningBudget.Unlimited());
        Assert.Equal("a", tied.Draws[0].Key);
    }

    /// <summary>A round that was walked is added up honestly: what is still
    /// owed, and what is still being carried.</summary>
    [Fact]
    public void A_round_is_added_up_from_what_was_planned_and_what_happened()
    {
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var area = new FakeArea();
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[]
            {
                Jobs.Target("a", 10f, 0f, 0, ("wood", 5)),
                Jobs.Target("b", 20f, 0f, 0, ("wood", 5)),
                Jobs.Target("c", 30f, 0f, 0, ("wood", 5)),
            },
            new[] { Jobs.Stock(chest, ("wood", 100)) },
            new PlanningStopObserver(),
            new FakeProbe(),
            PlanningBudget.Unlimited());

        JobTourPlan plan = new TourJobPlanner(snapshot, NpcCarryCapacity.Unlimited, Jobs.Actions)
            .PlanTours(Jobs.Request(area, Jobs.At(0f, 0f)));

        Assert.Equal(4, plan.Plan.Steps.Count);

        // Fetched fifteen; one target serviced, one turned out already done,
        // one never reached.
        JobReconciliation books = JobReconciler.Reconcile(plan, new[]
        {
            new StepResult(0, StepOutcome.Done),
            new StepResult(1, StepOutcome.Done),
            new StepResult(2, StepOutcome.Skipped),
            new StepResult(3, StepOutcome.NotReached),
        });

        Assert.Equal(4, books.Planned);
        Assert.Equal(2, books.Done);
        Assert.Equal(1, books.Skipped);
        Assert.Equal(1, books.NotReached);
        Assert.Equal(10, books.LeftOver.RequiredOf("wood"));
        Assert.Equal(5, books.Outstanding.RequiredOf("wood"));
        Assert.False(books.IsComplete);
        Assert.True(books.HasUnfinishedWork);
    }

    /// <summary>A round where everything was done owes nothing and carries
    /// nothing - and a step nobody reported on is never taken as done.</summary>
    [Fact]
    public void A_finished_round_owes_nothing_and_an_unreported_step_is_never_done()
    {
        var chest = new PlanningStockContainer("chest", Jobs.World, 0f, 0f);
        var area = new FakeArea();
        JobSnapshot snapshot = JobSnapshotBuilder.Take(
            area,
            Jobs.World,
            new[] { Jobs.Target("a", 10f, 0f, 0, ("wood", 5)) },
            new[] { Jobs.Stock(chest, ("wood", 100)) },
            new PlanningStopObserver(),
            new FakeProbe(),
            PlanningBudget.Unlimited());

        JobTourPlan plan = new TourJobPlanner(snapshot, NpcCarryCapacity.Unlimited, Jobs.Actions)
            .PlanTours(Jobs.Request(area, Jobs.At(0f, 0f)));

        JobReconciliation all = JobReconciler.Reconcile(plan, new[]
        {
            new StepResult(0, StepOutcome.Done),
            new StepResult(1, StepOutcome.Done),
        });
        Assert.True(all.IsComplete);
        Assert.True(all.LeftOver.IsEmpty);
        Assert.True(all.Outstanding.IsEmpty);

        JobReconciliation silent = JobReconciler.Reconcile(plan, null);
        Assert.False(silent.IsComplete);
        Assert.Equal(2, silent.NotReached);
        Assert.Equal(5, silent.Outstanding.RequiredOf("wood"));
    }
}
