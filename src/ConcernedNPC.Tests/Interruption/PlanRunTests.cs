using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

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

    /// <summary>What <c>Adopt</c> refuses, one test per refusal.
    ///
    /// <b>Every one of these is a defect an independent review reached.</b> The
    /// first version of <c>Adopt</c> checked that a decision existed and that the
    /// state named the same plan, and nothing else - so an invented
    /// <c>Replan</c> was a key to everything <c>Record</c> refuses by name. All
    /// four probes below succeeded against it, and the fourth reached the disk.
    /// </summary>
    [Fact]
    public void An_adopted_state_cannot_clear_a_movement_nobody_could_account_for()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);
        Assert.True(world.Run.Conclude(false, world.Run.State.Carried, 0, "nobody can say").IsSaved);
        Assert.Equal(NpcPlanCustody.Uncertain, world.Run.State.Custody);

        // A made-up re-plan, handed a tidier version of the same plan. This is the
        // probe that turned an uncertain plan into an actionable one: custody
        // Clear, MayAct true, and the one state in this library that is supposed to
        // stop for a person quietly did not.
        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.WorldReloaded, 0f, "made up");
        NpcPlanSave refused = world.Run.Adopt(
            world.Run.State
                .WithCustody(NpcPlanCustody.Clear, "tidier")
                .WithPhase(NpcPlanPhase.Observing, "start again"),
            replan);

        Assert.False(refused.IsSaved);
        Assert.Contains("stops it for a person", refused.Failure);
        Assert.Equal(NpcPlanCustody.Uncertain, world.Run.State.Custody);
        Assert.False(world.Run.MayAct);

        // Nor by keeping the custody honest and moving the phase instead: the only
        // decision that may be adopted over an unestablished movement is one that
        // stops the plan for a person.
        Assert.False(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Observing, "start again"), replan).IsSaved);

        var stop = new InterruptionOutcome(
            InterruptionResponse.NeedsAttention, InterruptionCause.TransferUncertain, 0f, "somebody look");
        Assert.True(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.NeedsAttention, "somebody look"), stop).IsSaved);
    }

    [Fact]
    public void A_plan_stopped_for_a_person_is_not_reopened_by_adopting_anything()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);
        Assert.True(world.Run.Conclude(false, world.Run.State.Carried, 0, "nobody can say").IsSaved);
        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);

        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.WorldReloaded, 0f, "the world was loaded");
        NpcPlanSave refused = world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Observing, "reopen it"), replan);

        // "Nothing in this library takes a plan out of NeedsAttention" is the
        // claim; Record honoured it and Adopt did not, which made the claim false
        // for the one verb a recovery actually uses.
        Assert.False(refused.IsSaved);
        Assert.Contains("has ended", refused.Failure);
        Assert.Equal(NpcPlanPhase.NeedsAttention, world.Run.State.Phase);
        Assert.Equal(NpcPlanPhase.NeedsAttention, world.Journal().Load().Plan!.Phase);

        // Including the answer a healthy revalidation would produce for it.
        var carryOn = new InterruptionOutcome(
            InterruptionResponse.Continue, InterruptionCause.PausedByPlayer, 0f, "it was paused");
        Assert.False(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Observing, "carry on"), carryOn).IsSaved);
    }

    [Fact]
    public void An_adopted_state_may_only_be_in_the_phase_the_decision_leaves_a_plan_in()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(1);
        Assert.Equal(NpcPlanPhase.Planned, world.Run.State.Phase);

        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.AreaChanged, 0f, "the area moved");

        // Five phases ahead, on a decision that authorises going back to looking.
        // This was saved by the first version, which is a plan closing books for
        // reservations it never took - the exact failure the phase table exists to
        // refuse.
        NpcPlanSave refused = world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Reconciling, "skip to the end"), replan);
        Assert.False(refused.IsSaved);
        Assert.Contains("not the phase this decision leaves a plan in", refused.Failure);

        // And one phase ahead is refused for the same reason: a re-plan means
        // Observing, not "somewhere forward".
        Assert.False(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Manifested, "one step on"), replan).IsSaved);
        Assert.Equal(NpcPlanPhase.Planned, world.Run.State.Phase);

        Assert.True(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Observing, "look again"), replan).IsSaved);
        Assert.Equal(NpcPlanPhase.Observing, world.Run.State.Phase);
    }

    /// <summary>The rule that says a recovery changes the phase and the note and
    /// nothing else, isolated.
    ///
    /// <b>Why it needed its own test.</b> The first corrective pass added the rule
    /// and no case that only it refuses: every probe the review reported is also
    /// caught by the custody rule or the phase rule, so deleting this refusal from
    /// <c>Adopt</c> left all 902 tests green. The four states below are legal in
    /// every other respect - right decision, right phase, right custody - and each
    /// rewrites the plan under cover of a decision about it.</summary>
    [Fact]
    public void An_adopted_state_may_not_change_what_the_plan_is_holding()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        NpcPlanState plan = world.Run.State;
        Assert.Equal(NpcPlanPhase.Routed, plan.Phase);

        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.AreaChanged, 0f, "the area moved");

        NpcPlanSave refused = world.Run.Adopt(
            plan.WithHoldings(plan.Reservations, new[] { new NpcMaterialStack(world.Stone, 99) })
                .WithPhase(NpcPlanPhase.Observing, "start again"),
            replan);

        Assert.False(refused.IsSaved);
        Assert.Contains("phase and the note and nothing else", refused.Failure);

        // A different cart, more progress than the plan made, and a plan quietly
        // letting go of what it had set aside - all refused for the one reason.
        Assert.False(world.Run.Adopt(
            plan.WithRoute(plan.SourceKey, plan.DestinationKey, "somebody-elses-cart")
                .WithPhase(NpcPlanPhase.Observing, "start again"),
            replan).IsSaved);
        Assert.False(world.Run.Adopt(
            plan.WithProgress(plan.TargetsDone + 1).WithPhase(NpcPlanPhase.Observing, "start again"),
            replan).IsSaved);
        Assert.False(world.Run.Adopt(
            plan.WithHoldings(new ReservationId[0], plan.Carried)
                .WithPhase(NpcPlanPhase.Observing, "start again"),
            replan).IsSaved);

        Assert.Equal(NpcPlanPhase.Routed, world.Run.State.Phase);
        Assert.Equal(plan.CarriedUnits, world.Run.State.CarriedUnits);
    }

    [Fact]
    public void An_adopted_state_has_to_say_whether_anything_is_in_flight()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.WorldReloaded, 0f, "the world was loaded");

        // The refusal Record states by name - "not saying is not the same as no" -
        // which Adopt did not make, so a state with an unset custody field reached
        // the disk and came back from it as uncertain for ever.
        NpcPlanSave refused = world.Run.Adopt(
            world.Run.State
                .WithCustody(NpcPlanCustody.Unspecified, "nobody said")
                .WithPhase(NpcPlanPhase.Observing, "start again"),
            replan);

        Assert.False(refused.IsSaved);
        Assert.Contains("whether anything is in flight", refused.Failure);
        Assert.Equal(NpcPlanCustody.Clear, world.Journal().Load().Plan!.Custody);
    }

    /// <summary>The write-ahead discipline as a refusal rather than a convention.
    ///
    /// <b>Why this test exists.</b> A review of #379 walked a run from
    /// <c>Reserved</c> to <c>Reconciling</c> with no <c>Intend</c> and no
    /// <c>Conclude</c> anywhere, reloaded it, and was told the plan could be
    /// resumed - and <c>MovesMaterialToReach</c>, the one function naming the two
    /// material-moving transitions, had no callers at all: replacing its body with
    /// <c>false</c> left every test green while two design documents asserted that
    /// those transitions "are written twice".</summary>
    [Fact]
    public void A_phase_reached_by_moving_material_is_refused_unless_the_movement_was_written_first()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(4);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);
        Assert.Equal(NpcPlanCustody.Clear, world.Run.State.Custody);

        NpcPlanSave refused = world.Run.Advance(NpcPlanPhase.Provisioned, "call it loaded");
        Assert.False(refused.IsSaved);
        Assert.Contains("moved twice", refused.Failure);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        // The way through is the one the whole design is about: say it is about to
        // happen, then say what became of it.
        Assert.True(world.Run.Intend("about to load up").IsSaved);
        Assert.True(world.Run.Conclude(true, world.Run.State.Carried, 0, "loaded").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved);

        // And the pair is spent where it was written, not banked for the rest of
        // the plan: the delivery has to be written down too.
        Assert.True(world.Run.Advance(NpcPlanPhase.Routed, "walk ordered").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Executing, "walking it").IsSaved);

        NpcPlanSave second = world.Run.Advance(NpcPlanPhase.Reconciling, "closing the books");
        Assert.False(second.IsSaved);
        Assert.Contains("moved twice", second.Failure);

        Assert.True(world.Run.Intend("about to hand over").IsSaved);
        Assert.True(world.Run.Conclude(true, new NpcMaterialStack[0], 4, "delivered").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Reconciling, "closing the books").IsSaved);
    }

    /// <summary>A conclusion that establishes nothing stops the plan rather than
    /// buying the next step.
    ///
    /// <b>Which rule does the refusing, stated rather than implied.</b> The custody
    /// rule, not the material-movement rule: an unestablished outcome leaves the
    /// plan <c>Uncertain</c>, and an uncertain plan may only be stopped for a
    /// person. So the <c>Clear</c> test inside the run's concluded-intent tracker
    /// is unreachable defence-in-depth - relaxing it to "no longer pending" leaves
    /// all 902 tests green - and this test does not pretend otherwise. What it
    /// proves is that the two rules compose: there is no order of writes that
    /// reaches <c>Provisioned</c> without an outcome somebody could
    /// establish.</summary>
    [Fact]
    public void An_outcome_nobody_could_establish_stops_the_plan_instead_of_buying_the_next_step()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);

        Assert.True(world.Run.Conclude(false, world.Run.State.Carried, 0, "nobody can say").IsSaved);
        Assert.False(world.Run.Advance(NpcPlanPhase.Provisioned, "call it loaded").IsSaved);
        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);
    }

    [Fact]
    public void A_plan_resumed_from_the_disk_says_the_movement_again_before_it_claims_it_happened()
    {
        using var world = new PlanRehearsal();

        // The conclusion of the gather is on the disk; the phase change it earns is
        // not, because that is the next operation and the process died first.
        world.Start().RunUpTo(7);

        NpcPlanState fromTheDisk = world.Reload().Plan!;
        NpcPlanRun? resumed = NpcPlanRun.Resume(world.Journal(), fromTheDisk);
        Assert.NotNull(resumed);
        Assert.Equal(NpcPlanPhase.Reserved, resumed!.State.Phase);

        // What a run knows about its own concluded intent is in memory, so a new
        // process knows nothing about it and is refused. This is the documented
        // limit of the enforcement, asserted here rather than left in a paragraph.
        Assert.False(resumed.Advance(NpcPlanPhase.Provisioned, "he was loaded before the crash").IsSaved);

        // The way on costs two writes and moves nothing: Conclude records what was
        // measured, so a body that is already loaded is recorded as already loaded
        // and nothing is fetched a second time.
        Assert.True(resumed.Intend("checking what he is actually holding").IsSaved);
        Assert.True(resumed.Conclude(
            true, resumed.State.Carried, resumed.State.TargetsDone, "he has it").IsSaved);
        Assert.True(resumed.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved);
        Assert.Equal(PlanRehearsal.Units, resumed.State.CarriedUnits);
        Assert.Equal(1, world.Gathers);
    }

    /// <summary>The exit from staleness, which had no caller and no test.
    ///
    /// <b>The loop this closes.</b> A plan off the disk is in no world, so it is
    /// stale, so a stale plan re-plans - and then it is still in no world, so it
    /// re-plans again. A review of #379 observed five consecutive revalidations
    /// answering "the world reloaded, so re-plan" against entirely healthy
    /// evidence, and <c>NpcPlanState.WithWorld</c> - the documented way out - had
    /// zero references and zero tests.</summary>
    [Fact]
    public void A_re_planned_plan_re_attached_to_this_world_stops_being_stale()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(13);

        NpcPlanState fromTheDisk = world.Reload().Plan!;
        NpcPlanRecovered decision = NpcPlanRecovery.Revalidate(fromTheDisk, Healthy(world), null);
        Assert.Equal(InterruptionResponse.Replan, decision.Outcome.Response);

        NpcPlanRun? resumed = NpcPlanRun.Resume(world.Journal(), fromTheDisk);
        Assert.True(resumed!.Adopt(decision.Next, decision.Outcome).IsSaved);

        // Still in no world, so still stale, so still re-plan - for ever, until
        // somebody says otherwise.
        Assert.False(resumed.State.IsLiveIn(world.World));
        NpcPlanRecovered again = NpcPlanRecovery.Revalidate(resumed.State, Healthy(world), null);
        Assert.Equal(InterruptionResponse.Replan, again.Outcome.Response);
        Assert.Equal(InterruptionCause.WorldReloaded, again.Outcome.Cause);

        // The role says it has found its things. Nothing here checks that claim,
        // because nothing here can see the world - and nothing about the work
        // changes, so an unresolved movement would stay unresolved.
        Assert.True(resumed.Reattach(world.World, "his pile and his depot are where the plan says").IsSaved);
        Assert.True(resumed.State.IsLiveIn(world.World));
        Assert.True(resumed.State.CarriesTheSameWorkAs(decision.Next));
        Assert.Equal(NpcPlanPhase.Observing, resumed.State.Phase);

        // And the loop is actually broken: the reload is no longer what a
        // revalidation reports.
        Assert.NotEqual(
            InterruptionCause.WorldReloaded,
            NpcPlanRecovery.Revalidate(resumed.State, Healthy(world), null).Outcome.Cause);

        // A plan cannot be re-attached to no world at all, which is what it already
        // says it is in.
        Assert.False(resumed.Reattach(NpcWorldEpoch.Unknown, "nowhere").IsSaved);
    }

    private static NpcPlanEvidence Healthy(PlanRehearsal world) =>
        new NpcPlanEvidence(
            world.World,
            bodiesAnswering: 1,
            bodyDied: false,
            areaIsReadable: true,
            areaMoved: false,
            mayWork: true,
            containersAvailable: true,
            routeAvailable: true,
            playerPaused: false,
            at: 100f);
}
