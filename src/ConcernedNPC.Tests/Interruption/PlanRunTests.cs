using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The ordering rule, tested by trying to break it.
///
/// <b>What the rule buys, and therefore what these tests protect.</b> A caller may
/// only act on a phase the record already says it is in, so the record is always at
/// or ahead of the world and the last written record is always a safe place to
/// resume from. Every refusal below is a way a caller could get ahead of its own
/// record, and each one of them would turn the kill suite's single recovery answer
/// into a list of special cases.</summary>
public class PlanRunTests
{
    [Fact]
    public void A_plan_with_a_movement_in_flight_may_not_walk_on_to_the_next_phase()
    {
        using var world = new PlanRehearsal();

        // Killed straight after the intent: the world may or may not have moved.
        world.Start().RunUpTo(5);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);
        Assert.False(world.Run.MayAct);

        NpcPlanSave refused = world.Run.Advance(NpcPlanPhase.Provisioned, "call it loaded");

        Assert.False(refused.IsSaved);
        Assert.Contains("moved twice", refused.Failure);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        // Saying what happened is always available, which is the only way out.
        Assert.True(world.Run.Conclude(true, world.Run.State.Carried, 0, "nothing moved").IsSaved);
        Assert.True(world.Run.MayAct);
        Assert.True(world.Run.Advance(NpcPlanPhase.Provisioned, "loaded").IsSaved);
    }

    [Fact]
    public void An_unresolved_movement_may_be_stopped_for_a_person_and_nothing_else()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);

        Assert.True(world.Run.Conclude(false, world.Run.State.Carried, 0, "nobody can say").IsSaved);
        Assert.Equal(NpcPlanCustody.Uncertain, world.Run.State.Custody);
        Assert.False(world.Run.MayAct);

        // Not forward, not finished, and not quietly clear again.
        Assert.False(world.Run.Advance(NpcPlanPhase.Provisioned, "carry on").IsSaved);
        Assert.False(world.Run.Stop(NpcPlanPhase.Settled, "done").IsSaved);
        Assert.False(world.Run.Conclude(true, world.Run.State.Carried, 0, "it was fine after all").IsSaved);

        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);
    }

    [Fact]
    public void A_write_the_role_refused_leaves_the_plan_exactly_where_it_was()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(2);

        NpcPlanState before = world.Run.State;
        world.Codec.Refuse = true;

        Assert.False(world.Run.Advance(NpcPlanPhase.Reserved, "set aside").IsSaved);

        // Fails closed. A caller that could not record what it is about to do must
        // not do it, and the in-memory state is what a caller reads to decide.
        Assert.Same(before, world.Run.State);
        Assert.Equal(NpcPlanPhase.Manifested, world.Run.State.Phase);
    }

    [Fact]
    public void An_ending_is_an_ending()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(15);

        Assert.Equal(NpcPlanPhase.Settled, world.Run.State.Phase);
        Assert.False(world.Run.MayAct);

        foreach (NpcPlanPhase phase in new[]
                 {
                     NpcPlanPhase.Observing, NpcPlanPhase.Executing, NpcPlanPhase.Settled,
                     NpcPlanPhase.Refunded, NpcPlanPhase.NeedsAttention,
                 })
        {
            NpcPlanSave refused = world.Run.Advance(phase, "once more");
            Assert.False(refused.IsSaved);
            Assert.Contains("has ended", refused.Failure);
        }
    }

    [Fact]
    public void A_phase_that_does_not_follow_the_current_one_is_refused()
    {
        using var world = new PlanRehearsal();
        world.Start();

        // Straight from looking to closing the books: a plan that held nothing it
        // thinks it holds.
        Assert.False(world.Run.Advance(NpcPlanPhase.Reconciling, "skip to the end").IsSaved);
        Assert.False(world.Run.Advance(NpcPlanPhase.Reserved, "skip the plan").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Planned, "one step").IsSaved);
    }

    [Fact]
    public void One_run_writes_one_plan()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(3);

        NpcPlanState somebodyElses = new NpcPlanState(
            Identities.Thorstein,
            "build-round",
            NpcPlanPhase.Provisioned,
            NpcPlanCustody.Clear,
            new[] { ReservationId.For("build-round", 0) },
            new[] { new NpcMaterialStack(NpcMaterial.Of("wood"), 4) },
            0,
            1,
            null,
            null,
            null,
            world.World,
            0,
            "a different NPC's plan");

        NpcPlanSave refused = world.Run.Record(somebodyElses);

        Assert.False(refused.IsSaved);
        Assert.Contains("different plan", refused.Failure);
    }

    [Fact]
    public void A_plan_starts_by_looking_and_starts_named()
    {
        using var world = new PlanRehearsal();

        Assert.Null(NpcPlanRun.Begin(world.Journal(), null, out NpcPlanSave nothing));
        Assert.False(nothing.IsSaved);

        Assert.Null(NpcPlanRun.Begin(
            world.Journal(),
            NpcPlanState.Opening(Identities.Gunnar, "haul", world.World, 2)
                .WithPhase(NpcPlanPhase.Executing, "already walking"),
            out NpcPlanSave midway));
        Assert.False(midway.IsSaved);
        Assert.Contains("starts by looking", midway.Failure);

        Assert.Null(NpcPlanRun.Begin(null, NpcPlanState.Opening(Identities.Gunnar, "haul", world.World, 2), out _));
        Assert.Null(NpcPlanRun.Resume(world.Journal(), null));
    }

    [Fact]
    public void A_state_nothing_decided_is_never_adopted()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        NpcPlanState plan = world.Run.State;

        // No decision at all.
        Assert.False(world.Run.Adopt(plan, default).IsSaved);

        // A decision that says the job may not go on, handed a state that has not
        // ended. This is the shape that would let the backward path be reached by
        // making up an outcome.
        var refund = new InterruptionOutcome(
            InterruptionResponse.Refund, InterruptionCause.AuthorityLost, 0f, "work may not run here");
        NpcPlanSave refused = world.Run.Adopt(plan.WithPhase(NpcPlanPhase.Observing, "start again"), refund);

        Assert.False(refused.IsSaved);
        Assert.Contains("has to end somewhere", refused.Failure);

        // The same decision with an ending is adopted, and a re-plan may go
        // backwards - which is the one write that may.
        Assert.True(world.Run.Adopt(plan.WithPhase(NpcPlanPhase.Refunded, "released"), refund).IsSaved);
        Assert.Equal(NpcPlanPhase.Refunded, world.Run.State.Phase);
    }

    [Fact]
    public void A_re_plan_is_the_one_write_that_may_go_backwards()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        NpcPlanState plan = world.Run.State;
        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.AreaChanged, 0f, "the area moved");

        // Refused as an ordinary write, accepted as an adopted decision.
        Assert.False(world.Run.Record(plan.WithPhase(NpcPlanPhase.Observing, "start again")).IsSaved);
        Assert.True(world.Run.Adopt(plan.WithPhase(NpcPlanPhase.Observing, "start again"), replan).IsSaved);
        Assert.Equal(NpcPlanPhase.Observing, world.Run.State.Phase);
        Assert.True(world.Run.State.CarriesTheSameWorkAs(plan));
    }
}
