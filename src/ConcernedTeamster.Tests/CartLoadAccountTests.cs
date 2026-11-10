using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>How the ledger records a load into the cart assigned to him (#381,
/// owner decision 2026-10-31, `DECISIONS.md` D22): what was measured arriving
/// moves off his back into the cart, nothing else moves, and the total never
/// changes.</summary>
public sealed class CartLoadAccountTests
{
    private static DepositResult Moved(DepositOutcome outcome, string item, int units) =>
        new DepositResult(outcome, new[] { new KeyValuePair<string, int>(item, units) }, "measured");

    [Fact]
    public void A_measured_load_moves_his_units_into_the_cart_and_conserves_them()
    {
        var account = new CollectionAccount();
        account.Record("p1", "Stone", 30, StopResult.Took());

        Assert.Equal(30, account.RecordCartLoad("load-1", Moved(DepositOutcome.Deposited, "Stone", 30)));

        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(30, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.Equal(30, account.Ledger.Acquired);
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_partial_load_moves_only_what_arrived()
    {
        var account = new CollectionAccount();
        account.Record("p1", "Wood", 20, StopResult.Took());

        Assert.Equal(12, account.RecordCartLoad("load-1", Moved(DepositOutcome.PartlyDeposited, "Wood", 12)));

        Assert.Equal(8, account.Ledger.At("Wood", CargoPlace.Carried));
        Assert.Equal(12, account.Ledger.At("Wood", CargoPlace.InCart));
        Assert.True(account.Ledger.IsConserved);
    }

    [Theory]
    [InlineData((int)DepositOutcome.Refused)]
    [InlineData((int)DepositOutcome.Uncertain)]
    [InlineData((int)DepositOutcome.Unspecified)]
    public void A_refused_or_unmeasured_load_moves_nothing(int outcomeValue)
    {
        var outcome = (DepositOutcome)outcomeValue;
        var account = new CollectionAccount();
        account.Record("p1", "Stone", 10, StopResult.Took());

        Assert.Equal(0, account.RecordCartLoad("load-1", Moved(outcome, "Stone", 10)));

        Assert.Equal(10, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_load_that_measured_more_than_the_ledger_saw_him_take_acquires_nothing_on_paper()
    {
        // Units he carried from before this session were never acquired here.
        // They are not acquired now to make the numbers agree.
        var account = new CollectionAccount();
        account.Record("p1", "Stone", 5, StopResult.Took());

        Assert.Equal(5, account.RecordCartLoad("load-1", Moved(DepositOutcome.Deposited, "Stone", 9)));

        Assert.Equal(5, account.Ledger.Acquired);
        Assert.Equal(5, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_load_reported_twice_is_moved_once()
    {
        var account = new CollectionAccount();
        account.Record("p1", "Stone", 10, StopResult.Took());
        DepositResult load = Moved(DepositOutcome.Deposited, "Stone", 4);

        Assert.Equal(4, account.RecordCartLoad("load-1", load));
        Assert.Equal(0, account.RecordCartLoad("load-1", load));

        Assert.Equal(6, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(4, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_load_with_nothing_on_his_back_moves_nothing()
    {
        var account = new CollectionAccount();

        Assert.Equal(0, account.RecordCartLoad("load-1", Moved(DepositOutcome.Deposited, "Stone", 3)));
        Assert.Equal(0, account.Ledger.Acquired);
        Assert.True(account.Ledger.IsConserved);
    }
}
