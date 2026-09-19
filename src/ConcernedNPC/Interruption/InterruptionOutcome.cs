namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>What stopped the job. One value, and it is the thing a player is
/// eventually told.</summary>
internal enum InterruptionCause
{
    /// <summary>Nobody said. Never treated as harmless.</summary>
    Unspecified = 0,

    /// <summary>The world was loaded. Every in-session name the job held now
    /// points at something else.</summary>
    WorldReloaded = 1,

    /// <summary>The NPC's body is not there: unloaded, destroyed, or never
    /// found after a reload.</summary>
    BodyLost = 2,

    /// <summary>More than one body answers to this identity. Nothing proceeds:
    /// acting would act on an arbitrary one of them.</summary>
    BodyDuplicated = 3,

    /// <summary>The NPC died. What it was carrying is wherever the world put
    /// it.</summary>
    BodyDied = 4,

    /// <summary>The work area moved under the job.</summary>
    AreaChanged = 5,

    /// <summary>The work area is gone or unreadable.</summary>
    AreaInvalid = 6,

    /// <summary>A container the plan depended on refuses now - a ward went up, a
    /// privacy setting changed, the player took it, somebody has it open.
    /// </summary>
    ContainerRefused = 7,

    /// <summary>The NPC cannot get where it needs to be.</summary>
    RouteRefused = 8,

    /// <summary>This process may no longer run work: the setting was turned off,
    /// the world unloaded, another player connected.</summary>
    AuthorityLost = 9,

    /// <summary>The player paused it. <b>Nothing is wrong</b>, and the sentence
    /// shown must not read like a fault.</summary>
    PausedByPlayer = 10,

    /// <summary>Something moved, or did not, and the record cannot say which.
    /// The one cause that is never resolved automatically.</summary>
    TransferUncertain = 11,
}

/// <summary>What the job should do about it.
///
/// <b>Four answers, ordered by how much they presume.</b> Each does strictly
/// less than the one before it on the job's behalf, and the last does nothing at
/// all except tell somebody.</summary>
internal enum InterruptionResponse
{
    /// <summary>Nobody decided. Never carry on.</summary>
    Unspecified = 0,

    /// <summary>Carry on with the plan as it stands. The interruption changed
    /// nothing the plan depends on.</summary>
    Continue = 1,

    /// <summary>The plan is stale; work the job out again from where things
    /// actually are now. Holds taken by the old plan are released first, and the
    /// new plan re-takes what it needs - which is safe precisely because
    /// reservation ids are derived from the job and the step rather than
    /// minted, so re-taking the same hold is satisfied rather than doubled.
    /// </summary>
    Replan = 2,

    /// <summary>Give back what was set aside and never spent, then stop.
    ///
    /// <b>This is a release, never a compensation.</b> A refund puts back
    /// material that is still held - reserved and not yet moved - and touches
    /// nothing that has been moved. It is never used to undo a transfer, because
    /// undoing a transfer whose outcome is unknown either duplicates a player's
    /// material or destroys it. If anything about the job is uncertain, the
    /// answer is <see cref="NeedsAttention"/>, not this.</summary>
    Refund = 3,

    /// <summary>Stop, keep the evidence, and wait for a person.
    ///
    /// <b>Nothing is replayed, compensated or guessed.</b> Every recovery path
    /// in every shipped product converges here for the same reason: when the
    /// record and the world disagree, both ways of resolving it silently are
    /// wrong, and only one of them is visible to the player. So the job stops,
    /// the reason is kept with what was observed, and a person decides. A job in
    /// this state is never downgraded to merely paused by anything except a
    /// person.</summary>
    NeedsAttention = 4,
}

/// <summary>What happened, when, and against what the job was running.
///
/// <b>What it guarantees.</b> That the decision about what to do next is made
/// from stated facts rather than from ambient state. Everything the policy may
/// consider is in here, so the same interruption decides the same way in a test
/// as in a session.</summary>
internal readonly struct Interruption
{
    internal Interruption(
        InterruptionCause cause,
        string jobId,
        int planStepIndex,
        bool planIsStale,
        bool anythingHeld,
        bool anythingUncertain,
        float at)
    {
        Cause = cause;
        JobId = jobId ?? string.Empty;
        PlanStepIndex = planStepIndex;
        PlanIsStale = planIsStale;
        AnythingHeld = anythingHeld;
        AnythingUncertain = anythingUncertain;
        At = at;
    }

    /// <summary>What stopped it.</summary>
    internal InterruptionCause Cause { get; }

    /// <summary>The job that was running.</summary>
    internal string JobId { get; }

    /// <summary>The step it had reached, or -1 if it had not started one.
    /// </summary>
    internal int PlanStepIndex { get; }

    /// <summary>Whether the plan was computed against a world that has since
    /// moved - a different area revision, a different world load.</summary>
    internal bool PlanIsStale { get; }

    /// <summary>Whether the job is holding anything: reservations taken,
    /// material carried, a tool issued. A job holding nothing can be abandoned
    /// far more cheaply than one that is not.</summary>
    internal bool AnythingHeld { get; }

    /// <summary>Whether anything about the job's record cannot be resolved from
    /// evidence. <b>When this is true the answer is always
    /// <see cref="InterruptionResponse.NeedsAttention"/></b>, whatever else is
    /// true, because every other response acts on a belief about what happened
    /// and there is not one.</summary>
    internal bool AnythingUncertain { get; }

    /// <summary>The caller's clock, in seconds.</summary>
    internal float At { get; }
}

/// <summary>What to do about an interruption, and when.</summary>
internal readonly struct InterruptionOutcome
{
    internal InterruptionOutcome(
        InterruptionResponse response, InterruptionCause cause, float notBefore, string reason)
    {
        Response = response;
        Cause = cause;
        NotBefore = notBefore;
        Reason = reason ?? string.Empty;
    }

    /// <summary>What the job should do.</summary>
    internal InterruptionResponse Response { get; }

    /// <summary>The cause this answers, carried through so the sentence a player
    /// reads names what happened rather than what was decided.</summary>
    internal InterruptionCause Cause { get; }

    /// <summary>The earliest time, on the caller's clock, to act on this -
    /// backing off before trying again rather than retrying into the same
    /// failure. Zero means now.</summary>
    internal float NotBefore { get; }

    /// <summary>Evidence: what was observed, in words. Kept with a
    /// <see cref="InterruptionResponse.NeedsAttention"/> outcome so the person
    /// who looks at it later has what the NPC had.</summary>
    internal string Reason { get; }

    /// <summary>Whether the job may go on at all, now or after
    /// <see cref="NotBefore"/>. False for refund and for needs-attention.
    /// </summary>
    internal bool MayResume =>
        Response == InterruptionResponse.Continue || Response == InterruptionResponse.Replan;
}

/// <summary>Deciding what a job does when something stops it.
///
/// <b>What it guarantees.</b> That the decision is made in one place, from
/// stated facts, and that the conservative answers win. Uncertainty always
/// produces <see cref="InterruptionResponse.NeedsAttention"/>; a stale plan
/// never produces <see cref="InterruptionResponse.Continue"/>; and nothing here
/// ever decides that a transfer whose outcome is unknown did or did not happen.
///
/// <b>Why it is a seam rather than a method.</b> Because the four NPCs interrupt
/// differently and the differences are real: one carries nothing and can be
/// abandoned where it stands, one is holding a player's tools, one is holding a
/// player's material and one is not a worker at all. The vocabulary above is
/// shared; the policy that maps a situation onto it is per job kind, written in
/// a leaf of this library, and tested against a table of situations rather than
/// by reading.
///
/// <b>Never throws.</b> A policy that threw while deciding how to recover would
/// be a failure inside failure handling.</summary>
internal interface IInterruptionPolicy
{
    /// <summary>Decides. Never throws.</summary>
    InterruptionOutcome Decide(in Interruption interruption);
}
