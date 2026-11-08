using System;
using System.Collections.Generic;
using System.IO;
using Shared.Settlement.Tests;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;

namespace ConcernedForeman.Tests;

/// <summary>#379 end to end: Thorstein's real collection loop, witnessed by his
/// real shared plan on a real plan file, interrupted and reloaded. A pick the
/// process died inside is never repeated and nothing more moves until a person
/// resolves it; a pause or a reload between movements continues the same plan;
/// and every source is picked exactly once across the interruption.</summary>
public sealed class ThorsteinCollectionPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cf-379-loop", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ThorsteinCollectionPlanTests()
    {
        Directory.CreateDirectory(_root);
        _path = new ThorsteinPlanFiles(_root).ResolvePath(new SettlementScope(77, new SettlementId("camp")), ThorsteinWorkPlan.CollectJobId);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A locked temp file must never fail the suite.
        }
    }

    private static NpcPlanEvidence Evidence(NpcWorldEpoch world) =>
        new NpcPlanEvidence(world, 1, false, true, false, true, true, true, false, 0f);

    private sealed class Session
    {
        public Session(string path, NpcWorldEpoch world)
        {
            World = world;
            Plan = new ThorsteinWorkPlan(ForemanNpcRole.Id, ThorsteinWorkPlan.CollectJobId);
            Plan.Open(path, null);
            CollectionRig? rig = null;
            Rig = new CollectionRig(witness: new CollectionPlanWitness(Plan, () => Evidence(world), () => Measure(rig!)));
            rig = Rig;
        }

        public NpcWorldEpoch World { get; }

        public ThorsteinWorkPlan Plan { get; }

        public CollectionRig Rig { get; }

        public static IReadOnlyList<KeyValuePair<string, int>> Measure(CollectionRig rig) =>
            new[]
            {
                new KeyValuePair<string, int>("Stone", rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone))),
                new KeyValuePair<string, int>("Wood", rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Wood))),
            };
    }

    [Fact]
    public void APickTheProcessDiedInsideIsNeverRepeatedAndNothingMovesUntilResolved()
    {
        var first = new Session(_path, NpcWorldEpoch.Mint());
        var keys = new List<SourceKey>();
        for (int index = 0; index < 4; index++)
        {
            keys.Add(first.Rig.AddSource(CollectedResource.Stone, 4f + index, 4f));
        }

        Assert.Equal(CollectionIntakeRefusal.Unspecified, first.Rig.Accept(first.Rig.Order(stone: 4, wood: 0, hold: true)));
        int dieAt = 2;
        first.Rig.Pickup.DieOn = _ => first.Rig.Pickup.Picks == dieAt;
        Assert.Throws<InvalidOperationException>(() => first.Rig.RunUntil(() => false, maxTicks: 2000));
        Assert.Equal(dieAt, first.Rig.Pickup.Picks);

        // The process is gone. The next one finds the announced pick with no
        // outcome, and refuses the order rather than picking anything.
        var second = new Session(_path, NpcWorldEpoch.Mint());
        Assert.Equal(ThorsteinPlanStanding.Recovered, second.Plan.Standing);
        for (int index = dieAt; index < keys.Count; index++)
        {
            second.Rig.AddSource(CollectedResource.Stone, 4f + index, 4f);
        }

        Assert.Equal(CollectionIntakeRefusal.WorkPlanBlocked, second.Rig.Accept(second.Rig.Order(stone: 4, wood: 0, hold: true)));
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, second.Plan.Standing);
        second.Rig.Tick(200);
        Assert.Equal(0, second.Rig.Pickup.Picks);

        // A person looks and resolves from what he holds; the work then goes on,
        // and only the sources nobody picked are picked.
        Assert.True(second.Plan.Resolve(second.World, Session.Measure(second.Rig), out string said), said);
        Assert.Equal(CollectionIntakeRefusal.Unspecified, second.Rig.Accept(second.Rig.Order(stone: 2, wood: 0, hold: true, id: "collect-2")));
        Assert.True(second.Rig.RunUntil(() => second.Rig.Loop.State == CollectionOrderState.HoldingForPlayer));
        Assert.Equal(keys.Count - dieAt, second.Rig.Pickup.Picks);
    }

    [Fact]
    public void AReloadBetweenMovementsContinuesTheSamePlanAndPicksEverySourceOnce()
    {
        var first = new Session(_path, NpcWorldEpoch.Mint());
        for (int index = 0; index < 4; index++)
        {
            first.Rig.AddSource(CollectedResource.Stone, 4f + index, 4f);
        }

        first.Rig.Accept(first.Rig.Order(stone: 4, wood: 0, hold: true));
        Assert.True(first.Rig.RunUntil(() => first.Rig.Pickup.Picks == 2));
        Assert.Equal(ControlOutcome.Done, first.Rig.Loop.Pause(first.Rig.Now).Outcome);
        Assert.Equal(ThorsteinPlanStanding.Suspended, first.Plan.Standing);
        int carried = first.Rig.WorkerInventory.Count(MaterialItem.Of(CollectedResource.Stone));

        // Reloaded: the same plan comes back, is revalidated by the next start,
        // and continues; he still carries what he carried.
        var second = new Session(_path, NpcWorldEpoch.Mint());
        Assert.Equal(ThorsteinPlanStanding.Recovered, second.Plan.Standing);
        second.Rig.WorkerInventory.Set(CollectedResource.Stone, carried);
        second.Rig.AddSource(CollectedResource.Stone, 6f, 4f);
        second.Rig.AddSource(CollectedResource.Stone, 7f, 4f);
        Assert.Equal(CollectionIntakeRefusal.Unspecified, second.Rig.Accept(second.Rig.Order(stone: 2, wood: 0, hold: true, id: "collect-2")));
        Assert.Equal(ThorsteinPlanStanding.Running, second.Plan.Standing);
        Assert.Equal(carried, second.Plan.State!.CarriedUnits);
        Assert.True(second.Plan.State.World.Matches(second.World));
        Assert.True(second.Rig.RunUntil(() => second.Rig.Loop.State == CollectionOrderState.HoldingForPlayer));
        Assert.Equal(2, second.Rig.Pickup.Picks);
    }
}
