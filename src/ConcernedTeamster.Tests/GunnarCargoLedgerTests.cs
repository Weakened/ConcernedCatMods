using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Material is conserved, retries record nothing twice, and a destroyed
/// cart invents nothing (#381).</summary>
public sealed class GunnarCargoLedgerTests
{
    [Fact]
    public void Taking_is_the_only_thing_that_raises_the_total()
    {
        var ledger = new CargoLedger();

        Assert.Equal(CargoOutcome.Recorded, ledger.Take("r1#0:take", "Stone", 5));
        Assert.Equal(5, ledger.Acquired);
        Assert.Equal(5, ledger.At("Stone", CargoPlace.Carried));
        Assert.True(ledger.IsConserved);

        Assert.Equal(CargoOutcome.Recorded,
            ledger.Move("r1#0:stow", "Stone", 5, CargoPlace.Carried, CargoPlace.InCart));
        Assert.Equal(5, ledger.Acquired);
        Assert.True(ledger.IsConserved);
    }

    [Fact]
    public void Conservation_holds_after_every_single_step()
    {
        var ledger = new CargoLedger();

        ledger.Take("t1", "Stone", 10);
        Assert.True(ledger.IsConserved);
        ledger.Move("m1", "Stone", 4, CargoPlace.Carried, CargoPlace.InCart);
        Assert.True(ledger.IsConserved);
        ledger.Move("m2", "Stone", 6, CargoPlace.Carried, CargoPlace.Delivered);
        Assert.True(ledger.IsConserved);
        ledger.Move("m3", "Stone", 4, CargoPlace.InCart, CargoPlace.Lost);
        Assert.True(ledger.IsConserved);
        Assert.Equal(10, ledger.TotalEverywhere);
    }

    [Fact]
    public void A_retry_over_the_same_step_records_nothing_twice()
    {
        var ledger = new CargoLedger();

        Assert.Equal(CargoOutcome.Recorded, ledger.Take("r1#0:take", "Stone", 5));
        Assert.Equal(CargoOutcome.AlreadyRecorded, ledger.Take("r1#0:take", "Stone", 5));
        Assert.Equal(CargoOutcome.AlreadyRecorded, ledger.Take("r1#0:take", "Stone", 5));

        Assert.Equal(5, ledger.Acquired);
    }

    [Fact]
    public void A_step_name_used_for_something_else_is_refused_and_the_first_payload_is_kept()
    {
        // The failure this guards is precise: forty stone out of a chest,
        // answered as "already recorded" on the strength of a partial match,
        // recorded nowhere, and the conservation check true because nothing was
        // written down to conserve.
        var ledger = new CargoLedger();
        ledger.Take("r1#0:take", "Stone", 40);

        Assert.Equal(CargoOutcome.NameReused, ledger.Take("r1#0:take", "Stone", 12));
        Assert.Equal(CargoOutcome.NameReused, ledger.Take("r1#0:take", "Wood", 40));
        Assert.Equal(40, ledger.AcquiredOf("Stone"));
        Assert.Equal(0, ledger.AcquiredOf("Wood"));
    }

    [Fact]
    public void A_move_of_more_than_is_there_is_refused_rather_than_clamped()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 3);

        Assert.Equal(CargoOutcome.NotThatMuchThere,
            ledger.Move("m", "Stone", 4, CargoPlace.Carried, CargoPlace.Delivered));
        Assert.Equal(3, ledger.At("Stone", CargoPlace.Carried));
        Assert.True(ledger.IsConserved);
    }

    [Fact]
    public void A_destroyed_cart_puts_what_it_held_on_the_ground_and_replaces_nothing()
    {
        var ledger = new CargoLedger();
        ledger.Take("t1", "Stone", 30);
        ledger.Take("t2", "Wood", 20);
        ledger.Move("s1", "Stone", 30, CargoPlace.Carried, CargoPlace.InCart);
        ledger.Move("s2", "Wood", 12, CargoPlace.Carried, CargoPlace.InCart);

        Assert.Equal(CargoOutcome.Recorded, ledger.CartDestroyed("boom"));

        Assert.Equal(0, ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(30, ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.Equal(12, ledger.At("Wood", CargoPlace.SpilledFromCart));
        Assert.Equal(8, ledger.At("Wood", CargoPlace.Carried));
        Assert.Equal(50, ledger.Acquired);
        Assert.Equal(50, ledger.TotalEverywhere);
        Assert.True(ledger.IsConserved);
    }

    [Fact]
    public void A_cart_destroyed_twice_is_still_one_cart()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 10);
        ledger.Move("s", "Stone", 10, CargoPlace.Carried, CargoPlace.InCart);

        Assert.Equal(CargoOutcome.Recorded, ledger.CartDestroyed("boom"));
        Assert.Equal(CargoOutcome.AlreadyRecorded, ledger.CartDestroyed("boom"));
        Assert.Equal(10, ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.True(ledger.IsConserved);
    }

    [Fact]
    public void An_empty_cart_being_destroyed_is_recorded_so_a_retry_is_answered()
    {
        var ledger = new CargoLedger();

        Assert.Equal(CargoOutcome.Recorded, ledger.CartDestroyed("boom"));
        Assert.Equal(CargoOutcome.AlreadyRecorded, ledger.CartDestroyed("boom"));
    }

    [Fact]
    public void Malformed_requests_change_nothing()
    {
        var ledger = new CargoLedger();

        Assert.Equal(CargoOutcome.Malformed, ledger.Take(string.Empty, "Stone", 1));
        Assert.Equal(CargoOutcome.Malformed, ledger.Take("t", string.Empty, 1));
        Assert.Equal(CargoOutcome.Malformed, ledger.Take("t", "Stone", 0));
        Assert.Equal(CargoOutcome.Malformed, ledger.Take("t", "Stone", -4));
        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("m", "Stone", 1, CargoPlace.Carried, CargoPlace.Carried));
        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("m", "Stone", 1, CargoPlace.Unspecified, CargoPlace.Delivered));
        Assert.Equal(0, ledger.Acquired);
        Assert.Equal(0, ledger.TotalEverywhere);
    }

    [Fact]
    public void What_he_is_holding_comes_back_in_the_same_order_every_time()
    {
        var ledger = new CargoLedger();
        ledger.Take("t1", "Wood", 3);
        ledger.Take("t2", "Stone", 4);
        ledger.Take("t3", "Flint", 1);

        var held = ledger.Holding(CargoPlace.Carried);

        Assert.Equal(3, held.Count);
        Assert.Equal("Flint", held[0].Key);
        Assert.Equal("Stone", held[1].Key);
        Assert.Equal("Wood", held[2].Key);
    }

    [Fact]
    public void A_place_that_empties_is_gone_rather_than_a_zero_nobody_asked_for()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 2);
        ledger.Move("m", "Stone", 2, CargoPlace.Carried, CargoPlace.Delivered);

        Assert.Empty(ledger.Holding(CargoPlace.Carried));
        Assert.Equal(0, ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(2, ledger.At("Stone", CargoPlace.Delivered));
    }

    // ------------------------------------------------------------------
    // #376: the invariant is Concerned NPC's, and Gunnar's names stay one
    // ------------------------------------------------------------------

    [Fact]
    public void Every_unit_lives_in_the_library_ledger_and_balances_there()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 10);
        ledger.Move("stow", "Stone", 4, CargoPlace.Carried, CargoPlace.InCart);
        ledger.Move("give", "Stone", 3, CargoPlace.Carried, CargoPlace.Delivered);
        ledger.CartDestroyed("boom");

        NpcCustodyLedger custody = ledger.Custody;
        NpcMaterial stone = NpcMaterial.Of("Stone");
        Assert.True(custody.IsConserved("gunnar-collection", stone));
        Assert.Equal(10, custody.Acquired("gunnar-collection", stone));
        Assert.Equal(10, custody.TotalEverywhere("gunnar-collection", stone));

        // Where the library holds them is where Gunnar says they are: three on
        // his back, three delivered, four released with the cart.
        var places = custody.Holdings
            .Where(holding => holding.Material.Equals(stone))
            .ToDictionary(holding => holding.Location.Place, holding => holding.Count);
        Assert.Equal(3, places[NpcCustodyPlace.Carried]);
        Assert.Equal(3, places[NpcCustodyPlace.Delivered]);
        Assert.Equal(4, places[NpcCustodyPlace.Released]);
        Assert.False(places.ContainsKey(NpcCustodyPlace.Stored));
    }

    [Fact]
    public void A_spill_and_a_death_drop_are_released_rather_than_lost_and_stay_apart()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 6);
        ledger.Move("stow", "Stone", 2, CargoPlace.Carried, CargoPlace.InCart);
        ledger.CartDestroyed("boom");
        ledger.Move("died", "Stone", 3, CargoPlace.Carried, CargoPlace.DroppedOnDeath);
        ledger.Move("held", "Stone", 1, CargoPlace.Carried, CargoPlace.UnresolvedAtDeath);

        Assert.Equal(2, ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.Equal(3, ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.Equal(1, ledger.At("Stone", CargoPlace.UnresolvedAtDeath));
        Assert.Equal(0, ledger.At("Stone", CargoPlace.Lost));
        Assert.True(ledger.IsConserved);

        int released = ledger.Custody.Holdings
            .Where(holding => holding.Location.Place == NpcCustodyPlace.Released)
            .Sum(holding => holding.Count);
        int lost = ledger.Custody.Holdings
            .Where(holding => holding.Location.Place == NpcCustodyPlace.Lost)
            .Sum(holding => holding.Count);
        Assert.Equal(5, released);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void A_name_means_one_thing_across_takes_moves_and_spills()
    {
        var ledger = new CargoLedger();
        Assert.Equal(CargoOutcome.Recorded, ledger.Take("a", "Stone", 5));
        Assert.Equal(CargoOutcome.NameReused, ledger.Move("a", "Stone", 5, CargoPlace.Carried, CargoPlace.InCart));
        Assert.Equal(CargoOutcome.NameReused, ledger.CartDestroyed("a"));

        Assert.Equal(CargoOutcome.Recorded, ledger.Move("b", "Stone", 2, CargoPlace.Carried, CargoPlace.InCart));
        Assert.Equal(CargoOutcome.NameReused, ledger.Take("b", "Stone", 2));
        Assert.Equal(CargoOutcome.NameReused, ledger.CartDestroyed("b"));
        Assert.Equal(CargoOutcome.AlreadyRecorded,
            ledger.Move("b", "Stone", 2, CargoPlace.Carried, CargoPlace.InCart));
        Assert.Equal(CargoOutcome.NameReused, ledger.Move("b", "Stone", 1, CargoPlace.Carried, CargoPlace.InCart));

        Assert.Equal(CargoOutcome.Recorded, ledger.CartDestroyed("c"));
        Assert.Equal(CargoOutcome.NameReused, ledger.Take("c", "Stone", 1));
        Assert.Equal(CargoOutcome.NameReused, ledger.Move("c", "Stone", 1, CargoPlace.Carried, CargoPlace.Delivered));

        Assert.Equal(5, ledger.Acquired);
        Assert.Equal(3, ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(0, ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(2, ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.True(ledger.IsConserved);
    }

    [Fact]
    public void Material_that_left_him_for_good_is_never_moved_again_as_his()
    {
        var ledger = new CargoLedger();
        ledger.Take("t", "Stone", 8);
        ledger.Move("d", "Stone", 2, CargoPlace.Carried, CargoPlace.Delivered);
        ledger.Move("l", "Stone", 2, CargoPlace.Carried, CargoPlace.Lost);
        ledger.Move("x", "Stone", 2, CargoPlace.Carried, CargoPlace.DroppedOnDeath);
        ledger.Move("stow", "Stone", 2, CargoPlace.Carried, CargoPlace.InCart);
        ledger.CartDestroyed("boom");

        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("back-1", "Stone", 1, CargoPlace.Delivered, CargoPlace.Carried));
        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("back-2", "Stone", 1, CargoPlace.Lost, CargoPlace.Carried));
        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("back-3", "Stone", 1, CargoPlace.DroppedOnDeath, CargoPlace.Carried));
        Assert.Equal(CargoOutcome.Malformed,
            ledger.Move("back-4", "Stone", 1, CargoPlace.SpilledFromCart, CargoPlace.InCart));

        Assert.Equal(0, ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(2, ledger.At("Stone", CargoPlace.Delivered));
        Assert.Equal(2, ledger.At("Stone", CargoPlace.SpilledFromCart));
        Assert.Equal(8, ledger.TotalEverywhere);
        Assert.True(ledger.IsConserved);

        // A refused name stays free for the move that is real.
        Assert.Equal(CargoOutcome.NotThatMuchThere,
            ledger.Move("back-1", "Stone", 1, CargoPlace.Carried, CargoPlace.Delivered));
    }

    [Fact]
    public void Each_item_balances_on_its_own_not_only_in_total()
    {
        var ledger = new CargoLedger();
        ledger.Take("t1", "Stone", 4);
        ledger.Take("t2", "Wood", 4);
        ledger.Move("m", "Wood", 4, CargoPlace.Carried, CargoPlace.InCart);

        Assert.Equal(8, ledger.Acquired);
        Assert.Equal(8, ledger.TotalEverywhere);
        Assert.Equal(4, ledger.AcquiredOf("Stone"));
        Assert.Equal(4, ledger.AcquiredOf("Wood"));
        Assert.True(ledger.Custody.IsConserved("gunnar-collection", NpcMaterial.Of("Stone")));
        Assert.True(ledger.Custody.IsConserved("gunnar-collection", NpcMaterial.Of("Wood")));
        Assert.True(ledger.IsConserved);
    }
}
