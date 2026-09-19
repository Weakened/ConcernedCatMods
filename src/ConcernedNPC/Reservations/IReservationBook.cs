using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Reservations;

/// <summary>What happened to a reservation.
///
/// Every value is modelled on the shipped source-reservation book, which is the
/// best of the three reservation implementations in this repository: it refuses
/// a stale epoch by construction, answers a repeat of the same reservation as
/// satisfied rather than as a conflict, and treats releasing something you do
/// not hold as an answer rather than an error.</summary>
internal enum ReservationOutcome
{
    /// <summary>Nobody asked. Never a hold.</summary>
    Unspecified = 0,

    /// <summary>Taken. This holder has it until it releases it.</summary>
    Reserved = 1,

    /// <summary>This holder already has exactly this. <b>What a retry looks
    /// like</b>, and the reason a job resumed after a reload can re-reserve
    /// every step it thought it held without taking anything twice.</summary>
    AlreadySatisfied = 2,

    /// <summary>Somebody else has it. Refused, never taken over: an NPC that
    /// could evict another's reservation is an NPC that can take material out
    /// from under a job in flight.</summary>
    HeldByAnother = 3,

    /// <summary>The subject was named in a different world load, so the name no
    /// longer points at anything knowable. Refused, not resolved.</summary>
    StaleEpoch = 4,

    /// <summary>Given up.</summary>
    Released = 5,

    /// <summary>Nothing to release: not reserved, or reserved by somebody else.
    /// <b>Not an error.</b> Cleanup after a reload, a death or a failure
    /// releases unconditionally, so this has to be an ordinary answer - which is
    /// how every release in every shipped product already behaves.</summary>
    NotHeld = 6,
}

/// <summary>Who currently has the right to act on what, within one world load.
///
/// <b>What it guarantees.</b> Four things.
///
/// <i>One holder per subject.</i> A second holder is refused, never queued and
/// never preferred. Two NPCs walking to the same tree is a wasted trip; two NPCs
/// withdrawing the same forty nails is a player's chest emptied twice over.
///
/// <i>A stale epoch resolves nothing.</i> Every subject is named within one
/// world load. A reservation taken in a previous load is refused rather than
/// matched, because the name now points at a different object. A book whose own
/// epoch is unknown refuses everything, which is the correct behaviour before a
/// world is loaded.
///
/// <i>Reserving twice is satisfied, not refused.</i> Idempotence is not a
/// convenience here - it is what lets a job resumed after an interruption
/// re-establish its holds by walking its plan again, without a separate "did I
/// already?" record that could itself be wrong.
///
/// <i>Releasing what you do not hold is an answer.</i> So recovery can release
/// everything unconditionally without first proving what it had.
///
/// <b>What it is not: durable.</b> Nothing here is written to disk. A world load
/// begins a new book, every reservation from the previous one is gone, and the
/// job re-establishes its holds from its plan - which it can, because the ids
/// are derived rather than allocated. Persisting reservations would mean
/// persisting the names of world objects, and those names do not survive a
/// load.</summary>
/// <typeparam name="TSubject">What is reserved - a source, a container, a place.
/// The book never interprets it beyond equality, so it stays this library's
/// business that something is held and the role's business what it is.
/// </typeparam>
internal interface IReservationBook<TSubject>
{
    /// <summary>The world load every subject in this book is named within.</summary>
    NpcWorldEpoch Epoch { get; }

    /// <summary>How many reservations are held.</summary>
    int Count { get; }

    /// <summary>Takes <paramref name="subject"/> for <paramref name="holder"/>,
    /// or says who has it and why not.</summary>
    ReservationOutcome Reserve(TSubject subject, ReservationId holder);

    /// <summary>Gives it up. Answers <see cref="ReservationOutcome.NotHeld"/>
    /// rather than failing when this holder does not have it.</summary>
    ReservationOutcome Release(TSubject subject, ReservationId holder);

    /// <summary>Releases everything held by one job - every step's id at once -
    /// and returns how many. The verb an interruption uses; it never needs to
    /// know which steps had got as far as reserving.</summary>
    int ReleaseAllFor(string jobId);

    /// <summary>Whether this holder has this subject right now.</summary>
    bool IsHeldBy(TSubject subject, ReservationId holder);
}
