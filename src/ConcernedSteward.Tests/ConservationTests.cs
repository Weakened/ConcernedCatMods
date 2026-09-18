using System;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>The one promise this product makes: every unit of a player's wood
/// is in exactly one real place at every moment, and the Steward would rather
/// stop than lose track of one.
///
/// The faults below are injected at the two boundaries where a mutation and a
/// record can disagree — around a withdrawal, and around a unit going into a
/// fire. In a live game they are a disk going read-only, a chest unloading
/// under a running call, a process dying. None of them can be staged reliably
/// in game, which is exactly why they are staged here.</summary>
public sealed class ConservationTests
{
    [Fact]
    public void The_ledger_invariant_holds_after_every_kind_of_movement()
    {
        var custody = new FuelCustody();
        Assert.True(custody.Balances);

        custody.RecordWithdrawn(10);
        Assert.True(custody.Balances);
        Assert.Equal(10, custody.Carried);

        custody.RecordBurned(4);
        Assert.True(custody.Balances);

        custody.RecordReturned(3);
        Assert.True(custody.Balances);

        custody.RecordLost(2);
        Assert.True(custody.Balances);

        Assert.Equal(10, custody.Withdrawn);
        Assert.Equal(1, custody.Carried);
        Assert.Equal(4, custody.Burned);
        Assert.Equal(3, custody.Returned);
        Assert.Equal(2, custody.Unaccounted);
    }

    [Fact]
    public void Moving_more_out_of_his_hands_than_he_holds_is_refused_loudly()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(2);

        // Not clamped, not ignored. A caller that measured a move the record
        // cannot account for has a bug, and silently allowing it would make
        // the invariant meaningless.
        Assert.Throws<InvalidOperationException>(() => custody.RecordBurned(3));
        Assert.True(custody.Balances);
    }

    [Fact]
    public void A_recorded_loss_is_never_reversed_only_acknowledged()
    {
        var custody = new FuelCustody();
        custody.RecordWithdrawn(5);
        custody.RecordLost(5);

        Assert.True(custody.HasLoss);
        Assert.False(custody.TryReset());

        custody.AcknowledgeLoss();

        Assert.False(custody.HasLoss);
        Assert.Equal(0, custody.Unaccounted);
        Assert.True(custody.Balances);

        // Nothing was recreated: the acknowledgement removes the units from the
        // run's totals, it does not put wood anywhere.
        Assert.Equal(0, custody.Carried);
        Assert.Equal(0, custody.Withdrawn);
    }

    // ------------------------------------------------------------------
    // Faults at the withdrawal boundary
    // ------------------------------------------------------------------

    [Fact]
    public void A_withdrawal_that_cannot_write_its_intent_moves_nothing()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Journal.FailNextIntentFor = UpkeepStep.Withdraw;

        f.Run(3);

        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(50, f.Depot.Count(StewardFixture.Wood));
        Assert.Equal(0, f.Pack.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_withdrawal_that_throws_part_way_is_uncertain_and_is_not_retried()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Depot.MoveThrows = true;

        f.Run(4);

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Contains("could not be accounted for", f.Loop.Explanation);

        // The fake took the units out of the chest and did not deliver them,
        // which is the window the ordering exists to keep out of the real
        // adapter. Nothing was tried again, and nothing was invented to
        // balance the books.
        f.Run(10);
        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Burned);
        Assert.Empty(f.Fires.Fed);
    }

    [Fact]
    public void A_withdrawal_whose_two_sides_disagree_is_uncertain_and_stops_him()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);

        // The chest loses ten and his pack gains none: the deltas do not match,
        // so nothing may be claimed about where the wood is.
        f.Depot.MoveLoses = true;

        f.Run(4);

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Carried);
        Assert.Empty(f.Fires.Fed);
    }

    [Fact]
    public void A_withdrawal_whose_receipt_cannot_be_written_is_uncertain_not_completed()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Journal.FailNextReceiptFor = UpkeepStep.Withdraw;

        f.Run(4);

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);

        // The step stays open in the record, so the next load knows a mutation
        // may have happened and reconciles rather than assuming.
        Assert.Contains(f.Journal.UnresolvedIntents, i => i.Step == UpkeepStep.Withdraw);
    }

    [Fact]
    public void A_chest_that_cannot_be_counted_moves_nothing()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(2);
        Assert.Equal(UpkeepPhase.Withdrawing, f.Loop.Phase);

        f.Depot.CountThrows = true;
        f.Run();

        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(0, f.Pack.Count(StewardFixture.Wood));
    }

    // ------------------------------------------------------------------
    // Faults at the fuel boundary
    // ------------------------------------------------------------------

    [Fact]
    public void A_unit_that_leaves_his_hands_without_lighting_anything_is_recorded_as_lost()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Lost);

        f.Run(5);

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Equal(1, f.Loop.Custody.Unaccounted);
        Assert.True(f.Loop.Custody.HasLoss);
        Assert.True(f.Loop.Custody.Balances);

        // It has NOT been replaced, and the count in the world reflects that.
        Assert.Equal(40, f.Depot.Count(StewardFixture.Wood));
        Assert.Equal(9, f.Pack.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 50);

        Assert.Contains("has not been replaced", f.Loop.Explanation);
    }

    [Fact]
    public void An_unreadable_result_at_the_fire_stops_him_rather_than_guessing()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Uncertain);

        f.Run(5);

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Contains("could not tell", f.Loop.Explanation);

        // One attempt, and no second one.
        Assert.Single(f.Fires.Fed);
        f.Run(10);
        Assert.Single(f.Fires.Fed);
    }

    [Fact]
    public void A_feed_that_throws_is_uncertain_and_the_exception_does_not_escape()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(4);

        f.Fires.FeedThrows = true;
        f.Run();

        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);
        Assert.Contains("threw", f.Loop.Explanation);
    }

    [Fact]
    public void He_stays_stopped_until_a_person_says_they_have_looked()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Lost);
        f.Run(5);
        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);

        f.Run(30);
        Assert.Equal(UpkeepPhase.NeedsAttention, f.Loop.Phase);

        string answer = f.Loop.Acknowledge();

        Assert.Contains("Nothing was recreated", answer);
        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.False(f.Loop.Custody.HasLoss);

        // And he works again.
        f.Run(4);
        Assert.NotEqual(UpkeepPhase.NeedsAttention, f.Loop.Phase);
    }

    // ------------------------------------------------------------------
    // Depositing
    // ------------------------------------------------------------------

    [Fact]
    public void A_chest_with_no_room_leaves_him_holding_it_rather_than_dropping_it()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        f.Run(3);
        Assert.Equal(10, f.Loop.Custody.Carried);

        // Somebody filled the chest while he was away.
        f.Depot.Capacity = 40;
        f.Depot.Put(StewardFixture.Wood, 40);

        f.Run(8);

        Assert.Equal(10, f.Loop.Custody.Carried);
        Assert.Equal(0, f.Loop.Custody.Unaccounted);
        Assert.Contains(f.Reported, line => line.Contains("no room in the supply chest"));
        Assert.True(f.Loop.Custody.Balances);
    }

    [Fact]
    public void A_full_chest_is_retried_on_the_scan_interval_rather_than_every_tick()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        f.Run(3);
        Assert.Equal(10, f.Loop.Custody.Carried);

        f.Depot.Capacity = 40;
        f.Depot.Put(StewardFixture.Wood, 40);
        f.Motion.WalkedTo.Clear();

        // Two hundred ticks of game time at 1/20 s each: ten seconds, which is
        // less than one scan interval. Without the throttle this is a walk to
        // the chest every third tick, for as long as the chest stays full.
        for (int tick = 0; tick < 200; tick++)
        {
            f.Loop.Tick(f.Tick());
            f.Now += 0.05f;
        }

        Assert.True(
            f.Motion.WalkedTo.Count <= 200,
            "he was sent to the chest " + f.Motion.WalkedTo.Count + " times in ten seconds");
        Assert.Equal(10, f.Loop.Custody.Carried);
        Assert.True(f.Loop.Custody.Balances);
    }

    [Fact]
    public void A_chest_with_partial_room_takes_what_fits_and_he_keeps_the_rest()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        f.Run(3);
        Assert.Equal(10, f.Loop.Custody.Carried);
        Assert.Equal(40, f.Depot.Count(StewardFixture.Wood));

        f.Depot.Capacity = 46;
        f.Run(8);

        Assert.Equal(6, f.Loop.Custody.Returned);
        Assert.Equal(4, f.Loop.Custody.Carried);
        Assert.Equal(46, f.Depot.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 50);
    }

    // ------------------------------------------------------------------
    // The journal's ordering
    // ------------------------------------------------------------------

    [Fact]
    public void The_intent_is_written_before_the_mutation_and_the_receipt_after()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);

        f.Run(5);

        // intent(withdraw) ... receipt(withdraw) ... intent(feed) ... receipt(feed)
        int withdrawIntent = IndexOf(f, "intent", "Withdraw");
        int withdrawReceipt = IndexOf(f, "receipt", "Withdraw");
        int feedIntent = IndexOf(f, "intent", "Feed");
        int feedReceipt = IndexOf(f, "receipt", "Feed");

        Assert.True(withdrawIntent >= 0 && withdrawReceipt > withdrawIntent);
        Assert.True(feedIntent > withdrawReceipt);
        Assert.True(feedReceipt > feedIntent);
    }

    [Fact]
    public void Every_step_of_one_trip_gets_its_own_deterministic_request_id()
    {
        var order = new OrderId("steward-trip-1");
        Assert.Equal("steward-trip-1-0", RequestId.For(order, 0).Value);
        Assert.Equal("steward-trip-1-7", RequestId.For(order, 7).Value);

        // The same step of the same trip produces the same id after a restart,
        // which is what makes a replayed attempt recognisable as the same
        // attempt rather than a new one.
        Assert.Equal(RequestId.For(order, 3), RequestId.For(new OrderId("steward-trip-1"), 3));
    }

    private static int IndexOf(StewardFixture f, string kind, string step)
    {
        for (int index = 0; index < f.Journal.Lines.Count; index++)
        {
            string line = f.Journal.Lines[index];
            if (line.StartsWith(kind, StringComparison.Ordinal) && line.Contains(step))
            {
                return index;
            }
        }

        return -1;
    }
}
