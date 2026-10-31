using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>A kill injected at every transition of Gunnar's tour plan (#379).
///
/// <b>How a kill is made.</b> The plan writes through a codec that stops
/// accepting writes after <c>n</c> of them, which from the plan's side is the
/// process dying after its n-th write. The scripted tour stops at the first
/// refused write. Because each movement is announce, act, conclude, the kills
/// land in both kinds of instant: before an announcement (the world never
/// moves) and between the move and its conclusion (the world moved and the
/// record does not say so). Every n from zero to the whole tour is run.
///
/// <b>What every recovery must satisfy.</b> A verdict with a sentence; nothing
/// left pending on the disk; a plan that may resume holds exactly what his body
/// holds; a record that disagrees with the body is stopped for a person; and,
/// carried on to the end - resolved first where it stopped - every unit is in
/// the chest exactly once and nothing was made or lost.</summary>
public sealed class GunnarTourPlanKillTests
{
    private const int Sources = 4;
    private const int UnitsPerSource = 2;

    /// <summary>How many writes the whole scripted tour makes, counted by
    /// running it, so a longer or shorter script cannot quietly shrink the
    /// sweep.</summary>
    private static int WholeTourWrites()
    {
        using var folder = new TourPlanFolder();
        var counting = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, counting), new TourWorld(Sources, UnitsPerSource),
            NpcWorldEpoch.Mint());
        Assert.True(script.RunWholeTour(), script.Said);
        return counting.Writes;
    }

    public static IEnumerable<object[]> EveryKill()
    {
        int writes = WholeTourWrites();
        for (int kill = 0; kill <= writes; kill++)
        {
            yield return new object[] { kill };
        }
    }

    [Fact]
    public void The_sweep_covers_every_phase_of_the_pipeline_and_both_movements()
    {
        using var folder = new TourPlanFolder();
        var counting = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, counting), new TourWorld(Sources, UnitsPerSource),
            NpcWorldEpoch.Mint());
        Assert.True(script.RunWholeTour(), script.Said);

        var phases = new HashSet<NpcPlanPhase>();
        int pendingAtWork = 0;
        foreach (var write in counting.Written)
        {
            phases.Add(write.Phase);
            if (write.Phase == NpcPlanPhase.Executing && write.Custody == NpcPlanCustody.Pending)
            {
                pendingAtWork++;
            }
        }

        foreach (NpcPlanPhase phase in new[]
                 {
                     NpcPlanPhase.Observing, NpcPlanPhase.Planned, NpcPlanPhase.Manifested, NpcPlanPhase.Reserved,
                     NpcPlanPhase.Provisioned, NpcPlanPhase.Routed, NpcPlanPhase.Executing, NpcPlanPhase.Reconciling,
                     NpcPlanPhase.Settled,
                 })
        {
            Assert.Contains(phase, phases);
        }

        // Two picks, one trip to the chest and the closing restatement.
        Assert.Equal(4, pendingAtWork);
        Assert.Equal(20, counting.Writes);
    }

    [Theory]
    [MemberData(nameof(EveryKill))]
    public void A_kill_after_any_write_recovers_without_duplicating_or_losing_a_unit(int kill)
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(Sources, UnitsPerSource);
        var dying = new TourScript(
            TourPlans.Opened(folder.PlanPath, new KillingCodec(Codec(), kill)), world, NpcWorldEpoch.Mint());
        dying.RunWholeTour();
        world.AssertConserved();

        // The world reloads: a new process, a new world load, the same file.
        NpcPlanState? onDisk = folder.Raw();
        var plan = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        TourPlanVerdict verdict = plan.Revalidate(script.Facts());

        Assert.False(string.IsNullOrWhiteSpace(verdict.Sentence), "kill " + kill + " gave no sentence");

        NpcPlanState? after = folder.Raw();
        if (after != null)
        {
            Assert.NotEqual(NpcPlanCustody.Pending, after.Custody);
        }

        bool openWork = onDisk != null
            && onDisk.Phase != NpcPlanPhase.Settled
            && onDisk.Phase != NpcPlanPhase.Refunded
            && onDisk.Phase != NpcPlanPhase.NeedsAttention;
        if (openWork && (onDisk!.Custody != NpcPlanCustody.Clear
                || !SameLoad(onDisk.Carried, TourPlans.Load(world))))
        {
            // The record and the body disagree, or the record cannot say:
            // stopped for a person, never resumed.
            Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
            Assert.False(verdict.MayResume);
            Assert.False(script.Start(), "a plan stopped for a person was started again at kill " + kill);
            Assert.True(plan.BlocksOrders);
        }

        if (plan.Standing == TourPlanStanding.Replanned || plan.Standing == TourPlanStanding.Suspended)
        {
            Assert.True(SameLoad(plan.State!.Carried, TourPlans.Load(world)),
                "a plan that may resume holds something his body does not, at kill " + kill);
        }

        if (plan.Standing == TourPlanStanding.NeedsAttention)
        {
            Assert.True(plan.Resolve(script.Facts(), busy: false, out string resolved), resolved);
        }

        script.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
        Assert.Equal(NpcPlanPhase.Settled, folder.Raw()!.Phase);
    }

    [Fact]
    public void Killed_between_a_pick_and_its_conclusion_the_pick_is_neither_repeated_nor_forgotten()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(Sources, UnitsPerSource);
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var dying = new TourScript(TourPlans.Opened(folder.PlanPath, codec), world, NpcWorldEpoch.Mint());
        Assert.True(dying.Start());
        Assert.True(dying.Pick(0));

        // The next announcement is written; the conclusion after the move is not.
        codec.Allowed = codec.Writes + 1;
        Assert.False(dying.Pick(1));
        Assert.Equal(4, world.Gunnar.Count);
        Assert.Equal(NpcPlanCustody.Pending, folder.Raw()!.Custody);

        var plan = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        TourPlanVerdict verdict = plan.Revalidate(script.Facts());
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("ct_collect resolve", verdict.Sentence);
        Assert.Equal(NpcPlanCustody.Uncertain, folder.Raw()!.Custody);

        // The source that was picked is not picked again, and what reached him
        // is not forgotten: resolving records the 4 he holds.
        Assert.True(plan.Resolve(script.Facts(), false, out string resolved), resolved);
        Assert.Equal(4, plan.State!.CarriedUnits);
        script.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
    }

    [Fact]
    public void Killed_between_a_deposit_and_its_conclusion_the_load_is_never_deposited_twice()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(Sources, UnitsPerSource);
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var dying = new TourScript(TourPlans.Opened(folder.PlanPath, codec), world, NpcWorldEpoch.Mint());
        Assert.True(dying.Start() && dying.Pick(0) && dying.Pick(1));
        List<int> load = new List<int>(world.Gunnar);

        codec.Allowed = codec.Writes + 1;
        Assert.False(dying.Deposit());
        Assert.Equal(4, world.Chest.Count);

        // The plan still says he carries 4; the chest has them. A plan that
        // trusted its record would deposit them again.
        Assert.Equal(4, folder.Raw()!.CarriedUnits);
        var plan = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        Assert.Equal(InterruptionResponse.NeedsAttention, plan.Revalidate(script.Facts()).Response);
        Assert.False(script.Start());
        Assert.Equal(1, world.DepositsThatMoved);

        Assert.True(plan.Resolve(script.Facts(), false, out string resolved), resolved);
        script.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
        Assert.All(load, unit => Assert.Single(world.Received, unit));
    }

    [Fact]
    public void The_fixture_catches_a_replayed_deposit()
    {
        // The conservation check is only worth something if it fails on the
        // defect it exists for.
        var world = new TourWorld(1, 2);
        world.Pick(0);
        var load = new List<int>(world.Gunnar);
        world.Deposit();
        world.ReplayDeposit(load);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => world.AssertConserved());
    }

    private static GunnarTourPlanCodec Codec() =>
        new GunnarTourPlanCodec(new GunnarTourPlan().Identity, GunnarTourPlan.JobId);

    private static bool SameLoad(
        IReadOnlyList<TheConcernedCat.ConcernedNPC.Custody.NpcMaterialStack> left,
        IReadOnlyList<TheConcernedCat.ConcernedNPC.Custody.NpcMaterialStack> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
