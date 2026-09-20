using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using Situation = TheConcernedCat.ConcernedNPC.Interruption.Interruption;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The situation table, tested as a table rather than read.
///
/// <b>Why the exhaustive test is the important one.</b> A policy is a function
/// from a situation to one of four answers, and the way a policy like this fails
/// in practice is not a wrong row - a wrong row is obvious in review - but a
/// combination nobody thought of falling through to whatever the last branch
/// happened to be. So every cause is crossed with every combination of the three
/// flags, and the properties that must hold everywhere are asserted over the whole
/// cross product. The hand-written rows below it are there to pin the answers a
/// player would notice, not to establish totality.</summary>
public class InterruptionPolicyTests
{
    private static readonly InterruptionCause[] EveryCause =
    {
        InterruptionCause.Unspecified,
        InterruptionCause.WorldReloaded,
        InterruptionCause.BodyLost,
        InterruptionCause.BodyDuplicated,
        InterruptionCause.BodyDied,
        InterruptionCause.AreaChanged,
        InterruptionCause.AreaInvalid,
        InterruptionCause.ContainerRefused,
        InterruptionCause.RouteRefused,
        InterruptionCause.AuthorityLost,
        InterruptionCause.PausedByPlayer,
        InterruptionCause.TransferUncertain,
    };

    public static IEnumerable<object[]> EverySituation
    {
        get
        {
            foreach (InterruptionCause cause in EveryCause)
            {
                for (int bits = 0; bits < 8; bits++)
                {
                    // The cause travels as its number, not as itself. The whole
                    // vocabulary of this area is internal on purpose, and an
                    // internal enum cannot appear in the signature of a test that
                    // xunit can see - so the enum is rebuilt on the inside.
                    yield return new object[] { (int)cause, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0 };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(EverySituation))]
    public void Every_situation_reaches_a_stated_answer_with_a_sentence(
        int causeNumber, bool stale, bool held, bool uncertain)
    {
        var cause = (InterruptionCause)causeNumber;
        InterruptionOutcome outcome = Decide(cause, stale, held, uncertain);

        // Total: no path falls off the end, and nothing comes back as the value
        // that means nobody decided.
        Assert.NotEqual(InterruptionResponse.Unspecified, outcome.Response);
        Assert.NotEqual(string.Empty, outcome.Reason);
        Assert.Equal(cause, outcome.Cause);

        // Uncertainty wins over everything, which is the row a later edit would be
        // most tempted to soften.
        if (uncertain)
        {
            Assert.Equal(InterruptionResponse.NeedsAttention, outcome.Response);
        }

        // A stale plan never continues. It may re-plan, refund or stop; it never
        // walks a route computed against a world that has moved.
        if (stale)
        {
            Assert.NotEqual(InterruptionResponse.Continue, outcome.Response);
        }

        // A refund and a needs-attention never say the job may go on, whatever
        // else is true. This is the contract's own property and it is asserted
        // here against every reachable outcome rather than against four
        // hand-made ones.
        Assert.Equal(
            outcome.Response == InterruptionResponse.Continue || outcome.Response == InterruptionResponse.Replan,
            outcome.MayResume);
    }

    [Theory]
    // Holding nothing: the cheap endings.
    [InlineData(4, false, 3)]  // BodyDied -> Refund
    [InlineData(2, false, 3)]  // BodyLost -> Refund
    // Holding something: the same two causes, and now somebody has to look.
    [InlineData(4, true, 4)]  // BodyDied -> NeedsAttention
    [InlineData(2, true, 4)]  // BodyLost -> NeedsAttention
    // Nothing about these two depends on what is held.
    [InlineData(9, true, 3)]  // AuthorityLost -> Refund
    [InlineData(6, true, 3)]  // AreaInvalid -> Refund
    [InlineData(3, false, 4)]  // BodyDuplicated -> NeedsAttention
    [InlineData(11, false, 4)]  // TransferUncertain -> NeedsAttention
    [InlineData(0, false, 4)]  // Unspecified -> NeedsAttention
    [InlineData(5, true, 2)]  // AreaChanged -> Replan
    [InlineData(7, true, 2)]  // ContainerRefused -> Replan
    [InlineData(8, true, 2)]  // RouteRefused -> Replan
    [InlineData(1, true, 2)]  // WorldReloaded -> Replan
    public void The_table_answers_what_it_says_it_answers(int causeNumber, bool held, int expectedResponse)
    {
        Assert.Equal(
            (InterruptionResponse)expectedResponse,
            Decide((InterruptionCause)causeNumber, stale: false, held: held, uncertain: false).Response);
    }

    [Fact]
    public void A_pause_is_not_a_fault_and_carries_on_from_where_it_was()
    {
        InterruptionOutcome outcome = Decide(
            InterruptionCause.PausedByPlayer, stale: false, held: true, uncertain: false);

        Assert.Equal(InterruptionResponse.Continue, outcome.Response);
        Assert.True(outcome.MayResume);

        // The sentence a player reads. Nothing is wrong, so nothing here may read
        // like a fault.
        foreach (string blame in new[] { "fail", "error", "wrong", "broken", "could not" })
        {
            Assert.DoesNotContain(blame, outcome.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_pause_over_a_plan_the_world_has_moved_under_replans_instead_of_carrying_on()
    {
        // The staleness rule, at the one row that would otherwise reach Continue.
        // It lives above the table so that eleven rows do not each have to
        // remember it.
        InterruptionOutcome outcome = Decide(
            InterruptionCause.PausedByPlayer, stale: true, held: true, uncertain: false);

        Assert.Equal(InterruptionResponse.Replan, outcome.Response);
        Assert.Equal(InterruptionCause.PausedByPlayer, outcome.Cause);
    }

    [Fact]
    public void Only_the_things_that_might_merely_be_busy_wait_before_being_tried_again()
    {
        // A backoff on a permanent condition is an NPC standing still for a reason
        // that will never change; no backoff on a transient one is an NPC walking
        // into the same warded chest every tick.
        Assert.True(Decide(InterruptionCause.ContainerRefused, false, true, false).NotBefore > 100f);
        Assert.True(Decide(InterruptionCause.RouteRefused, false, true, false).NotBefore > 100f);
        Assert.True(Decide(InterruptionCause.PausedByPlayer, false, true, false).NotBefore > 100f);

        Assert.Equal(0f, Decide(InterruptionCause.WorldReloaded, false, true, false).NotBefore);
        Assert.Equal(0f, Decide(InterruptionCause.AreaChanged, false, true, false).NotBefore);
        Assert.Equal(0f, Decide(InterruptionCause.BodyDuplicated, false, true, false).NotBefore);
        Assert.Equal(0f, Decide(InterruptionCause.AuthorityLost, false, true, false).NotBefore);
    }

    [Fact]
    public void The_policy_that_ships_is_the_one_a_caller_gets_when_it_names_none()
    {
        // Recovery takes a policy so that a role can have its own, and falls back
        // to this one. A fallback that was some other behaviour would make every
        // test above true of something nothing runs.
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(4);
        NpcPlanState plan = world.Reload().Plan!;

        var evidence = new NpcPlanEvidence(
            world.World, 1, false, true, false, true, true, true, false, 5f);

        NpcPlanRecovered withNone = NpcPlanRecovery.Revalidate(plan, evidence, null);
        NpcPlanRecovered withIt = NpcPlanRecovery.Revalidate(plan, evidence, NpcWorkInterruptionPolicy.Instance);

        Assert.Equal(withIt.Outcome.Response, withNone.Outcome.Response);
        Assert.Equal(withIt.Outcome.Reason, withNone.Outcome.Reason);
    }

    /// <summary>The cause a situation is reported under, which is a table of its
    /// own: a reload commonly makes four of these true at once and a player is
    /// owed the worst one rather than the first one checked.</summary>
    [Fact]
    public void The_worst_thing_that_is_true_is_the_one_a_player_is_told_about()
    {
        NpcPlanState plan = Plan(NpcPlanCustody.Clear);
        NpcWorldEpoch now = Identities.AWorld();

        // Everything wrong at once. Two bodies outranks the lot.
        Assert.Equal(
            InterruptionCause.BodyDuplicated,
            All(now, bodies: 2, died: true, readable: false, moved: true, work: false).CauseFor(plan));

        // One body, and an unrecorded movement outranks losing authority.
        Assert.Equal(
            InterruptionCause.TransferUncertain,
            All(now, bodies: 1, died: true, readable: false, moved: true, work: false)
                .CauseFor(Plan(NpcPlanCustody.Uncertain)));

        Assert.Equal(
            InterruptionCause.AuthorityLost,
            All(now, bodies: 1, died: true, readable: false, moved: true, work: false).CauseFor(plan));

        Assert.Equal(
            InterruptionCause.BodyDied,
            All(now, bodies: 1, died: true, readable: false, moved: true, work: true).CauseFor(plan));

        Assert.Equal(
            InterruptionCause.BodyLost,
            All(now, bodies: 0, died: false, readable: false, moved: true, work: true).CauseFor(plan));

        Assert.Equal(
            InterruptionCause.AreaInvalid,
            All(now, bodies: 1, died: false, readable: false, moved: true, work: true).CauseFor(plan));

        Assert.Equal(
            InterruptionCause.AreaChanged,
            All(now, bodies: 1, died: false, readable: true, moved: true, work: true).CauseFor(plan));

        // Nothing wrong at all except that this plan's names were minted in a
        // world load that has ended. The last row, because it is true of every
        // reconstruction and anything above it is a better sentence.
        Assert.Equal(
            InterruptionCause.WorldReloaded,
            All(now, bodies: 1, died: false, readable: true, moved: false, work: true).CauseFor(plan));

        // And a plan that is live in this world, with nothing wrong, is not an
        // interruption at all.
        Assert.Equal(
            InterruptionCause.Unspecified,
            All(now, bodies: 1, died: false, readable: true, moved: false, work: true)
                .CauseFor(NpcPlanState.Opening(Identities.Gunnar, "haul-round", now, 3)));
    }

    private static NpcPlanEvidence All(
        NpcWorldEpoch now, int bodies, bool died, bool readable, bool moved, bool work) =>
        new NpcPlanEvidence(now, bodies, died, readable, moved, work, true, true, false, 1f);

    private static NpcPlanState Plan(NpcPlanCustody custody) =>
        NpcPlanState.Opening(Identities.Gunnar, "haul-round", Identities.AWorld(), 3)
            .WithCustody(custody, "a note");

    private static InterruptionOutcome Decide(
        InterruptionCause cause, bool stale, bool held, bool uncertain) =>
        NpcWorkInterruptionPolicy.Instance.Decide(
            new Situation(cause, "haul-round", 2, stale, held, uncertain, 100f));
}
