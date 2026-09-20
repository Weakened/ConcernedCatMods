using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Work;

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
/// touched and <see cref="Conclude"/> after - and a move into either of those two
/// phases is <b>refused</b> unless this run has done exactly that, which is
/// <see cref="NpcPlanProgression.MovesMaterialToReach"/>'s rule and carries that
/// function's own note about what a run can and cannot remember across a reload.
/// A record found in
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

    /// <summary>The phase in which this run last concluded an intent with an
    /// established outcome, or <see cref="NpcPlanPhase.Unspecified"/> if it never
    /// has. <b>The only state here that is not the plan</b>, and what makes the
    /// write-ahead discipline a refusal rather than a convention - see
    /// <see cref="NpcPlanProgression.MovesMaterialToReach"/>, which also says why
    /// it is deliberately not durable.</summary>
    private NpcPlanPhase _concludedIn = NpcPlanPhase.Unspecified;

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
            if (_state.Custody == NpcPlanCustody.Pending
                && proposed!.Custody == NpcPlanCustody.Clear
                && proposed.Phase == _state.Phase)
            {
                // An intent this run wrote has just been concluded with an
                // established outcome, in the phase it was written in. That is the
                // one thing that earns a move into a material-moving phase; the
                // last rule in WhyNot is where it is spent.
                //
                // Requiring Clear rather than merely "no longer Pending" is
                // defence-in-depth and nothing more: an outcome nobody could
                // establish leaves the plan Uncertain, and the Uncertain rule above
                // already refuses every move that could spend this flag, so
                // relaxing this test to "not Pending" leaves the whole suite green.
                // It is written as Clear so the field means what its name says
                // rather than because a test would catch it.
                _concludedIn = _state.Phase;
            }

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
    /// to <see cref="NpcPlanPhase.Observing"/>, because the plan it would
    /// otherwise resume from was computed against a world that has moved - and the
    /// only one, which is why it is a separate verb with the decision that
    /// authorised it passed in rather than a relaxation of <see cref="Record"/>.
    ///
    /// <b>What it checks, and why every check is here rather than assumed.</b>
    /// A review of the first version of this method found that it checked the
    /// decision's <i>existence</i> and nothing else, so any invented
    /// <see cref="InterruptionResponse.Replan"/> adopted any same-identity state:
    /// it cleared an uncertain movement, reopened a plan that had stopped for a
    /// person, skipped five phases at once, and wrote a plan whose custody field
    /// nobody had set - all four of which <see cref="Record"/> refuses by name.
    /// So the four rules <c>Record</c> enforces are enforced here too, in the one
    /// form that suits a backward write:
    ///
    /// <list type="bullet">
    /// <item>an ending is an ending, here as well. There is one exception, added
    /// later and scoped to it: a record that says it <c>Settled</c> or
    /// <c>Refunded</c> while something it set in motion had no recorded outcome may
    /// be moved to <see cref="NpcPlanPhase.NeedsAttention"/>, and only there, and
    /// only on the decision that means it - otherwise nothing is adopted over a
    /// terminal phase at all. What keeps
    /// <see cref="NpcPlanPhase.NeedsAttention"/> one-way is therefore not that
    /// endings are unwritable but that the exception's only permitted target is
    /// <see cref="NpcPlanPhase.NeedsAttention"/> itself, and that a plan already
    /// there is excluded from it;</item>
    /// <item>a movement with no established outcome may be followed only by a
    /// decision that stops the plan for a person;</item>
    /// <item>a state has to say whether anything is in flight;</item>
    /// <item>the work itself has to come through unchanged - a recovery changes
    /// the phase and the note, which is exactly what
    /// <see cref="NpcPlanState.CarriesTheSameWorkAs"/> asks;</item>
    /// <item>and the phase has to be the one this decision actually leaves a plan
    /// in, which is <see cref="NpcPlanProgression.PhaseAfter"/> - the same
    /// function the recovery path produced it with.</item>
    /// </list>
    ///
    /// So a caller cannot reach the backward path, or any other path, by handing
    /// in a made-up outcome.</summary>
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

        if (NpcPlanProgression.IsTerminal(_state.Phase)
            && !(decision.Response == InterruptionResponse.NeedsAttention
                && revalidated.Phase == NpcPlanPhase.NeedsAttention
                && _state.Phase != NpcPlanPhase.NeedsAttention
                && _state.Custody != NpcPlanCustody.Clear))
        {
            // The one move out of an ending, and only for the decision that means
            // it. Without the response test a fabricated Continue over a plan
            // already stopped for a person was saved - nothing about the work
            // changed and MayAct stayed false, so it was not a live defect, but
            // this method's own summary promises a caller cannot reach any path by
            // handing in a made-up outcome, and a resume-authorising response over
            // a terminal plan was one.
            //
            // The three conditions have to read the same as WhyNot's rule, which
            // does the same job for Stop: the decision means it, the phase moved to
            // is NeedsAttention, the phase moved from is a terminal one that is not
            // NeedsAttention - a plan already waiting for a person has nothing to
            // correct, and including it reopens writes that "one-way" is supposed
            // to forbid - and the custody is not Clear.
            //
            // That last one is deliberately not NpcPlanState.IsUncertain. IsUncertain is Uncertain or
            // Unspecified and excludes Pending, while NpcPlanRecovery.AlreadyOver
            // answers NeedsAttention for any terminal row whose custody is not
            // Clear - Pending included - so narrowing either site to IsUncertain
            // makes a terminal-over-Pending row unwritable again, which is the bug
            // this allowance exists to fix. A test pins the pair.
            return NpcPlanSave.Refused(
                "this plan has ended, and an ending that the next write can undo is not an ending");
        }

        if (_state.Phase == NpcPlanPhase.Unspecified
            && revalidated.Phase != NpcPlanPhase.NeedsAttention)
        {
            // A record somebody wrote wrong is not the beginning. A review found
            // this the sixth way in: a decoded phase of zero reached Resume,
            // could not be stopped, and a recovery answering Replan laundered it
            // into a live plan at Observing still claiming a load, its
            // reservations and its cart. The only decision that may be written
            // over it is one that hands it to a person.
            return NpcPlanSave.Refused(
                "this plan's record is in no phase at all, which is not the beginning, so the only thing that "
                + "may be written over it is a stop for a person");
        }

        if (revalidated.Custody == NpcPlanCustody.Unspecified)
        {
            return NpcPlanSave.Refused(
                "a plan has to say whether anything is in flight; not saying is not the same as no");
        }

        if (_state.Custody != NpcPlanCustody.Clear
            && revalidated.Phase != NpcPlanPhase.NeedsAttention)
        {
            return NpcPlanSave.Refused(
                "something this plan set in motion has no established outcome, and the only decision that may "
                + "be adopted over that is one that stops it for a person");
        }

        if (!decision.MayResume && !NpcPlanProgression.IsTerminal(revalidated.Phase))
        {
            return NpcPlanSave.Refused("a plan that may not go on has to end somewhere");
        }

        if (!revalidated.CarriesTheSameWorkAs(_state))
        {
            return NpcPlanSave.Refused(
                "a recovery changes the phase and the note and nothing else, and that state changes what the "
                + "plan is about or what it is holding");
        }

        if (revalidated.Phase != NpcPlanProgression.PhaseAfter(decision.Response, _state.Phase))
        {
            return NpcPlanSave.Refused("that is not the phase this decision leaves a plan in");
        }

        NpcPlanSave written = _journal.Save(revalidated);
        if (written.IsSaved)
        {
            // <b>Every response, and two of them are checkable.</b> A re-plan
            // is the case that makes the reset necessary: the phases it is about to
            // walk again are ahead of it, and a concluded intent kept across it
            // would be spent a second time on the way back up. A Continue is the
            // second: the plan has just been revalidated against the world, which
            // is exactly the moment a role should say again what it is about to
            // move rather than lean on what it said before the interruption. Both
            // are pinned by tests.
            //
            // For a refund and for a stop for a person the reset is not
            // observable at all - those responses leave the plan terminal, so
            // nothing can be spent afterwards - and narrowing the reset to exclude
            // them would change no test. It is unconditional because the field
            // should mean what its name says for every response, not because
            // anything checks those two. This is the second time a comment on this
            // field has claimed more than its tests, so the claim now stops where
            // the evidence does.
            _concludedIn = NpcPlanPhase.Unspecified;
            _state = revalidated;
        }

        return written;
    }

    /// <summary>Says that this plan's references have been found again in the
    /// world that is loaded now.
    ///
    /// <b>The one exit from staleness, and the reason it is a verb here.</b> A
    /// plan off the disk carries <see cref="NpcWorldEpoch.Unknown"/>, which matches
    /// nothing, so it is stale and a stale plan never continues - it re-plans. That
    /// is right at the moment of reconstruction and wrong for ever: without this,
    /// a re-planned plan revalidates to "the world reloaded, so re-plan" on every
    /// call, indefinitely, which a review of #379 observed five times in a row
    /// against healthy evidence. Only the role can say when it has found its
    /// chests and its piles again, so only the role can end it, and nothing here
    /// checks the claim because nothing here can see the world.
    ///
    /// Written through <see cref="Record"/> like everything else, so the phase and
    /// the load are unchanged and an unresolved movement stays unresolved: this
    /// re-attaches a plan to a world, and resolves nothing about it.</summary>
    internal NpcPlanSave Reattach(NpcWorldEpoch now, string? note)
    {
        if (now.IsUnknown)
        {
            return NpcPlanSave.Refused(
                "a plan cannot be re-attached to no world at all, which is what it already says it is in");
        }

        return Record(_state.WithWorld(now).WithPhase(_state.Phase, note));
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

        if (proposed.Phase == NpcPlanPhase.NeedsAttention
            && proposed.CarriesTheSameWorkAs(_state)
            && (!NpcPlanProgression.IsTerminal(_state.Phase)
                || (_state.Phase != NpcPlanPhase.NeedsAttention
                    && _state.Custody != NpcPlanCustody.Clear)))
        {
            // <b>Handing a plan to a person is always available.</b> Whatever is
            // wrong with this plan - a movement with no outcome, a phase nobody
            // set, a record somebody wrote wrong - it can be stopped and left for
            // somebody to look at.
            //
            // <b>What "nothing but the phase and the note" means here, exactly.</b>
            // The same-work test is what holds this open path shut: it is the only
            // thing standing between an unconditional write and every rule below -
            // both pending rules, the uncertain rule, the load rule and the refusal
            // of an unset custody field - so without it this exemption reopens the
            // erasure shape it sits above, and a stop could drop a load or launder
            // a pending custody on the way past. It is not quite "the phase and the
            // note", though, and the difference is worth stating rather than
            // rounding off: CarriesTheSameWorkAs deliberately ignores the world
            // epoch and the attempt count, so a stop may also re-stamp those, and
            // it <i>compares</i> the custody rather than requiring one, so a record
            // whose custody nobody set can be stopped past the refusal below.
            // Both are conservative and neither is depended on - adding either
            // guard changes no test - which is why the latitude is documented
            // rather than closed.
            //
            // <b>And one ending may become this one.</b> An ending is otherwise an
            // ending, but a record that says it finished while something it set in
            // motion had no recorded outcome is not a well-formed ending at all:
            // NpcPlanRecovery answers NeedsAttention for it every time it is
            // loaded, and until this allowance existed no verb could write that
            // answer down, so the record went on claiming success for ever and
            // nothing could correct it. What the write buys is exactly that - the
            // record stops claiming it finished - and not an end to the answer: a
            // plan at NeedsAttention is still answered NeedsAttention on every
            // load, by AlreadyOver's first branch, which is the documented and
            // desired steady state.
            //
            // <b>Scoped to Settled and Refunded, and to a custody that is not
            // Clear.</b> Both halves matter. A plan whose custody is Clear is
            // untouched, so a job that really did finish is never reopened and
            // "this job finished" stays distinguishable from "this job never
            // existed". And a plan already at NeedsAttention is excluded, because
            // it has nothing to correct and including it would quietly reopen three
            // verbs on a plan stopped for a person - a second NeedsAttention write,
            // a Suspend and a Reattach, all of which were refused before this
            // allowance existed and are refused again.
            //
            // This is where the asymmetry in NpcPlanProgression.MayFollow is
            // answered rather than in the table. MayFollow refuses Unspecified on
            // both sides, which is right for a table about the pipeline's shape -
            // a phase nobody set is not a position on the line - but it left one
            // state the library could hold and could not hand to anybody: a
            // resumed record whose phase is zero could not even be stopped, while
            // a recovery would happily re-plan it. A library with such a state has
            // a failure nobody is ever told about, which is the one outcome this
            // whole area exists to avoid.
            return string.Empty;
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
            if (!concluding && proposed.Custody != NpcPlanCustody.Pending)
            {
                // The write that erases the evidence. A phase change that also
                // drops the pending custody leaves a record saying nothing was
                // ever in flight, and not even AsRecovered can get it back -
                // which is worse than walking past the question, because after
                // this nobody knows there was one.
                return "a movement with no recorded outcome cannot be written out of existence by a phase "
                    + "change: that erases the only evidence anything was in flight";
            }

            if (!concluding)
            {
                // Including into an ending. A plan that says it finished while
                // something it set in motion had no recorded outcome is a plan
                // that silently lost or duplicated a player's material and then
                // reported success - and a refund is documented as a release of
                // what is still held, never a way to close an open question.
                return "something this plan set in motion has no recorded outcome yet, and walking past it - "
                    + "into the next phase or into an ending - is how the same material gets moved twice";
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

        if (proposed.Phase != _state.Phase
            && NpcPlanProgression.MovesMaterialToReach(proposed.Phase)
            && _concludedIn != _state.Phase)
        {
            // Getting to that phase means a player's material moved, and nothing
            // this run wrote says the movement was announced first and accounted
            // for afterwards. A record that reached it anyway is one a recovery
            // reads as "nothing was in flight, carry on" - over a move that may
            // have happened.
            return "getting there moves a player's material, and this plan never said the movement was about "
                + "to happen or what became of it, so the same load would be moved twice";
        }

        if (!proposed.CarriesTheSameLoadAs(_state) && _state.Custody != NpcPlanCustody.Pending)
        {
            // <b>The load, not only the transition.</b> The rule above binds the
            // two phase changes; this binds the field. Without it any same-phase
            // write could rewrite Carried in any phase with no intent at all, so
            // "the two transitions that move a player's material are written
            // twice" would be a statement about which phase the plan is in rather
            // than about what the NPC is holding. What he is carrying is now only
            // ever the recorded outcome of a movement that was announced first.
            //
            // Deliberately only Carried. Reservations, the route and the progress
            // count are not a player's material in transit - a reservation name is
            // derived, so re-taking it is satisfied rather than doubled - and
            // binding them here would refuse the write that records them.
            return "what the NPC is carrying only ever changes as the recorded outcome of a movement this plan "
                + "said was about to happen";
        }

        return string.Empty;
    }
}
