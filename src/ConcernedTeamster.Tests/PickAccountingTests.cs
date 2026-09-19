using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>The part of a pick that can mint material, and can be tested
/// (#381).
///
/// Each test below is a defect an independent review found in the first version
/// of the pickup port, moved into a game-free type so that it is proved rather
/// than reasoned about. The port binds Unity and no test in this repository can
/// load it; everything that survives in the port is a game fact, and the
/// handoff says which is which.</summary>
public sealed class PickAccountingTests
{
    private static PickAccounting Started(string source = "zdo-1", int expected = 3)
    {
        var accounting = new PickAccounting();
        accounting.Began(source, expected);
        return accounting;
    }

    // ------------------------------------------------------------------
    // The one that mints
    // ------------------------------------------------------------------

    [Fact]
    public void A_source_picked_once_is_refused_until_the_world_says_it_was()
    {
        // The window the game leaves open: its pick raises two routed messages,
        // and between them the source still reports that it can be picked. A
        // second pick in that window is a second full yield out of nothing.
        PickAccounting accounting = Started("stone-7", expected: 2);
        accounting.MayCredit(1);
        accounting.MayCredit(1);
        accounting.Finish();

        Assert.Equal(PickGuard.AwaitingConfirmation, accounting.MayBegin("stone-7", 2));
    }

    [Fact]
    public void Finishing_the_pick_is_not_what_clears_the_refusal()
    {
        // The defect in its exact shape: the old guard was "a pick is in
        // flight", which ends the instant the last drop is gathered - possibly
        // the same frame. Finishing must not be enough.
        PickAccounting accounting = Started("stone-7", expected: 1);
        accounting.MayCredit(1);

        Assert.True(accounting.IsComplete);
        accounting.Finish();
        Assert.False(accounting.InFlight);
        Assert.Equal(PickGuard.AwaitingConfirmation, accounting.MayBegin("stone-7", 1));
    }

    [Fact]
    public void The_world_confirming_is_what_clears_it()
    {
        PickAccounting accounting = Started("stone-7", expected: 1);
        accounting.MayCredit(1);
        accounting.Finish();

        accounting.Confirmed("stone-7");

        Assert.Equal(PickGuard.None, accounting.MayBegin("stone-7", 1));
        Assert.Equal(0, accounting.AwaitingConfirmation);
    }

    [Fact]
    public void Another_source_is_never_refused_for_this_ones_pick()
    {
        PickAccounting accounting = Started("stone-7", expected: 1);
        accounting.MayCredit(1);
        accounting.Finish();

        Assert.Equal(PickGuard.None, accounting.MayBegin("stone-8", 1));
    }

    [Fact]
    public void A_pick_that_gathered_nothing_still_refuses_the_source()
    {
        // The pick may well have happened: the interaction was made, and what
        // came back says nothing about it. Refusing is the only reading that
        // cannot mint.
        PickAccounting accounting = Started("stone-7", expected: 4);

        Assert.Equal(0, accounting.Finish());
        Assert.Equal(PickGuard.AwaitingConfirmation, accounting.MayBegin("stone-7", 4));
    }

    [Fact]
    public void Confirming_a_source_nobody_picked_changes_nothing()
    {
        PickAccounting accounting = Started("stone-7", expected: 1);
        accounting.Finish();

        accounting.Confirmed("some-other-stone");
        accounting.Confirmed(null);
        accounting.Confirmed(string.Empty);

        Assert.Equal(PickGuard.AwaitingConfirmation, accounting.MayBegin("stone-7", 1));
    }

    [Fact]
    public void A_world_going_away_forgets_every_unconfirmed_source()
    {
        // The sources named do not exist in the next world, and a record that
        // outlived them would refuse picks of whatever inherited their names.
        PickAccounting accounting = Started("stone-7", expected: 1);
        accounting.Finish();
        accounting.Began("stone-8", 1);
        accounting.Finish();
        Assert.Equal(2, accounting.AwaitingConfirmation);

        accounting.ForgetWorld();

        Assert.Equal(0, accounting.AwaitingConfirmation);
        Assert.False(accounting.InFlight);
        Assert.Equal(PickGuard.None, accounting.MayBegin("stone-7", 1));
    }

    // ------------------------------------------------------------------
    // The count a refused start could read
    // ------------------------------------------------------------------

    [Fact]
    public void A_finished_picks_count_is_handed_back_once_and_never_readable_again()
    {
        PickAccounting accounting = Started("a", expected: 3);
        accounting.MayCredit(1);
        accounting.MayCredit(1);

        Assert.Equal(2, accounting.Finish());
        Assert.Equal(0, accounting.Taken);

        // "Once" is the load-bearing word, and asking twice is the only way to
        // tell. Reading Taken cannot: it is gated on a pick being in flight, so
        // it answers zero whether or not the counter behind it was cleared -
        // which is how the first version of this test passed with the clearing
        // deleted.
        Assert.Equal(0, accounting.Finish());
    }

    [Fact]
    public void A_refused_start_cannot_read_the_previous_picks_count()
    {
        // The defect: a caller crediting on what it read after a refusal
        // credited units it never gathered.
        PickAccounting accounting = Started("a", expected: 3);
        accounting.MayCredit(1);
        accounting.MayCredit(1);
        accounting.Finish();

        Assert.Equal(PickGuard.Malformed, accounting.MayBegin(string.Empty, 3));
        Assert.Equal(0, accounting.Taken);
    }

    // ------------------------------------------------------------------
    // What may be counted
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Only_a_single_unit_counts_because_that_is_what_the_game_drops(int stack)
    {
        // A larger stack arriving inside the window is somebody else's - most
        // likely a player emptying their inventory beside him.
        PickAccounting accounting = Started("a", expected: 5);

        Assert.False(accounting.MayCredit(stack));
        Assert.Equal(0, accounting.Taken);
    }

    [Fact]
    public void A_pick_can_be_short_but_never_generous()
    {
        PickAccounting accounting = Started("a", expected: 2);

        Assert.True(accounting.MayCredit(1));
        Assert.True(accounting.MayCredit(1));
        Assert.False(accounting.MayCredit(1));
        Assert.Equal(2, accounting.Taken);
    }

    [Fact]
    public void Nothing_counts_while_no_pick_is_in_flight()
    {
        var accounting = new PickAccounting();

        Assert.False(accounting.MayCredit(1));
        Assert.Equal(0, accounting.Taken);
    }

    [Fact]
    public void A_take_the_world_refused_comes_back_off_the_count()
    {
        // Counting something still lying on the ground is how a shortfall
        // becomes invisible.
        PickAccounting accounting = Started("a", expected: 3);
        accounting.MayCredit(1);
        accounting.MayCredit(1);

        accounting.Uncredit();

        Assert.Equal(1, accounting.Taken);
        Assert.Equal(1, accounting.Finish());
    }

    [Fact]
    public void Uncrediting_more_than_was_credited_never_goes_below_nothing()
    {
        PickAccounting accounting = Started("a", expected: 3);
        accounting.MayCredit(1);

        accounting.Uncredit();
        accounting.Uncredit();
        accounting.Uncredit();

        Assert.Equal(0, accounting.Taken);
    }

    // ------------------------------------------------------------------
    // Starting at all
    // ------------------------------------------------------------------

    [Fact]
    public void One_body_picks_one_thing()
    {
        PickAccounting accounting = Started("a", expected: 3);

        Assert.Equal(PickGuard.Busy, accounting.MayBegin("b", 1));
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("a", 0)]
    [InlineData("a", -2)]
    public void A_malformed_request_is_refused_rather_than_started(string? source, int expected)
    {
        Assert.Equal(PickGuard.Malformed, new PickAccounting().MayBegin(source, expected));
    }

    [Fact]
    public void A_fresh_accounting_permits_a_pick_and_holds_nothing()
    {
        var accounting = new PickAccounting();

        Assert.Equal(PickGuard.None, accounting.MayBegin("a", 1));
        Assert.False(accounting.InFlight);
        Assert.Equal(0, accounting.Taken);
        Assert.Equal(0, accounting.AwaitingConfirmation);
        Assert.Equal(string.Empty, accounting.InFlightSource);
    }
}
