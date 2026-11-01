using System;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A trip topped up from below its seed's band takes the next most
/// important targets first, and only then the nearest (#382).</summary>
public sealed class TourPartitionerBandTests : IDisposable
{
    private readonly NpcRoleRegistry _registry = new NpcRoleRegistry();
    private readonly NpcWorldEpoch _world;

    public TourPartitionerBandTests()
    {
        _world = _registry.BeginWorldLoad(out _);
    }

    public void Dispose() => _registry.EndWorldLoad();

    private JobTarget Target(string key, float x, int priority, int units) =>
        new JobTarget(
            key,
            _world,
            new NpcPoint(x, 0f, 0f),
            string.Empty,
            priority,
            new JobManifest(new[] { new JobManifestLine("wood", units) }));

    [Fact]
    public void Below_the_seeds_band_the_higher_band_fills_before_the_nearer_lower_one()
    {
        // A role packed {A, B, C} as one trip and D as the next. A has a band of
        // its own; D sits right beside it. Nearness alone would take D into the
        // first trip and push C out of it.
        JobTarget[] targets =
        {
            Target("A", 0f, 5, 4),
            Target("B", 10f, 2, 3),
            Target("C", 12f, 2, 3),
            Target("D", 1f, 1, 3),
        };

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(10), new NpcPoint(0f, 0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(2, partition.Tours.Count);
        Assert.Equal(new[] { "A", "B", "C" }, partition.Tours[0].Targets.Select(t => t.Key).OrderBy(k => k).ToArray());
        Assert.Equal(new[] { "D" }, partition.Tours[1].Targets.Select(t => t.Key).ToArray());
    }

    [Fact]
    public void Within_one_band_the_nearest_still_comes_first()
    {
        JobTarget[] targets =
        {
            Target("seed", 0f, 3, 2),
            Target("far", 20f, 1, 2),
            Target("near", 2f, 1, 2),
        };

        TourPartition partition = TourPartitioner.Partition(
            targets, new NpcCarryCapacity(4), new NpcPoint(0f, 0f, 0f), PlanningBudget.Unlimited());

        Assert.Equal(new[] { "seed", "near" }, partition.Tours[0].Targets.Select(t => t.Key).ToArray());
    }
}
