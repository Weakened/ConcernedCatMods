using System;
using System.Collections.Generic;
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
    internal NpcReservationBook(NpcWorldEpoch epoch)
        : this(epoch, null)
    {
    }

    /// <param name="comparer">How subjects are compared. Supplied when the
    /// subject is a reference type whose equality is not the right one - a
    /// container read fresh each tick, for instance, where identity is the key
    /// rather than the object.</param>
    internal NpcReservationBook(NpcWorldEpoch epoch, IEqualityComparer<TSubject>? comparer)
    {
        Epoch = epoch;
        _held = comparer == null
            ? new Dictionary<TSubject, ReservationId>()
            : new Dictionary<TSubject, ReservationId>(comparer);
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
