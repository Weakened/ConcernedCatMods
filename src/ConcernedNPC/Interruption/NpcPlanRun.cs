using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Custody;

namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>One plan being worked, with every phase written down before it is
/// acted on.
///
/// <b>The whole design is one ordering rule.</b> A caller may only act on the
/// phase the record already says it is in. So the record is always at or ahead of
/// the world, never behind it, and the last written record is therefore always a
/// safe place to resume from - whatever instant the process died at. That is why
/// there is no list of recovery special cases for the twenty-odd points inside a
/// round: there is one class of point, "the record is the truth and the world may
/// be one action short of it", and one answer to it.
///
/// <b>Why that is not enough on its own, and what closes the gap.</b> Two of the
/// pipeline's transitions move a player's material, and for those the record
/// being ahead is not a safe resume point by itself: the move may have happened.
/// So those two are written twice - <see cref="Intend"/> before the world is
/// touched and <see cref="Conclude"/> after - and a record found in
/// <see cref="NpcPlanCustody.Pending"/> is one where the answer is genuinely
/// unknown. It is never replayed, because replaying a move that may have
/// happened duplicates a player's material, and never discarded, because
/// discarding one that may not have happened deletes it. It becomes
/// <see cref="NpcPlanCustody.Uncertain"/> and a person decides. This is the
/// transfer executor's own add-before-remove asymmetry raised one level: of the
/// two possible failures, choose the one that leaves evidence.
///
/// <b>What it refuses, and why each refusal is the point.</b> A phase that does
/// not follow the current one, because a plan that skipped reserving would hold
/// nothing it thinks it holds. A forward move while a movement is pending,
/// because that is a plan walking past a question. Anything at all once the plan
/// has ended, because an ending that can be undone by the next write is not an
/// ending. And a plan whose write was refused does not advance in memory either -
/// so a caller that cannot record cannot act, which is the same fail-closed rule
/// the custody journal already imposes on one transfer.
///
/// <b>What it is not.</b> Not a planner, not a driver, not a scheduler. It holds
/// no targets, walks no steps and knows nothing about the world. What a phase
/// means is the role's and <c>NpcJobDriver</c>'s; this says when it is safe to be
/// in one.</summary>
internal sealed class NpcPlanRun
{
    private readonly NpcPlanJournal _journal;
    private NpcPlanState _state;

    private NpcPlanRun(NpcPlanJournal journal, NpcPlanState state)
    {
        _journal = journal;
        _state = state;
    }

    /// <summary>Starts a plan by writing it down first.
    ///
    /// <b>Before anything, deliberately.</b> A plan whose opening record could
    /// not be written is a plan nothing will remember, and the cheapest moment to
    /// refuse it is the one where it is holding nothing.</summary>
    internal static NpcPlanRun? Begin(NpcPlanJournal? journal, NpcPlanState? opening, out NpcPlanSave written)
    {
        if (journal == null)
        {
            written = NpcPlanSave.Refused("a plan needs somewhere to be written down");
            return null;
        }

        written = OpeningWrite(journal, opening);
        return written.IsSaved ? new NpcPlanRun(journal, opening!) : null;
    }

    /// <summary>Picks a reconstructed plan back up. Writes nothing: the record on
    /// the disk is what this run is resuming from, and rewriting it before
    /// anything has been revalidated would raise the attempt count for a plan
    /// nobody has decided about yet.</summary>
    internal static NpcPlanRun? Resume(NpcPlanJournal? journal, NpcPlanState? recovered)
    {
        if (journal == null || recovered == null || !recovered.IsNamed)
        {
            return null;
        }

        return new NpcPlanRun(journal, recovered);
    }

    /// <summary>Where the plan is, as the record says.</summary>
    internal NpcPlanState State => _state;

    /// <summary>Whether the caller may touch the world right now: the plan has
    /// not ended, and nothing is in flight or unresolved.
    ///
    /// <b>The one question before acting.</b> It is false while a movement is
    /// pending, because a second action taken on top of an unanswered one is how
    /// two of the same delivery happen.</summary>
    internal bool MayAct =>
        !NpcPlanProgression.IsTerminal(_state.Phase)
        && _state.Phase != NpcPlanPhase.Unspecified
        && _state.Custody == NpcPlanCustody.Clear;

    /// <summary>Writes a proposed state down and adopts it if the write
    /// succeeded. <b>The only way this run's state changes.</b></summary>
    internal NpcPlanSave Record(NpcPlanState? proposed)
    {
        string refusal = WhyNot(proposed);
        if (refusal.Length != 0)
        {
            return NpcPlanSave.Refused(refusal);
        }

        NpcPlanSave written = _journal.Save(proposed);
        if (written.IsSaved)
        {
            _state = proposed!;
        }

        return written;
    }

    /// <summary>Moves the plan on one phase.</summary>
    internal NpcPlanSave Advance(NpcPlanPhase to, string? note) =>
        Record(_state.WithPhase(to, note));

    /// <summary>Says a movement of material is about to happen, before the world
    /// is touched. <b>Written first, always</b>: an intent with no outcome is
    /// recoverable and an outcome with no intent is not.</summary>
    internal NpcPlanSave Intend(string? note) =>
        Record(_state.WithCustody(NpcPlanCustody.Pending, note));

    /// <summary>Says what became of it, after the world was touched.
    /// </summary>
    /// <param name="established">Whether the outcome is known from evidence -
    /// both inventories counted, both deltas agreeing. False is not a failure
    /// report: it is "nobody can say", which is the one answer that stops the
    /// plan for a person rather than guessing.</param>
    /// <param name="carried">What the body is now holding on this plan's behalf,
    /// measured. Never defaulted: a silent empty list says the NPC is carrying
    /// nothing this plan may spend, which is a claim.</param>
    /// <param name="targetsDone">How many of the plan's targets are done now.
    /// Also a claim, also never defaulted.</param>
    internal NpcPlanSave Conclude(
        bool established, IReadOnlyList<NpcMaterialStack>? carried, int targetsDone, string? note)
    {
        NpcPlanState proposed = _state
            .WithHoldings(_state.Reservations, carried)
            .WithProgress(targetsDone)
            .WithCustody(established ? NpcPlanCustody.Clear : NpcPlanCustody.Uncertain, note);

        return Record(proposed);
    }

    /// <summary>Ends the plan.</summary>
    internal NpcPlanSave Stop(NpcPlanPhase ending, string? note)
    {
        if (!NpcPlanProgression.IsTerminal(ending))
        {
            return NpcPlanSave.Refused("that is not an ending");
        }

        return Record(_state.WithPhase(ending, note));
    }

    /// <summary>Notes that something of higher priority has taken the body, with
    /// the plan left exactly as it stands.
    ///
    /// <b>A failed write here is not a lost plan.</b> The phase does not change
    /// and nothing new is claimed, so the record already on the disk is the same
    /// safe resume point it was a moment ago - which is the property the ordering
    /// rule at the top of this file buys, and the reason an urgent interruption
    /// does not have to wait on a disk.</summary>
    internal NpcPlanSave Suspend(string? note) => Record(_state.WithPhase(_state.Phase, note));

    /// <summary>Adopts a revalidated state after a reconstruction, once a
    /// recovery decision has been made about it.
    ///
    /// <b>The one write that may move a plan backwards</b> - a re-plan returns it
    /// to <see cref="NpcPlanPhase.Planned"/> - and the only one, which is why it
    /// is a separate verb with the decision that authorised it passed in rather
    /// than a relaxation of <see cref="Record"/>. A decision that does not
    /// authorise a move is refused here, so a caller cannot reach the backward
    /// path by handing in a made-up outcome.</summary>
    internal NpcPlanSave Adopt(NpcPlanState? revalidated, in InterruptionOutcome decision)
    {
        if (revalidated == null || !revalidated.IsNamed)
        {
            return NpcPlanSave.Refused("a revalidated plan still has to name itself");
        }

        if (!revalidated.Identity.Equals(_state.Identity)
            || !string.Equals(revalidated.JobId, _state.JobId, StringComparison.Ordinal))
        {
            return NpcPlanSave.Refused("that is a different plan");
        }

        if (decision.Response == InterruptionResponse.Unspecified)
        {
            return NpcPlanSave.Refused("nothing decided this, so nothing is adopted");
        }

        if (!decision.MayResume && !NpcPlanProgression.IsTerminal(revalidated.Phase))
        {
            return NpcPlanSave.Refused("a plan that may not go on has to end somewhere");
        }

        NpcPlanSave written = _journal.Save(revalidated);
        if (written.IsSaved)
        {
            _state = revalidated;
        }

        return written;
    }

    private static NpcPlanSave OpeningWrite(NpcPlanJournal journal, NpcPlanState? opening)
    {
        if (opening == null || !opening.IsNamed)
        {
            return NpcPlanSave.Refused("a plan with no identity and no job name cannot be resumed, so it is "
                + "not started");
        }

        if (opening.Phase != NpcPlanPhase.Observing)
        {
            return NpcPlanSave.Refused("a plan starts by looking at the world, whatever else it does later");
        }

        return journal.Save(opening);
    }

    private string WhyNot(NpcPlanState? proposed)
    {
        if (proposed == null || !proposed.IsNamed)
        {
            return "a plan with no identity and no job name cannot be resumed, so it is not written";
        }

        if (!proposed.Identity.Equals(_state.Identity)
            || !string.Equals(proposed.JobId, _state.JobId, StringComparison.Ordinal))
        {
            return "that is a different plan";
        }

        if (!NpcPlanProgression.MayFollow(_state.Phase, proposed.Phase))
        {
            return NpcPlanProgression.IsTerminal(_state.Phase)
                ? "this plan has ended, and an ending that the next write can undo is not an ending"
                : "that phase does not follow the one this plan is in";
        }

        if (proposed.Custody == NpcPlanCustody.Unspecified)
        {
            return "a plan has to say whether anything is in flight; not saying is not the same as no";
        }

        if (proposed.Phase == _state.Phase && proposed.CarriesTheSameWorkAs(_state))
        {
            // Nothing but the note changed. Always allowed, whatever is in
            // flight: writing down why a plan stopped must not be the one thing
            // a plan in trouble cannot do.
            return string.Empty;
        }

        if (_state.Custody == NpcPlanCustody.Pending)
        {
            bool concluding = proposed.Phase == _state.Phase && proposed.Custody != NpcPlanCustody.Pending;
            if (!concluding && !NpcPlanProgression.IsTerminal(proposed.Phase))
            {
                return "something this plan set in motion has no recorded outcome yet, and walking past it is "
                    + "how the same material gets moved twice";
            }
        }

        if (_state.Custody == NpcPlanCustody.Uncertain)
        {
            bool stopping = proposed.Phase == NpcPlanPhase.NeedsAttention;
            bool noting = proposed.Phase == _state.Phase && proposed.Custody == NpcPlanCustody.Uncertain;
            if (!stopping && !noting)
            {
                // Nothing here clears an uncertain movement, including a write
                // that merely looks tidier. A person resolves it through the
                // role's own custody record, and the plan that follows is a new
                // one.
                return "this plan is waiting for a person, and nothing but a person changes that";
            }
        }

        return string.Empty;
    }
}
