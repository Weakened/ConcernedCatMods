namespace TheConcernedCat.ConcernedNPC.Interruption;

/// <summary>What an NPC is doing, on the one ladder that decides which of two
/// things wins.
///
/// <b>Seven rungs, in the order the brief states them</b>: lifecycle and recovery
/// safety, then danger, then an explicit player order, then required role work,
/// then background maintenance, then social idle, then the camp-border stroll.
/// The numbers rise with priority so that a comparison reads the way the sentence
/// does, and the sentence is the specification.
///
/// <b>Why each rung is above the one below it, briefly.</b> Recovery safety is
/// first because everything else assumes the record and the world agree, and a
/// plan that is being made safe again is the thing that makes that true - an NPC
/// that fled a boar in the middle of reconciling its books would leave exactly the
/// uncertain state this library spends most of its effort avoiding. Danger is next
/// because everything below it can wait and a death cannot be undone. A player's
/// explicit order outranks the NPC's own judgement about its job, because it is
/// the player's settlement. Required role work outranks maintenance because
/// maintenance is what an NPC does when there is nothing it must do. And idling,
/// chatting and strolling are the bottom three in that order, because being
/// somewhere pleasant is the least important thing an NPC can be doing and is also
/// the thing a player notices being interrupted least.
///
/// <b>Zero is not the bottom rung.</b> It is "nobody said", and it behaves as
/// neither the top nor the bottom - see <see cref="NpcActivityArbiter"/>, which
/// explains why an unnamed activity neither interrupts nor is interrupted.
/// </summary>
internal enum NpcActivityPriority
{
    /// <summary>Nobody said. Never a claim, and never something to displace.
    /// </summary>
    Unspecified = 0,

    /// <summary>Walking the edge of the camp because there is nothing else to do.
    /// </summary>
    CampBorderStroll = 1,

    /// <summary>Standing about, sitting, talking to somebody.</summary>
    SocialIdle = 2,

    /// <summary>Work the NPC took on itself because it was there: tidying,
    /// topping something up, a round nobody asked for.</summary>
    BackgroundMaintenance = 3,

    /// <summary>The job this NPC exists to do.</summary>
    RequiredRoleWork = 4,

    /// <summary>Something the player said to do, in so many words.</summary>
    PlayerOrder = 5,

    /// <summary>Something is trying to kill him.</summary>
    Danger = 6,

    /// <summary>Making the record and the world agree again: reconstruction,
    /// revalidation, giving a claim back, writing down what happened. <b>The top
    /// rung</b>, because every other rung is only safe while this has
    /// finished.</summary>
    LifecycleSafety = 7,
}

/// <summary>Which of two activities gets the body, and what happens to the plan
/// the loser was in the middle of.
///
/// <b>The rule: strictly higher wins, and equal does not.</b> An arriving
/// activity of the same priority does not displace the running one, because the
/// running one has a plan in progress and the arriving one does not, and
/// displacing on a tie is how an NPC spends a session swapping between two jobs
/// and finishing neither.
///
/// <b>Why an unnamed activity neither interrupts nor is interrupted.</b> An
/// arrival with no stated priority has not made a claim, so it displaces nothing:
/// the alternative is that a caller which forgot to say what it wanted gets to
/// preempt a player's own order. And a running activity with no stated priority
/// might be any rung, including the top one, so displacing it could displace the
/// very thing that is making the record safe again. Both directions fail closed,
/// and an activity nobody named is a programmer's error that shows up as an NPC
/// that will not change task rather than as a silently corrupted plan.
///
/// <b>What an interruption preserves, and how that is guaranteed rather than
/// intended.</b> Everything: inventory, reservations, custody, the plan, the
/// cart, progress, source, destination and identity. It is guaranteed by what a
/// handover does <i>not</i> contain - there is no path here that edits a plan.
/// The suspended plan is handed back as the same value, and
/// <see cref="NpcPlanState.CarriesTheSameWorkAs"/> is the question a test asks
/// about it.
///
/// <b>Why a failed write does not refuse the handover.</b> Because of the
/// ordering rule in <see cref="NpcPlanRun"/>: a caller may only act on a phase the
/// record already says it is in, so the record on the disk is always a safe place
/// to resume from and a suspension adds nothing to it but a note. Making a boar
/// wait for a disk would be a rule that kills NPCs to keep a diary tidy. The
/// handover says whether the note was written, and a role that wants to tell a
/// player why its NPC stopped can say "it was interrupted" either way.</summary>
internal static class NpcActivityArbiter
{
    /// <summary>Whether <paramref name="arriving"/> may take the body from
    /// <paramref name="running"/>.</summary>
    internal static bool MayInterrupt(NpcActivityPriority running, NpcActivityPriority arriving)
    {
        if (running == NpcActivityPriority.Unspecified || arriving == NpcActivityPriority.Unspecified)
        {
            return false;
        }

        return arriving > running;
    }

    /// <summary>Hands the body from one activity to another, leaving the plan
    /// exactly as it stands and noting why.
    ///
    /// <b>The note is attempted, and its failure is reported rather than
    /// fatal.</b> See the type's own summary for why.</summary>
    /// <param name="run">The plan being interrupted, or null when the running
    /// activity has no plan - a stroll does not.</param>
    internal static NpcActivityHandover Interrupt(
        NpcActivityPriority running, NpcActivityPriority arriving, NpcPlanRun? run, string? note)
    {
        if (!MayInterrupt(running, arriving))
        {
            return NpcActivityHandover.Refused(
                running,
                arriving,
                running == NpcActivityPriority.Unspecified || arriving == NpcActivityPriority.Unspecified
                    ? "one of the two activities did not say what it was, and an activity nobody named neither "
                        + "takes a body nor gives one up"
                    : "what he is doing already matters at least as much as what is asking for him",
                run?.State);
        }

        if (run == null)
        {
            return NpcActivityHandover.Granted(running, arriving, true, null);
        }

        NpcPlanState suspended = run.State;
        NpcPlanSave written = run.Suspend(note);

        // The state is read back from the run rather than reused from above, so
        // that the plan handed over is the one the run is actually in - and the
        // preservation property is asserted against what a resumption would find.
        return NpcActivityHandover.Granted(running, arriving, written.IsSaved, run.State)
            .Against(suspended);
    }
}

/// <summary>What happened when one activity asked another for the body.</summary>
internal readonly struct NpcActivityHandover
{
    private NpcActivityHandover(
        bool granted,
        NpcActivityPriority yielded,
        NpcActivityPriority took,
        bool noteRecorded,
        string reason,
        NpcPlanState? suspended,
        NpcPlanState? before)
    {
        IsGranted = granted;
        Yielded = yielded;
        Took = took;
        IsNoteRecorded = noteRecorded;
        Reason = reason ?? string.Empty;
        Suspended = suspended;
        Before = before;
    }

    /// <summary>Whether the arriving activity has the body now.</summary>
    internal bool IsGranted { get; }

    /// <summary>What gave it up.</summary>
    internal NpcActivityPriority Yielded { get; }

    /// <summary>What took it.</summary>
    internal NpcActivityPriority Took { get; }

    /// <summary>Whether the note saying why reached the disk. False is not a
    /// lost plan: the record was already at a safe resume point.</summary>
    internal bool IsNoteRecorded { get; }

    /// <summary>Why it was refused. Empty on a grant.</summary>
    internal string Reason { get; }

    /// <summary>The plan as it now stands, for the activity that will resume it.
    /// Null when the interrupted activity had no plan.</summary>
    internal NpcPlanState? Suspended { get; }

    /// <summary>The plan as it stood before the handover, kept so that "nothing
    /// about the work changed" is a comparison rather than a claim.</summary>
    internal NpcPlanState? Before { get; }

    /// <summary>Whether the interrupted plan came through unchanged in
    /// everything but its note: inventory, reservations, custody, progress, the
    /// cart, source, destination and identity, and the phase it was in.
    ///
    /// <b>True when there was no plan</b>, because an interruption that had
    /// nothing to preserve preserved it.</summary>
    internal bool PreservedThePlan
    {
        get
        {
            if (Before == null && Suspended == null)
            {
                return true;
            }

            if (Before == null || Suspended == null)
            {
                return false;
            }

            return Suspended.Phase == Before.Phase && Suspended.CarriesTheSameWorkAs(Before);
        }
    }

    internal static NpcActivityHandover Granted(
        NpcActivityPriority yielded, NpcActivityPriority took, bool noteRecorded, NpcPlanState? suspended) =>
        new NpcActivityHandover(true, yielded, took, noteRecorded, string.Empty, suspended, suspended);

    internal static NpcActivityHandover Refused(
        NpcActivityPriority yielded, NpcActivityPriority took, string reason, NpcPlanState? unchanged) =>
        new NpcActivityHandover(false, yielded, took, false, reason, unchanged, unchanged);

    /// <summary>The same handover, remembering what the plan looked like before
    /// it.</summary>
    internal NpcActivityHandover Against(NpcPlanState? before) =>
        new NpcActivityHandover(IsGranted, Yielded, Took, IsNoteRecorded, Reason, Suspended, before);

    public override string ToString() =>
        (IsGranted ? "granted " : "refused ") + Yielded + " to " + Took
        + (Reason.Length == 0 ? string.Empty : ": " + Reason);
}
