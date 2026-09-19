using System;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>How much of a container's contents this job may actually plan on.
///
/// <b>Why a planner may not just count what is in the chest.</b> Chest C holds
/// a hundred wood. Job A plans a hundred-wood wall against it; a tick later job
/// B plans a sixty-wood fence against the same observation, because the
/// observation still says a hundred. Both plans are perfectly formed and
/// between them they plan a hundred and sixty wood out of a chest holding a
/// hundred. Custody's measured deltas stop the second one <i>minting</i>
/// anything, so the cost is a stall and a wasted walk rather than duplicated
/// material - but a stall on every tick, on a job the player can see is doable,
/// is exactly what an NPC gets no credit for surviving.
///
/// So a planner asks this instead: what is in there, less what somebody else has
/// already set aside.
///
/// <b>The asking job is baked in, not passed.</b> A selector that carried a job
/// id could pass the wrong one, and the wrong one is not a crash - it is a plan
/// that quietly counts another job's reserved units as free. The implementation
/// knows whose it is, and the selector cannot get it wrong because it never
/// holds it.
///
/// <b>Null is the honest default, not a safe one.</b> A pipeline built without
/// an availability is a pipeline that plans against raw counts, which is the
/// behaviour above. It is allowed because the first roles will land before their
/// reservation books do, and it is the caller's decision rather than a silent
/// one.</summary>
internal interface INpcSourceAvailability
{
    /// <summary>How many units of <paramref name="item"/> in the container named
    /// <paramref name="containerKey"/> this job may count on, given that
    /// <paramref name="observed"/> were seen in it.
    ///
    /// Never more than <paramref name="observed"/> and never below zero. An
    /// implementation that cannot tell answers <paramref name="observed"/> -
    /// failing towards a plan that may stall rather than towards one that
    /// under-provisions, because a stall is visible and an under-provisioned
    /// round is an NPC that walks away from half a wall.</summary>
    int AvailableIn(string containerKey, string item, int observed);
}

/// <summary>The shipped <see cref="INpcSourceAvailability"/>: what a material
/// reservation book says is still free in a container.
///
/// <b>It matches on the item's name alone, and that is deliberate.</b> A
/// reservation is over an <see cref="NpcMaterial"/>, which carries a quality and
/// a variant; a planner's item token is a bare string the role chose, with no
/// way to say which quality it meant. Matching name-only therefore subtracts
/// <i>every</i> quality of that name, which can only ever report <b>less</b>
/// available than a quality-exact match would. That is the safe direction: the
/// worst case is a job that waits for material it could have had, and the
/// alternative's worst case is two jobs planning the same units, which is the
/// failure this type exists to prevent.
///
/// It is also why this does not call the book's own <c>AvailableIn</c>, which
/// takes a material and would need a quality this layer cannot supply. The
/// arithmetic is the same arithmetic; only the match is wider - and keeping it
/// here means the book's signature can change without a planner noticing, which
/// it has already done once.
///
/// <b>It excludes one reservation, never one job, and the difference is a
/// defect somebody already found.</b> The obvious rule - "ignore everything my
/// own job holds" - is wrong for an arithmetic of quantities: one job's two
/// steps each see forty free nails in a chest holding forty, and between them
/// set aside eighty. So the exclusion is a single name, for the one case that
/// needs it: a re-plan re-stating a claim it already owns. A planner building a
/// fresh plan excludes nothing and sees every held unit, its own job's
/// included, which is the conservative and correct answer.</summary>
internal sealed class NpcReservedSourceAvailability : INpcSourceAvailability
{
    private readonly NpcMaterialReservationBook _book;
    private readonly ReservationId _exceptThis;

    /// <summary>Reads a book.</summary>
    /// <param name="book">Where the claims live.</param>
    /// <param name="exceptThis">The one reservation to look past, for a re-plan
    /// re-stating a claim it already owns. Default excludes nothing, which is
    /// what a fresh plan wants.</param>
    internal NpcReservedSourceAvailability(NpcMaterialReservationBook book, ReservationId exceptThis = default)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _exceptThis = exceptThis;
    }

    /// <inheritdoc />
    public int AvailableIn(string containerKey, string item, int observed)
    {
        if (observed <= 0 || string.IsNullOrEmpty(containerKey) || string.IsNullOrEmpty(item))
        {
            return observed < 0 ? 0 : observed;
        }

        int reserved = 0;
        foreach (NpcMaterialReservation reservation in _book.Reservations)
        {
            if (reservation.State != NpcReservationState.Held
                || !string.Equals(reservation.Container.Key, containerKey, StringComparison.Ordinal)
                || Excluded(reservation))
            {
                continue;
            }

            foreach (NpcMaterialStack stack in reservation.Stacks)
            {
                if (string.Equals(stack.Material.ItemName, item, StringComparison.Ordinal))
                {
                    reserved += stack.Count;
                }
            }
        }

        int free = observed - reserved;
        return free < 0 ? 0 : free;
    }

    private bool Excluded(NpcMaterialReservation reservation) =>
        !_exceptThis.IsEmpty
        && string.Equals(reservation.Id.Value, _exceptThis.Value, StringComparison.Ordinal);
}
