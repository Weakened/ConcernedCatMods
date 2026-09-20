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
}
