using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>What the world says now, for a plan that was interrupted.
///
/// <b>Everything the decision may consider is in here.</b> That is the same rule
/// <see cref="Interruption"/> states for itself, one step earlier: a recovery
/// that reached for ambient state would decide differently in a session than in a
/// test, and this is the one decision where being unable to reproduce it is
/// worst. Nothing here reads the world; a role observes and answers.</summary>
internal readonly struct NpcPlanEvidence
{
    internal NpcPlanEvidence(
        NpcWorldEpoch world,
        int bodiesAnswering,
        bool bodyDied,
        bool areaIsReadable,
        bool areaMoved,
        bool mayWork,
        bool containersAvailable,
        bool routeAvailable,
        bool playerPaused,
        float at)
    {
        World = world;
        BodiesAnswering = bodiesAnswering < 0 ? 0 : bodiesAnswering;
        BodyDied = bodyDied;
        AreaIsReadable = areaIsReadable;
        AreaMoved = areaMoved;
        MayWork = mayWork;
        ContainersAvailable = containersAvailable;
        RouteAvailable = routeAvailable;
        PlayerPaused = playerPaused;
        At = at;
    }

    /// <summary>The world load that is on now.</summary>
    internal NpcWorldEpoch World { get; }

    /// <summary>How many bodies answer to this identity, from the role's own
    /// census. <b>Two is a refusal and zero is a refusal</b>; only one is a body
    /// to re-attach to.</summary>
    internal int BodiesAnswering { get; }

    /// <summary>Whether the NPC died. Separate from the body being absent,
    /// because a death has a place a player can go and look and an unloaded zone
    /// does not.</summary>
    internal bool BodyDied { get; }

    /// <summary>Whether the work area could be read at all.</summary>
    internal bool AreaIsReadable { get; }

    /// <summary>Whether it moved under the plan.</summary>
    internal bool AreaMoved { get; }

    /// <summary>Whether this process may run work here now: opted in, the host,
    /// not dedicated, nobody else connected. <b>Fails closed</b> - a role that
    /// cannot tell says no.</summary>
    internal bool MayWork { get; }

    /// <summary>Whether the containers the plan depended on can be used.
    /// </summary>
    internal bool ContainersAvailable { get; }

    /// <summary>Whether he can get where the plan wanted him.</summary>
    internal bool RouteAvailable { get; }

    /// <summary>Whether the player paused it. Not a fault, and the one cause
    /// whose sentence must not read like one.</summary>
    internal bool PlayerPaused { get; }

    /// <summary>The caller's clock, in seconds.</summary>
    internal float At { get; }

    /// <summary>The single cause this situation is reported under.
    ///
    /// <b>One cause, chosen by severity, and the order is the content.</b> A
    /// reload commonly makes four of these true at once, and the sentence a
    /// player reads should name the worst thing rather than the first thing
    /// checked. Two bodies outranks everything because it makes every other
    /// answer act on an arbitrary one of them; an unrecorded movement comes next
    /// because it is the only state that can duplicate or destroy a player's
    /// material; then losing authority, because nothing may run at all; then the
    /// body, the area, the chests and the route, in the order a player would
    /// investigate them. A plain reload is last: it is true of every
    /// reconstruction, so anything more specific is a better sentence for the
    /// same event.</summary>
    internal InterruptionCause CauseFor(NpcPlanState? plan)
    {
        if (BodiesAnswering > 1)
        {
            return InterruptionCause.BodyDuplicated;
        }

        if (plan != null && plan.IsUncertain)
        {
            return InterruptionCause.TransferUncertain;
        }

        if (!MayWork)
        {
            return InterruptionCause.AuthorityLost;
        }

        if (BodyDied)
        {
            return InterruptionCause.BodyDied;
        }

        if (BodiesAnswering == 0)
        {
            return InterruptionCause.BodyLost;
        }

        if (!AreaIsReadable)
        {
            return InterruptionCause.AreaInvalid;
        }

        if (AreaMoved)
        {
            return InterruptionCause.AreaChanged;
        }

        if (!ContainersAvailable)
        {
            return InterruptionCause.ContainerRefused;
        }

        if (!RouteAvailable)
        {
            return InterruptionCause.RouteRefused;
        }

        if (PlayerPaused)
        {
            return InterruptionCause.PausedByPlayer;
        }

        if (plan != null && !plan.IsLiveIn(World))
        {
            return InterruptionCause.WorldReloaded;
        }

        return InterruptionCause.Unspecified;
    }
}

/// <summary>What one revalidation decided, and the plan as it stands
/// afterwards.</summary>
internal readonly struct NpcPlanRecovered
{
    internal NpcPlanRecovered(
        InterruptionOutcome outcome, NpcPlanState next, BodyClaim claim, bool wasAlreadyOver)
    {
        Outcome = outcome;
        Next = next;
        Claim = claim;
        WasAlreadyOver = wasAlreadyOver;
    }

    /// <summary>What to do, why, and the sentence for a player.</summary>
    internal InterruptionOutcome Outcome { get; }

    /// <summary>The plan after the decision. <b>The same work, in a new
    /// position</b>: identity, job, reservations, carried material, progress,
    /// source, destination, vehicle and custody are carried through every one of
    /// the four responses, and the phase and the note are the only things a
    /// recovery changes. That is the "an interrupted job resumes rather than
    /// restarts" property, as a value rather than a paragraph.</summary>
    internal NpcPlanState Next { get; }

    /// <summary>The body claim this recovery made, if it made one.
    /// <b>Re-attachment, never construction</b>:
    /// <see cref="BodyClaimStatus.AlreadyHeld"/> is the ordinary answer for a
    /// runtime that re-asks after a reload, and nothing here builds a body or
    /// tears one down.</summary>
    internal BodyClaim Claim { get; }

    /// <summary>Whether this was answered without asking the policy or the
    /// registry anything. <b>True for a plan that had already ended</b> - a
    /// finished plan is not resumed and a plan waiting for a person is not quietly
    /// taken off that list - and also for the two records that are not plans at
    /// all: one that names nothing, and one whose phase nobody set. The name is
    /// about the plan; the value is about the answer, which is why it is true for a
    /// record that never started either. No body is claimed on any of those
    /// paths.</summary>
    internal bool WasAlreadyOver { get; }
}

/// <summary>Turning a reconstructed plan into a decision, through the arbiter and
/// the policy and nothing else.
///
/// <b>The two rules that make this more than a switch.</b> A plan re-attaches to
/// the body that is already there, asked for through
/// <see cref="NpcRoleRegistry.TryClaimBody"/> - so a reload cannot produce a
/// second Hulgi standing next to the first one, and two bodies answering is a
/// refusal rather than a choice. And a recovery that does not authorise going on
/// gives the body back before it returns, because a plan that stopped while
/// holding a claim is a body nothing will ever release.
///
/// <b>What it never does.</b> It never constructs a body, never destroys one,
/// never moves material, never writes a file and never decides anything itself:
/// the decision is <see cref="IInterruptionPolicy"/>'s and the writing is
/// <see cref="NpcPlanRun"/>'s. It is the wiring between them, in one place,
/// because four roles wiring it four times is four chances to leave out the
/// release.</summary>
internal static class NpcPlanRecovery
{
    /// <summary>Revalidates a reconstructed plan and re-attaches it to its body.
    /// </summary>
    /// <param name="registry">The registry this identity is registered in. Null
    /// is a programmer's error rather than a world condition, and it fails
    /// closed: no claim is attempted and the plan stops for a person.</param>
    /// <param name="holder">Who is asking, so the claim can be released by the
    /// same name later.</param>
    internal static NpcPlanRecovered Reconstruct(
        NpcRoleRegistry? registry,
        NpcPlanState? plan,
        NpcBodyKind kind,
        string? holder,
        in NpcPlanEvidence evidence,
        IInterruptionPolicy? policy)
    {
        if (plan == null || !plan.IsNamed)
        {
            return Over(
                InterruptionResponse.NeedsAttention,
                InterruptionCause.Unspecified,
                NpcPlanState.Opening(default, null, NpcWorldEpoch.Unknown, 0),
                "there is no plan here to resume, and inventing one would be a plan for work nobody ordered");
        }

        if (plan.Phase == NpcPlanPhase.Unspecified)
        {
            return InNoPhaseAtAll(plan);
        }

        if (NpcPlanProgression.IsTerminal(plan.Phase))
        {
            return AlreadyOver(plan);
        }

        IInterruptionPolicy decided = policy ?? NpcWorkInterruptionPolicy.Instance;

        // The body first, because whether one is there and whether there are two
        // of them outrank every other fact about the plan - and because the claim
        // has to be released again if the answer turns out to be "stop".
        BodyClaim claim = default;
        InterruptionCause cause;

        if (registry == null)
        {
            cause = InterruptionCause.Unspecified;
        }
        else if (evidence.BodiesAnswering > 1)
        {
            // Deliberately not claimed. A claim would be granted - the arbiter
            // knows about holders and kinds, not about how many objects in the
            // scene answer to a name - and it would be a claim on an arbitrary
            // one of the two.
            cause = InterruptionCause.BodyDuplicated;
        }
        else if (evidence.BodiesAnswering == 0)
        {
            cause = InterruptionCause.BodyLost;
        }
        else
        {
            claim = registry.TryClaimBody(plan.Identity, kind, holder ?? string.Empty);
            cause = claim.IsGranted
                ? evidence.CauseFor(plan)
                : InterruptionCause.BodyLost;
        }

        var interruption = new Interruption(
            cause,
            plan.JobId,
            plan.TargetsDone,
            !plan.IsLiveIn(evidence.World),
            plan.HoldsAnything,
            plan.IsUncertain,
            evidence.At);

        InterruptionOutcome outcome = decided.Decide(interruption);
        NpcPlanState next = Apply(plan, outcome);

        if (!outcome.MayResume && claim.IsGranted && registry != null)
        {
            // Give the body back. A plan that has stopped and still holds a claim
            // is the "permanently busy" failure the arbiter takes care to avoid
            // one level down, reintroduced one level up where nothing can reach
            // it.
            registry.ReleaseBody(plan.Identity, kind, holder ?? string.Empty);
        }

        return new NpcPlanRecovered(outcome, next, claim, false);
    }

    /// <summary>Revalidates a plan that is still live in this session - a pause,
    /// a chest that refuses, an area that moved - with no body question asked,
    /// because the body has not been anywhere.</summary>
    internal static NpcPlanRecovered Revalidate(
        NpcPlanState? plan, in NpcPlanEvidence evidence, IInterruptionPolicy? policy)
    {
        if (plan == null || !plan.IsNamed)
        {
            return Over(
                InterruptionResponse.NeedsAttention,
                InterruptionCause.Unspecified,
                NpcPlanState.Opening(default, null, NpcWorldEpoch.Unknown, 0),
                "there is no plan here to revalidate");
        }

        if (plan.Phase == NpcPlanPhase.Unspecified)
        {
            return InNoPhaseAtAll(plan);
        }

        if (NpcPlanProgression.IsTerminal(plan.Phase))
        {
            return AlreadyOver(plan);
        }

        IInterruptionPolicy decided = policy ?? NpcWorkInterruptionPolicy.Instance;

        var interruption = new Interruption(
            evidence.CauseFor(plan),
            plan.JobId,
            plan.TargetsDone,
            !plan.IsLiveIn(evidence.World),
            plan.HoldsAnything,
            plan.IsUncertain,
            evidence.At);

        InterruptionOutcome outcome = decided.Decide(interruption);
        return new NpcPlanRecovered(outcome, Apply(plan, outcome), default, false);
    }

    /// <summary>The plan a response leaves behind.
    ///
    /// <b>Only the phase and the note change.</b> Every response keeps the
    /// reservations - their names are derived, so re-taking them is satisfied
    /// rather than doubled, and dropping them would throw away the only record of
    /// what this plan is holding. Every response keeps the carried material,
    /// because a record that forgot it is how material on a body stops being
    /// anybody's. And no response sets the world: a re-planned plan is still
    /// holding keys minted in a world load that has ended, and it is the role that
    /// says when it has found its things again, through
    /// <see cref="NpcPlanRun.Reattach"/>.
    ///
    /// <b>Which phase, though, is <see cref="NpcPlanProgression.PhaseAfter"/>'s
    /// answer rather than this method's.</b> The same function checks the state
    /// handed to <see cref="NpcPlanRun.Adopt"/>, so the writer cannot be talked
    /// into a phase the decision never produced - and the table cannot be edited
    /// on one side only, which is how a comment here came to say a re-plan returns
    /// a plan to <c>Planned</c> while this code returned <c>Observing</c>.
    /// </summary>
    private static NpcPlanState Apply(NpcPlanState plan, in InterruptionOutcome outcome) =>
        plan.WithPhase(NpcPlanProgression.PhaseAfter(outcome.Response, plan.Phase), outcome.Reason);

    /// <summary>A plan that had already ended.
    ///
    /// <b>Needs-attention is never downgraded, including by this.</b> A plan that
    /// stopped for a person stays stopped for a person however many world loads go
    /// by, because the whole value of saying so is lost if the next one quietly
    /// decides otherwise. A finished or refunded plan answers with the response
    /// that does least - a release of nothing, and stop - rather than with a
    /// resumption.</summary>
    private static NpcPlanRecovered AlreadyOver(NpcPlanState plan)
    {
        if (plan.Phase == NpcPlanPhase.NeedsAttention)
        {
            return Over(
                InterruptionResponse.NeedsAttention,
                InterruptionCause.TransferUncertain,
                plan,
                plan.Note.Length != 0
                    ? plan.Note
                    : "this plan was already stopped for somebody to look at, and it stays stopped");
        }

        if (plan.Custody != NpcPlanCustody.Clear)
        {
            // <b>An ending is not evidence that the movement completed.</b> This
            // short-circuits before the policy, so without this the policy's first
            // row - anything uncertain is needs attention, whatever else is true -
            // never ran for a record that had ended, and a plan that finished over
            // an unrecorded movement was reported as "already ended, nothing to
            // resume" with a refund. NpcPlanRun now refuses to write such a
            // record; this is the second line of defence for one that exists
            // anyway, from an older build, a hand edit, or a role that found
            // another way.
            //
            // <b>The re-phased state is writable, and that is deliberate.</b>
            // Handing back a state nothing could write would mean this decision
            // re-issued on every world load with no way to record that anybody had
            // seen it - a fix that reports a problem for ever and never resolves
            // it. So NpcPlanRun makes exactly this one move out of an ending
            // available: to NeedsAttention, only while the custody is not Clear,
            // through Stop or through Adopt. A plan that really did finish stays
            // finished. What this does not do is resolve anything: the custody is
            // still uncertain afterwards, so the plan is in the state the
            // NeedsAttention precondition describes, and a person is still the
            // only way out of it.
            return Over(
                InterruptionResponse.NeedsAttention,
                InterruptionCause.TransferUncertain,
                plan.WithPhase(NpcPlanPhase.NeedsAttention, "an ending over a movement nobody accounted for"),
                "this plan's record says it ended while something it set in motion had no outcome anybody "
                + "wrote down, so it is not treated as finished");
        }

        return Over(
            InterruptionResponse.Refund,
            InterruptionCause.WorldReloaded,
            plan,
            "this plan had already ended, so there is nothing to resume");
    }

    /// <summary>A record whose phase nobody set.
    ///
    /// <b>Not the beginning, and never guessed at.</b> Left to the ordinary path
    /// this answered <c>Replan</c>, because a plan in no phase is not live in any
    /// world and a stale plan re-plans - so the one record the phase enum says is
    /// "never resumed, never continued, never counted as the start" became a live
    /// plan at <c>Observing</c> still claiming a load it might not have. No body is
    /// asked for and nothing is claimed; the state handed back is already phased
    /// <see cref="NpcPlanPhase.NeedsAttention"/>, so the role has something
    /// <see cref="NpcPlanRun.Adopt"/> will accept and a person gets told.</summary>
    private static NpcPlanRecovered InNoPhaseAtAll(NpcPlanState plan) =>
        Over(
            InterruptionResponse.NeedsAttention,
            InterruptionCause.Unspecified,
            plan.WithPhase(NpcPlanPhase.NeedsAttention, "a record in no phase at all"),
            "this plan's record is in no phase at all, which is neither a beginning nor an ending, so it is "
            + "handed to somebody rather than guessed at");

    private static NpcPlanRecovered Over(
        InterruptionResponse response, InterruptionCause cause, NpcPlanState plan, string reason) =>
        new NpcPlanRecovered(
            new InterruptionOutcome(response, cause, 0f, reason), plan, default, true);
}
