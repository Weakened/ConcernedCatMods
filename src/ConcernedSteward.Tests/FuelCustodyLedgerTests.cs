using System;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Sunniva's fuel ledger is the second role on Concerned NPC's
/// conservation ledger (#376), after Gunnar's collection account.
///
/// <see cref="ConservationTests"/> and <see cref="RecoveryTests"/> are unchanged
/// and still prove the Steward's own words. These prove the same thing in the
/// library's terms - so a <see cref="FuelCustody"/> that went back to keeping
/// counters of its own would fail here while every older test stayed green -
/// and prove the one behaviour this adoption fixed: a loss restored twice is
/// recorded once.</summary>
public sealed class FuelCustodyLedgerTests
{
    [Fact]
    public void Every_transition_conserves_in_the_library_ledger_and_in_her_words()
    {
        var custody = new FuelCustody();
        AssertBothBalance(custody);

        custody.RecordWithdrawn(10);
        AssertBothBalance(custody);
        Assert.Equal(10, custody.Ledger.Acquired(FuelCustody.Job, FuelCustody.Fuel));

        custody.RecordBurned(4);
        AssertBothBalance(custody);

        custody.RecordReturned(3);
        AssertBothBalance(custody);

        custody.RecordLost(2);
        AssertBothBalance(custody);

        Assert.Equal(1, custody.RestateCarried(2));
        AssertBothBalance(custody);

        Assert.Equal(-1, custody.RestateCarried(1));
        AssertBothBalance(custody);

        // Every unit is in exactly one library place, and each of her places is
        // a distinct one.
        Assert.Equal(1, HeldAt(custody, NpcCustodyPlace.Carried));
        Assert.Equal(4, HeldAt(custody, NpcCustodyPlace.Delivered));
        Assert.Equal(3, HeldAt(custody, NpcCustodyPlace.Handed));
        Assert.Equal(3, HeldAt(custody, NpcCustodyPlace.Lost));
        Assert.Equal(11, custody.Ledger.TotalEverywhere(FuelCustody.Job, FuelCustody.Fuel));
        Assert.Equal(11, custody.Withdrawn);
    }

    [Fact]
    public void Her_counts_are_read_from_the_library_ledger_and_nowhere_else()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(6);
        custody.RecordBurned(2);
        custody.RecordReturned(1);
        custody.RecordLost(1);

        // Each of her four numbers is the library's holding at her place.
        NpcHolding[] held = custody.Ledger.Holdings.ToArray();
        Assert.All(held, holding => Assert.Equal(FuelCustody.Job, holding.JobId));
        Assert.All(held, holding => Assert.Equal(FuelCustody.Fuel, holding.Material));
        Assert.Equal(custody.Carried, HeldAt(custody, NpcCustodyPlace.Carried));
        Assert.Equal(custody.Burned, HeldAt(custody, NpcCustodyPlace.Delivered));
        Assert.Equal(custody.Returned, HeldAt(custody, NpcCustodyPlace.Handed));
        Assert.Equal(custody.Unaccounted, HeldAt(custody, NpcCustodyPlace.Lost));
        Assert.Equal(4, held.Length);
    }

    [Fact]
    public void A_refused_move_records_nothing_in_the_library_either()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(2);
        int revision = custody.Revision;

        Assert.Throws<InvalidOperationException>(() => custody.RecordBurned(3));
        Assert.Throws<InvalidOperationException>(() => custody.RecordReturned(3));
        Assert.Throws<InvalidOperationException>(() => custody.RecordLost(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => custody.RecordWithdrawn(0));

        Assert.Equal(revision, custody.Revision);
        Assert.Equal(2, custody.Carried);
        Assert.Equal(0, HeldAt(custody, NpcCustodyPlace.Delivered));
        Assert.Equal(0, HeldAt(custody, NpcCustodyPlace.Handed));
        Assert.Equal(0, HeldAt(custody, NpcCustodyPlace.Lost));
        AssertBothBalance(custody);
    }

    [Fact]
    public void Fuel_put_back_in_the_depot_and_fuel_lost_are_never_the_jobs_to_move_again()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(4);
        custody.RecordReturned(2);
        custody.RecordLost(1);

        // Lead default: the depot is a terminal place for this job, so units
        // put back cannot be moved out again under the job's name - a later
        // withdrawal is a fresh, measured acquisition. Lost is terminal as it
        // always was. Asked under the run's own open job, of places that hold
        // units, so the only refusal left is the place's own: the control
        // below proves the same move out of her pack is applied.
        NpcCustodyLocation depot = LocationKeyed(custody, "depot");
        NpcCustodyLocation lost = LocationKeyed(custody, "unaccounted");
        NpcCustodyLocation pack = LocationKeyed(custody, "pack");
        Assert.True(custody.Ledger.HoldingAt(FuelCustody.Job, depot, FuelCustody.Fuel) >= 1);
        Assert.True(custody.Ledger.HoldingAt(FuelCustody.Job, lost, FuelCustody.Fuel) >= 1);

        Assert.Equal(NpcCustodyOutcome.Rejected, custody.Ledger.RecordMeasuredMove(
            ReservationId.For(FuelCustody.Job, 100001), depot, pack, FuelCustody.Fuel, 1));
        Assert.Equal(NpcCustodyOutcome.Rejected, custody.Ledger.RecordMeasuredMove(
            ReservationId.For(FuelCustody.Job, 100002), lost, pack, FuelCustody.Fuel, 1));
        Assert.Equal(1, custody.Carried);
        Assert.Equal(2, custody.Returned);
        Assert.Equal(1, custody.Unaccounted);
        AssertBothBalance(custody);

        // The control: the same call, out of a place the job may leave.
        Assert.Equal(NpcCustodyOutcome.Applied, custody.Ledger.RecordMeasuredMove(
            ReservationId.For(FuelCustody.Job, 100003), pack, depot, FuelCustody.Fuel, 1));
        Assert.Equal(3, custody.Returned);
        AssertBothBalance(custody);
    }

    [Fact]
    public void An_acknowledged_loss_moves_nothing_in_the_library_and_recreates_nothing()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(5);
        custody.RecordBurned(2);
        custody.RecordLost(3);

        custody.AcknowledgeLoss();

        Assert.False(custody.HasLoss);
        Assert.Equal(0, custody.Unaccounted);
        Assert.Equal(2, custody.Withdrawn);
        AssertBothBalance(custody);

        // The library still says where the three went. Nothing was taken out
        // of it and nothing was put back anywhere.
        Assert.Equal(3, HeldAt(custody, NpcCustodyPlace.Lost));
        Assert.Equal(5, custody.Ledger.Acquired(FuelCustody.Job, FuelCustody.Fuel));
        Assert.Equal(0, custody.Carried);

        // A loss after the acknowledgement is a new loss, counted in full.
        custody.RecordWithdrawn(2);
        custody.RecordLost(2);
        Assert.Equal(2, custody.Unaccounted);
        Assert.Equal(4, custody.Withdrawn);
        AssertBothBalance(custody);
    }

    [Fact]
    public void A_fresh_run_is_a_fresh_library_ledger_and_inherits_nothing()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(5);
        custody.RecordBurned(3);
        custody.RecordReturned(2);
        NpcCustodyLedger first = custody.Ledger;

        Assert.True(custody.TryReset());

        Assert.NotSame(first, custody.Ledger);
        Assert.Empty(custody.Ledger.Holdings);
        Assert.Equal(0, custody.Withdrawn);
        AssertBothBalance(custody);

        custody.RecordWithdrawn(1);
        Assert.Equal(1, custody.Withdrawn);
        AssertBothBalance(custody);
    }

    [Fact]
    public void A_run_holding_fuel_or_a_loss_keeps_its_ledger()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(2);
        NpcCustodyLedger first = custody.Ledger;
        Assert.False(custody.TryReset());
        Assert.Same(first, custody.Ledger);

        custody.RecordLost(2);
        Assert.False(custody.TryReset());
        Assert.Same(first, custody.Ledger);

        custody.AcknowledgeLoss();
        Assert.True(custody.TryReset());
        Assert.NotSame(first, custody.Ledger);
    }

    // ------------------------------------------------------------------
    // A loss restored twice is recorded once (#376)
    // ------------------------------------------------------------------

    [Fact]
    public void A_loss_restored_twice_is_recorded_once()
    {
        var custody = new FuelCustody();

        custody.RestoreLoss(3);
        custody.RestoreLoss(3);

        Assert.Equal(3, custody.Unaccounted);
        Assert.Equal(3, custody.Withdrawn);
        Assert.Equal(3, custody.Ledger.Acquired(FuelCustody.Job, FuelCustody.Fuel));
        AssertBothBalance(custody);
    }

    [Fact]
    public void Leaving_a_world_and_loading_it_again_does_not_double_its_recorded_loss()
    {
        // What the runtime does: it reads the loss back off disk on every world
        // load and hands it to the same loop, which lives as long as the
        // runtime. Before #376 the second load made it six, the next save wrote
        // six, and every reload after that doubled it again.
        var loop = new UpkeepLoop(UpkeepLimits.Default, new MemoryUpkeepJournal(), _ => { });
        var pack = new FakeStore("the Steward's pack");

        loop.Custody.RestoreLoss(3);
        loop.OnWorldLoaded(pack, StewardFixture.Wood);
        loop.Stop("The world went away.");

        loop.Custody.RestoreLoss(loop.Custody.Unaccounted);
        loop.OnWorldLoaded(pack, StewardFixture.Wood);

        Assert.Equal(3, loop.Custody.Unaccounted);
        Assert.True(loop.Custody.HasLoss);
        Assert.True(loop.Custody.Balances);
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
    }

    [Fact]
    public void A_record_behind_the_ledger_never_lowers_a_loss_and_one_ahead_raises_it()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(5);
        custody.RecordLost(5);

        // A record the runtime could not write after the loss says less.
        custody.RestoreLoss(2);
        Assert.Equal(5, custody.Unaccounted);
        AssertBothBalance(custody);

        // A record that says more than this ledger knows: the shortfall left
        // the depot in a session this ledger did not see.
        custody.RestoreLoss(7);
        Assert.Equal(7, custody.Unaccounted);
        Assert.Equal(7, custody.Withdrawn);
        AssertBothBalance(custody);
    }

    [Fact]
    public void A_loss_acknowledged_here_but_still_on_disk_comes_back_once()
    {
        var custody = new FuelCustody();
        custody.RestoreLoss(4);
        custody.AcknowledgeLoss();
        Assert.False(custody.HasLoss);

        // The acknowledgement did not reach the record, so the next load hands
        // the old number back. It is a loss again - the safe direction - and it
        // is four, not eight.
        custody.RestoreLoss(4);
        Assert.Equal(4, custody.Unaccounted);
        Assert.True(custody.HasLoss);
        AssertBothBalance(custody);

        custody.RestoreLoss(4);
        Assert.Equal(4, custody.Unaccounted);
        AssertBothBalance(custody);
    }

    [Fact]
    public void Nothing_restored_is_nothing_recorded()
    {
        var custody = new FuelCustody();
        int revision = custody.Revision;

        custody.RestoreLoss(0);
        custody.RestoreLoss(-2);

        Assert.Equal(revision, custody.Revision);
        Assert.Empty(custody.Ledger.Holdings);

        // Not even with a loss already recorded, where a subtraction from the
        // most negative number would wrap round to a very large one.
        custody.RecordWithdrawn(1);
        custody.RecordLost(1);
        revision = custody.Revision;
        custody.RestoreLoss(int.MinValue);
        Assert.Equal(revision, custody.Revision);
        Assert.Equal(1, custody.Unaccounted);

        AssertBothBalance(custody);
    }

    [Fact]
    public void Every_place_is_named_in_the_unknown_load()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(4);
        custody.RecordBurned(1);
        custody.RecordReturned(1);
        custody.RecordLost(1);

        Assert.All(custody.Ledger.Holdings, holding =>
            Assert.Equal(NpcWorldEpoch.Unknown, holding.Location.Epoch));
    }

    private static void AssertBothBalance(FuelCustody custody)
    {
        Assert.True(custody.Ledger.IsConserved(FuelCustody.Job, FuelCustody.Fuel),
            "the library ledger does not conserve: " + custody.Describe());
        Assert.True(custody.Balances, "the Steward's ledger does not balance: " + custody.Describe());
    }

    private static int HeldAt(FuelCustody custody, NpcCustodyPlace place) =>
        custody.Ledger.Holdings
            .Where(holding => holding.Location.Place == place)
            .Sum(holding => holding.Count);

    private static NpcCustodyLocation LocationKeyed(FuelCustody custody, string key) =>
        custody.Ledger.Holdings.Single(holding => holding.Location.Key == key).Location;
}
