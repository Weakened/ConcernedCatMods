using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Material set aside from one container for one step of one job.
///
/// <b>What this is for, as distinct from a subject reservation.</b>
/// <see cref="NpcReservationBook{TSubject}"/> stops two jobs walking to the
/// same chest. This stops two jobs <i>assuming the same forty nails inside
/// it</i> - which is a different failure with a worse ending: both plans look
/// feasible, both start, and the second one discovers halfway through that its
/// material is gone.
///
/// <b>Moved rather than invented.</b> This is the shipped settlement
/// reservation, with its payload-checked idempotence and its one-way settle,
/// re-keyed onto the derived reservation name and freed of the two material
/// names it happened to be written around.
///
/// <b>Refunds go back to exactly the container it came from</b>, in the same
/// world load - never to whatever is nearest at the time. A container key alone
/// cannot say which chest a reservation came from once a world has been
/// reloaded, so the epoch travels with it and a refund across a load is refused
/// rather than sent somewhere plausible.</summary>
internal sealed class NpcMaterialReservation
{
    private readonly List<NpcMaterialStack> _stacks = new List<NpcMaterialStack>();

    internal NpcMaterialReservation(
        ReservationId id, NpcCustodyLocation container, IEnumerable<NpcMaterialStack>? stacks)
    {
        Id = id;
        Container = container;

        if (stacks != null)
        {
            foreach (NpcMaterialStack stack in stacks)
            {
                if (stack.IsValid)
                {
                    _stacks.Add(stack);
                }
            }
        }
    }

    /// <summary>The reservation's stable name: <c>job#step</c>, the same after
    /// a restart, which is what lets a resumed job ask whether it already has
    /// this material instead of taking it again.</summary>
    internal ReservationId Id { get; }

    internal string JobId => Id.JobId;

    /// <summary>Which container this came out of, in which world load.
    /// </summary>
    internal NpcCustodyLocation Container { get; }

    internal IReadOnlyList<NpcMaterialStack> Stacks => _stacks;

    internal NpcReservationState State { get; private set; } = NpcReservationState.Held;

    internal bool IsSettled => State != NpcReservationState.Held;

    /// <summary>A reservation with nothing in it is not a reservation, and one
    /// with no name or no container cannot be refunded to anywhere.</summary>
    internal bool IsWellFormed =>
        !Id.IsEmpty && Container.IsSpecified && !Container.Epoch.IsUnknown && _stacks.Count > 0;

    /// <summary>How many units of one material this reservation holds.
    /// </summary>
    internal int CountOf(NpcMaterial material)
    {
        int total = 0;
        foreach (NpcMaterialStack stack in _stacks)
        {
            if (stack.Material.Equals(material))
            {
                total += stack.Count;
            }
        }

        return total;
    }

    /// <summary>Whether this came from exactly the container named, in the same
    /// world load.</summary>
    internal bool CameFrom(NpcCustodyLocation container) => Container.Equals(container);

    /// <summary>The same reservation: the same job, the same container in the
    /// same load, and exactly the same stacks in the same order. What
    /// idempotence is checked against, so a reused name carrying a different
    /// payload is caught rather than silently answered as done.</summary>
    internal bool SamePayloadAs(NpcMaterialReservation? other)
    {
        if (other == null
            || !string.Equals(JobId, other.JobId, StringComparison.Ordinal)
            || !Container.Equals(other.Container)
            || _stacks.Count != other._stacks.Count)
        {
            return false;
        }

        for (int index = 0; index < _stacks.Count; index++)
        {
            if (!_stacks[index].Equals(other._stacks[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The one-way door. Returns true only for the transition that
    /// actually happened here, so a caller can count settlements and know that
    /// each one happened once.</summary>
    internal bool TrySettle(NpcReservationState settled)
    {
        if (settled == NpcReservationState.Held || State != NpcReservationState.Held)
        {
            // Already settled - the same way or another way. Changing it would
            // be exactly the double spend this type exists to prevent.
            return false;
        }

        State = settled;
        return true;
    }

    public override string ToString() => Id + " " + State + " from " + Container;
}
