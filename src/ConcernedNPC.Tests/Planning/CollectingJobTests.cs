using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A job whose targets FILL the NPC rather than empty him (0.5.0).
///
/// <b>The defect this closes.</b> A trip's capacity was spent only on what the
/// trip fetched. A collecting role's targets fetch nothing, so every one of them
/// cost a trip nothing and a whole job of loose stones came back as ONE trip
/// whatever he could carry - the planner had no way to say "this is one load".
/// <see cref="JobTarget.Carries"/> is that load, and these tests hold the
/// partition to the same rule a provisioning job is held to: the fewest trips
/// that fit, filled until the next target genuinely will not, never one target a
/// trip.</summary>
public sealed class CollectingJobTests : IDisposable
{
    private static readonly NpcIdentity Who = new NpcIdentity("product", "collector");
    private readonly NpcRoleRegistry _registry = new NpcRoleRegistry();
    private readonly NpcWorldEpoch _world;

    public CollectingJobTests()
    {
        Assert.Equal(RoleRegistrationStatus.Registered, _registry.Register(new CollectorNpcRole()).Status);
        _world = _registry.BeginWorldLoad(out _);
    }

    public void Dispose()
    {
        _registry.EndWorldLoad();
        NpcJobBooks.Forget();
    }

    [Fact]
    public void A_target_built_the_old_way_carries_nothing()
    {
        var target = new JobTarget("t", _world, new NpcPoint(1f, 0f, 1f), string.Empty, 0, JobManifest.Empty);
        Assert.Equal(0, target.Carries);
        Assert.Equal(0, target.TripLoad);
    }

    [Fact]
    public void A_negative_load_is_read_as_nothing()
    {
        var target = new JobTarget("t", _world, new NpcPoint(1f, 0f, 1f), string.Empty, 0, JobManifest.Empty, -4);
        Assert.Equal(0, target.Carries);
    }

    [Fact]
    public void A_load_too_large_to_add_saturates_rather_than_wrapping()
    {
        var needs = new JobManifest(new[] { new JobManifestLine("wood", 10) });
        var target = new JobTarget("t", _world, new NpcPoint(1f, 0f, 1f), string.Empty, 0, needs, int.MaxValue);
        Assert.Equal(int.MaxValue, target.TripLoad);
        Assert.Equal(TourPartitionOutcome.TargetTooLarge,
            TourPartitioner.Partition(new[] { target }, new NpcCarryCapacity(1000), Point(0f, 0f),
                PlanningBudget.Unlimited()).Outcome);
    }

    [Fact]
    public void A_collecting_job_is_partitioned_into_the_fewest_loads_that_fit()
    {
        // Twelve stones of one unit each, five to a load: three loads, filled.
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(Stone("s" + index.ToString("00"), index * 2f, 0f, 1));
        }

        var capacity = new NpcCarryCapacity(5);
        TourPartition partition = TourPartitioner.Partition(targets, capacity, Point(0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(TourPartitionOutcome.Planned, partition.Outcome);
        Assert.Equal(capacity.FewestToursFor(12), partition.Tours.Count);
        Assert.All(partition.Tours, tour => Assert.True(tour.Targets.Sum(t => t.Carries) <= 5));
        Assert.Equal(new[] { 5, 5, 2 }, partition.Tours.Select(tour => tour.Targets.Sum(t => t.Carries)).ToArray());
        Assert.True(partition.CoversTheWholeJob);
    }

    [Fact]
    public void Without_a_load_the_whole_collecting_job_was_one_trip()
    {
        // The defect, pinned so a revert of the partitioner is caught here: the
        // same twelve stones with no load are one trip at any capacity.
        var targets = new List<JobTarget>();
        for (int index = 0; index < 12; index++)
        {
            targets.Add(new JobTarget("s" + index, _world, Point(index * 2f, 0f), string.Empty, 0, JobManifest.Empty));
        }

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(5), Point(0f, 0f), PlanningBudget.Unlimited());
        Assert.Single(partition.Tours);
    }

    [Fact]
    public void A_single_source_larger_than_a_load_is_refused_rather_than_split()
    {
        TourPartition partition = TourPartitioner.Partition(
            new[] { Stone("big", 1f, 0f, 9) }, new NpcCarryCapacity(5), Point(0f, 0f), PlanningBudget.Unlimited());
        Assert.Equal(TourPartitionOutcome.TargetTooLarge, partition.Outcome);
        Assert.Empty(partition.Tours);
    }

    [Fact]
    public void Loads_are_geographically_coherent_rather_than_interleaved()
    {
        // Two clusters, far apart; a load of three. Each load comes from one
        // cluster, so he clears one end and then the other.
        var targets = new List<JobTarget>
        {
            Stone("east-a", 50f, 0f, 1), Stone("west-a", -50f, 0f, 1), Stone("east-b", 51f, 0f, 1),
            Stone("west-b", -51f, 0f, 1), Stone("east-c", 52f, 1f, 1), Stone("west-c", -52f, 1f, 1),
        };

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(3), Point(40f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(2, partition.Tours.Count);
        foreach (JobTour tour in partition.Tours)
        {
            string side = tour.Targets[0].Key.Substring(0, 4);
            Assert.All(tour.Targets, target => Assert.StartsWith(side, target.Key, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Through_the_driver_a_collecting_job_is_walked_load_by_load_and_finishes()
    {
        var role = new CollectorRole(_world);
        for (int index = 0; index < 7; index++)
        {
            role.Wants("s" + index, Point(3f + index, 0f), 2);
        }

        var order = new NpcJobOrder(
            Who, "collect", role.Area, _world, new NpcCarryCapacity(6), new JobStepActions("take", "pick"));
        NpcJobDriver driver = NpcJobDriver.For(order, role, _registry);

        var tours = new List<int>();
        int guard = 0;
        NpcJobAdvance advance = driver.Next(Point(0f, 0f));
        while (advance.Progress == NpcJobProgress.Do && guard++ < 100)
        {
            Assert.False(advance.Step.IsCollect, "a collecting job fetches nothing from any chest");
            tours.Add(advance.Step.Tour);
            role.Complete(advance.Step.Target.Key);
            driver.Done();
            advance = driver.Next(advance.Step.Step.At);
        }

        Assert.Equal(NpcJobProgress.Finished, advance.Progress);
        Assert.Equal(7, tours.Count);

        // Seven sources of two each, six to a load: three to a load, so three
        // loads (3 + 3 + 1), each walked whole before the next begins.
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1, 2 }, tours.ToArray());
    }

    [Fact]
    public void A_ground_probe_does_not_strip_what_a_target_leaves_him_carrying()
    {
        // The snapshot rebuilds every target a probe moves onto the ground. A
        // rebuild through the old constructor dropped Carries back to nothing,
        // and the whole job became one load again for any role with a probe.
        var role = new CollectorRole(_world, probe: true);
        for (int index = 0; index < 7; index++)
        {
            role.Wants("s" + index, Point(3f + index, 0f), 2);
        }

        var order = new NpcJobOrder(
            Who, "collect-probed", role.Area, _world, new NpcCarryCapacity(6), new JobStepActions("take", "pick"));
        NpcJobDriver driver = NpcJobDriver.For(order, role, _registry);

        var tours = new List<int>();
        int guard = 0;
        NpcJobAdvance advance = driver.Next(Point(0f, 0f));
        while (advance.Progress == NpcJobProgress.Do && guard++ < 100)
        {
            tours.Add(advance.Step.Tour);
            role.Complete(advance.Step.Target.Key);
            driver.Done();
            advance = driver.Next(advance.Step.Step.At);
        }

        Assert.Equal(NpcJobProgress.Finished, advance.Progress);
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1, 2 }, tours.ToArray());
    }

    private JobTarget Stone(string key, float x, float z, int carries) =>
        new JobTarget(key, _world, Point(x, z), string.Empty, 0, JobManifest.Empty, carries);

    private static NpcPoint Point(float x, float z) => new NpcPoint(x, 0f, z);

    private sealed class CollectorNpcRole : INpcRole
    {
        public NpcIdentity Identity => Who;

        public NpcBodyContract Body => NpcBodyContract.ForPresentation();

        public INpcDataPaths Paths { get; } = new CollectorPaths();
    }

    private sealed class CollectorPaths : INpcDataPaths
    {
        public string Root => Path.Combine(Path.GetTempPath(), "collecting-job-test");

        public bool TryResolveFile(string purpose, out string absolutePath)
        {
            absolutePath = Path.Combine(Root, purpose);
            return purpose.Length != 0;
        }
    }

    private sealed class CollectorArea : INpcWorkArea
    {
        public bool Contains(NpcPoint point) => point.HorizontalDistanceTo(BoundingCentre) <= BoundingRadiusMetres;

        public NpcPoint BoundingCentre => new NpcPoint(0f, 0f, 0f);

        public float BoundingRadiusMetres => 64f;

        public int Revision => 1;

        public string Describe => "the work area";
    }

    private sealed class StandableGround : INpcAreaProbe
    {
        public AreaSample Probe(NpcPoint point) =>
            new AreaSample(AreaSampleVerdict.Standable, point, AreaRejection.None);
    }

    private sealed class CollectorRole : INpcJobRole
    {
        private readonly NpcWorldEpoch _world;
        private readonly List<JobTarget> _targets = new List<JobTarget>();
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);

        internal CollectorRole(NpcWorldEpoch world, bool probe = false)
        {
            _world = world;
            Probe = probe ? new StandableGround() : null;
        }

        internal CollectorArea Area { get; } = new CollectorArea();

        public INpcAreaProbe? Probe { get; }

        public INpcSourceAvailability? Availability => null;

        internal void Wants(string key, NpcPoint at, int carries) =>
            _targets.Add(new JobTarget(key, _world, at, string.Empty, 0, JobManifest.Empty, carries));

        internal void Complete(string key) => _done.Add(key);

        public IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world) => _targets;

        public IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world) => Array.Empty<SourceStock>();

        public StopStatus Observe(in RouteStop stop) =>
            _done.Contains(stop.Key) ? StopStatus.AlreadyDone : StopStatus.Actionable;
    }
}
