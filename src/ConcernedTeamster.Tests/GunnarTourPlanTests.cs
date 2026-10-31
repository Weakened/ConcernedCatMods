using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's collection tour surviving an interruption (#379): one test
/// per reconstruction outcome, the codec and its refusals, relogs that must not
/// duplicate, death, the person's exit, and arbitration.</summary>
public sealed class GunnarTourPlanTests
{
    // ------------------------------------------------------------------
    // Reconstruction: one test per outcome
    // ------------------------------------------------------------------

    [Fact]
    public void Continue_a_tour_stopped_this_session_carries_on_with_the_same_work()
    {
        using var folder = new TourPlanFolder();
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, codec), new TourWorld(4, 2), NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));
        Assert.True(script.Stop("you stopped it"));
        Assert.Equal(TourPlanStanding.Suspended, script.Plan.Standing);
        Assert.True(script.Plan.BlocksOrders);
        NpcPlanState stopped = script.Plan.State!;

        Assert.True(script.Start(), script.Said);

        NpcPlanState resumed = script.Plan.State!;
        Assert.Equal(TourPlanStanding.Running, script.Plan.Standing);
        Assert.Equal(NpcPlanPhase.Executing, resumed.Phase);
        Assert.Equal(stopped.JobId, resumed.JobId);
        Assert.Equal(1, resumed.TargetsDone);
        Assert.Equal(stopped.Carried, resumed.Carried);
        Assert.Equal(stopped.Attempt, resumed.Attempt);
        Assert.Contains("carries on", script.Said);
    }

    [Fact]
    public void Replan_after_a_reload_keeps_progress_and_the_measured_load()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0) && first.Pick(1));

        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.Recovered, plan.Standing);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint()) { Area = "area 5.0,0.0,5.0 r20 rev1", Chest = "chest-9" };
        TourPlanVerdict verdict = plan.Revalidate(script.Facts());

        Assert.Equal(InterruptionResponse.Replan, verdict.Response);
        Assert.Equal(TourPlanStanding.Replanned, plan.Standing);
        Assert.Equal(NpcPlanPhase.Observing, plan.State!.Phase);
        Assert.Equal(2, plan.State.TargetsDone);
        Assert.Equal(TourPlans.Load(world), plan.State.Carried);
        Assert.Contains("choose the chest and the area again", verdict.Sentence);
        Assert.True(plan.BlocksOrders);

        Assert.True(script.Start(), script.Said);
        Assert.Equal(NpcPlanPhase.Executing, plan.State.Phase);
        Assert.Equal(2, plan.State.TargetsDone);
        Assert.Equal(1, plan.State.Attempt);
        Assert.Equal("chest-9", plan.State.DestinationKey);
        Assert.Equal("area 5.0,0.0,5.0 r20 rev1", plan.State.SourceKey);
        Assert.True(plan.State.World.Matches(script.Epoch));
    }

    [Fact]
    public void Replan_in_the_same_session_when_the_area_or_chest_was_chosen_again()
    {
        using var folder = new TourPlanFolder();
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), new TourWorld(4, 2), NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Stop("you stopped it"));

        script.Chest = "chest-2";
        Assert.True(script.Start(), script.Said);
        Assert.Equal("chest-2", script.Plan.State!.DestinationKey);
        Assert.Equal(1, script.Plan.State.TargetsDone);
        Assert.Equal(2, script.Plan.State.CarriedUnits);
    }

    [Fact]
    public void Refund_a_plan_that_holds_nothing_after_a_reload()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start());

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts());

        Assert.Equal(InterruptionResponse.Refund, verdict.Response);
        Assert.Equal(TourPlanStanding.Idle, plan.Standing);
        Assert.False(plan.BlocksOrders);
        Assert.Equal(NpcPlanPhase.Refunded, folder.Raw()!.Phase);
        Assert.Empty(folder.Raw()!.Carried);
    }

    [Fact]
    public void Refund_a_plan_that_holds_nothing_when_his_body_is_gone()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        Assert.True(new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint()).Start());

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts(bodies: 0));
        Assert.Equal(InterruptionResponse.Refund, verdict.Response);
        Assert.Equal(TourPlanStanding.Idle, plan.Standing);
    }

    [Fact]
    public void NeedsAttention_when_a_movement_was_pending_on_the_disk()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start());
        Assert.True(script.Plan.IntendPick("about to pick", out _));

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts());

        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Equal(InterruptionCause.TransferUncertain, verdict.Cause);
        Assert.Contains("neither repeated nor forgotten", verdict.Sentence);
        Assert.Contains("ct_collect resolve", verdict.Sentence);
        Assert.Equal(NpcPlanPhase.NeedsAttention, folder.Raw()!.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, folder.Raw()!.Custody);
    }

    [Fact]
    public void NeedsAttention_when_the_disk_already_says_uncertain()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        world.Pick(0);
        WriteRaw(folder, NpcPlanPhase.Executing, NpcPlanCustody.Uncertain, world.Carried());

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts());
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
    }

    [Fact]
    public void NeedsAttention_when_the_measured_load_differs_and_says_it_may_be_a_crash_rollback()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Pick(1));

        world.Gunnar.RemoveAt(0);
        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts());

        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("crash rollback", verdict.Sentence);
        Assert.Contains("ct_collect resolve", verdict.Sentence);
        Assert.Equal(NpcPlanPhase.NeedsAttention, folder.Raw()!.Phase);
        Assert.Equal(4, folder.Raw()!.CarriedUnits);
    }

    [Fact]
    public void NeedsAttention_when_his_load_cannot_be_read()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourPlanFacts(NpcWorldEpoch.Mint(), 1, null, true, 0f));
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("could not be read", verdict.Sentence);
        Assert.False(plan.Resolve(new TourPlanFacts(NpcWorldEpoch.Mint(), 1, null, true, 0f), false, out string said));
        Assert.Contains("could not be read", said);
    }

    [Fact]
    public void NeedsAttention_when_two_bodies_answer()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        Assert.True(new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint()).Start());

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts(bodies: 2));
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Equal(InterruptionCause.BodyDuplicated, verdict.Cause);
        Assert.Contains("Two bodies", verdict.Sentence);
    }

    [Fact]
    public void NeedsAttention_when_his_body_is_gone_while_holding_something_retire_force_included()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Stop("you stopped it"));

        // ct_haul retire force: the body and what it held leave the world.
        world.Gunnar.Clear();
        Assert.False(script.Plan.TryStart(script.Facts(bodies: 0), script.Area, script.Chest, out string said));
        Assert.Contains("retire force", said);
        Assert.Contains("Nothing was recreated", said);
        Assert.Equal(TourPlanStanding.NeedsAttention, script.Plan.Standing);

        // And the same after a reload.
        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
    }

    [Fact]
    public void A_stopped_plan_whose_body_the_census_cannot_find_stops_for_a_person_without_a_start()
    {
        // #379 review B1: what the runtime really produces after ct_haul retire
        // force in the same session - no body, so no load can be read, and the
        // tour gate would refuse a start for having no worker. The frame
        // watcher asks this, and it must decide the body-gone outcome itself.
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Stop("you stopped it"));
        Assert.Equal(TourPlanStanding.Suspended, script.Plan.Standing);

        // A body that answers decides nothing.
        Assert.Equal(InterruptionResponse.Unspecified,
            script.Plan.RevalidateIfBodyGone(script.Facts(bodies: 1)).Response);
        Assert.Equal(TourPlanStanding.Suspended, script.Plan.Standing);

        world.Gunnar.Clear();
        var noBody = new TourPlanFacts(script.Epoch, 0, null, true, 0f);
        TourPlanVerdict verdict = script.Plan.RevalidateIfBodyGone(noBody);

        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("retire force", verdict.Sentence);
        Assert.Equal(TourPlanStanding.NeedsAttention, script.Plan.Standing);
        Assert.Equal(TourPlanStanding.NeedsAttention, TourPlans.Opened(folder.PlanPath).Standing);

        // He is spawned again, empty: resolve closes it, and nothing is recreated.
        Assert.True(script.Plan.Resolve(script.Facts(), false, out string resolved), resolved);
        Assert.False(script.Plan.BlocksOrders);
        Assert.Empty(world.Carried());
    }

    [Fact]
    public void A_replanned_plan_whose_body_the_census_cannot_find_stops_for_a_person()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0));

        var plan = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        Assert.Equal(InterruptionResponse.Replan, plan.Revalidate(script.Facts()).Response);
        Assert.Equal(TourPlanStanding.Replanned, plan.Standing);

        world.Gunnar.Clear();
        TourPlanVerdict verdict = plan.RevalidateIfBodyGone(new TourPlanFacts(script.Epoch, 0, null, true, 0f));
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("retire force", verdict.Sentence);
    }

    [Fact]
    public void The_body_gone_check_leaves_a_plan_nobody_stopped_alone()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start());
        Assert.Equal(TourPlanStanding.Running, script.Plan.Standing);
        Assert.Equal(InterruptionResponse.Unspecified,
            script.Plan.RevalidateIfBodyGone(new TourPlanFacts(script.Epoch, 0, null, true, 0f)).Response);
        Assert.Equal(TourPlanStanding.Running, script.Plan.Standing);
    }

    [Fact]
    public void NeedsAttention_rather_than_refund_while_he_carries_a_load()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0));

        var plan = TourPlans.Opened(folder.PlanPath);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts(mayWork: false));
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Equal(2, folder.Raw()!.CarriedUnits);
    }

    [Fact]
    public void NeedsAttention_when_the_plan_file_cannot_be_read_and_resolve_quarantines_it()
    {
        using var folder = new TourPlanFolder();
        File.WriteAllLines(folder.PlanPath, new[] { "format\t1", "plan\tgarbage" });

        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
        Assert.Contains("could not be read", plan.AttentionReason);
        Assert.True(plan.BlocksOrders);
        Assert.Equal(new[] { "format\t1", "plan\tgarbage" }, File.ReadAllLines(folder.PlanPath));

        var world = new TourWorld(2, 2);
        world.Pick(0);
        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        Assert.True(plan.Resolve(script.Facts(), false, out string said), said);
        Assert.Contains(".corrupt", said);
        Assert.Contains(Directory.GetFiles(folder.Path), path => path.Contains(".corrupt"));
        Assert.Equal(TourPlanStanding.Suspended, plan.Standing);
        Assert.Equal(2, folder.Raw()!.CarriedUnits);

        script.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
    }

    [Fact]
    public void A_plan_that_was_already_stopped_for_a_person_stays_stopped_across_a_reload()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));
        world.Gunnar.Clear();
        Assert.False(script.Stop("stopped"));

        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
        Assert.Contains("ct_collect resolve", plan.AttentionReason);
    }

    // ------------------------------------------------------------------
    // Relogs: nothing is ever deposited twice
    // ------------------------------------------------------------------

    [Fact]
    public void Two_relogs_mid_load_deposit_the_load_exactly_once()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0) && first.Pick(1));

        var second = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(InterruptionResponse.Replan,
            second.Revalidate(new TourScript(second, world, NpcWorldEpoch.Mint()).Facts()).Response);

        var third = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(third, world, NpcWorldEpoch.Mint());
        Assert.Equal(InterruptionResponse.Replan, third.Revalidate(script.Facts()).Response);
        Assert.Equal(2, third.State!.Attempt);
        Assert.Equal(2, third.State.TargetsDone);

        script.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
        Assert.Equal(2, world.DepositsThatMoved);
    }

    [Fact]
    public void A_settled_plan_resumes_nothing()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(2, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.RunWholeTour(), first.Said);

        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.Idle, plan.Standing);
        Assert.False(plan.BlocksOrders);
        TourPlanVerdict verdict = plan.Revalidate(new TourScript(plan, world, NpcWorldEpoch.Mint()).Facts());
        Assert.Equal(InterruptionResponse.Unspecified, verdict.Response);
        Assert.Equal(NpcPlanPhase.Settled, folder.Raw()!.Phase);

        var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
        Assert.True(script.Start());
        Assert.Equal(0, plan.State!.TargetsDone);
        Assert.Empty(plan.State.Carried);
        Assert.Equal(1, world.DepositsThatMoved);
    }

    [Fact]
    public void An_uncertain_deposit_never_deposits_again_until_resolved()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0));

        // The trip happened and nobody could establish it: one leg uncertain.
        Assert.True(first.Plan.IntendDeposit("about to deposit", out _));
        world.Deposit();
        Assert.False(first.Plan.ConcludeDeposit(false, world.Carried(), "a leg was uncertain", out string said));
        Assert.Contains("ct_collect resolve", said);
        Assert.Equal(TourPlanStanding.NeedsAttention, first.Plan.Standing);

        for (int relog = 0; relog < 2; relog++)
        {
            var plan = TourPlans.Opened(folder.PlanPath);
            var script = new TourScript(plan, world, NpcWorldEpoch.Mint());
            Assert.Equal(TourPlanStanding.NeedsAttention, plan.Standing);
            Assert.False(script.Start());
            Assert.True(plan.BlocksOrders);
            Assert.Equal(1, world.DepositsThatMoved);
        }

        var resolving = TourPlans.Opened(folder.PlanPath);
        var after = new TourScript(resolving, world, NpcWorldEpoch.Mint());
        Assert.True(resolving.Resolve(after.Facts(), false, out string resolved), resolved);
        Assert.Equal(TourPlanStanding.Idle, resolving.Standing);
        Assert.Equal(NpcPlanPhase.Refunded, folder.Raw()!.Phase);
        after.ResumeToTheEnd();
        world.AssertAllInTheChestOnce();
    }

    [Fact]
    public void A_crash_rollback_to_fewer_units_gives_the_crash_sentence()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var first = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(first.Start() && first.Pick(0));
        TourWorld saved = world.Snapshot();
        Assert.True(first.Pick(1));

        // The game closed without saving: the world is what it was at the last
        // save, and the plan - written the moment it changed - is not.
        var plan = TourPlans.Opened(folder.PlanPath);
        var script = new TourScript(plan, saved, NpcWorldEpoch.Mint());
        TourPlanVerdict verdict = plan.Revalidate(script.Facts());
        Assert.Equal(InterruptionResponse.NeedsAttention, verdict.Response);
        Assert.Contains("crash rollback", verdict.Sentence);
        Assert.Contains("carried 2 Stone, 2 Wood, but he holds 2 Stone now", verdict.Sentence);

        Assert.True(plan.Resolve(script.Facts(), false, out string resolved), resolved);
        Assert.Equal(2, plan.State!.CarriedUnits);
        script.ResumeToTheEnd();
        saved.AssertAllInTheChestOnce();
    }

    // ------------------------------------------------------------------
    // D16: death
    // ------------------------------------------------------------------

    [Fact]
    public void Death_with_a_full_measured_drop_ends_refunded_and_never_reenters()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));

        script.Plan.PrepareForDeath();
        world.DieAndDrop();
        script.Plan.Died(held: 2, dropped: 2, unresolved: 0, "recorded in teamster_worker_deaths_4242.tsv");

        Assert.Equal(TourPlanStanding.Idle, script.Plan.Standing);
        NpcPlanState onDisk = folder.Raw()!;
        Assert.Equal(NpcPlanPhase.Refunded, onDisk.Phase);
        Assert.Empty(onDisk.Carried);
        Assert.Contains("teamster_worker_deaths_4242.tsv", onDisk.Note);

        var plan = TourPlans.Opened(folder.PlanPath);
        Assert.Equal(TourPlanStanding.Idle, plan.Standing);
        Assert.False(plan.BlocksOrders);
        world.AssertConserved();
    }

    [Fact]
    public void Death_with_unresolved_units_ends_in_needs_attention()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Pick(1));

        script.Plan.PrepareForDeath();
        script.Plan.Died(held: 4, dropped: 3, unresolved: 1, "recorded in teamster_worker_deaths_4242.tsv");

        Assert.Equal(TourPlanStanding.NeedsAttention, script.Plan.Standing);
        Assert.Contains("could not account for 1", script.Plan.AttentionReason);
        Assert.Equal(NpcPlanPhase.NeedsAttention, folder.Raw()!.Phase);
        Assert.Equal(TourPlanStanding.NeedsAttention, TourPlans.Opened(folder.PlanPath).Standing);
    }

    [Fact]
    public void Death_whose_intent_could_not_be_written_never_closes_the_plan_as_refunded()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, codec), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));

        codec.Allowed = codec.Writes;
        script.Plan.PrepareForDeath();
        codec.Allowed = int.MaxValue;
        script.Plan.Died(held: 2, dropped: 2, unresolved: 0, "recorded");

        Assert.Equal(TourPlanStanding.NeedsAttention, script.Plan.Standing);
    }

    // ------------------------------------------------------------------
    // Write-ahead refusals in the session
    // ------------------------------------------------------------------

    [Fact]
    public void A_pick_whose_announcement_cannot_be_written_is_not_made()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, codec), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start());

        codec.Allowed = codec.Writes;
        Assert.False(script.Pick(0));
        Assert.Empty(world.Gunnar);
        Assert.Equal(2, world.Sources[0].Count);
        Assert.False(script.Plan.IntendPick("again", out _));
    }

    [Fact]
    public void Nothing_moves_outside_a_running_plan()
    {
        var plan = new GunnarTourPlan();
        Assert.False(plan.IntendPick("pick", out _));
        Assert.False(plan.IntendDeposit("deposit", out _));
        Assert.False(plan.TryStart(
            new TourPlanFacts(NpcWorldEpoch.Mint(), 1, new List<KeyValuePair<string, int>>(), true, 0f),
            "area", "chest", out string said));
        Assert.Contains("No tour starts", said);
    }

    [Fact]
    public void No_world_id_means_no_journal_and_a_tour_refuses_with_a_sentence()
    {
        Assert.Null(GunnarTourPlanFiles.PathFor(Path.GetTempPath(), null, out string none));
        Assert.Null(GunnarTourPlanFiles.PathFor(Path.GetTempPath(), 0L, out _));
        var plan = new GunnarTourPlan();
        plan.Open(null, none);
        Assert.Equal(TourPlanStanding.Closed, plan.Standing);
        Assert.False(plan.TryStart(
            new TourPlanFacts(NpcWorldEpoch.Mint(), 1, new List<KeyValuePair<string, int>>(), true, 0f),
            "area", "chest", out string said));
        Assert.Contains("no world could be named", said);
    }

    [Fact]
    public void The_plan_file_sits_beside_the_death_record_and_is_named_by_the_world()
    {
        Assert.Equal("teamster_gunnar_tour_77.tsv", GunnarTourPlanFiles.FileNameFor(77L));
        string? path = GunnarTourPlanFiles.PathFor(Path.GetTempPath(), 77L, out _);
        Assert.Equal(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "teamster_gunnar_tour_77.tsv"), path);
    }

    [Fact]
    public void A_stopped_empty_tour_closes_as_refunded_and_a_finished_one_settles()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(2, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Stop("you stopped it"));
        Assert.Equal(NpcPlanPhase.Refunded, folder.Raw()!.Phase);
        Assert.Equal(TourPlanStanding.Idle, script.Plan.Standing);

        Assert.True(script.RunWholeTour(), script.Said);
        Assert.Equal(NpcPlanPhase.Settled, folder.Raw()!.Phase);
        Assert.Equal(2, folder.Raw()!.TargetsDone);
        Assert.Equal(2, folder.Raw()!.TargetsTotal);
    }

    [Fact]
    public void A_tour_cut_off_mid_trip_to_the_chest_is_stopped_for_a_person()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(2, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));
        Assert.True(script.Plan.IntendDeposit("about to deposit", out _));

        // The deposit legs threw part way: the tour is cancelled from the fault.
        Assert.False(script.Plan.Ended(false, world.Carried(), "the tour faulted"));
        Assert.Equal(TourPlanStanding.NeedsAttention, script.Plan.Standing);
    }

    [Fact]
    public void A_tour_cut_off_mid_pick_concludes_from_the_measurement()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(2, 2);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start());
        Assert.True(script.Plan.IntendPick("about to pick", out _));
        world.Pick(0);

        Assert.True(script.Plan.Ended(false, world.Carried(), "you stopped it"));
        Assert.Equal(TourPlanStanding.Suspended, script.Plan.Standing);
        Assert.Equal(2, folder.Raw()!.CarriedUnits);
        Assert.Equal(NpcPlanCustody.Clear, folder.Raw()!.Custody);
    }

    // ------------------------------------------------------------------
    // Arbitration
    // ------------------------------------------------------------------

    /// <summary>ConcernedNPC's activity ladder does not apply to Gunnar: his
    /// identity lease lets exactly one job hold him at a time and nothing
    /// preempts another. So "an interrupted job resumes rather than restarts" is
    /// the plan staying open while a haul holds him, and being continued, not
    /// begun again, when the tour is started after it.</summary>
    [Fact]
    public void A_suspended_tour_resumes_after_a_haul_held_him_rather_than_restarting()
    {
        using var folder = new TourPlanFolder();
        var world = new TourWorld(4, 2);
        var codec = new KillingCodec(Codec(), int.MaxValue);
        var script = new TourScript(TourPlans.Opened(folder.PlanPath, codec), world, NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0) && script.Pick(1));
        Assert.True(script.Stop("a haul was ordered, and one job holds him at a time"));
        NpcPlanState before = script.Plan.State!;
        int writesBefore = codec.Written.Count;

        // The haul runs and ends; nothing about it touches the tour's plan.
        var lease = new CollectionIdentityLease();
        Assert.False(lease.IsHeld);

        Assert.True(script.Start(), script.Said);
        NpcPlanState after = script.Plan.State!;
        Assert.Equal(before.JobId, after.JobId);
        Assert.Equal(GunnarTourPlan.JobId, after.JobId);
        Assert.Equal(2, after.TargetsDone);
        Assert.Equal(before.Carried, after.Carried);
        Assert.DoesNotContain(codec.Written.Skip(writesBefore), write => write.Phase == NpcPlanPhase.Observing);

        script.CarryOnToTheEnd();
        world.AssertAllInTheChestOnce();
    }

    // ------------------------------------------------------------------
    // The codec
    // ------------------------------------------------------------------

    [Fact]
    public void The_codec_round_trips_a_plan()
    {
        var state = new NpcPlanState(
            new GunnarTourPlan().Identity, GunnarTourPlan.JobId, NpcPlanPhase.Executing, NpcPlanCustody.Clear,
            null,
            new[]
            {
                new NpcMaterialStack(NpcMaterial.Of("Stone"), 12),
                new NpcMaterialStack(NpcMaterial.Of("Wood"), 3),
            },
            5, 6, "area 1.0,2.0,3.0 r20 rev2", "1234:5678", string.Empty, NpcWorldEpoch.Mint(), 2,
            "a note\twith a tab\nand a line break");

        IReadOnlyList<string> lines = Codec().Encode(state)!;
        Assert.Equal("end\t4", lines[lines.Count - 1]);
        Assert.True(Codec().TryDecode(lines, out NpcPlanState? back, out string reason), reason);
        Assert.Equal(state.Phase, back!.Phase);
        Assert.Equal(state.Custody, back.Custody);
        Assert.Equal(state.Carried, back.Carried);
        Assert.Equal(5, back.TargetsDone);
        Assert.Equal(6, back.TargetsTotal);
        Assert.Equal(state.SourceKey, back.SourceKey);
        Assert.Equal(state.DestinationKey, back.DestinationKey);
        Assert.Equal(2, back.Attempt);
        Assert.Equal("a note with a tab and a line break", back.Note);
        Assert.True(back.World.IsUnknown);
        Assert.Empty(back.Reservations);
    }

    [Theory]
    [InlineData("extra plan row")]
    [InlineData("unknown row")]
    [InlineData("missing trailer")]
    [InlineData("wrong count")]
    [InlineData("row after trailer")]
    [InlineData("duplicate material")]
    [InlineData("vehicle")]
    [InlineData("another job")]
    [InlineData("second format")]
    public void The_codec_refuses_a_record_it_cannot_account_for(string damage)
    {
        List<string> lines = Codec().Encode(Sample(NpcPlanPhase.Executing, 2))!.ToList();
        int trailer = lines.Count - 1;
        string plan = lines.Single(line => line.StartsWith("plan\t"));
        switch (damage)
        {
            case "extra plan row":
                lines.Insert(trailer, plan);
                lines[lines.Count - 1] = "end\t5";
                break;
            case "unknown row":
                lines.Insert(trailer, "reservation\tteamster-collect#0");
                lines[lines.Count - 1] = "end\t5";
                break;
            case "missing trailer":
                lines.RemoveAt(trailer);
                break;
            case "wrong count":
                lines[trailer] = "end\t9";
                break;
            case "row after trailer":
                lines.Add("carried\tWood\t1\t0\t1");
                break;
            case "duplicate material":
                lines.Insert(trailer, lines[trailer - 1]);
                lines[lines.Count - 1] = "end\t5";
                break;
            case "vehicle":
                lines[lines.IndexOf(plan)] = ReplaceField(plan, 9, "cart-1");
                break;
            case "another job":
                lines[lines.IndexOf(plan)] = ReplaceField(plan, 2, "teamster-collect-3");
                break;
            case "second format":
                lines.Insert(1, "format\t1");
                lines[lines.Count - 1] = "end\t5";
                break;
        }

        Assert.False(Codec().TryDecode(lines, out _, out string reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void The_codec_refuses_a_finished_plan_that_still_claims_a_load_both_ways()
    {
        Assert.Null(Codec().Encode(Sample(NpcPlanPhase.Refunded, 2)));
        Assert.Null(Codec().Encode(Sample(NpcPlanPhase.Settled, 2)));
        Assert.NotNull(Codec().Encode(Sample(NpcPlanPhase.NeedsAttention, 2)));

        List<string> lines = Codec().Encode(Sample(NpcPlanPhase.NeedsAttention, 2))!.ToList();
        string plan = lines.Single(line => line.StartsWith("plan\t"));
        lines[lines.IndexOf(plan)] = ReplaceField(plan, 3, ((int)NpcPlanPhase.Refunded).ToString());
        Assert.False(Codec().TryDecode(lines, out _, out string reason));
        Assert.Contains("finished plan", reason);
    }

    [Theory]
    [InlineData("area\twith a tab", "chest")]
    [InlineData("area", "chest\nwith a line break")]
    [InlineData("area\r", "chest")]
    public void The_codec_refuses_keys_with_tabs_or_line_breaks(string source, string destination)
    {
        NpcPlanState state = Sample(NpcPlanPhase.Executing, 1).WithRoute(source, destination, string.Empty);
        Assert.Null(Codec().Encode(state));
        Assert.False(GunnarTourPlanCodec.IsKey(source + destination));
    }

    [Fact]
    public void The_codec_refuses_a_reservation_and_a_vehicle_because_a_tour_has_neither()
    {
        NpcPlanState state = Sample(NpcPlanPhase.Executing, 1);
        Assert.Null(Codec().Encode(state.WithHoldings(new[] { ReservationId.For(GunnarTourPlan.JobId, 0) }, state.Carried)));
        Assert.Null(Codec().Encode(state.WithRoute(state.SourceKey, state.DestinationKey, "cart-1")));
    }

    [Fact]
    public void A_tour_never_writes_a_reservation_or_a_vehicle()
    {
        using var folder = new TourPlanFolder();
        var script = new TourScript(TourPlans.Opened(folder.PlanPath), new TourWorld(2, 2), NpcWorldEpoch.Mint());
        Assert.True(script.Start() && script.Pick(0));
        NpcPlanState onDisk = folder.Raw()!;
        Assert.Empty(onDisk.Reservations);
        Assert.Equal(string.Empty, onDisk.VehicleKey);
        Assert.Equal(GunnarTourPlan.JobId, onDisk.JobId);
        Assert.Equal(WorkerKey.Gunnar.Value, onDisk.Identity.Value);
    }

    // ------------------------------------------------------------------
    // Waiting before revalidation, and the ordered refusals
    // ------------------------------------------------------------------

    [Fact]
    public void Revalidation_waits_until_body_record_switch_and_authority_can_answer()
    {
        Assert.Equal(string.Empty, GunnarTourPlan.WaitingFor(WorkerBodyStatus.Bound, true, true, true));
        Assert.Equal(string.Empty, GunnarTourPlan.WaitingFor(WorkerBodyStatus.NotFound, false, true, true));
        Assert.Equal(string.Empty, GunnarTourPlan.WaitingFor(WorkerBodyStatus.Duplicated, false, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Bound, false, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Searching, true, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.NotLoaded, true, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Faulted, true, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Unspecified, true, true, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Bound, true, false, true));
        Assert.NotEmpty(GunnarTourPlan.WaitingFor(WorkerBodyStatus.Bound, true, true, false));

        Assert.Equal(1, GunnarTourPlan.BodiesFor(WorkerBodyStatus.Bound));
        Assert.Equal(0, GunnarTourPlan.BodiesFor(WorkerBodyStatus.NotFound));
        Assert.Equal(2, GunnarTourPlan.BodiesFor(WorkerBodyStatus.Duplicated));
        Assert.Equal(-1, GunnarTourPlan.BodiesFor(WorkerBodyStatus.Searching));
    }

    [Fact]
    public void Ordered_picks_and_deposits_refuse_while_a_plan_is_open_and_the_tours_own_deposit_does_not()
    {
        var pick = new CollectionOrderRequest(
            true, true, WorkAuthorityVerdict.Granted, true, true, false, false, false, true,
            "Pickable_Stone", "Stone", 1, 2f, 1f, interruptedTourOpen: true);
        Assert.Equal(CollectionOrderRefusal.InterruptedTourOpen, CollectionOrderGate.Evaluate(pick));
        Assert.Equal(GunnarTourPlan.OpenPlanRefusal, CollectionOrderGate.Describe(CollectionOrderRefusal.InterruptedTourOpen));

        var ordered = new DepositRequest(
            true, true, WorkAuthorityVerdict.Granted, true, true, false, false, false, true, true, true,
            interruptedTourOpen: true);
        Assert.Equal(DepositRefusal.InterruptedTourOpen, DepositOrderGate.Evaluate(ordered));
        Assert.Contains("ct_collect resolve",
            DepositSentences.Describe(DepositRefusal.InterruptedTourOpen, WorkAuthorityVerdict.Granted));

        var tours = new DepositRequest(
            true, true, WorkAuthorityVerdict.Granted, true, true, false, false, false, true, true, true);
        Assert.Equal(DepositRefusal.None, DepositOrderGate.Evaluate(tours));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static GunnarTourPlanCodec Codec() =>
        new GunnarTourPlanCodec(new GunnarTourPlan().Identity, GunnarTourPlan.JobId);

    private static NpcPlanState Sample(NpcPlanPhase phase, int stone) =>
        new NpcPlanState(
            new GunnarTourPlan().Identity, GunnarTourPlan.JobId, phase, NpcPlanCustody.Clear, null,
            stone == 0 ? null : new[] { new NpcMaterialStack(NpcMaterial.Of("Stone"), stone) },
            1, 1, "area", "chest", string.Empty, NpcWorldEpoch.Unknown, 0, "note");

    private static string ReplaceField(string row, int field, string value)
    {
        string[] fields = row.Split('\t');
        fields[field] = value;
        return string.Join("\t", fields);
    }

    private static void WriteRaw(
        TourPlanFolder folder, NpcPlanPhase phase, NpcPlanCustody custody,
        IReadOnlyList<KeyValuePair<string, int>> carried)
    {
        var state = new NpcPlanState(
            new GunnarTourPlan().Identity, GunnarTourPlan.JobId, phase, custody, null,
            GunnarTourPlan.Measured(carried), 1, 1, "area", "chest", string.Empty, NpcWorldEpoch.Unknown, 0, "raw");
        File.WriteAllLines(folder.PlanPath, Codec().Encode(state)!);
    }
}
