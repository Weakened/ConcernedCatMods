namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>What a working NPC does when something stops it. The first
/// implementation of <see cref="IInterruptionPolicy"/>, and the one that fits
/// every NPC that is holding a player's material or a player's tools.
///
/// <b>The table, in the order it is read.</b> Order is the whole content of a
/// policy like this: nearly every row is reachable together with several others,
/// and which one wins is the decision.
///
/// <list type="number">
/// <item><b>Anything uncertain, whatever else is true, is
/// <see cref="InterruptionResponse.NeedsAttention"/>.</b> Every other response
/// acts on a belief about what happened, and there is not one. This row is first
/// so that no cause below it can reach a resumption while the record and the
/// world may disagree.</item>
/// <item><b>Two bodies answer to this NPC: needs attention.</b> Acting would act
/// on an arbitrary one of them, and which one is holding the axe is exactly the
/// question a player would be asking. Never resolved by picking one.</item>
/// <item><b>An unrecorded movement: needs attention.</b> Same reason as the
/// first row, arrived at from the cause rather than from the flag.</item>
/// <item><b>Nobody said why: needs attention.</b> An interruption with no stated
/// cause is not a harmless one; it is one nothing described, and a policy that
/// carried on from it would be guessing about a situation nobody characterised.
/// </item>
/// <item><b>Authority is gone: refund.</b> Work may not run here now - the
/// setting went off, another player connected - so nothing may go on, and what
/// was set aside and never spent goes back. A refund is a release and never a
/// compensation: it touches nothing that has moved.</item>
/// <item><b>He died.</b> Holding nothing, that is a refund: release and stop.
/// Holding something, it is needs attention, because what he was carrying is
/// wherever the world put it and the record cannot say where without somebody
/// looking.</item>
/// <item><b>His body is not there.</b> The same split, for the same reason. A
/// plan holding nothing can be abandoned where it stands far more cheaply than
/// one that is not.</item>
/// <item><b>The work area cannot be read: refund.</b> Fail closed; nothing
/// widens to a default, and a plan whose area is unreadable has no ground to
/// re-plan against.</item>
/// <item><b>The area moved, a container refuses, or he cannot get there:
/// re-plan</b>, the last two after a wait, because retrying immediately into the
/// same refusal is how an NPC spends a session walking into a warded chest. A
/// re-plan is safe precisely because reservation names are derived from the job
/// and the step: re-taking the same hold is satisfied rather than doubled.
/// </item>
/// <item><b>The player paused it.</b> Nothing is wrong, so this is a
/// continuation - after a wait, because the player is doing something - and the
/// sentence must not read like a fault.</item>
/// <item><b>The world reloaded: re-plan.</b> Every in-session name the plan held
/// now points at something else, so there is nothing to continue on to. This is
/// last of the causes because every more specific one above it is a better
/// sentence for the same reload.</item>
/// </list>
///
/// <b>The two rules that sit above the table.</b> A stale plan never continues -
/// it re-plans instead, whatever the cause said - because continuing means
/// walking a route computed against a world that has moved. And uncertainty
/// always wins, which is the first row and is repeated here because it is the one
/// a later edit would be most tempted to soften.
///
/// <b>Total, rather than promised not to throw.</b> Every cause and every
/// combination of the three flags reaches a stated response; there is no path
/// that falls off the end and none that divides, indexes or dereferences. A
/// policy that threw while deciding how to recover would be a failure inside
/// failure handling, and the tests enumerate the whole cross product rather than
/// trusting this paragraph.</summary>
internal sealed class NpcWorkInterruptionPolicy : IInterruptionPolicy
{
    /// <summary>How long to wait before acting on something that may simply be
    /// busy: a chest somebody has open, a route blocked by a cart, a player who
    /// paused the job a moment ago. Seconds on the caller's own clock.
    ///
    /// <b>Not a retry budget.</b> Nothing here counts attempts; what bounds a
    /// repeating refusal is the plan's own attempt count, which rises on every
    /// reconstruction and is evidence for a person rather than a limit enforced
    /// in a policy.</summary>
    private const float Wait = 10f;

    internal static NpcWorkInterruptionPolicy Instance { get; } = new NpcWorkInterruptionPolicy();

    public InterruptionOutcome Decide(in Interruption interruption)
    {
        InterruptionCause cause = interruption.Cause;

        if (interruption.AnythingUncertain)
        {
            return Answer(
                InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                "something this job set in motion has no outcome anybody can establish, so it is stopped with "
                + "what was observed rather than guessed at");
        }

        switch (cause)
        {
            case InterruptionCause.BodyDuplicated:
                return Answer(
                    InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                    "more than one body answers to this NPC, and acting would act on an arbitrary one of them");

            case InterruptionCause.TransferUncertain:
                return Answer(
                    InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                    "the record cannot say whether the last thing he moved arrived, and neither replaying it "
                    + "nor forgetting it is safe");

            case InterruptionCause.Unspecified:
                return Answer(
                    InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                    "the job stopped and nothing recorded why, which is not the same as nothing being wrong");

            case InterruptionCause.AuthorityLost:
                return Answer(
                    InterruptionResponse.Refund, cause, interruption, 0f,
                    "work may not run here now, so what was set aside goes back and nothing else is touched");

            case InterruptionCause.BodyDied:
                return interruption.AnythingHeld
                    ? Answer(
                        InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                        "he died holding something, and where it ended up is a question the record cannot "
                        + "answer on its own")
                    : Answer(
                        InterruptionResponse.Refund, cause, interruption, 0f,
                        "he died holding nothing, so the job gives back what it had set aside and stops");

            case InterruptionCause.BodyLost:
                return interruption.AnythingHeld
                    ? Answer(
                        InterruptionResponse.NeedsAttention, cause, interruption, 0f,
                        "his body is not there and the job was holding something, so where that something is "
                        + "needs somebody to look")
                    : Answer(
                        InterruptionResponse.Refund, cause, interruption, 0f,
                        "his body is not there and the job was holding nothing, so it gives back its claims "
                        + "and stops");

            case InterruptionCause.AreaInvalid:
                return Answer(
                    InterruptionResponse.Refund, cause, interruption, 0f,
                    "the place he was working could not be read, and nothing widens to a default");

            case InterruptionCause.AreaChanged:
                return Answer(
                    InterruptionResponse.Replan, cause, interruption, 0f,
                    "the place he was working moved under the job, so the job is worked out again from where "
                    + "things are now");

            case InterruptionCause.ContainerRefused:
                return Answer(
                    InterruptionResponse.Replan, cause, interruption, Wait,
                    "a container the plan depended on refuses now, so the job waits and is worked out again");

            case InterruptionCause.RouteRefused:
                return Answer(
                    InterruptionResponse.Replan, cause, interruption, Wait,
                    "he cannot get where the plan wanted him, so the job waits and is worked out again");

            case InterruptionCause.PausedByPlayer:
                // Stated as a plain continuation. Whether it can actually be one
                // is the staleness rule's answer, in Answer, and not this row's -
                // eleven rows each remembering it is eleven chances to forget.
                return Answer(
                    InterruptionResponse.Continue, cause, interruption, Wait,
                    "it was paused, and it carries on from where it was");

            case InterruptionCause.WorldReloaded:
            default:
                return Answer(
                    InterruptionResponse.Replan, cause, interruption, 0f,
                    "the world was loaded, so every name the plan held points at something else and the job "
                    + "is worked out again from where things are now");
        }
    }

    /// <summary>Applies the two rules above the table, then builds the answer.
    /// <b>The staleness rule lives here rather than in each row</b>, because a
    /// row that forgot it would produce a plan walking a route computed against a
    /// world that has moved - and there are eleven rows.</summary>
    private static InterruptionOutcome Answer(
        InterruptionResponse response,
        InterruptionCause cause,
        in Interruption interruption,
        float wait,
        string reason)
    {
        float notBefore = wait <= 0f ? 0f : interruption.At + wait;

        if (response == InterruptionResponse.Continue && interruption.PlanIsStale)
        {
            return new InterruptionOutcome(
                InterruptionResponse.Replan, cause, notBefore,
                "the plan was worked out against a world that has moved since, so it is worked out again "
                + "rather than walked");
        }

        return new InterruptionOutcome(response, cause, notBefore, reason);
    }
}
