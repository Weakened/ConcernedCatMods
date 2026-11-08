using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Identity;

namespace ConcernedForeman.Tests;

/// <summary>#379: Thorstein's shared-interruption plan, against real plan files.
///
/// <b>How a kill is made.</b> The plan writes through a codec that stops
/// accepting writes after <c>n</c> of them, which from the plan's side is the
/// process dying after its n-th write. The scripted job stops at the first
/// refused write. Each movement is announce, act, conclude, so the kills land
/// both before an announcement (the world never moves) and between the act and
/// its conclusion (the world moved and the record does not say so). Every n
/// from zero to the whole job is run.
///
/// <b>What every recovery must satisfy.</b> A movement in flight is never
/// continued: the plan stops for a person and refuses every movement until it
/// is resolved from a measurement. Carried on to the end afterwards, every unit
/// is in the chest exactly once - and the executor here deliberately trusts the
/// plan's own load for what it deposits, so a resumed in-flight deposit would
/// put the load in twice and fail the sweep.</summary>
public sealed class ThorsteinWorkPlanTests
{
    private const int Sources = 3;

    private sealed class KillingCodec : INpcPlanCodec
    {
        private readonly INpcPlanCodec _inner;
        private readonly int _allowed;

        public KillingCodec(INpcPlanCodec inner, int allowed)
        {
            _inner = inner;
            _allowed = allowed;
        }

        public int Writes { get; private set; }

        public IReadOnlyList<string>? Encode(NpcPlanState state)
        {
            if (Writes >= _allowed)
            {
                return null;
            }

            Writes++;
            return _inner.Encode(state);
        }

        public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason) =>
            _inner.TryDecode(lines, out state, out reason);
    }

    /// <summary>The world the job works in: loose stones, his pack, the chest.
    /// </summary>
    private sealed class World
    {
        public int Remaining = Sources;
        public int Carried;
        public int Chest;

        public IReadOnlyList<KeyValuePair<string, int>> Measure() =>
            new[] { new KeyValuePair<string, int>("Stone", Carried) };
    }

    private sealed class Folder : IDisposable
    {
        public Folder()
        {
            Root = Path.Combine(Path.GetTempPath(), "cf-379-plan", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Path_ = new ThorsteinPlanFiles(Root).ResolvePath(new SettlementScope(4242, new SettlementId("camp")), ThorsteinWorkPlan.CollectJobId);
        }

        public string Root { get; }

        public string Path_ { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // A locked temp file must never fail the suite.
            }
        }
    }

    private static ThorsteinWorkPlan Opened(string path, INpcPlanCodec? codec = null)
    {
        var plan = new ThorsteinWorkPlan(ForemanNpcRole.Id, ThorsteinWorkPlan.CollectJobId, codec);
        plan.Open(path, null);
        return plan;
    }

    private static INpcPlanCodec Codec() => new ThorsteinPlanCodec(ForemanNpcRole.Id, ThorsteinWorkPlan.CollectJobId);

    private static NpcPlanEvidence Evidence(NpcWorldEpoch world, bool mayWork = true, int bodies = 1, bool areaReadable = true) =>
        new NpcPlanEvidence(world, bodies, false, areaReadable, false, mayWork, true, true, false, 0f);

    /// <summary>The executor: start, pick every stone (announce, act,
    /// conclude), deposit what the plan says he carries, end. Stops at the
    /// first refusal and says so.</summary>
    private static bool Run(ThorsteinWorkPlan plan, World world, NpcWorldEpoch epoch, out string said)
    {
        if (!plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out said))
        {
            return false;
        }

        while (world.Remaining > 0)
        {
            if (!plan.Intend("about to pick", out said))
            {
                return false;
            }

            world.Remaining--;
            world.Carried++;
            if (!plan.Conclude(true, world.Measure(), "picked", out said))
            {
                return false;
            }
        }

        // Deliberately naive: the amount comes from the PLAN's load. A plan that
        // resumed an in-flight deposit with its old load would deposit twice.
        int load = plan.State!.CarriedUnits;
        if (load > 0)
        {
            if (!plan.Intend("about to deposit", out said))
            {
                return false;
            }

            world.Chest += load;
            world.Carried -= Math.Min(world.Carried, load);
            if (!plan.Conclude(true, world.Measure(), "deposited", out said))
            {
                return false;
            }
        }

        plan.Ended(true, world.Measure(), "done");
        said = plan.LastSentence;
        return plan.Standing == ThorsteinPlanStanding.Idle;
    }

    private static int WholeJobWrites()
    {
        using var folder = new Folder();
        var counting = new KillingCodec(Codec(), int.MaxValue);
        var world = new World();
        Assert.True(Run(Opened(folder.Path_, counting), world, NpcWorldEpoch.Mint(), out string said), said);
        Assert.Equal(Sources, world.Chest);
        return counting.Writes;
    }

    public static IEnumerable<object[]> EveryKill()
    {
        int writes = WholeJobWrites();
        Assert.True(writes >= 10, "the script is too short to mean anything: " + writes);
        for (int kill = 0; kill <= writes; kill++)
        {
            yield return new object[] { kill };
        }
    }

    [Theory]
    [MemberData(nameof(EveryKill))]
    public void AKillAfterAnyWriteRecoversWithoutDuplicatingOrLosingAUnit(int kill)
    {
        using var folder = new Folder();
        var world = new World();
        Run(Opened(folder.Path_, new KillingCodec(Codec(), kill)), world, NpcWorldEpoch.Mint(), out _);

        // The process is gone. A new one opens the same file in a new world load.
        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        NpcWorldEpoch next = NpcWorldEpoch.Mint();
        NpcPlanState? onDisk = reloaded.State;
        bool wasInFlight = onDisk != null && onDisk.Custody != NpcPlanCustody.Clear;

        bool started = reloaded.Start(Evidence(next), "harvest", "chest", world.Measure(), out string said);
        if (wasInFlight)
        {
            // Never continued: stopped for a person, and nothing may move.
            Assert.False(started, "an in-flight movement was continued after a reload (kill " + kill + ")");
            Assert.Equal(ThorsteinPlanStanding.NeedsAttention, reloaded.Standing);
            Assert.False(reloaded.Intend("anything", out _));
            Assert.True(reloaded.Resolve(next, world.Measure(), out said), said);
        }
        else
        {
            Assert.True(started || reloaded.Standing == ThorsteinPlanStanding.Idle, "kill " + kill + ": " + said);
        }

        // Carried on to the end: every unit in the chest exactly once.
        Assert.True(Run(reloaded, world, next, out said), "kill " + kill + ": " + said);
        Assert.Equal(Sources, world.Chest);
        Assert.Equal(0, world.Carried);
        Assert.Equal(0, world.Remaining);
    }

    [Fact]
    public void TheSweepLandsBetweenEachKindOfActAndItsConclusionAndWouldCatchAReplayedDeposit()
    {
        int writes = WholeJobWrites();
        int picksInFlight = 0;
        bool depositCaught = false;
        var seen = new List<string>();
        for (int kill = 0; kill <= writes; kill++)
        {
            using var folder = new Folder();
            var world = new World();
            Run(Opened(folder.Path_, new KillingCodec(Codec(), kill)), world, NpcWorldEpoch.Mint(), out _);
            NpcPlanState? onDisk = Opened(folder.Path_).State;
            seen.Add(kill + ":" + (onDisk == null ? "none" : onDisk.Phase + "/" + onDisk.Custody + "/" + onDisk.Note));
            // A movement written as announced and never concluded reads back as
            // one nobody can vouch for.
            if (onDisk == null || onDisk.Custody == NpcPlanCustody.Clear)
            {
                continue;
            }

            if (onDisk.Note.Contains("pick"))
            {
                picksInFlight++;
            }
            else if (onDisk.Note.Contains("deposit"))
            {
                // The deposit happened; the record does not say so. An executor
                // that took the record's old load and carried on would put it in
                // twice - which is what the sweep's exact-once assertion catches.
                Assert.Equal(Sources, world.Chest);
                Assert.NotEqual(Sources, world.Chest + onDisk.CarriedUnits);
                depositCaught = true;
            }
        }

        Assert.True(Sources == picksInFlight, string.Join(" | ", seen));
        Assert.Contains(seen, entry => entry.Contains("Reserved/Uncertain"));
        Assert.True(depositCaught, "no kill landed between the deposit and its conclusion");
    }

    [Fact]
    public void AReloadReplansAndContinuesTheSamePlanRatherThanRestartingIt()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        NpcWorldEpoch first = NpcWorldEpoch.Mint();
        Assert.True(plan.Start(Evidence(first), "harvest", "chest", world.Measure(), out string said), said);
        Assert.True(plan.Intend("pick", out said), said);
        world.Remaining--;
        world.Carried++;
        Assert.True(plan.Conclude(true, world.Measure(), "picked", out said), said);
        int attempt = plan.State!.Attempt;

        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        Assert.Equal(ThorsteinPlanStanding.Recovered, reloaded.Standing);
        Assert.False(reloaded.Intend("pick", out _), "a plan read back moved before it was revalidated");

        NpcWorldEpoch second = NpcWorldEpoch.Mint();
        Assert.True(reloaded.Start(Evidence(second), "harvest", "chest", world.Measure(), out said), said);
        Assert.Equal(ThorsteinPlanStanding.Running, reloaded.Standing);
        Assert.Equal(ThorsteinWorkPlan.CollectJobId, reloaded.State!.JobId);
        Assert.Equal(NpcPlanPhase.Executing, reloaded.State.Phase);
        Assert.True(reloaded.State.World.Matches(second));
        Assert.Equal(1, reloaded.State.CarriedUnits);
        Assert.True(reloaded.State.Attempt >= attempt);
    }

    [Fact]
    public void APauseContinuesInTheSameSessionAndAnEmptyEndClosesThePlan()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        NpcWorldEpoch epoch = NpcWorldEpoch.Mint();
        Assert.True(plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out string said), said);
        plan.Suspend("paused by you");
        Assert.Equal(ThorsteinPlanStanding.Suspended, plan.Standing);
        Assert.False(plan.Intend("pick", out _));
        Assert.True(plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out said), said);
        Assert.True(Run(plan, world, epoch, out said), said);
        Assert.Equal(ThorsteinPlanStanding.Idle, Opened(folder.Path_).Standing);
    }

    [Fact]
    public void EndingWhileCarryingKeepsThePlanOpenForTheNextOrder()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        NpcWorldEpoch epoch = NpcWorldEpoch.Mint();
        Assert.True(plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out string said), said);
        Assert.True(plan.Intend("pick", out said), said);
        world.Remaining--;
        world.Carried++;
        Assert.True(plan.Conclude(true, world.Measure(), "picked", out said), said);
        plan.Ended(false, world.Measure(), "cancelled");
        Assert.Equal(ThorsteinPlanStanding.Suspended, plan.Standing);

        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        Assert.Equal(ThorsteinPlanStanding.Recovered, reloaded.Standing);
        Assert.Equal(1, reloaded.State!.CarriedUnits);
    }

    [Fact]
    public void AnUncertainOutcomeStopsThePlanForAPersonUntilResolved()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        NpcWorldEpoch epoch = NpcWorldEpoch.Mint();
        Assert.True(plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out string said), said);
        Assert.True(plan.Intend("pick", out said), said);
        Assert.False(plan.Conclude(false, world.Measure(), "nobody can say", out _));
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, plan.Standing);
        Assert.False(plan.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out _));

        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, reloaded.Standing);
        Assert.True(reloaded.Resolve(epoch, world.Measure(), out said), said);
        Assert.True(reloaded.Start(Evidence(epoch), "harvest", "chest", world.Measure(), out said), said);
    }

    [Fact]
    public void ARefundOfMaterialHeHoldsIsNeverAccepted()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        Assert.True(plan.Start(Evidence(NpcWorldEpoch.Mint()), "harvest", "chest", world.Measure(), out string said), said);
        Assert.True(plan.Intend("pick", out said), said);
        world.Remaining--;
        world.Carried++;
        Assert.True(plan.Conclude(true, world.Measure(), "picked", out said), said);

        // The work area could not be read across the reload: the shared policy
        // answers refund, which would quietly drop the stone he carries.
        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        Assert.False(reloaded.Start(Evidence(NpcWorldEpoch.Mint(), areaReadable: false), "harvest", "chest", world.Measure(), out _));
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, reloaded.Standing);
    }

    [Fact]
    public void APassingConditionAfterAReloadWaitsAndWritesNothing()
    {
        using var folder = new Folder();
        var world = new World();
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        Assert.True(plan.Start(Evidence(NpcWorldEpoch.Mint()), "harvest", "chest", world.Measure(), out string said), said);
        Assert.True(plan.Intend("pick", out said), said);
        world.Remaining--;
        world.Carried++;
        Assert.True(plan.Conclude(true, world.Measure(), "picked", out said), said);
        string before = File.ReadAllText(folder.Path_);

        // #379 review: a peer connected, or his body not loaded yet, is not a
        // reason to refund or stop a plan he carries material for.
        ThorsteinWorkPlan reloaded = Opened(folder.Path_);
        NpcWorldEpoch next = NpcWorldEpoch.Mint();
        Assert.False(reloaded.Start(Evidence(next, mayWork: false), "harvest", "chest", world.Measure(), out _));
        Assert.Equal(ThorsteinPlanStanding.Recovered, reloaded.Standing);
        Assert.False(reloaded.Start(Evidence(next, bodies: 0), "harvest", "chest", world.Measure(), out _));
        Assert.Equal(ThorsteinPlanStanding.Recovered, reloaded.Standing);
        Assert.Equal(before, File.ReadAllText(folder.Path_));

        Assert.True(reloaded.Start(Evidence(next), "harvest", "chest", world.Measure(), out said), said);
        Assert.Equal(1, reloaded.State!.CarriedUnits);
    }

    [Fact]
    public void AResolutionAgainstAnUnknownLoadIsRefused()
    {
        using var folder = new Folder();
        File.WriteAllText(folder.Path_, "not a plan" + Environment.NewLine);
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        Assert.False(plan.Resolve(NpcWorldEpoch.Mint(), null, out _));
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, plan.Standing);
    }

    [Fact]
    public void AnUnreadablePlanIsLeftAloneUntilAPersonResolvesIt()
    {
        using var folder = new Folder();
        File.WriteAllText(folder.Path_, "this is not a plan\n");
        ThorsteinWorkPlan plan = Opened(folder.Path_);
        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, plan.Standing);
        Assert.Equal("this is not a plan\n", File.ReadAllText(folder.Path_));

        NpcWorldEpoch epoch = NpcWorldEpoch.Mint();
        Assert.True(plan.Resolve(epoch, new World().Measure(), out string said), said);
        Assert.Equal(ThorsteinPlanStanding.Idle, plan.Standing);
        Assert.True(plan.Start(Evidence(epoch), "harvest", "chest", new World().Measure(), out said), said);
    }

    [Fact]
    public void TheCodecRoundTripsTwoMaterialsAndRefusesAFinishedPlanThatCarries()
    {
        var codec = Codec();
        NpcPlanState state = NpcPlanState.Opening(ForemanNpcRole.Id, ThorsteinWorkPlan.CollectJobId, NpcWorldEpoch.Unknown, 0)
            .WithHoldings(null, ThorsteinWorkPlan.Measured(new[]
            {
                new KeyValuePair<string, int>("Wood", 3), new KeyValuePair<string, int>("Stone", 2),
            }));
        IReadOnlyList<string> lines = codec.Encode(state)!;
        Assert.True(codec.TryDecode(lines, out NpcPlanState? back, out string reason), reason);
        Assert.Equal(5, back!.CarriedUnits);
        Assert.Equal(2, back.Carried.Count);

        NpcPlanState settled = state.WithPhase(NpcPlanPhase.Settled, "done");
        Assert.False(codec.TryDecode(codec.Encode(settled)!, out _, out _));
    }
}
