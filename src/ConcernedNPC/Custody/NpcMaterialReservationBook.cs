using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Every material reservation, and the arithmetic that stops two jobs
/// planning on the same units.
///
/// <b>The three guarantees, and how each is made rather than intended.</b>
///
/// <i>Release exactly once.</i> A reservation leaves
/// <see cref="NpcReservationState.Held"/> through one one-way transition, and
/// the book returns <see cref="NpcCustodyOutcome.AlreadySatisfied"/> for a
/// second attempt rather than applying it. So a retry loop that does not know
/// what its last attempt achieved is safe, and a refund cannot be paid twice.
///
/// <i>Restoring twice duplicates nothing.</i> Names are derived from the job
/// and the step, so a job re-stating its reservations after a reload re-states
/// names the book already has. Same name, same payload: nothing happens. Same
/// name, different payload: refused, loudly, because that is a caller defect
/// and answering "already done" to it is how the wrong material ends up
/// recorded as taken.
///
/// <i>Two jobs never plan on the same units.</i>
/// <see cref="AvailableIn"/> subtracts what other jobs already hold from a
/// container before a planner is told what is in it - so the second job plans
/// against what is really free, rather than discovering it halfway through.
///
/// <b>What this book does not do: move anything.</b> A reservation is a claim.
/// The units physically moving is a transfer, through
/// <see cref="NpcTransferExecutor"/>, and the two are kept apart on purpose:
/// the moment a claim could move material, a crash between the claim and the
/// move would have no honest answer.</summary>
internal sealed class NpcMaterialReservationBook
{
    private readonly Dictionary<string, NpcMaterialReservation> _byName =
        new Dictionary<string, NpcMaterialReservation>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();

    internal int Count => _order.Count;

    internal IReadOnlyList<NpcMaterialReservation> Reservations
    {
        get
        {
            var list = new List<NpcMaterialReservation>(_order.Count);
            foreach (string name in _order)
            {
                list.Add(_byName[name]);
            }

            return list;
        }
    }

    internal bool TryGet(ReservationId id, out NpcMaterialReservation reservation)
    {
        reservation = null!;
        return !id.IsEmpty && _byName.TryGetValue(id.Value, out reservation!);
    }

    /// <summary>Records material set aside for a job.</summary>
    internal NpcCustodyOutcome Reserve(NpcMaterialReservation reservation)
    {
        if (reservation == null)
        {
            throw new ArgumentNullException(nameof(reservation));
        }

        if (!reservation.IsWellFormed)
        {
            return NpcCustodyOutcome.Rejected;
        }

        if (_byName.TryGetValue(reservation.Id.Value, out NpcMaterialReservation? recorded))
        {
            if (!recorded!.SamePayloadAs(reservation))
            {
                return NpcCustodyOutcome.RejectedDifferentPayload;
            }

            if (recorded.IsSettled)
            {
                // The same defect the ledger's acquisition half had, in the
                // place nobody looked for it: comparing the payload and not the
                // state made a committed or refunded claim indistinguishable
                // from a live one, so re-stating a spent name was answered
                // "already satisfied" - a success - and set nothing aside. A
                // name is spent once. A re-plan takes a new one.
                return NpcCustodyOutcome.Stale;
            }

            return NpcCustodyOutcome.AlreadySatisfied;
        }

        _byName.Add(reservation.Id.Value, reservation);
        _order.Add(reservation.Id.Value);
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>Spends a reservation on what it was for. Exactly once.
    /// </summary>
    internal NpcCustodyOutcome Commit(ReservationId id) =>
        Settle(id, NpcReservationState.Committed);

    /// <summary>Returns unspent material to the container it came from. Exactly
    /// once.</summary>
    internal NpcCustodyOutcome Refund(ReservationId id) =>
        Settle(id, NpcReservationState.Refunded);

    /// <summary>Marks a reservation whose commit outcome is unknown. Neither
    /// committed nor refundable: this build does not know whether the material
    /// became a wall or is still owed back, and inventing an answer would
    /// either conjure it or lose it.</summary>
    internal NpcCustodyOutcome MarkUncertain(ReservationId id) =>
        Settle(id, NpcReservationState.Uncertain);

    /// <summary>Everything still owed back if this job were cancelled right
    /// now. The verb an interruption uses, and it never needs to know which
    /// steps got as far as reserving.</summary>
    internal IReadOnlyList<NpcMaterialReservation> HeldFor(string? jobId)
    {
        var held = new List<NpcMaterialReservation>();
        foreach (string name in _order)
        {
            NpcMaterialReservation reservation = _byName[name];
            if (reservation.State == NpcReservationState.Held
                && string.Equals(reservation.JobId, jobId ?? string.Empty, StringComparison.Ordinal))
            {
                held.Add(reservation);
            }
        }

        return held;
    }

    /// <summary>Refunds everything a job still holds, and returns how many
    /// refunds this call actually made - not how many it found. A second call
    /// returns zero, which is what "a refund happens exactly once" looks like
    /// from the outside.</summary>
    internal int RefundAllFor(string? jobId)
    {
        int refunded = 0;
        foreach (NpcMaterialReservation reservation in HeldFor(jobId))
        {
            if (Settle(reservation.Id, NpcReservationState.Refunded) == NpcCustodyOutcome.Applied)
            {
                refunded++;
            }
        }

        return refunded;
    }

    /// <summary>True while any reservation is in an unknown state, so a role
    /// must stop and say so rather than carry on.</summary>
    internal bool HasUncertain
    {
        get
        {
            foreach (string name in _order)
            {
                if (_byName[name].State == NpcReservationState.Uncertain)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>How many units of one material are currently set aside out of
    /// one container, by everything except <paramref name="exceptThis"/>.
    ///
    /// <b>The exception is one reservation, not one job, and that is the whole
    /// correction.</b> It used to exclude every reservation belonging to the
    /// asking job, which is right when the question is "does this claim
    /// conflict with itself?" and catastrophic when the question is "how many
    /// units are free?". A job's step 1 would set aside all forty nails; its
    /// step 2 would then ask and be told forty were still free, because its own
    /// hold was excluded, and reserve them again under a different name that
    /// the payload check has no reason to object to. Eighty nails reserved out
    /// of a chest holding forty, both <c>Held</c>, both counted.
    ///
    /// A re-plan that wants to move a claim from one step to another does not
    /// need the old exclusion: it refunds the old name first, which the
    /// one-way latch makes safe and exactly once, and then asks.</summary>
    /// <param name="exceptThis">A reservation to leave out of the sum, so a
    /// step can ask what it could have if it were re-planning its own claim.
    /// <c>default</c> excludes nothing.</param>
    internal int ReservedIn(NpcCustodyLocation container, NpcMaterial material, ReservationId exceptThis)
    {
        int total = 0;
        foreach (string name in _order)
        {
            NpcMaterialReservation reservation = _byName[name];
            if (reservation.State != NpcReservationState.Held
                || !reservation.CameFrom(container)
                || (!exceptThis.IsEmpty && reservation.Id.Equals(exceptThis)))
            {
                continue;
            }

            total += reservation.CountOf(material);
        }

        return total;
    }

    /// <summary>What a planner may count on: what the container actually holds,
    /// less everything already set aside out of it. Never below zero - a
    /// container the player emptied under two live reservations is a
    /// reconciliation finding, not a negative number handed to a planner.
    ///
    /// <b>Null in, null out.</b> <paramref name="actuallyPresent"/> is
    /// nullable for the same reason the observer seam's count is: a chest that
    /// could not be read is not an empty chest. A planner told "zero" plans a
    /// shortfall and sends the player looking for material that is sitting in
    /// an unloaded zone; a planner told "unknown" waits.</summary>
    internal int? AvailableIn(
        NpcCustodyLocation container, NpcMaterial material, int? actuallyPresent, ReservationId exceptThis)
    {
        if (!actuallyPresent.HasValue)
        {
            return null;
        }

        int present = actuallyPresent.Value < 0 ? 0 : actuallyPresent.Value;
        int free = present - ReservedIn(container, material, exceptThis);
        return free < 0 ? 0 : free;
    }

    /// <summary>Total counted per material across reservations in one state.
    /// The conservation tests compare these sums before and after forced
    /// failures.</summary>
    internal IReadOnlyDictionary<string, int> Totals(NpcReservationState state)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in _order)
        {
            NpcMaterialReservation reservation = _byName[name];
            if (reservation.State != state)
            {
                continue;
            }

            foreach (NpcMaterialStack stack in reservation.Stacks)
            {
                totals.TryGetValue(stack.Material.ItemName, out int running);
                totals[stack.Material.ItemName] = running + stack.Count;
            }
        }

        return totals;
    }

    private NpcCustodyOutcome Settle(ReservationId id, NpcReservationState settled)
    {
        if (!TryGet(id, out NpcMaterialReservation reservation))
        {
            return NpcCustodyOutcome.Rejected;
        }

        if (reservation.State == settled)
        {
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        return reservation.TrySettle(settled)
            ? NpcCustodyOutcome.Applied
            : NpcCustodyOutcome.Rejected;
    }
}
