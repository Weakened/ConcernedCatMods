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
    /// <summary>Nobody asked, and nothing is held.
    ///
    /// Two things land here, deliberately. A defaulted value, so a caller that
    /// forgets to look at the answer has not accidentally been granted
    /// anything. And a call that named nothing to reserve - a null subject, an
    /// empty reservation name - which is a defect in the caller rather than a
    /// fact about the world, and therefore has no reason worth reporting to a
    /// player. The book answers rather than throwing because a reservation is
    /// taken inside a job's tick, and a throw there costs the job its state to
    /// tell a programmer something a test should have told them.</summary>
    Unspecified = 0,

    /// <summary>Taken. This holder has it until it releases it.</summary>
    Reserved = 1,

    /// <summary>This <b>job</b> already has this subject - under this
    /// reservation's name, or under another of its own. <b>What a retry looks
    /// like</b>, and the reason a job resumed after a reload can re-reserve
    /// every step it thought it held without taking anything twice, <i>and</i>
    /// the reason a re-plan that moves a subject from step 3 to step 2 is not
    /// refused by the job itself.</summary>
    AlreadySatisfied = 2,

    /// <summary>Another <b>job</b> has it. Refused, never taken over: an NPC
    /// that could evict another's reservation is an NPC that can take material
    /// out from under a job in flight.</summary>
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
/// <i>One job per subject.</i> A second job is refused, never queued and never
/// preferred. Two NPCs walking to the same tree is a wasted trip; two NPCs
/// withdrawing the same forty nails is a player's chest emptied twice over.
///
/// <i>A stale epoch resolves nothing.</i> Every subject is named within one
/// world load and says so - that is what <see cref="INpcEpochScoped"/> is for,
/// and it is a constraint rather than a convention precisely so this guarantee
/// is reachable: a book whose subject could not be asked its epoch could never
/// produce <see cref="ReservationOutcome.StaleEpoch"/>, and the promise would be
/// decorative. A reservation over a subject from another load is refused rather
/// than matched, because the name now points at a different object, and a book
/// whose own epoch is unknown refuses everything, which is the correct behaviour
/// before a world is loaded.
///
/// <i>Reserving twice is satisfied, not refused.</i> Idempotence is not a
/// convenience here - it is what lets a job resumed after an interruption
/// re-establish its holds by walking its plan again, without a separate "did I
/// already?" record that could itself be wrong. It is decided on the job half of
/// the reservation's name, so a re-plan that renumbers steps does not make a job
/// conflict with itself.
///
/// <i>Releasing what you do not hold is an answer.</i> So recovery can release
/// everything unconditionally without first proving what it had.
///
/// <b>What it is not: durable.</b> Nothing here is written to disk. A world load
/// begins a new book, every reservation from the previous one is gone, and the
/// job re-establishes its holds from its plan - which it can, because the names
/// are derived rather than allocated. Persisting reservations would mean
/// persisting the names of world objects, and those names do not survive a
/// load.</summary>
/// <typeparam name="TSubject">What is reserved - a source, a container, a place.
/// The book interprets it in exactly two ways, equality and
/// <see cref="INpcEpochScoped.Epoch"/>, so it stays this library's business that
/// something is held and the role's business what it is.</typeparam>
internal interface IReservationBook<TSubject>
    where TSubject : INpcEpochScoped
{
    /// <summary>The world load every subject in this book is named within.</summary>
    NpcWorldEpoch Epoch { get; }

    /// <summary>How many reservations are held.</summary>
    int Count { get; }

    /// <summary>Takes <paramref name="subject"/> under the name
    /// <paramref name="reservation"/>, or says which job has it and why not.
    /// Refuses a subject from another world load with
    /// <see cref="ReservationOutcome.StaleEpoch"/>.</summary>
    ReservationOutcome Reserve(TSubject subject, ReservationId reservation);

    /// <summary>Gives it up. Answers <see cref="ReservationOutcome.NotHeld"/>
    /// rather than failing when this reservation's job does not have it.
    /// </summary>
    ReservationOutcome Release(TSubject subject, ReservationId reservation);

    /// <summary>Releases everything held by one job - every step at once - and
    /// returns how many. The verb an interruption uses; it never needs to know
    /// which steps had got as far as reserving.</summary>
    int ReleaseAllFor(string jobId);

    /// <summary>Whether this reservation's job has this subject right now,
    /// under this name or any other of its own.</summary>
    bool IsHeldBy(TSubject subject, ReservationId reservation);

    /// <summary>The reservation currently holding this subject, if any. For the
    /// evidence line that names which job and which step, which is the only
    /// reason the step is carried at all.</summary>
    bool TryGetHolder(TSubject subject, out ReservationId reservation);
}
