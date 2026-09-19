using System;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>How much an NPC can take with it in one go, in the role's own units.
///
/// <b>Units, not weight, and the choice is deliberate.</b> Weight is the host's
/// arithmetic and it needs item data this library must never learn. A role that
/// cares about weight converts its own carry limit into a number of units before
/// it gets here, because only the role knows what one of its units weighs. What
/// this library needs is the one thing weight and slots and stack sizes all
/// reduce to: how many more can he take.
///
/// <b>Zero is not unlimited.</b> A defaulted capacity carries nothing, which
/// fails closed exactly as every other default in this package does. Unlimited
/// is a value somebody chose.</summary>
internal readonly struct NpcCarryCapacity
{
    internal NpcCarryCapacity(int unitsPerTour)
    {
        UnitsPerTour = unitsPerTour < 0 ? 0 : unitsPerTour;
    }

    /// <summary>How many units fit in one trip.</summary>
    internal int UnitsPerTour { get; }

    /// <summary>A capacity nothing exceeds - for a job whose targets consume
    /// nothing, or a role that has already bounded the batch another way.
    /// </summary>
    internal static NpcCarryCapacity Unlimited => new NpcCarryCapacity(int.MaxValue);

    internal bool IsUnlimited => UnitsPerTour == int.MaxValue;

    /// <summary>Whether a whole manifest goes in one trip.</summary>
    internal bool Fits(JobManifest manifest) => manifest.TotalUnits <= UnitsPerTour;

    /// <summary>Whether <paramref name="units"/> more fit on top of
    /// <paramref name="carried"/>.</summary>
    internal bool RoomFor(int carried, int units)
    {
        if (units <= 0)
        {
            return true;
        }

        if (IsUnlimited)
        {
            return true;
        }

        long total = (long)carried + units;
        return total <= UnitsPerTour;
    }

    /// <summary>The fewest trips a total of this many units can be carried in.
    /// <b>The number a plan is judged against</b>: a partitioner that produced
    /// more tours than this turned a batched job into a one-target-at-a-time
    /// loop, which is the failure the whole pipeline exists to prevent, and a
    /// test says so.</summary>
    internal int FewestToursFor(int units)
    {
        if (units <= 0)
        {
            return 0;
        }

        if (IsUnlimited || UnitsPerTour >= units)
        {
            return 1;
        }

        if (UnitsPerTour == 0)
        {
            return 0;
        }

        return ((units - 1) / UnitsPerTour) + 1;
    }

    public override string ToString() => IsUnlimited ? "unlimited" : UnitsPerTour + " units a trip";
}
