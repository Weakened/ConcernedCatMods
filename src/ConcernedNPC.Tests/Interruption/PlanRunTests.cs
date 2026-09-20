using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
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

    /// <summary>A movement with no recorded outcome cannot be written into an
    /// ending.
    ///
    /// <b>The blanket exemption this removes was an inconsistency, not a
    /// decision.</b> The pending rule exempted every terminal phase, so a plan
    /// could report itself finished, or refunded, over a movement nobody had
    /// accounted for - and the reload called it "already ended, nothing to
    /// resume". No document mentions the exemption; three passages say the
    /// opposite, including <c>InterruptionResponse.Refund</c>'s own summary. And it
    /// is reachable by an ordinary role: <c>Refund</c> is the answer to lost
    /// authority, which can fire in the window between <c>Intend</c> and
    /// <c>Conclude</c>, and <c>Stop(Refunded)</c> is the obvious verb for it.
    /// </summary>
    [Fact]
    public void A_movement_with_no_recorded_outcome_cannot_be_written_into_an_ending()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);

        NpcPlanSave settled = world.Run.Stop(NpcPlanPhase.Settled, "call it done");
        Assert.False(settled.IsSaved);
        Assert.Contains("moved twice", settled.Failure);

        Assert.False(world.Run.Stop(NpcPlanPhase.Refunded, "give back what was set aside").IsSaved);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        // The one ending that is available is the one that tells somebody, and it
        // keeps the pending custody rather than tidying it away - because it is
        // AsRecovered that turns a pending movement into the uncertainty a person
        // resolves, and it can only do that if the evidence is still there.
        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);

        NpcPlanState fromTheDisk = world.Journal().Load().Plan!;
        Assert.Equal(NpcPlanPhase.NeedsAttention, fromTheDisk.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, fromTheDisk.Custody);
    }

    [Fact]
    public void An_ending_cannot_erase_the_evidence_that_something_was_in_flight()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);

        // The worse of the two shapes, and the one a reload cannot undo: a single
        // write that both ends the plan and says nothing was ever in flight. After
        // it, not even AsRecovered can get the question back, because nothing is
        // left saying there was one.
        NpcPlanSave erased = world.Run.Record(
            world.Run.State
                .WithCustody(NpcPlanCustody.Clear, "all fine after all")
                .WithPhase(NpcPlanPhase.Refunded, "released"));

        Assert.False(erased.IsSaved);
        Assert.Contains("erases the only evidence", erased.Failure);

        // The same for the other ending, and for an ordinary phase change: it is
        // the laundered custody that is refused, not the destination.
        Assert.False(world.Run.Record(
            world.Run.State
                .WithCustody(NpcPlanCustody.Clear, "all fine after all")
                .WithPhase(NpcPlanPhase.Settled, "done")).IsSaved);
        Assert.False(world.Run.Record(
            world.Run.State
                .WithCustody(NpcPlanCustody.Clear, "all fine after all")
                .WithPhase(NpcPlanPhase.Provisioned, "loaded")).IsSaved);

        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);
        Assert.Equal(NpcPlanCustody.Uncertain, world.Journal().Load().Plan!.Custody);
    }

    [Fact]
    public void A_plan_row_in_no_phase_at_all_is_an_unreadable_record_rather_than_a_plan()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);
        world.RewriteThePhaseAsNobodySet();

        NpcPlanLoad load = world.Journal().Load();

        // Neither a plan nor an absence: there is something there and it is not
        // something to act on. A role that gets this quarantines the file and tells
        // the player, rather than resuming a record somebody wrote wrong.
        Assert.False(load.IsLoaded);
        Assert.Null(load.Plan);
        Assert.Contains("no phase at all", load.Failure);
    }

    /// <summary>The one record the phase enum calls "never resumed, never
    /// continued, never counted as the start" used to be the one record that could
    /// not be stopped and could be laundered live.
    ///
    /// <b>How the asymmetry was decided.</b> <c>MayFollow</c> refuses
    /// <c>Unspecified</c> on both sides and still does: that table is about the
    /// shape of the pipeline, and a phase nobody set is not a position on the line.
    /// What was wrong was the consequence - the library could hold such a state and
    /// could not hand it to anybody - so <c>NpcPlanRun.WhyNot</c> makes "stop this
    /// for a person" available whatever phase a plan is in, as long as the stop
    /// changes nothing but the phase and the note. Everything else stays
    /// refused.</summary>
    [Fact]
    public void A_plan_in_no_phase_at_all_can_be_stopped_for_a_person_and_nothing_else()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        NpcPlanState wrong = world.Run.State.WithPhase(NpcPlanPhase.Unspecified, "somebody wrote this wrong");
        NpcPlanRun? resumed = NpcPlanRun.Resume(world.Journal(), wrong);
        Assert.NotNull(resumed);
        Assert.False(resumed!.MayAct);

        Assert.False(resumed.Advance(NpcPlanPhase.Observing, "start from the top").IsSaved);
        Assert.False(resumed.Advance(NpcPlanPhase.Routed, "carry on").IsSaved);
        Assert.False(resumed.Stop(NpcPlanPhase.Settled, "call it done").IsSaved);
        Assert.False(resumed.Stop(NpcPlanPhase.Refunded, "give it back").IsSaved);

        // And a recovery cannot launder it into a live plan. This was the sixth way
        // into Adopt: Reconstruct answered Replan, Adopt saved it, and the plan was
        // live at Observing still claiming ten units of stone, its reservations and
        // its cart.
        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.WorldReloaded, 0f, "the world was loaded");
        NpcPlanSave laundered = resumed.Adopt(wrong.WithPhase(NpcPlanPhase.Observing, "start again"), replan);

        Assert.False(laundered.IsSaved);
        Assert.Contains("no phase at all", laundered.Failure);
        Assert.False(resumed.MayAct);

        // Both verbs can hand it to a person, which is the point of the decision:
        // there is no state this library holds that it cannot pass on.
        var stop = new InterruptionOutcome(
            InterruptionResponse.NeedsAttention, InterruptionCause.Unspecified, 0f, "a record in no phase");
        Assert.True(resumed.Adopt(wrong.WithPhase(NpcPlanPhase.NeedsAttention, "somebody look"), stop).IsSaved);

        NpcPlanRun? again = NpcPlanRun.Resume(world.Journal(), wrong);
        Assert.True(again!.Stop(NpcPlanPhase.NeedsAttention, "this record is in no phase at all").IsSaved);
        Assert.Equal(NpcPlanPhase.NeedsAttention, again.State.Phase);
        Assert.Equal(NpcPlanPhase.NeedsAttention, world.Journal().Load().Plan!.Phase);
    }

    [Fact]
    public void A_plan_in_no_phase_at_all_is_handed_to_a_person_rather_than_re_planned()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(9);

        NpcPlanState wrong = world.Run.State.WithPhase(NpcPlanPhase.Unspecified, "somebody wrote this wrong");

        // Not Replan, which is what "in no world, so stale, so re-plan" produced
        // for it: the plan is not live in any world precisely because its phase is
        // zero, so the staleness rule answered a question nobody should have been
        // asking.
        NpcPlanRecovered decision = NpcPlanRecovery.Revalidate(wrong, Healthy(world), null);

        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
        Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);
        Assert.True(decision.WasAlreadyOver);
        Assert.NotEqual(string.Empty, decision.Outcome.Reason);
        Assert.True(decision.Next.CarriesTheSameWorkAs(wrong));

        // The same through the policy the products would actually pass in, so this
        // is the recovery path's answer rather than a default policy's.
        Assert.Equal(
            InterruptionResponse.NeedsAttention,
            NpcPlanRecovery.Revalidate(wrong, Healthy(world), NpcWorkInterruptionPolicy.Instance)
                .Outcome.Response);

        // And through Reconstruct, which is the path with a body in it and was the
        // one with no test: deleting its arm left all 913 green while Revalidate's
        // arm carried the claim. Without it this claims a body and answers Replan.
        NpcPlanRecovered reconstructed = NpcPlanRecovery.Reconstruct(
            world.Registry, wrong, NpcBodyKind.Worker, "recovery", Healthy(world), null);

        Assert.Equal(InterruptionResponse.NeedsAttention, reconstructed.Outcome.Response);
        Assert.Equal(NpcPlanPhase.NeedsAttention, reconstructed.Next.Phase);
        Assert.True(reconstructed.WasAlreadyOver);

        // "Before it asks for a body" is the documented order, so it is asserted
        // rather than read: nothing was claimed and nothing has to be released.
        Assert.False(reconstructed.Claim.IsGranted);
        Assert.Equal(NpcBodyKind.Unspecified, world.Registry.CurrentBodyKind(world.Identity));
    }

    [Fact]
    public void A_concluded_intent_is_not_carried_across_a_re_plan_and_spent_a_second_time()
    {
        using var world = new PlanRehearsal();

        // The gather was announced and accounted for, so this run has earned the
        // move into Provisioned - once.
        world.Start().RunUpTo(7);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        var replan = new InterruptionOutcome(
            InterruptionResponse.Replan, InterruptionCause.AreaChanged, 0f, "the area moved");
        Assert.True(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Observing, "look again"), replan).IsSaved);

        // Back up the pipeline to the same phase. Without Adopt's reset the flag
        // still says Reserved, and this move is granted a second time on the
        // strength of a movement that was accounted for before the re-plan -
        // deleting that one line leaves all 903 tests green.
        Assert.True(world.Run.Advance(NpcPlanPhase.Planned, "a plan exists").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Manifested, "totalled").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Reserved, "set aside").IsSaved);

        NpcPlanSave refused = world.Run.Advance(NpcPlanPhase.Provisioned, "still loaded from last time");
        Assert.False(refused.IsSaved);
        Assert.Contains("moved twice", refused.Failure);
    }

    [Fact]
    public void A_note_written_in_the_phase_does_not_buy_the_move_that_moves_material()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(4);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        // A suspension is a same-phase write, and the suite used to pin only "some
        // same-phase write happened": replacing the whole condition that sets the
        // concluded-intent flag with `true` left all 903 tests green, and under
        // that mutation this note bought the move into Provisioned.
        Assert.True(world.Run.Suspend("a boar turned up").IsSaved);
        NpcPlanSave afterANote = world.Run.Advance(NpcPlanPhase.Provisioned, "call it loaded");
        Assert.False(afterANote.IsSaved);
        Assert.Contains("moved twice", afterANote.Failure);

        // Nor does an intent on its own, with no outcome written after it.
        Assert.True(world.Run.Intend("about to load up").IsSaved);
        Assert.False(world.Run.Advance(NpcPlanPhase.Provisioned, "call it loaded").IsSaved);

        // Only the pair.
        Assert.True(world.Run.Conclude(true, world.Run.State.Carried, 0, "loaded").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved);
    }

    /// <summary>The write-ahead discipline binds the load, not only the two
    /// transitions.
    ///
    /// <b>Why both rules are needed.</b> In-phase writes skip the pending, the
    /// uncertain and the material-transition rules, so with only a rule about the
    /// two phase changes any same-phase write could rewrite what the NPC is
    /// carrying, in any phase, with no intent at all - and the discipline would bind
    /// which phase a plan was in rather than what it was holding.</summary>
    [Fact]
    public void What_the_NPC_is_carrying_only_changes_as_the_outcome_of_a_movement_written_down_first()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(7);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);

        NpcPlanSave invented = world.Run.Record(
            world.Run.State.WithHoldings(
                world.Run.State.Reservations, new[] { new NpcMaterialStack(world.Stone, 99) }));

        Assert.False(invented.IsSaved);
        Assert.Contains("recorded outcome of a movement", invented.Failure);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);

        // And in a phase where no material moves at all, which is where a rule about
        // the two transitions would never have looked. Losing a load silently is
        // refused the same way as inventing one.
        Assert.True(world.Run.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Routed, "walk ordered").IsSaved);
        Assert.False(world.Run.Record(
            world.Run.State.WithHoldings(
                world.Run.State.Reservations, new NpcMaterialStack[0])).IsSaved);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);

        // The way to change it is the one the whole design is about.
        Assert.True(world.Run.Advance(NpcPlanPhase.Executing, "walking it").IsSaved);
        Assert.True(world.Run.Intend("handing some over").IsSaved);
        Assert.True(world.Run.Conclude(
            true, new[] { new NpcMaterialStack(world.Stone, 4) }, 1, "handed six over").IsSaved);
        Assert.Equal(4, world.Run.State.CarriedUnits);
    }

    /// <summary>The one guard on the always-available stop, pinned.
    ///
    /// <b>Why a single conjunct needs its own test.</b> The stop-for-a-person rule
    /// sits above every other rule in <c>WhyNot</c>, so
    /// <c>CarriesTheSameWorkAs</c> is the only thing between it and both pending
    /// rules, the uncertain rule, the load rule and the refusal of an unset custody
    /// field. A review of #379 deleted that one conjunct and found all 913 tests
    /// green, then showed the escape was real: a stop could drop a load or launder
    /// a pending custody on the way past, which is the erasure shape the rule
    /// immediately below it exists to refuse.</summary>
    [Fact]
    public void A_stop_for_a_person_may_not_change_the_load_or_launder_the_custody_on_the_way_past()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(7);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);
        Assert.Equal(NpcPlanCustody.Clear, world.Run.State.Custody);

        // Stopping for a person while quietly putting down ten units of stone.
        NpcPlanSave dropped = world.Run.Record(
            world.Run.State
                .WithHoldings(world.Run.State.Reservations, new NpcMaterialStack[0])
                .WithPhase(NpcPlanPhase.NeedsAttention, "somebody look"));

        Assert.False(dropped.IsSaved);
        Assert.Contains("recorded outcome of a movement", dropped.Failure);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);

        // And the same escape from a pending movement, which is worse: this is
        // MAJOR C's erasure shape reached through the one path that is allowed to
        // ignore the pending rules.
        Assert.True(world.Run.Intend("about to hand over").IsSaved);
        NpcPlanSave laundered = world.Run.Record(
            world.Run.State
                .WithCustody(NpcPlanCustody.Clear, "all fine after all")
                .WithPhase(NpcPlanPhase.NeedsAttention, "somebody look"));

        Assert.False(laundered.IsSaved);
        Assert.Contains("erases the only evidence", laundered.Failure);

        // The honest stop, which changes the phase and the note and nothing else,
        // is still available - and keeps the evidence.
        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);
        Assert.Equal(PlanRehearsal.Units, world.Run.State.CarriedUnits);
    }

    [Fact]
    public void A_continue_decision_does_not_carry_a_concluded_intent_across_the_interruption()
    {
        using var world = new PlanRehearsal();

        // The gather was announced and accounted for before the interruption.
        world.Start().RunUpTo(7);
        Assert.Equal(NpcPlanPhase.Reserved, world.Run.State.Phase);

        // A revalidation that says carry on. It keeps the phase, so nothing about
        // the plan moves - which is exactly why the reset here is easy to miss:
        // narrowing it to a re-plan alone left all 913 tests green, and then this
        // Continue bought the move into Provisioned on an intent concluded before
        // the interruption rather than after it.
        var carryOn = new InterruptionOutcome(
            InterruptionResponse.Continue, InterruptionCause.PausedByPlayer, 0f, "it was paused");
        Assert.True(world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.Reserved, "it was paused"), carryOn).IsSaved);

        NpcPlanSave refused = world.Run.Advance(NpcPlanPhase.Provisioned, "still loaded from before");
        Assert.False(refused.IsSaved);
        Assert.Contains("moved twice", refused.Failure);

        // Saying it again is all it takes, and it moves nothing.
        Assert.True(world.Run.Intend("checking what he is holding").IsSaved);
        Assert.True(world.Run.Conclude(true, world.Run.State.Carried, 0, "he has it").IsSaved);
        Assert.True(world.Run.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved);
        Assert.Equal(1, world.Gathers);
    }

    /// <summary>An ending written over an open question can be recorded as the
    /// ending it should have had - once.
    ///
    /// <b>The decision this pins, and why it went this way.</b> The recovery path
    /// answers <c>NeedsAttention</c> for a terminal record whose custody is not
    /// <c>Clear</c>, and it hands back a state phased <c>NeedsAttention</c>. Until
    /// this round no verb could write that state, because both refuse over a
    /// terminal phase - so the corrupt row stayed on the disk and the same decision
    /// re-issued on every world load, a fix that reported a problem for ever and
    /// could never record that anybody had seen it. So exactly one move out of an
    /// ending exists: to <c>NeedsAttention</c>, only while the custody is not
    /// <c>Clear</c>. It resolves nothing - the custody is still uncertain - and a
    /// plan that really did finish is never reopened, which is what keeps "this job
    /// finished" distinguishable from "this job never existed".</summary>
    [Fact]
    public void An_ending_written_over_an_open_question_can_be_recorded_as_the_ending_it_should_have_had()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);

        NpcPlanState corrupt = world.Run.State.WithPhase(NpcPlanPhase.Settled, "call it done").AsRecovered();
        Assert.Equal(NpcPlanPhase.Settled, corrupt.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, corrupt.Custody);

        NpcPlanRecovered decision = NpcPlanRecovery.Revalidate(corrupt, Healthy(world), null);
        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
        Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);

        // Through Adopt, which is the verb a role writing a decision down uses.
        NpcPlanRun? adopting = NpcPlanRun.Resume(world.Journal(), corrupt);
        Assert.True(adopting!.Adopt(decision.Next, decision.Outcome).IsSaved);
        Assert.Equal(NpcPlanPhase.NeedsAttention, adopting.State.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, adopting.State.Custody);
        Assert.Equal(NpcPlanPhase.NeedsAttention, world.Journal().Load().Plan!.Phase);

        // And through Stop, so a role that has no decision in hand is not stuck.
        NpcPlanRun? stopping = NpcPlanRun.Resume(world.Journal(), corrupt);
        Assert.True(stopping!.Stop(NpcPlanPhase.NeedsAttention, "an ending over an open question").IsSaved);

        // Nothing was resolved by writing it down: the plan is in the state the
        // NeedsAttention precondition describes, and a person is still the only way
        // out. It is simply on the record now rather than re-decided every load.
        Assert.False(stopping.MayAct);
        Assert.False(
            NpcPlanRecovery.Revalidate(stopping.State, Healthy(world), null).Outcome.MayResume);

        // The allowance is this shape and no wider. A plan that really did finish
        // is not reopened, by either verb.
        using var finished = new PlanRehearsal();
        finished.Start().RunUpTo(15);
        Assert.Equal(NpcPlanPhase.Settled, finished.Run.State.Phase);
        Assert.Equal(NpcPlanCustody.Clear, finished.Run.State.Custody);

        NpcPlanSave refused = finished.Run.Stop(NpcPlanPhase.NeedsAttention, "look at this anyway");
        Assert.False(refused.IsSaved);
        Assert.Contains("has ended", refused.Failure);

        var stop = new InterruptionOutcome(
            InterruptionResponse.NeedsAttention, InterruptionCause.TransferUncertain, 0f, "look anyway");
        NpcPlanSave alsoRefused = finished.Run.Adopt(
            finished.Run.State.WithPhase(NpcPlanPhase.NeedsAttention, "look anyway"), stop);
        Assert.False(alsoRefused.IsSaved);
        Assert.Contains("has ended", alsoRefused.Failure);
        Assert.Equal(NpcPlanPhase.Settled, finished.Run.State.Phase);
    }

    /// <summary>The same corrupt ending, with the custody still <c>Pending</c>
    /// rather than <c>Uncertain</c>.
    ///
    /// <b>This exists to pin a predicate match, and the match is the whole fix.</b>
    /// <c>NpcPlanRecovery.AlreadyOver</c> answers <c>NeedsAttention</c> for any
    /// terminal row whose custody is not <c>Clear</c>, and <c>Pending</c> is one of
    /// those - while <c>NpcPlanState.IsUncertain</c> is <c>Uncertain</c> or
    /// <c>Unspecified</c> and excludes it. So narrowing either of the two sites that
    /// let such a row be written - <c>Adopt</c>'s terminal check or
    /// <c>WhyNot</c>'s stop-for-a-person rule - to <c>IsUncertain</c>, or to
    /// <c>Uncertain</c> alone, makes this row unwritable again, which is exactly the
    /// bug the allowance exists to fix. Both narrowings left all 916 tests green.
    ///
    /// <b>Why the row is worth defending against at all.</b> It cannot arrive
    /// through <c>Load</c>, because <c>AsRecovered</c> maps <c>Pending</c> to
    /// <c>Uncertain</c>, and <c>WhyNot</c> refuses <c>Pending</c> into an ending
    /// in-session. It is defence in depth against precisely what
    /// <c>AlreadyOver</c> names: an older build, a hand edit, a role that found
    /// another way.</summary>
    [Fact]
    public void An_ending_over_a_movement_still_marked_pending_is_writable_and_keeps_its_evidence()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);

        NpcPlanState pendingEnding = world.Run.State.WithPhase(NpcPlanPhase.Settled, "call it done");
        Assert.Equal(NpcPlanCustody.Pending, pendingEnding.Custody);
        Assert.False(pendingEnding.IsUncertain);

        NpcPlanRecovered decision = NpcPlanRecovery.Revalidate(pendingEnding, Healthy(world), null);
        Assert.Equal(InterruptionResponse.NeedsAttention, decision.Outcome.Response);
        Assert.Equal(NpcPlanPhase.NeedsAttention, decision.Next.Phase);

        // Through Adopt.
        NpcPlanRun? adopting = NpcPlanRun.Resume(world.Journal(), pendingEnding);
        Assert.True(adopting!.Adopt(decision.Next, decision.Outcome).IsSaved);

        // Written, and the evidence is preserved rather than laundered on the way:
        // the custody is still Pending, and it comes back off the disk as the
        // uncertainty a person resolves.
        Assert.Equal(NpcPlanCustody.Pending, adopting.State.Custody);
        Assert.Equal(NpcPlanPhase.NeedsAttention, world.Journal().Load().Plan!.Phase);
        Assert.Equal(NpcPlanCustody.Uncertain, world.Journal().Load().Plan!.Custody);

        // And through Stop, which is the other site the match has to hold at.
        NpcPlanRun? stopping = NpcPlanRun.Resume(world.Journal(), pendingEnding);
        Assert.True(stopping!.Stop(NpcPlanPhase.NeedsAttention, "an ending over an open question").IsSaved);
        Assert.Equal(NpcPlanCustody.Pending, stopping.State.Custody);
        Assert.Equal(NpcPlanPhase.NeedsAttention, stopping.State.Phase);
    }

    /// <summary>A plan already stopped for a person is not reopened by the
    /// allowance that lets a corrupt ending be corrected.
    ///
    /// <b>Three verbs that the allowance widened by accident.</b> Confining it to
    /// <c>Settled</c> and <c>Refunded</c> restores what was refused before it
    /// existed: a second <c>NeedsAttention</c> write, a <c>Suspend</c>, and a
    /// <c>Reattach</c>. All three were conservative - recovery short-circuits in
    /// <c>AlreadyOver</c> before the epoch is ever consulted, so a refreshed epoch
    /// was inert - but a plan waiting for a person having three writable verbs is
    /// not what "one-way" means, and the widening was undocumented.</summary>
    [Fact]
    public void A_plan_waiting_for_a_person_is_not_reopened_by_the_allowance_for_a_corrupt_ending()
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(5);
        Assert.True(world.Run.Stop(NpcPlanPhase.NeedsAttention, "somebody please look").IsSaved);
        Assert.Equal(NpcPlanCustody.Pending, world.Run.State.Custody);

        // Nothing more is written over it, including the write that put it there.
        Assert.False(world.Run.Stop(NpcPlanPhase.NeedsAttention, "saying it again").IsSaved);
        Assert.False(world.Run.Suspend("a boar turned up as well").IsSaved);
        Assert.False(world.Run.Reattach(world.World, "his things are where the plan says").IsSaved);

        // And not through Adopt either, with the decision the recovery path actually
        // gives for it - which is the same answer, on every load, for ever. That
        // recurrence is the documented steady state rather than something to fix.
        NpcPlanRecovered still = NpcPlanRecovery.Revalidate(world.Run.State, Healthy(world), null);
        Assert.Equal(InterruptionResponse.NeedsAttention, still.Outcome.Response);
        Assert.False(world.Run.Adopt(still.Next, still.Outcome).IsSaved);

        // A made-up decision that authorises going on gets no further, which is what
        // Adopt's summary promises for any invented outcome.
        var carryOn = new InterruptionOutcome(
            InterruptionResponse.Continue, InterruptionCause.PausedByPlayer, 0f, "it was paused");
        NpcPlanSave refused = world.Run.Adopt(
            world.Run.State.WithPhase(NpcPlanPhase.NeedsAttention, "carry on"), carryOn);
        Assert.False(refused.IsSaved);
        Assert.Contains("has ended", refused.Failure);
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
