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
    /// a person does.</summary>
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

    /// <summary>The working phases, in pipeline order.</summary>
    internal static NpcPlanPhase[] Pipeline => (NpcPlanPhase[])Line.Clone();

    /// <summary>Every forward step of the pipeline, as the pairs a kill is
    /// injected between. <c>Observing</c> to <c>Planned</c> through
    /// <c>Reconciling</c> to <c>Settled</c>: eight transitions, which is the
    /// list the recovery suite enumerates rather than one a test author chose.
    /// </summary>
    internal static NpcPlanPhase[][] Transitions()
    {
        var pairs = new NpcPlanPhase[Line.Length][];
        for (int index = 0; index < Line.Length; index++)
        {
            NpcPlanPhase to = index + 1 < Line.Length ? Line[index + 1] : NpcPlanPhase.Settled;
            pairs[index] = new[] { Line[index], to };
        }

        return pairs;
    }

    /// <summary>Whether this phase is one of the three endings.</summary>
    internal static bool IsTerminal(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Settled
        || phase == NpcPlanPhase.Refunded
        || phase == NpcPlanPhase.NeedsAttention;

    /// <summary>Whether a plan in this phase has moved a player's material by
    /// getting there. The two transitions where an interruption can leave a
    /// question rather than an answer, named once so that no caller has to
    /// remember which they were.</summary>
    internal static bool MovesMaterialToReach(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Provisioned || phase == NpcPlanPhase.Reconciling;

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
