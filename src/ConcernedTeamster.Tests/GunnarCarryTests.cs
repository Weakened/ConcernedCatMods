using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>How much Gunnar can carry: the game's own carry-weight semantics,
/// not a giant inventory (#381).
///
/// <b>What these tests deliberately do not pin.</b> Any particular number of
/// kilograms. The base limit and the belt's contribution are read from the live
/// game - one is a field on the player, the other lives in the belt's own
/// effect - and a test that wrote either of them down would be asserting that
/// this product knows better than the game. What is pinned is the arithmetic
/// and, much more importantly, the direction every failure goes in.</summary>
public sealed class GunnarCarryTests
{
    private static readonly CollectionLimits Limits = CollectionLimits.Default;

    [Fact]
    public void His_limit_is_what_the_game_gives_a_player_plus_what_the_belt_adds()
    {
        var facts = new CarryFacts(baseLimitKilograms: 300f, beltBonusKilograms: 150f, carriedKilograms: 20f,
            beltEquipped: true);

        CarryBudget budget = CarryBudget.From(facts, Limits);

        Assert.Equal(450f, budget.LimitKilograms);
        Assert.Equal(430f, budget.FreeKilograms);
        Assert.True(budget.BeltEquipped);
    }

    [Fact]
    public void Without_the_belt_he_carries_what_any_other_body_carries()
    {
        var facts = new CarryFacts(300f, 0f, 0f, beltEquipped: false);

        CarryBudget budget = CarryBudget.From(facts, Limits);

        Assert.Equal(300f, budget.LimitKilograms);
        Assert.False(budget.BeltEquipped);
    }

    [Fact]
    public void A_reading_that_failed_gives_him_no_room_at_all()
    {
        // The whole point of the direction. An adapter that could not read the
        // game produces an NPC who picks up nothing, and a player who asks why
        // - never an NPC with an unbounded inventory and a player who finds out
        // later.
        CarryBudget budget = CarryBudget.From(default, Limits);

        Assert.Equal(0f, budget.LimitKilograms);
        Assert.Equal(0f, budget.FreeKilograms);
        Assert.Equal(0, budget.HowManyFit(2f, 50));
    }

    [Fact]
    public void Nonsense_readings_are_treated_as_nothing_rather_than_as_infinity()
    {
        var facts = new CarryFacts(float.NaN, -50f, float.NaN, beltEquipped: true);

        CarryBudget budget = CarryBudget.From(facts, Limits);

        Assert.Equal(0f, budget.LimitKilograms);
    }

    [Theory]
    [InlineData(100f, 2f, 100, 50)]
    [InlineData(100f, 2f, 10, 10)]
    [InlineData(1f, 2f, 10, 0)]
    [InlineData(100f, 0.3f, 1000, 333)]
    public void Only_whole_units_that_actually_fit_are_counted(
        float free, float unit, int wanted, int expected)
    {
        var budget = CarryBudget.From(new CarryFacts(free, 0f, 0f, false), Limits);

        Assert.Equal(expected, budget.HowManyFit(unit, wanted));
    }

    [Fact]
    public void An_unreadable_unit_weight_fits_nothing_rather_than_everything()
    {
        var budget = CarryBudget.From(new CarryFacts(450f, 0f, 0f, true), Limits);

        Assert.Equal(0, budget.HowManyFit(0f, 100));
        Assert.Equal(0, budget.HowManyFit(float.NaN, 100));
        Assert.Equal(0, budget.HowManyFit(float.PositiveInfinity, 100));
        Assert.Equal(0, budget.HowManyFit(-1f, 100));
    }

    [Fact]
    public void Already_over_the_limit_is_a_fact_to_report_and_not_a_fault_to_correct()
    {
        // Vanilla lets a character go over - equipment comes off, a stack grows
        // - and answers it by slowing them down, not by deleting anything. So
        // does this: no room, and nothing is thrown away to make some.
        var budget = CarryBudget.From(new CarryFacts(300f, 0f, 340f, false), Limits);

        Assert.True(budget.IsOverloaded);
        Assert.Equal(0f, budget.FreeKilograms);
        Assert.Equal(0, budget.HowManyFit(1f, 1));
    }

    [Fact]
    public void Each_thing_he_picks_up_is_measured_against_what_the_last_one_took()
    {
        CarryBudget budget = CarryBudget.From(new CarryFacts(10f, 0f, 0f, false), Limits);

        Assert.Equal(5, budget.HowManyFit(2f, 5));
        budget = budget.After(8f);
        Assert.Equal(1, budget.HowManyFit(2f, 5));
        budget = budget.After(2f);
        Assert.Equal(0, budget.HowManyFit(2f, 5));
    }

    [Fact]
    public void A_cart_with_no_assignment_has_no_room()
    {
        Assert.Equal(0, CartCapacity.None.Units);
        Assert.False(CartCapacity.None.Assigned);
        Assert.Equal(0, new CartCapacity(assigned: false, freeSlots: 18, slotStackSize: 50).Units);
    }

    [Fact]
    public void A_carts_room_is_its_slots_and_never_a_weight()
    {
        // Vanilla's hand cart holds what fits in its grid and gets heavier as
        // it fills; there is no weight at which it refuses an item, and nothing
        // in this product writes, scales or caps a cart's mass.
        var cart = new CartCapacity(assigned: true, freeSlots: 4, slotStackSize: 50);

        Assert.Equal(200, cart.Units);
    }

    [Fact]
    public void A_cart_whose_numbers_could_not_be_read_has_no_room()
    {
        Assert.Equal(0, new CartCapacity(assigned: true, freeSlots: 0, slotStackSize: 50).Units);
        Assert.Equal(0, new CartCapacity(assigned: true, freeSlots: 4, slotStackSize: 0).Units);
        Assert.Equal(0, new CartCapacity(assigned: true, freeSlots: -4, slotStackSize: -50).Units);
    }
}
