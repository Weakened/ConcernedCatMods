using System;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>How far one plan has got. <b>The durable part of a plan</b>: it is
/// written down before the thing it names is done, and it is the first question
/// a reconstructed plan is asked.
///
/// <b>The values are the pipeline that already exists</b>, not a new one. A job
/// looks at the world, works out a plan, totals it into a manifest, sets aside
/// what it needs, gathers it, orders the walk, carries the steps out and closes
/// the books - <c>NpcJobDriver</c> does exactly that sequence today, in memory.
/// These are the same eight positions given names that survive the process
/// dying, plus the three ways a plan can end. Nothing here invents a stage the
/// runtime does not already have, because a durable phase the code never
/// actually occupies is a record that cannot be checked against anything.
///
/// <b>Zero is not a phase.</b> A plan whose phase nobody set has not started,
/// has not finished and is not resumable; it is a record somebody wrote wrong,
/// and every path treats it as such rather than as the beginning.</summary>
internal enum NpcPlanPhase
{
    /// <summary>Nobody said. Never resumed, never continued, never counted as
    /// the start.</summary>
    Unspecified = 0,

    /// <summary>Looking at the world. Nothing is decided and nothing is held.
    /// </summary>
    Observing = 1,

    /// <summary>A plan exists: which targets, in what order.</summary>
    Planned = 2,

    /// <summary>The plan has been totalled into what it will take.</summary>
    Manifested = 3,

    /// <summary>Everything the plan needs is set aside, under names derived
    /// from the job and the step rather than minted - which is what lets a
    /// resumed plan re-take exactly what it already holds.</summary>
    Reserved = 4,

    /// <summary>What was set aside is now on the body. <b>The first phase
    /// reached by moving a player's material</b>, so the transition into it is
    /// one of the two that can leave a question rather than an answer.</summary>
    Provisioned = 5,

    /// <summary>The walk is ordered: which stop, in which sequence.</summary>
    Routed = 6,

    /// <summary>The steps are being carried out.</summary>
    Executing = 7,

    /// <summary>The books are being closed: what was done, what was skipped,
    /// what is still carried. <b>The second phase reached by moving a player's
    /// material</b>, because getting here means the deliveries happened.
    /// </summary>
    Reconciling = 8,

    /// <summary>Finished. Nothing is owed, nothing is held, and the record is
    /// kept rather than deleted so that the next run can tell "this job
    /// finished" from "this job never existed".</summary>
    Settled = 9,

    /// <summary>Stopped after giving back what was set aside and never spent.
    /// Terminal. <b>Never used to undo a transfer</b> - see
    /// <see cref="InterruptionResponse.Refund"/>, which says why at
    /// length.</summary>
    Refunded = 10,

    /// <summary>Stopped, with the evidence, waiting for a person.
    ///
    /// <b>Durable on purpose, and one-way.</b> A plan that reached this had
    /// something about it that no automatic answer could settle, and the whole
    /// value of saying so is lost if the next world load quietly decides
    /// otherwise. Nothing in this library moves a plan out of this phase; only
    /// a person does.
    ///
    /// <b>The way out is not implemented anywhere, and that is a precondition on
    /// #380, #381 and #382 rather than a future-work aside.</b> There is no
    /// resolution UI; <c>NpcCustodyLedger.CloseOpenIntents</c> exists and is not
    /// joined to a plan; nothing creates "the plan that follows"; and a re-planned
    /// plan re-plans for ever until a role re-attaches it to the world that is
    /// loaded now through <see cref="NpcPlanRun.Reattach"/>. So the first role
    /// that holds a player's material through this library inherits a state that
    /// nothing in this repository can clear, and it has to bring its own
    /// resolution path or accept that. Stopping for a person is the right failure
    /// direction - it leaves evidence rather than guessing - and it is still a
    /// dead end until somebody builds the exit.</summary>
    NeedsAttention = 11,
}

/// <summary>Which phase may follow which, and the transitions a kill is
/// injected at.
///
/// <b>Why the order is a table rather than an integer comparison.</b> Comparing
/// the enum's numbers would say that <c>Observing</c> may go straight to
/// <c>Reconciling</c>, which would be a plan closing books for reservations it
/// never took. The pipeline is a line, each step follows exactly one other, and
/// the three endings may be reached from anywhere - so that is what is written
/// down.</summary>
internal static class NpcPlanProgression
{
    /// <summary>The pipeline, in order. The eight working phases only: the
    /// endings are not positions on the line, they are ways off it.</summary>
    private static readonly NpcPlanPhase[] Line =
    {
        NpcPlanPhase.Observing,
        NpcPlanPhase.Planned,
        NpcPlanPhase.Manifested,
        NpcPlanPhase.Reserved,
        NpcPlanPhase.Provisioned,
        NpcPlanPhase.Routed,
        NpcPlanPhase.Executing,
        NpcPlanPhase.Reconciling,
    };

    /// <summary>The working phases, in pipeline order. <b>What the kill suite
    /// compares itself against</b>: the phases its kills actually land in are
    /// collected and every one of these has to be among them, so a shortened
    /// rehearsal script fails rather than passing quietly over fewer cases.
    ///
    /// There was a <c>Transitions()</c> helper beside this that returned the eight
    /// forward pairs. It was deleted in the corrective round for #379, because its
    /// own docstring was false: it claimed to be "the list the recovery suite
    /// enumerates", and the recovery suite enumerates the rehearsal's
    /// fifteen-operation script instead. Its only consumer compared its length
    /// with this array's, which is a tautology of its own construction - making
    /// every pair a self-pair left the whole suite green. Nothing needed the
    /// pairs, so nothing has them.</summary>
    internal static NpcPlanPhase[] Pipeline => (NpcPlanPhase[])Line.Clone();

    /// <summary>Whether this phase is one of the three endings.</summary>
    internal static bool IsTerminal(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Settled
        || phase == NpcPlanPhase.Refunded
        || phase == NpcPlanPhase.NeedsAttention;

    /// <summary>Whether a plan in this phase has moved a player's material by
    /// getting there. The two transitions where an interruption can leave a
    /// question rather than an answer.
    ///
    /// <b>A rule, not a label.</b> <see cref="NpcPlanRun"/> refuses a move into
    /// either of these phases unless this run has already written an intent and
    /// concluded it with an established outcome, in the phase it is moving out of.
    /// So a role that forgets the write-ahead discipline is refused, rather than
    /// left holding a resumable record of a movement nothing described - which a
    /// recovery reads as "carry on", which is how the same load moves twice.
    /// Until the corrective round for #379 this function had no callers at all:
    /// the discipline was a convention of the test fixture, replacing this body
    /// with <c>false</c> left all 891 tests green, and the design documents
    /// nevertheless asserted that the two transitions "are written twice".
    ///
    /// <b>The limit of the enforcement, stated rather than implied.</b> What a run
    /// remembers about its own concluded intent is in memory and not on the disk.
    /// There is no durable field saying "the intent for this transition was
    /// concluded", and adding one would put a field this library demands into a
    /// format the role owns, which is the one thing this area is built not to do.
    /// So a plan resumed from the disk has to intend and conclude again before it
    /// may claim a material-moving phase. That costs two writes and moves nothing:
    /// <see cref="NpcPlanRun.Conclude"/> records what was measured, so a body that
    /// is already loaded is recorded as already loaded.</summary>
    internal static bool MovesMaterialToReach(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Provisioned || phase == NpcPlanPhase.Reconciling;

    /// <summary>The phase a recovery decision leaves a plan in.
    ///
    /// <b>One function, two callers, on purpose.</b>
    /// <see cref="NpcPlanRecovery"/> uses it to produce the revalidated state, and
    /// <see cref="NpcPlanRun.Adopt"/> uses it to check the state it is handed - so
    /// "the only backward write is the one a decision actually authorised" is one
    /// sentence in one place instead of two that can disagree. They did disagree:
    /// <c>NpcPlanRun</c>'s own comment said a re-plan returns a plan to
    /// <c>Planned</c> while the recovery path returned <c>Observing</c>, and
    /// nothing checked either claim because <c>Adopt</c> checked no phase at
    /// all.</summary>
    internal static NpcPlanPhase PhaseAfter(InterruptionResponse response, NpcPlanPhase current)
    {
        switch (response)
        {
            case InterruptionResponse.Continue:
                return current;

            case InterruptionResponse.Replan:
                // Back to looking, not back to planning: the plan it would
                // otherwise resume planning from was computed against a world that
                // has moved.
                return NpcPlanPhase.Observing;

            case InterruptionResponse.Refund:
                return NpcPlanPhase.Refunded;

            default:
                return NpcPlanPhase.NeedsAttention;
        }
    }

    /// <summary>Whether <paramref name="to"/> may follow <paramref name="from"/>.
    ///
    /// Forward by exactly one along the line, or off the line to an ending, or
    /// nowhere at all once an ending has been reached. Re-stating the phase a
    /// plan is already in is allowed, because writing the same phase again with
    /// a different note is how an interruption is recorded without pretending
    /// progress was made.</summary>
    internal static bool MayFollow(NpcPlanPhase from, NpcPlanPhase to)
    {
        if (from == NpcPlanPhase.Unspecified || to == NpcPlanPhase.Unspecified)
        {
            return false;
        }

        if (IsTerminal(from))
        {
            // An ending is an ending. The one exception a person might want -
            // taking a plan out of NeedsAttention - is deliberately not
            // available here; see the phase's own summary.
            return false;
        }

        if (from == to)
        {
            return true;
        }

        if (IsTerminal(to))
        {
            return true;
        }

        int at = Array.IndexOf(Line, from);
        return at >= 0 && at + 1 < Line.Length && Line[at + 1] == to;
    }
}
