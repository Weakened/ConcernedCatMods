using System;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Who currently has the right to act on what, within one world load.
///
/// <b>Moved rather than invented.</b> This is the shipped source-reservation
/// book - the best of the three reservation implementations in this repository,
/// and the only one that refuses a stale epoch by construction - generalised
/// over its subject and re-keyed onto the derived reservation name. Its four
/// behaviours are unchanged: a second job is refused rather than queued or
/// preferred, the same job asking twice is satisfied rather than refused, a
/// subject from another world load is refused rather than matched, and
/// releasing something you do not hold is an ordinary answer.
///
/// <b>Conflicts are decided on the job, not the name.</b> Two reservations with
/// the same <see cref="ReservationId.JobId"/> never conflict, whatever their
/// steps. Without that, a re-plan that moved the same chest from step 3 to step
/// 2 would ask for something the job already holds under another name and be
/// refused by itself - and re-planning from where things actually are is
/// exactly what recovering from an interruption is.
///
/// <b>Why it is not durable, and why that is the safe choice.</b> Nothing here
/// is written to disk. A world load begins a new book, every reservation from
/// the previous one is gone, and the job re-establishes its holds by walking
/// its plan again - which it can, because the names are derived from the job
/// and the step rather than allocated. Persisting them would mean persisting
/// the names of world objects, and those do not survive a load: the saved name
/// of yesterday's chest is, today, overwhelmingly likely to name something
/// else. <b>This is the reconcile-rather-than-replay rule in its simplest
/// form</b>, and it is why restoring a reservation twice can never duplicate
/// anything: restoring is re-deriving a name that either is already held by
/// this job, or is not held at all.</summary>
/// <typeparam name="TSubject">What is reserved. Interpreted in exactly two
/// ways: equality, and its epoch.</typeparam>
internal sealed class NpcReservationBook<TSubject> : IReservationBook<TSubject>
    where TSubject : INpcEpochScoped
{
    private readonly Dictionary<TSubject, ReservationId> _held;

    /// <param name="epoch">The world load this book belongs to. A book whose
    /// epoch is unknown refuses everything, which is the correct behaviour
    /// before a world is loaded - and is a refusal rather than a throw, because
    /// a role asking too early should be told no, not crash.</param>
    /// <exception cref="ArgumentException">The subject is a reference type or
    /// an interface that does not answer <c>Equals(object)</c> as a value. See
    /// the type summary: this constructor is safe only for a subject that
    /// compares by what it is rather than by which object it is, and for
    /// everything else there is
    /// <see cref="NpcSubjectComparer.ByKey{TSubject}"/>.</exception>
    internal NpcReservationBook(NpcWorldEpoch epoch)
        : this(epoch, null)
    {
    }

    /// <param name="epoch">The world load this book belongs to.</param>
    /// <param name="comparer">How subjects are compared. <b>Required for any
    /// subject that does not carry value equality of its own</b> - a container
    /// wrapper re-read each tick, for instance, where what identifies it is its
    /// key and not which object it happens to be this frame.</param>
    internal NpcReservationBook(NpcWorldEpoch epoch, IEqualityComparer<TSubject>? comparer)
    {
        if (comparer == null)
        {
            RefuseUnsafeDefaultComparer();
        }

        Epoch = epoch;
        _held = comparer == null
            ? new Dictionary<TSubject, ReservationId>()
            : new Dictionary<TSubject, ReservationId>(comparer);
    }

    /// <summary>Refuses to build a book that would silently compare subjects by
    /// reference.
    ///
    /// <b>Why a throw and not a comment.</b> This was documented - "pass the
    /// right comparer or the book holds nothing" - and documentation is the
    /// wrong instrument, twice over. First because the consequence is worse
    /// than the doc said: a role handing a fresh wrapper each tick does not
    /// find an empty book, it finds a miss, and the book <i>grants the
    /// reservation again</i>, so two jobs hold one chest and both believe they
    /// are the only holder. Second because nothing fails when it is got wrong -
    /// releasing still clears both entries, so the defect leaves no trace at
    /// all. A constructor that refuses turns that into an exception a test
    /// catches, at the one moment somebody is in a position to fix it.</summary>
    private static void RefuseUnsafeDefaultComparer()
    {
        Type subject = typeof(TSubject);
        if (subject.IsValueType)
        {
            // A struct's compiler-provided equality compares its fields, which
            // is what a subject means by "the same subject".
            return;
        }

        MethodInfo? equals = subject.GetMethod(nameof(Equals), new[] { typeof(object) });
        if (equals != null && equals.DeclaringType != typeof(object))
        {
            return;
        }

        throw new ArgumentException(
            "A reservation book over " + subject.Name + " would compare subjects by reference, so the " +
            "same chest read twice would be two subjects and could be reserved twice, by two different " +
            "jobs, with neither of them told. Build it with NpcSubjectComparer.ByKey(subject => " +
            "subject.Key), or give the subject value equality of its own.");
    }

    public NpcWorldEpoch Epoch { get; }

    public int Count => _held.Count;

    public ReservationOutcome Reserve(TSubject subject, ReservationId reservation)
    {
        if (subject == null || reservation.IsEmpty)
        {
            return ReservationOutcome.Unspecified;
        }

        if (!Usable(subject))
        {
            return ReservationOutcome.StaleEpoch;
        }

        if (_held.TryGetValue(subject, out ReservationId holder))
        {
            return holder.SameJobAs(reservation)
                ? ReservationOutcome.AlreadySatisfied
                : ReservationOutcome.HeldByAnother;
        }

        _held[subject] = reservation;
        return ReservationOutcome.Reserved;
    }

    public ReservationOutcome Release(TSubject subject, ReservationId reservation)
    {
        if (subject == null
            || reservation.IsEmpty
            || !_held.TryGetValue(subject, out ReservationId holder)
            || !holder.SameJobAs(reservation))
        {
            // Not an error. Cleanup after a reload, a death or a failure
            // releases unconditionally, so this has to be an ordinary answer.
            return ReservationOutcome.NotHeld;
        }

        _held.Remove(subject);
        return ReservationOutcome.Released;
    }

    public int ReleaseAllFor(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return 0;
        }

        var mine = new List<TSubject>();
        foreach (KeyValuePair<TSubject, ReservationId> pair in _held)
        {
            if (string.Equals(pair.Value.JobId, jobId, StringComparison.Ordinal))
            {
                mine.Add(pair.Key);
            }
        }

        foreach (TSubject subject in mine)
        {
            _held.Remove(subject);
        }

        return mine.Count;
    }

    public bool IsHeldBy(TSubject subject, ReservationId reservation) =>
        subject != null
        && !reservation.IsEmpty
        && _held.TryGetValue(subject, out ReservationId holder)
        && holder.SameJobAs(reservation);

    public bool TryGetHolder(TSubject subject, out ReservationId reservation)
    {
        reservation = default;
        return subject != null && _held.TryGetValue(subject, out reservation);
    }

    /// <summary>Whether a subject can be spoken about at all in this book: this
    /// book knows which world it is in, and the subject was named in it.
    /// </summary>
    private bool Usable(TSubject subject)
    {
        if (Epoch.IsUnknown)
        {
            return false;
        }

        NpcWorldEpoch subjectEpoch;
        try
        {
            subjectEpoch = subject.Epoch;
        }
        catch (Exception)
        {
            // A subject that cannot say which world it belongs to is treated as
            // belonging to none. Fail closed: custody and authority do.
            return false;
        }

        return subjectEpoch.Matches(Epoch);
    }
}
