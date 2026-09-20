using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What the game says about how much Gunnar can carry, read from the
/// live game and never assumed (#381).
///
/// <b>Why every number comes from the world instead of from a constant.</b> The
/// requirement is that his capacity approximates a vanilla player wearing
/// Megingjord - the game's real carry-weight semantics, not an arbitrary giant
/// inventory. The game computes a player's limit as a base value on the player
/// plus whatever equipped effects add to it, and the belt's contribution lives
/// in an asset, not in code. A constant here would be a number that is right
/// until the game or a mod changes it and then silently wrong, in the direction
/// that gives him a limit no player has. So the adapter reads the base and the
/// bonus, and this type only adds up.
///
/// <b>Defaults refuse.</b> <c>default(CarryFacts)</c> is a limit of zero, which
/// makes every candidate fail the carry clause. An adapter that could not read
/// the game produces an NPC who carries nothing, never one who carries
/// everything.</summary>
internal readonly struct CarryFacts
{
    public CarryFacts(float baseLimitKilograms, float beltBonusKilograms, float carriedKilograms, bool beltEquipped)
    {
        BaseLimitKilograms = Sane(baseLimitKilograms);
        BeltBonusKilograms = Sane(beltBonusKilograms);
        CarriedKilograms = Sane(carriedKilograms);
        BeltEquipped = beltEquipped;
    }

    /// <summary>The base carry limit the game gives a character before any
    /// equipped effect - read from the game, not written down here.</summary>
    public float BaseLimitKilograms { get; }

    /// <summary>What the equipped belt adds, read from the belt's own effect.
    /// Zero when no belt is equipped, and zero when the belt was found but its
    /// effect could not be read, which is the safe direction.</summary>
    public float BeltBonusKilograms { get; }

    /// <summary>What he is carrying right now.</summary>
    public float CarriedKilograms { get; }

    /// <summary>Whether Megingjord is actually on him. Kept separate from the
    /// bonus so a status line can tell "no belt" from "a belt that adds
    /// nothing", which have different fixes.</summary>
    public bool BeltEquipped { get; }

    private static float Sane(float value) => float.IsNaN(value) || value < 0f ? 0f : value;
}

/// <summary>How much more Gunnar can pick up, in kilograms and in units of a
/// given item.</summary>
internal readonly struct CarryBudget
{
    private CarryBudget(float limitKilograms, float carriedKilograms, bool beltEquipped)
    {
        LimitKilograms = limitKilograms;
        CarriedKilograms = carriedKilograms;
        BeltEquipped = beltEquipped;
    }

    public float LimitKilograms { get; }

    public float CarriedKilograms { get; }

    public bool BeltEquipped { get; }

    /// <summary>How much more he can take. Never negative: a character who is
    /// already over the limit has no room, rather than negative room.</summary>
    public float FreeKilograms => LimitKilograms - CarriedKilograms > 0f
        ? LimitKilograms - CarriedKilograms
        : 0f;

    /// <summary>Whether he is over the game's own limit right now. Vanilla lets
    /// this happen (equipment changes, a stack that grew) and answers it by
    /// slowing the character down, not by deleting anything - so this is a fact
    /// to report, not a fault to correct.</summary>
    public bool IsOverloaded => CarriedKilograms > LimitKilograms;

    /// <summary>The budget implied by what the game said, less whatever margin
    /// the limits hold back.</summary>
    public static CarryBudget From(CarryFacts facts, CollectionLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        float limit = facts.BaseLimitKilograms + facts.BeltBonusKilograms - limits.CarrySafetyMarginKilograms;
        return new CarryBudget(limit > 0f ? limit : 0f, facts.CarriedKilograms, facts.BeltEquipped);
    }

    /// <summary>How many units of an item weighing
    /// <paramref name="unitKilograms"/> still fit, never more than
    /// <paramref name="wanted"/>.
    ///
    /// A weightless or unreadable unit weight yields <b>zero</b> rather than
    /// everything: a divisor nobody could read is not permission to fill an
    /// inventory.</summary>
    public int HowManyFit(float unitKilograms, int wanted)
    {
        if (wanted <= 0 || unitKilograms <= 0f || float.IsNaN(unitKilograms) || float.IsInfinity(unitKilograms))
        {
            return 0;
        }

        float free = FreeKilograms;
        if (free <= 0f)
        {
            return 0;
        }

        int fits = (int)Math.Floor(free / unitKilograms);
        return fits < wanted ? (fits < 0 ? 0 : fits) : wanted;
    }

    /// <summary>The same budget with <paramref name="kilograms"/> more on his
    /// back. Used while a batch is being filled, so each candidate is measured
    /// against what the earlier ones already took.</summary>
    public CarryBudget After(float kilograms) =>
        new CarryBudget(LimitKilograms, CarriedKilograms + (kilograms > 0f ? kilograms : 0f), BeltEquipped);

    public override string ToString() =>
        CarriedKilograms.ToString("0.#") + "/" + LimitKilograms.ToString("0.#") + " kg" +
        (BeltEquipped ? " (belt)" : string.Empty);
}

/// <summary>How much room a cart has, as far as this slice needs to know.
///
/// <b>A cart's limit is its slots, not a weight.</b> Vanilla's hand cart holds
/// whatever fits in its container's grid and gets heavier as it fills; there is
/// no weight at which it refuses an item. So capacity here is free slots, and
/// mass is left entirely to the shipped load model, which already advises on it
/// and already refuses to change it. Nothing in this type writes, scales or
/// caps a cart's mass.</summary>
internal readonly struct CartCapacity
{
    public CartCapacity(bool assigned, int freeSlots, int slotStackSize)
    {
        Assigned = assigned;
        FreeSlots = freeSlots > 0 ? freeSlots : 0;
        SlotStackSize = slotStackSize > 0 ? slotStackSize : 0;
    }

    /// <summary>No cart. The default, so a batch planned without one never
    /// counts room that does not exist.</summary>
    public static CartCapacity None => default;

    /// <summary>Whether a cart is assigned to this job at all.</summary>
    public bool Assigned { get; }

    public int FreeSlots { get; }

    /// <summary>How many of the item being collected fit in one slot.</summary>
    public int SlotStackSize { get; }

    /// <summary>How many units the cart can take. Zero without a cart, and zero
    /// when either number could not be read.</summary>
    public int Units => Assigned ? FreeSlots * SlotStackSize : 0;
}
