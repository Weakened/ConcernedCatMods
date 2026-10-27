using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>How many units one load holds, from the game's own numbers (#381).
///
/// <b>Two limits, and the smaller wins.</b> Weight - what a vanilla player
/// wearing Megingjord could carry, less what he already holds that is not going
/// into the chest - and room: his inventory's free slots times the smallest
/// stack any yield comes in. Vanilla does not weigh a non-player character down,
/// so the weight limit is this product's discipline rather than the game's; the
/// slot limit is the game's, because a pick he has no room for fails.
///
/// <b>On foot only.</b> A cart's room is not added: moving material into a cart's
/// container is not one of the authorized calls, and the container permissions
/// refuse a container that moves.
///
/// <b>Every unreadable number is zero.</b> A unit weight, a slot count or a stack
/// size nobody could read gives a load of nothing, so a tour is refused rather
/// than sized on a guess.</summary>
internal static class CollectionCapacity
{
    public static int UnitsPerLoad(
        CarryFacts carry,
        CollectionLimits limits,
        float heaviestUnitKilograms,
        int freeSlots,
        int smallestStack)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (freeSlots <= 0 || smallestStack <= 0)
        {
            return 0;
        }

        CarryBudget budget = CarryBudget.From(carry, limits);
        int byWeight = budget.HowManyFit(heaviestUnitKilograms, int.MaxValue);
        long byRoom = (long)freeSlots * smallestStack;
        long units = byWeight < byRoom ? byWeight : byRoom;
        return units > int.MaxValue ? int.MaxValue : (int)units;
    }
}
