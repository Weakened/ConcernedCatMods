using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Interruption;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The seven-rung ladder, and what an interruption is allowed to do to
/// the plan underneath it.
///
/// <b>The ladder is asserted as an order, not as seven numbers.</b> A test that
/// checked each value against a literal would pass after somebody renumbered the
/// enum consistently and inverted the meaning of the comparison; a test that
/// checks that every rung outranks every rung below it, over every pair, would
/// not.
///
/// <b>The preservation property is the other half.</b> An interruption is allowed
/// to take the body and to say why. It is not allowed to change what the plan is
/// about or what it is holding, and it must leave the record at a point a
/// resumption can start from - which is asserted by resuming from the disk rather
/// than by reading the object that was handed back.</summary>
public class ActivityArbitrationTests
{
    /// <summary>The rungs, lowest first, in the order the brief states them:
    /// the camp-border stroll, social idle, background maintenance, required role
    /// work, an explicit player order, danger, and lifecycle and recovery safety.
    /// </summary>
    private static readonly NpcActivityPriority[] Ladder =
    {
        NpcActivityPriority.CampBorderStroll,
        NpcActivityPriority.SocialIdle,
        NpcActivityPriority.BackgroundMaintenance,
        NpcActivityPriority.RequiredRoleWork,
        NpcActivityPriority.PlayerOrder,
        NpcActivityPriority.Danger,
        NpcActivityPriority.LifecycleSafety,
    };

    [Fact]
    public void There_are_seven_rungs_and_every_one_outranks_every_rung_below_it()
    {
        Assert.Equal(7, Ladder.Length);

        for (int higher = 0; higher < Ladder.Length; higher++)
        {
            for (int lower = 0; lower < Ladder.Length; lower++)
            {
                bool expected = higher > lower;
                Assert.Equal(expected, NpcActivityArbiter.MayInterrupt(Ladder[lower], Ladder[higher]));
            }
        }
    }

    [Fact]
    public void An_equal_priority_never_takes_the_body_from_something_already_in_progress()
    {
        // The running activity has a plan and the arriving one does not. Swapping
        // on a tie is how an NPC spends a session starting two jobs and finishing
        // neither.
        foreach (NpcActivityPriority rung in Ladder)
        {
            Assert.False(NpcActivityArbiter.MayInterrupt(rung, rung));
        }
    }

    [Fact]
    public void An_activity_nobody_named_neither_takes_a_body_nor_gives_one_up()
    {
        foreach (NpcActivityPriority rung in Ladder)
        {
            // It cannot preempt: a caller that forgot to say what it wanted does
            // not get to displace a player's own order.
            Assert.False(NpcActivityArbiter.MayInterrupt(rung, NpcActivityPriority.Unspecified));

            // And it cannot be preempted: it might be any rung, including the one
            // that is making the record safe again.
            Assert.False(NpcActivityArbiter.MayInterrupt(NpcActivityPriority.Unspecified, rung));
        }

        NpcActivityHandover refused = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.Unspecified, NpcActivityPriority.Danger, null, "a boar");

        Assert.False(refused.IsGranted);
        Assert.Contains("nobody named", refused.Reason);
    }

    public static IEnumerable<object[]> EveryPhaseAndEveryHigherRung
    {
        get
        {
            // Every point in the pipeline an interruption can land at, crossed
            // with every rung that is allowed to interrupt required role work.
            using var shape = new PlanRehearsal();
            for (int after = 0; after <= shape.Operations.Count; after++)
            {
                yield return new object[] { after, (int)NpcActivityPriority.PlayerOrder };
                yield return new object[] { after, (int)NpcActivityPriority.Danger };
                yield return new object[] { after, (int)NpcActivityPriority.LifecycleSafety };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryPhaseAndEveryHigherRung))]
    public void An_interruption_preserves_the_plan_and_leaves_it_resumable(int interruptedAfter, int arrivingRung)
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(interruptedAfter);

        NpcPlanState before = world.Run.State;

        NpcActivityHandover handover = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.RequiredRoleWork,
            (NpcActivityPriority)arrivingRung,
            world.Run,
            "something more important arrived");

        Assert.True(handover.IsGranted);

        // Inventory, reservations, custody, the plan, the cart, progress, source,
        // destination and identity, and the phase it was in.
        Assert.True(handover.PreservedThePlan);
        Assert.Equal(before.Phase, handover.Suspended!.Phase);
        Assert.True(handover.Suspended!.CarriesTheSameWorkAs(before));

        // And the record on the disk is still something a resumption starts from,
        // which is the claim that actually matters: the object handed back could be
        // perfect while the file was not.
        NpcPlanState fromTheDisk = world.Journal().Load().Plan!;
        Assert.Equal(before.Phase, fromTheDisk.Phase);
        Assert.True(fromTheDisk.CarriesTheSameWorkAs(before.AsRecovered()));
    }

    [Fact]
    public void An_interrupted_job_resumes_where_it_was_rather_than_starting_again()
    {
        using var world = new PlanRehearsal();

        // Loaded, routed and walking. If an interruption restarted this job it
        // would gather a second load, and the units already on his back would be
        // material nobody has a record of.
        world.Start().RunUpTo(10);
        NpcPlanState before = world.Run.State;
        Assert.Equal(NpcPlanPhase.Executing, before.Phase);
        Assert.Equal(PlanRehearsal.Units, before.CarriedUnits);
        int gathers = world.Gathers;

        NpcActivityHandover handover = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.RequiredRoleWork, NpcActivityPriority.PlayerOrder, world.Run, "come here");
        Assert.True(handover.IsGranted);

        // The player's order is done with, and the job picks the plan back up. It
        // is at the step it was at, still carrying what it was carrying, and it
        // does not go back to looking.
        NpcPlanRun? resumed = NpcPlanRun.Resume(world.Journal(), world.Journal().Load().Plan);

        Assert.NotNull(resumed);
        Assert.Equal(NpcPlanPhase.Executing, resumed!.State.Phase);
        Assert.NotEqual(NpcPlanPhase.Observing, resumed.State.Phase);
        Assert.Equal(PlanRehearsal.Units, resumed.State.CarriedUnits);
        Assert.Equal(before.Reservations, resumed.State.Reservations);
        Assert.Equal(gathers, world.Gathers);
        Assert.True(resumed.MayAct);
    }

    [Fact]
    public void A_refused_handover_changes_nothing_at_all()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(8);

        NpcPlanState before = world.Run.State;
        int writesBefore = world.Codec.Writes;

        NpcActivityHandover refused = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.PlayerOrder, NpcActivityPriority.SocialIdle, world.Run, "a chat");

        Assert.False(refused.IsGranted);
        Assert.False(refused.IsNoteRecorded);
        Assert.Equal(writesBefore, world.Codec.Writes);
        Assert.Same(before, world.Run.State);
        Assert.True(refused.PreservedThePlan);
    }

    [Fact]
    public void A_note_that_could_not_be_written_does_not_make_an_NPC_stand_and_take_the_hit()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(8);

        NpcPlanState before = world.Run.State;
        world.Codec.Refuse = true;

        NpcActivityHandover handover = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.RequiredRoleWork, NpcActivityPriority.Danger, world.Run, "a troll");

        // Granted, and honest about what it could not do. The record was already
        // at a safe resume point before the note was attempted, which is the whole
        // value of writing every phase down before acting on it.
        Assert.True(handover.IsGranted);
        Assert.False(handover.IsNoteRecorded);
        Assert.True(handover.PreservedThePlan);

        world.Codec.Refuse = false;
        NpcPlanState fromTheDisk = world.Journal().Load().Plan!;
        Assert.Equal(before.Phase, fromTheDisk.Phase);
        Assert.True(fromTheDisk.CarriesTheSameWorkAs(before.AsRecovered()));
    }

    [Fact]
    public void Recovery_safety_is_the_one_thing_nothing_else_may_interrupt()
    {
        // The reason it is the top rung: every other rung assumes the record and
        // the world agree, and this is the activity that makes that true.
        foreach (NpcActivityPriority rung in Ladder)
        {
            Assert.False(NpcActivityArbiter.MayInterrupt(NpcActivityPriority.LifecycleSafety, rung));
        }

        Assert.True(NpcActivityArbiter.MayInterrupt(
            NpcActivityPriority.Danger, NpcActivityPriority.LifecycleSafety));
    }

    [Fact]
    public void An_activity_with_no_plan_to_preserve_has_preserved_it()
    {
        NpcActivityHandover handover = NpcActivityArbiter.Interrupt(
            NpcActivityPriority.CampBorderStroll, NpcActivityPriority.PlayerOrder, null, "come here");

        Assert.True(handover.IsGranted);
        Assert.True(handover.IsNoteRecorded);
        Assert.Null(handover.Suspended);
        Assert.True(handover.PreservedThePlan);
    }
}
