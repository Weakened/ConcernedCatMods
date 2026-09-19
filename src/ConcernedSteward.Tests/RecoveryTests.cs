using System.Collections.Generic;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>What happens after the game closes in the middle of a trip.
///
/// <b>The rule these all prove is negative: nothing is replayed.</b> An intent
/// with no receipt means a mutation may or may not have landed, and neither
/// redoing it nor writing it off is supportable — one duplicates a player's
/// wood and the other destroys it. What can be established is what he is
/// holding, so that is measured and the record is corrected to match.
///
/// A crash is modelled the only way it honestly can be: the journal survives
/// (it is on disk), the loop does not (it was in memory). Each test builds a
/// fresh loop over a journal that still holds the interrupted step.</summary>
public sealed class RecoveryTests
{
    private const string Wood = StewardFixture.Wood;

    [Fact]
    public void A_clean_reload_with_nothing_in_flight_just_carries_on()
    {
        var journal = new MemoryUpkeepJournal();
        var loop = NewLoop(journal);
        var pack = new FakeStore("the Steward's pack");

        loop.OnWorldLoaded(pack, Wood);

        Assert.Equal(UpkeepPhase.Idle, loop.Phase);
        Assert.Equal(0, loop.Custody.Carried);
        Assert.False(loop.Custody.HasLoss);
        Assert.True(loop.Custody.Balances);
    }

    [Fact]
    public void Wood_in_his_hands_that_the_record_never_saw_is_taken_as_withdrawn_not_invented()
    {
        // The crash landed between taking the wood out of the chest and writing
        // the receipt. The chest really is short by eight; his pack really holds
        // eight. The record is corrected to the truth.
        var journal = new MemoryUpkeepJournal();
        journal.TryRecordIntent(new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-1"), 0), UpkeepStep.Withdraw, Wood, 8));

        var loop = NewLoop(journal);
        var pack = new FakeStore("the Steward's pack", (Wood, 8));

        loop.OnWorldLoaded(pack, Wood);

        Assert.Equal(8, loop.Custody.Withdrawn);
        Assert.Equal(8, loop.Custody.Carried);
        Assert.Equal(0, loop.Custody.Unaccounted);
        Assert.True(loop.Custody.Balances);

        // Abandoned, not resumed: he is idle and holding it, and the first
        // thing he does is put it back.
        Assert.Equal(UpkeepPhase.Idle, loop.Phase);
        Assert.Contains("put it back", loop.Explanation);
    }

    [Fact]
    public void The_interrupted_step_is_closed_as_uncertain_and_never_re_run()
    {
        var journal = new MemoryUpkeepJournal();
        var request = RequestId.For(new OrderId("steward-trip-1"), 0);
        journal.TryRecordIntent(new UpkeepIntent(request, UpkeepStep.Withdraw, Wood, 8));

        var loop = NewLoop(journal);
        loop.OnWorldLoaded(new FakeStore("the Steward's pack", (Wood, 8)), Wood);

        // No open steps remain, and the one that was open was closed as
        // uncertain rather than completed or retried.
        Assert.Empty(journal.UnresolvedIntents);
        Assert.Contains(
            journal.Receipts,
            receipt => receipt.Request.Equals(request)
                && receipt.Outcome == UpkeepOutcome.Uncertain
                && receipt.Moved == 0
                && receipt.Evidence.Contains("not replayed"));
    }

    [Fact]
    public void Wood_the_record_expected_and_he_does_not_have_is_unaccounted_for_and_stops_him()
    {
        var journal = new MemoryUpkeepJournal();
        journal.TryRecordIntent(new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-1"), 1), UpkeepStep.Feed, "fire-1", 1));

        var loop = NewLoop(journal);
        loop.Custody.RestoreLoss(0);

        // A previous session recorded him carrying six; his pack holds two. The
        // four that are gone are recorded as gone, not quietly forgotten and
        // not quietly replaced.
        loop.Custody.RecordWithdrawn(6);
        loop.OnWorldLoaded(new FakeStore("the Steward's pack", (Wood, 2)), Wood);

        Assert.Equal(4, loop.Custody.Unaccounted);
        Assert.Equal(2, loop.Custody.Carried);
        Assert.True(loop.Custody.Balances);
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
        Assert.Contains("has not been replaced", loop.Explanation);
    }

    [Fact]
    public void A_loss_a_previous_session_recorded_survives_the_reload()
    {
        var journal = new MemoryUpkeepJournal();
        var loop = NewLoop(journal);

        // What the runtime does with the number it read back off disk.
        loop.Custody.RestoreLoss(3);
        loop.OnWorldLoaded(new FakeStore("the Steward's pack"), Wood);

        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
        Assert.True(loop.Custody.HasLoss);
        Assert.True(loop.Custody.Balances);
    }

    [Fact]
    public void A_pack_that_cannot_be_counted_after_loading_stops_him_rather_than_assuming_empty()
    {
        var journal = new MemoryUpkeepJournal();
        journal.TryRecordIntent(new UpkeepIntent(
            RequestId.For(new OrderId("steward-trip-1"), 0), UpkeepStep.Withdraw, Wood, 8));

        var loop = NewLoop(journal);
        var pack = new FakeStore("the Steward's pack", (Wood, 8)) { CountThrows = true };

        loop.OnWorldLoaded(pack, Wood);

        // "Could not count" is not "carrying nothing". Assuming the latter would
        // write a clean record over a pack that still holds a player's wood.
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
        Assert.Contains("could not be counted", loop.Explanation);
    }

    [Fact]
    public void An_unloaded_body_after_a_reload_is_not_mistaken_for_an_empty_one()
    {
        var journal = new MemoryUpkeepJournal();
        var loop = NewLoop(journal);
        loop.Custody.RecordWithdrawn(4);

        var pack = new FakeStore("the Steward's pack", (Wood, 4)) { Available = false };
        loop.OnWorldLoaded(pack, Wood);

        // Nothing was concluded, and in particular the four were NOT written
        // off as lost just because his part of the world had not loaded yet.
        Assert.Equal(0, loop.Custody.Unaccounted);
        Assert.Equal(4, loop.Custody.Carried);
        Assert.Equal(UpkeepPhase.NeedsAttention, loop.Phase);
    }

    /// <summary>Since the Concerned NPC adoption (#382) the identity is held by
    /// the library's arbiter, for the life of the body, and not by a mode owner
    /// this assembly compiled for itself - so what this test can still assert
    /// here, and the only thing it ever really meant, is that a reload leaves no
    /// trip running and nothing reserved. The arbiter half is proved where it
    /// lives, in <c>StewardNpcAdoptionTests</c>.</summary>
    [Fact]
    public void Reloading_releases_the_reservation_and_ends_the_trip()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(3);
        Assert.True(f.Loop.Reservation.IsHeld);
        Assert.True(f.Loop.IsWorking);

        f.Loop.OnWorldLoaded(f.Pack, StewardFixture.Wood);

        Assert.False(f.Loop.Reservation.IsHeld);
        Assert.False(f.Loop.IsWorking);
    }

    [Fact]
    public void After_recovering_he_puts_back_what_he_is_holding_before_starting_anything_new()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(3);
        Assert.Equal(10, f.Loop.Custody.Carried);

        // The world goes away and comes back. He is still holding ten.
        f.Loop.OnWorldLoaded(f.Pack, StewardFixture.Wood);
        Assert.Equal(10, f.Loop.Custody.Carried);

        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        f.RunUntilTripEnds();

        Assert.Equal(0, f.Loop.Custody.Carried);
        Assert.Equal(50, f.Depot.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 50);
    }

    private static UpkeepLoop NewLoop(IUpkeepJournal journal) =>
        new UpkeepLoop(
            UpkeepLimits.Default, journal, _ => { });
}
