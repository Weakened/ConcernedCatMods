using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A process killed at every transition of a plan, reloaded, and asked
/// what it thinks.
///
/// <b>Why every transition and not a convenient one.</b> A test that kills at one
/// point proves that recovery compiles. The failures that matter are at the
/// boundaries - after reserving and before carrying, after carrying and before
/// committing, after committing and before recording - and each of those is a
/// different disagreement between the record and the world. So the kill point is
/// a parameter over the whole script, the script is the pipeline, and adding a
/// phase adds cases rather than quietly going untested.
///
/// <b>The three properties, asserted over every one of those kills rather than
/// once each by hand.</b>
///
/// <i>A verdict, always.</i> Every kill reloads to one of the four responses with
/// a sentence attached. Never nothing, never a default, never silence.
///
/// <i>No duplicate.</i> Material is never fetched or delivered twice, and the NPC
/// never ends up as two NPCs: a reconstructed plan re-attaches to the body that is
/// there through the arbiter, and two bodies answering to one name is a refusal
/// rather than a choice between them.
///
/// <i>No loss, and no invented credit.</i> The three numbers that are the world
/// always add up to what the player owned. And the record and the world agree
/// about what is on the NPC's back exactly when the plan is allowed to go on -
/// which is the whole of "never crediting what was not gathered, never discarding
/// what was", as an implication a test can check.</summary>
public class PlanKillTests
{
    /// <summary>Every point the process can die at: after the opening record,
    /// after each of the fifteen operations, and after the last one. Sixteen
    /// cases, generated from the script rather than listed.</summary>
    public static IEnumerable<object[]> EveryKillPoint
    {
        get
        {
            using var rehearsal = new PlanRehearsal();
            for (int after = 0; after <= rehearsal.Operations.Count; after++)
            {
                yield return new object[] { after };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryKillPoint))]
    public void A_kill_at_every_transition_reloads_to_one_verdict_and_conserves_every_unit(int killedAfter)
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(killedAfter);

        int atSource = world.AtSource;
        int onBack = world.OnBack;
        int atDestination = world.AtDestination;
        Assert.Equal(PlanRehearsal.Units, world.TotalInTheWorld);

        NpcPlanLoad load = world.Reload();

        // The record is always there and always readable: every operation in the
        // script writes whole or not at all, so there is no such thing as half a
        // plan on the disk.
        Assert.True(load.IsLoaded, load.Failure);
        NpcPlanState recovered = load.Plan!;

        // A plan off the disk is never in a world, and never says a movement is
        // merely pending: an intent with no recorded outcome is the exact shape of
        // an interruption.
        Assert.True(recovered.World.IsUnknown);
        Assert.NotEqual(NpcPlanCustody.Pending, recovered.Custody);
        Assert.Equal(1, recovered.Attempt);

        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, recovered, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        // One: a verdict, with a sentence for a player.
        Assert.NotEqual(InterruptionResponse.Unspecified, decision.Outcome.Response);
        Assert.NotEqual(string.Empty, decision.Outcome.Reason);

        // Two: deciding moved nothing. Recovery has no inventory ports and no way
        // to reach one, and this is the assertion that keeps it that way.
        Assert.Equal(atSource, world.AtSource);
        Assert.Equal(onBack, world.OnBack);
        Assert.Equal(atDestination, world.AtDestination);
        Assert.Equal(PlanRehearsal.Units, world.TotalInTheWorld);

        // Three: nothing was fetched or delivered twice. The world's own counters,
        // not the record's - a record can be perfectly consistent with itself
        // while the world holds two of something.
        Assert.True(world.Gathers <= 1);
        Assert.True(world.Deliveries <= 1);

        // Four: the record and the world agree about the back exactly when the
        // plan may go on. The direction that matters is the second: when they
        // disagree, nothing is resumed, replanned or refunded - a person is told.
        bool recordAgreesWithTheWorld = recovered.CarriedUnits == onBack;
        if (decision.Outcome.MayResume)
        {
            Assert.True(
                recordAgreesWithTheWorld,
                "the plan was allowed to go on while the record said it carried " + recovered.CarriedUnits +
                " and the body actually had " + onBack);
        }

        if (!recordAgreesWithTheWorld)
        {
            Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
            Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);
        }

        // Five: whatever was decided, the plan is the same work. Reservations,
        // carried material, progress, the cart, source, destination and identity
        // all came through; the phase and the note are the only things a recovery
        // is allowed to change.
        Assert.True(decision.Next.CarriesTheSameWorkAs(recovered));

        // Six: the body. One answered, so a plan that had not already ended
        // re-attached to it - and a decision that does not allow going on gives it
        // straight back, because a stopped plan holding a claim is a body nothing
        // will ever release. A plan that had already ended asks for no body at
        // all.
        Assert.Equal(!decision.WasAlreadyOver, decision.Claim.IsGranted);
        Assert.Equal(
            decision.Outcome.MayResume ? NpcBodyKind.Worker : NpcBodyKind.Unspecified,
            world.Registry.CurrentBodyKind(world.Identity));
    }

    [Fact]
    public void The_kill_points_actually_visit_every_phase_of_the_pipeline()
    {
        // Every assertion above passes trivially over a script that never leaves
        // the first phase, and a renamed or shortened script is exactly how that
        // would happen quietly. So the phases the kills actually land in are
        // collected and compared with the pipeline itself.
        var visited = new HashSet<NpcPlanPhase>();
        using (var shape = new PlanRehearsal())
        {
            for (int after = 0; after <= shape.Operations.Count; after++)
            {
                using var world = new PlanRehearsal();
                world.Start().RunUpTo(after);
                NpcPlanLoad load = world.Reload();
                Assert.True(load.IsLoaded, load.Failure);
                visited.Add(load.Plan!.Phase);
            }
        }

        foreach (NpcPlanPhase phase in NpcPlanProgression.Pipeline)
        {
            Assert.Contains(phase, visited);
        }

        Assert.Contains(NpcPlanPhase.Settled, visited);
        Assert.Equal(NpcPlanProgression.Pipeline.Length, NpcPlanProgression.Transitions().Length);
    }

    [Theory]
    [InlineData(6, 5)]
    [InlineData(12, 11)]
    public void A_transfer_the_process_died_inside_stops_for_a_person_and_loses_nothing(
        int killedAfter, int halfMoveAt)
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(killedAfter, halfMoveAt);

        // Half the load moved. This is the one shape where both of the plan's
        // writes could have landed and the record would still be wrong, which is
        // why the intent is written before the world is touched rather than after.
        Assert.Equal(PlanRehearsal.Units, world.TotalInTheWorld);

        NpcPlanLoad load = world.Reload();
        Assert.True(load.IsLoaded, load.Failure);
        NpcPlanState recovered = load.Plan!;

        Assert.Equal(NpcPlanCustody.Uncertain, recovered.Custody);
        Assert.NotEqual(world.OnBack, recovered.CarriedUnits);

        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, recovered, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
        Assert.False(decision.Outcome.MayResume);
        Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);
        Assert.Equal(PlanRehearsal.Units, world.TotalInTheWorld);
    }

    [Fact]
    public void A_plan_stopped_for_a_person_is_never_walked_on_and_never_delivers_twice()
    {
        using var world = new PlanRehearsal();

        // Killed after the delivery and before the record of it: the one point
        // where replaying the step would hand the same stone over twice.
        world.Start().RunUpTo(12);
        Assert.Equal(1, world.Deliveries);

        NpcPlanLoad load = world.Reload();
        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, load.Plan, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);

        NpcPlanRun? resumed = NpcPlanRun.Resume(world.Journal(), decision.Next);
        Assert.NotNull(resumed);
        Assert.False(resumed!.MayAct);

        // Every way on is refused: the next phase, the phase it was in, and the
        // finish. A caller that ignored MayAct still cannot walk past this.
        Assert.False(resumed.Advance(NpcPlanPhase.Reconciling, "carry on").IsSaved);
        Assert.False(resumed.Advance(NpcPlanPhase.Executing, "again").IsSaved);
        Assert.False(resumed.Stop(NpcPlanPhase.Settled, "call it done").IsSaved);
        Assert.Equal(1, world.Deliveries);
    }

    [Fact]
    public void A_replan_keeps_the_progress_so_the_remaining_work_is_what_is_planned_again()
    {
        using var world = new PlanRehearsal();

        // Killed after the delivery was recorded: nothing is uncertain, so this
        // is the case where a plan legitimately carries on - and it must carry on
        // from what it had done rather than from zero.
        world.Start().RunUpTo(13);

        NpcPlanLoad load = world.Reload();
        NpcPlanState recovered = load.Plan!;
        Assert.Equal(PlanRehearsal.Targets, recovered.TargetsDone);

        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, recovered, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.Equal(InterruptionResponse.Replan, decision.Outcome.Response);
        Assert.Equal(NpcPlanPhase.Observing, decision.Next.Phase);
        Assert.Equal(PlanRehearsal.Targets, decision.Next.TargetsDone);
        Assert.True(decision.Next.CarriesTheSameWorkAs(recovered));
    }

    [Fact]
    public void Two_bodies_answering_to_one_NPC_is_never_resolved_by_claiming_one_of_them()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(8);

        NpcPlanLoad load = world.Reload();
        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry,
            load.Plan,
            NpcBodyKind.Worker,
            "recovery",
            Evidence(world, bodiesAnswering: 2),
            null);

        // Not claimed at all. The arbiter would have granted it - it knows about
        // holders and kinds, not about how many objects in the scene answer to a
        // name - and the claim would have been on an arbitrary one of the two.
        Assert.Equal(BodyClaimStatus.Unspecified, decision.Claim.Status);
        Assert.Equal(NpcBodyKind.Unspecified, world.Registry.CurrentBodyKind(world.Identity));
        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
        Assert.Equal(InterruptionCause.BodyDuplicated, decision.Outcome.Cause);
        Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);
    }

    [Fact]
    public void Asking_twice_in_one_world_re_attaches_rather_than_taking_a_second_body()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(13);

        NpcPlanLoad load = world.Reload();

        NpcPlanRecovered first = NpcPlanRecovery.Reconstruct(
            world.Registry, load.Plan, NpcBodyKind.Worker, "recovery", Healthy(world), null);
        NpcPlanRecovered second = NpcPlanRecovery.Reconstruct(
            world.Registry, first.Next, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.Equal(BodyClaimStatus.Claimed, first.Claim.Status);
        Assert.Equal(BodyClaimStatus.AlreadyHeld, second.Claim.Status);
        Assert.Equal(NpcBodyKind.Worker, world.Registry.CurrentBodyKind(world.Identity));
    }

    [Fact]
    public void A_plan_already_stopped_for_a_person_stays_stopped_however_many_loads_go_by()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(12);

        NpcPlanState plan = world.Reload().Plan!;
        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, plan, NpcBodyKind.Worker, "recovery", Healthy(world), null);
        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);

        // The record is written, the world is loaded again, and the answer does
        // not soften. Three times, because the failure this guards against is a
        // plan that quietly becomes resumable on some later load.
        // Written down the way a role does it: the run is resumed from what came
        // off the disk, and the decision is what authorises the state it moves to.
        NpcPlanRun? run = NpcPlanRun.Resume(world.Journal(), plan);
        Assert.True(run!.Adopt(decision.Next, decision.Outcome).IsSaved);

        for (int load = 0; load < 3; load++)
        {
            NpcPlanState again = world.Reload().Plan!;
            Assert.Equal(NpcPlanPhase.NeedsAttention, again.Phase);

            NpcPlanRecovered still = NpcPlanRecovery.Reconstruct(
                world.Registry, again, NpcBodyKind.Worker, "recovery", Healthy(world), null);

            Assert.True(still.WasAlreadyOver);
            Assert.Equal(InterruptionResponse.NeedsAttention, still.Outcome.Response);
            Assert.Equal(NpcPlanPhase.NeedsAttention, still.Next.Phase);

            // And no body was taken for it, on any of the loads.
            Assert.Equal(BodyClaimStatus.Unspecified, still.Claim.Status);
            Assert.Equal(NpcBodyKind.Unspecified, world.Registry.CurrentBodyKind(world.Identity));
        }
    }

    [Fact]
    public void A_finished_plan_is_not_resumed_and_not_treated_as_an_absence()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(15);

        NpcPlanState plan = world.Reload().Plan!;
        Assert.Equal(NpcPlanPhase.Settled, plan.Phase);

        NpcPlanRecovered decision = NpcPlanRecovery.Reconstruct(
            world.Registry, plan, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.True(decision.WasAlreadyOver);
        Assert.False(decision.Outcome.MayResume);
        Assert.Equal(NpcPlanPhase.Settled, decision.Next.Phase);
        Assert.Equal(PlanRehearsal.Units, world.AtDestination);
    }

    /// <summary>Re-taking a hold this plan already has is satisfied rather than
    /// doubled, which is what makes a re-plan safe. Asserted here rather than
    /// taken on trust, because every recovery path that answers
    /// <see cref="InterruptionResponse.Replan"/> depends on it.</summary>
    [Fact]
    public void Re_taking_the_same_hold_after_an_interruption_is_satisfied_rather_than_doubled()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(4);

        Assert.Equal(1, world.Holds.Count);
        Assert.Equal(
            ReservationOutcome.AlreadySatisfied,
            world.Holds.Reserve(
                new NpcCustodyLocation(NpcCustodyPlace.Stored, "the-pile", world.World),
                ReservationId.For(world.JobName, 0)));
        Assert.Equal(1, world.Holds.Count);
    }

    /// <summary>One reconstruction test per outcome, because the issue asks for
    /// them by name and because three of the four are reachable only through
    /// evidence the kill suite deliberately holds healthy.
    ///
    /// <b>Continue is not reachable from a reconstruction, and that is the
    /// design.</b> A plan off the disk carries an unknown world epoch, so it is
    /// always stale, and a stale plan never continues. It is reachable for an
    /// interruption that happened inside a session, which is the last case
    /// below.</summary>
    [Fact]
    public void A_reconstruction_reaches_each_of_the_four_outcomes_from_its_own_evidence()
    {
        // Re-plan: nothing wrong but the reload itself.
        Assert.Equal(InterruptionResponse.Replan, Reconstructed(13, Healthy).Outcome.Response);

        // Refund: work may not run here now. What was set aside goes back and
        // nothing that has moved is touched.
        NpcPlanRecovered refunded = Reconstructed(13, world => With(world, mayWork: false));
        Assert.Equal(InterruptionResponse.Refund, refunded.Outcome.Response);
        Assert.Equal(NpcPlanPhase.Refunded, refunded.Next.Phase);
        Assert.Equal(InterruptionCause.AuthorityLost, refunded.Outcome.Cause);

        // Refund again, from the other cause that fails closed rather than
        // widening to a default.
        Assert.Equal(
            InterruptionResponse.Refund,
            Reconstructed(13, world => With(world, areaIsReadable: false)).Outcome.Response);

        // Needs attention: he died holding something.
        NpcPlanRecovered stopped = Reconstructed(8, world => With(world, bodyDied: true));
        Assert.Equal(InterruptionResponse.NeedsAttention, stopped.Outcome.Response);
        Assert.Equal(NpcPlanPhase.NeedsAttention, stopped.Next.Phase);
        Assert.NotEqual(string.Empty, stopped.Outcome.Reason);

        // Continue: the interruption happened in this session, so the plan's names
        // still point at what they named, and a pause is not a fault.
        using var live = new PlanRehearsal();
        live.Start().RunUpTo(9);
        NpcPlanState stillLive = live.Run.State;

        NpcPlanRecovered paused = NpcPlanRecovery.Revalidate(
            stillLive,
            new NpcPlanEvidence(
                live.World, 1, false, true, false, true, true, true, playerPaused: true, at: 50f),
            null);

        Assert.Equal(InterruptionResponse.Continue, paused.Outcome.Response);
        Assert.Equal(stillLive.Phase, paused.Next.Phase);
        Assert.True(paused.Next.CarriesTheSameWorkAs(stillLive));
    }

    private static NpcPlanRecovered Reconstructed(
        int killedAfter, System.Func<PlanRehearsal, NpcPlanEvidence> evidence)
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(killedAfter);
        NpcPlanState plan = world.Reload().Plan!;
        return NpcPlanRecovery.Reconstruct(
            world.Registry, plan, NpcBodyKind.Worker, "recovery", evidence(world), null);
    }

    private static NpcPlanEvidence With(
        PlanRehearsal world,
        bool mayWork = true,
        bool areaIsReadable = true,
        bool bodyDied = false) =>
        new NpcPlanEvidence(
            world.World, 1, bodyDied, areaIsReadable, false, mayWork, true, true, false, 100f);

    private static NpcPlanEvidence Healthy(PlanRehearsal world) => Evidence(world, 1);

    private static NpcPlanEvidence Evidence(PlanRehearsal world, int bodiesAnswering) =>
        new NpcPlanEvidence(
            world.World,
            bodiesAnswering,
            bodyDied: false,
            areaIsReadable: true,
            areaMoved: false,
            mayWork: true,
            containersAvailable: true,
            routeAvailable: true,
            playerPaused: false,
            at: 100f);
}
