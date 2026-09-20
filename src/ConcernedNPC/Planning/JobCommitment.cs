using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>Everything one job has taken out, and the one verb that gives it all
/// back.</summary>
internal interface IJobCommitment
{
    /// <summary>Whose it is.</summary>
    string JobId { get; }

    /// <summary>How many subjects this commitment <b>newly</b> took out.
    ///
    /// <b>Not "how many this job holds", and never a reason to skip a
    /// cancel.</b> A job re-establishing its holds after an interruption is
    /// answered <see cref="ReservationOutcome.AlreadySatisfied"/> for every
    /// subject it already had, and none of those is counted here - so a caller
    /// written as <c>if (c.NewlyTaken > 0) c.Cancel();</c> would leak the whole
    /// job's reservations on exactly the path recovery takes. Cancelling is
    /// unconditional and always safe; that is what the latch is for.</summary>
    int NewlyTaken { get; }

    /// <summary>Whether the refund has happened.</summary>
    bool IsCancelled { get; }

    /// <summary>Gives everything back, <b>exactly once</b>. Answers how many
    /// were released, and zero for every call after the first.</summary>
    int Cancel();
}

/// <summary>What one job holds in one book, and the guarantee that cancelling it
/// refunds once.
///
/// <b>Why "exactly once" needs a type rather than a discipline.</b> A plan is
/// cancelled from more places than anybody expects: the player countermands it,
/// the NPC dies, the world unloads, an interruption gives up, and two of those
/// can happen in the same frame. Releasing twice is not merely untidy - a book
/// whose release is counted, or a ledger that refunds material on release, gives
/// the job its material back twice and the second lot came from nowhere. So the
/// latch lives here, next to the holds, and every one of those callers can
/// cancel unconditionally without first proving what it had. That is the same
/// reason <see cref="ReservationOutcome.NotHeld"/> is an ordinary answer rather
/// than an error one level down.
///
/// <b>Reserving twice is satisfied, not refused</b>, because conflicts are
/// decided on the job half of a reservation's name. A job that re-establishes
/// its holds after an interruption by walking its plan again gets
/// <see cref="ReservationOutcome.AlreadySatisfied"/> for everything it already
/// had, which is exactly what makes recovery possible without a separate record
/// of what was taken - and a separate record is the thing that would be
/// wrong.
///
/// <b>What it is not.</b> It is not custody and it holds no material. It records
/// that a job has the right to act on something; what is actually carried lives
/// in a ledger, in the custody layer, which is the only place that can say "this
/// may or may not have happened".</summary>
/// <typeparam name="TSubject">What is held: a target, a container.</typeparam>
internal sealed class JobCommitment<TSubject> : IJobCommitment
    where TSubject : INpcEpochScoped
{
    private readonly IReservationBook<TSubject> _book;
    private readonly List<Hold> _taken = new List<Hold>();

    internal JobCommitment(string? jobId, IReservationBook<TSubject> book)
    {
        JobId = jobId ?? string.Empty;
        _book = book ?? throw new ArgumentNullException(nameof(book));
    }

    /// <summary>One subject this commitment took out, under the step it was
    /// taken for. The step is kept because giving a hold back under the name it
    /// was taken under is the only way to give back exactly that hold and
    /// nothing else.</summary>
    private readonly struct Hold
    {
        internal Hold(TSubject subject, int step)
        {
            Subject = subject;
            Step = step;
        }

        internal TSubject Subject { get; }

        internal int Step { get; }
    }

    /// <inheritdoc />
    public string JobId { get; }

    /// <inheritdoc />
    public int NewlyTaken => _taken.Count;

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    /// <summary>Everything this job took out through this commitment, in the
    /// order it took it.</summary>
    internal IReadOnlyList<TSubject> Taken
    {
        get
        {
            var subjects = new List<TSubject>(_taken.Count);
            foreach (Hold hold in _taken)
            {
                subjects.Add(hold.Subject);
            }

            return subjects;
        }
    }

    /// <summary>Takes one subject out under the name of one step of the plan.
    ///
    /// A cancelled commitment asks for nothing:
    /// <see cref="ReservationOutcome.Unspecified"/> is returned and the book is
    /// not touched, because a job that has given everything back and then
    /// reserves one more thing has an orphan nobody will ever
    /// release.</summary>
    internal ReservationOutcome Reserve(TSubject subject, int step)
    {
        if (IsCancelled)
        {
            return ReservationOutcome.Unspecified;
        }

        ReservationId name = ReservationId.For(JobId, step);
        if (name.IsEmpty)
        {
            return ReservationOutcome.Unspecified;
        }

        ReservationOutcome outcome = _book.Reserve(subject, name);
        if (outcome == ReservationOutcome.Reserved)
        {
            _taken.Add(new Hold(subject, step));
        }

        return outcome;
    }

    /// <inheritdoc />
    public int Cancel()
    {
        if (IsCancelled)
        {
            // The whole point of the type. Every caller may cancel; only the
            // first one refunds.
            return 0;
        }

        IsCancelled = true;
        int released = _book.ReleaseAllFor(JobId);
        _taken.Clear();
        return released;
    }

    /// <summary>Gives back exactly what this commitment newly took, and leaves
    /// the commitment usable.
    ///
    /// <b>Why this is not <see cref="Cancel"/>, which was doing the job.</b>
    /// Cancel is the right verb for "this job is over": it releases by job id,
    /// which sweeps up holds taken by an earlier plan of the same job, and it
    /// latches, so nothing may be reserved through the object afterwards. Both
    /// are wrong for an attempt that was abandoned.
    ///
    /// <i>Releasing by job takes what this attempt never had.</i> On the
    /// recovery path a job re-establishing its holds is answered
    /// <see cref="ReservationOutcome.AlreadySatisfied"/> for everything it
    /// already owned, so none of that enters the list below - and a conflict at
    /// step five would have handed back the previous plan's legitimate holds
    /// along with this attempt's two.
    ///
    /// <i>Latching kills an object the caller still holds.</i> A runtime that
    /// keeps one commitment per job, adjusts its plan and tries again would get
    /// <see cref="ReservationOutcome.Unspecified"/> for ever after, and the
    /// failure would present as a reservation conflict rather than as a dead
    /// object.
    ///
    /// Each hold goes back under the name it was taken under, so a book that
    /// decides on the whole name and a book that decides on the job half behave
    /// the same here.</summary>
    /// <returns>How many were actually given back.</returns>
    internal int RollbackNewlyTaken()
    {
        if (IsCancelled)
        {
            return 0;
        }

        int released = 0;
        foreach (Hold hold in _taken)
        {
            if (_book.Release(hold.Subject, ReservationId.For(JobId, hold.Step))
                == ReservationOutcome.Released)
            {
                released++;
            }
        }

        _taken.Clear();
        return released;
    }
}

/// <summary>Several commitments cancelled together, still exactly once.
///
/// A job holds targets in one book and containers in another, and a cancellation
/// has to give both back without the caller remembering how many books there
/// were. The latch is here as well as in each part, so cancelling the set twice
/// refunds once even if somebody also cancelled one of the parts
/// directly.</summary>
internal sealed class JobCommitments : IJobCommitment
{
    private readonly List<IJobCommitment> _parts = new List<IJobCommitment>();

    internal JobCommitments(string? jobId, params IJobCommitment?[]? parts)
    {
        JobId = jobId ?? string.Empty;
        if (parts == null)
        {
            return;
        }

        foreach (IJobCommitment? part in parts)
        {
            if (part != null)
            {
                _parts.Add(part);
            }
        }
    }

    /// <inheritdoc />
    public string JobId { get; }

    /// <inheritdoc />
    public int NewlyTaken
    {
        get
        {
            int total = 0;
            foreach (IJobCommitment part in _parts)
            {
                total += part.NewlyTaken;
            }

            return total;
        }
    }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    /// <inheritdoc />
    public int Cancel()
    {
        if (IsCancelled)
        {
            return 0;
        }

        IsCancelled = true;
        int released = 0;
        foreach (IJobCommitment part in _parts)
        {
            released += part.Cancel();
        }

        return released;
    }
}

/// <summary>One step's attempt to take out what it is about to touch.</summary>
internal readonly struct ReservationAttempt
{
    internal ReservationAttempt(int step, ReservationId name, ReservationOutcome outcome, bool isCollect)
    {
        Step = step;
        Name = name;
        Outcome = outcome;
        IsCollect = isCollect;
    }

    internal int Step { get; }

    internal ReservationId Name { get; }

    internal ReservationOutcome Outcome { get; }

    internal bool IsCollect { get; }

    /// <summary>Whether this job may act on the subject: it took it, or it
    /// already had it. Those are the only two answers that are a yes.</summary>
    internal bool IsHeld =>
        Outcome == ReservationOutcome.Reserved || Outcome == ReservationOutcome.AlreadySatisfied;
}

/// <summary>What happened when a plan reserved everything it is going to
/// touch.</summary>
internal readonly struct JobReservationResult
{
    private readonly ReservationAttempt[]? _attempts;

    internal JobReservationResult(IReadOnlyList<ReservationAttempt>? attempts, bool rolledBack = false)
    {
        RolledBack = rolledBack;

        if (attempts == null || attempts.Count == 0)
        {
            _attempts = null;
        }
        else
        {
            var copy = new ReservationAttempt[attempts.Count];
            for (int index = 0; index < attempts.Count; index++)
            {
                copy[index] = attempts[index];
            }

            _attempts = copy;
        }
    }

    internal IReadOnlyList<ReservationAttempt> Attempts => _attempts ?? Array.Empty<ReservationAttempt>();

    /// <summary>Whether the attempt was abandoned and everything it had taken
    /// was given back.
    ///
    /// <b>True means the books are exactly as they were.</b> A plan that got
    /// four steps in and was refused the fifth has no business leaving four
    /// holds behind it: the plan is not walkable, so the holds belong to nobody
    /// and nothing will release them until the world unloads.
    ///
    /// <b>Exactly as they were, and no further back.</b> What goes back is what
    /// this attempt newly took, under the names it took them under - not
    /// everything the job holds. A job re-establishing holds after an
    /// interruption owns holds this attempt never took, and a rollback that
    /// released those would make the sentence above false in the one direction
    /// nobody would check. The commitments stay usable afterwards, so a caller
    /// that adjusts its plan and asks again is asking a live object.</summary>
    internal bool RolledBack { get; }

    /// <summary>How many were newly taken.</summary>
    internal int Taken
    {
        get
        {
            int total = 0;
            foreach (ReservationAttempt attempt in Attempts)
            {
                if (attempt.Outcome == ReservationOutcome.Reserved)
                {
                    total++;
                }
            }

            return total;
        }
    }

    /// <summary>How many another job already had. <b>Not an error</b> - it is
    /// the ordinary answer when two NPCs want the same tree - but it is the
    /// number that decides whether this plan can be walked as written.</summary>
    internal int Conflicts
    {
        get
        {
            int total = 0;
            foreach (ReservationAttempt attempt in Attempts)
            {
                if (attempt.Outcome == ReservationOutcome.HeldByAnother)
                {
                    total++;
                }
            }

            return total;
        }
    }

    /// <summary>How many were named in a world that has since gone.</summary>
    internal int Stale
    {
        get
        {
            int total = 0;
            foreach (ReservationAttempt attempt in Attempts)
            {
                if (attempt.Outcome == ReservationOutcome.StaleEpoch)
                {
                    total++;
                }
            }

            return total;
        }
    }

    /// <summary>Whether every step of the plan may act. <b>The one question
    /// before walking</b>, and false is never a partial success - see
    /// <see cref="RolledBack"/>.</summary>
    internal bool AllHeld
    {
        get
        {
            if (RolledBack)
            {
                return false;
            }

            foreach (ReservationAttempt attempt in Attempts)
            {
                if (!attempt.IsHeld)
                {
                    return false;
                }
            }

            return Attempts.Count > 0;
        }
    }

    /// <summary>Whether no two steps asked under the same name.
    ///
    /// <b>True by construction and checked anyway.</b> A name is the job plus
    /// the step index, and step indices are unique within a plan, so two steps
    /// cannot collide unless the plan renumbered itself - which is exactly the
    /// bug that would make one step release another's hold. It costs a pass over
    /// a short list to know rather than to believe.</summary>
    internal bool NamesAreDistinct
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReservationAttempt attempt in Attempts)
            {
                if (!seen.Add(attempt.Name.Value))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>Taking out everything a plan is going to touch, before any of it is
/// touched.
///
/// <b>Reserve after planning and before acting, in one go.</b> Not per step as
/// the NPC reaches it: a job that reserves the fourth tree when it gets there
/// has already walked past three, and the moment it finds the fourth taken it
/// has to decide what to do with material it fetched for it. Reserving the whole
/// plan up front turns that into a decision made once, standing still, with the
/// whole job in front of it.
///
/// <b>All of it or none of it.</b> The first subject this job may not have ends
/// the attempt, and everything already taken is given straight back. The
/// alternative - keep going, report the conflicts, let the caller decide - reads
/// as flexible and is not: it leaves a plan nobody will walk holding a tree
/// somebody else wants, until the world unloads. A caller that dropped the
/// result on the floor would never release them, and dropping a result on the
/// floor is the commonest thing any caller does.</summary>
internal static class PlanReservations
{
    /// <summary>Reserves every subject the plan names. Steps whose kind has no
    /// book are skipped rather than failed - a role that reserves targets and
    /// not containers is making a choice this library does not
    /// second-guess.</summary>
    internal static JobReservationResult TakeOut(
        in JobTourPlan plan,
        JobCommitment<JobTarget>? targets,
        JobCommitment<INpcContainer>? containers)
    {
        var attempts = new List<ReservationAttempt>();
        foreach (PlannedStep step in plan.Steps)
        {
            ReservationAttempt attempt;
            if (step.IsCollect)
            {
                INpcContainer? container = step.Source.Container;
                if (containers == null || container == null)
                {
                    continue;
                }

                attempt = new ReservationAttempt(
                    step.Step.Index,
                    ReservationId.For(containers.JobId, step.Step.Index),
                    containers.Reserve(container, step.Step.Index),
                    true);
            }
            else
            {
                if (targets == null)
                {
                    continue;
                }

                attempt = new ReservationAttempt(
                    step.Step.Index,
                    ReservationId.For(targets.JobId, step.Step.Index),
                    targets.Reserve(step.Target, step.Step.Index),
                    false);
            }

            attempts.Add(attempt);

            if (attempt.IsHeld)
            {
                continue;
            }

            // Stop here and put back exactly what this attempt took. The plan
            // cannot be walked as written, so nothing it took out is doing
            // anybody any good - but a cancel would also release holds an
            // earlier plan of the same job legitimately owns, and would latch
            // the caller's commitments so a second attempt could never reserve
            // anything again.
            targets?.RollbackNewlyTaken();
            containers?.RollbackNewlyTaken();
            return new JobReservationResult(attempts, rolledBack: true);
        }

        return new JobReservationResult(attempts);
    }
}
