using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>The trip, end to end and in every way it can go wrong.
///
/// Each test names the situation a player would be in rather than the method
/// under test, because the thing being proved is a behaviour: he fetches, he
/// walks, he measures, and when he cannot he stops and says so without losing
/// anything.</summary>
public sealed class UpkeepLoopTests
{
    [Fact]
    public void A_fire_that_needs_wood_is_fetched_for_walked_to_and_fed()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 2f);

        f.Run();
        Assert.Equal(UpkeepPhase.ToDepot, f.Loop.Phase);
        Assert.True(f.Loop.Reservation.IsHeld);

        f.Run();
        Assert.Equal(UpkeepPhase.Withdrawing, f.Loop.Phase);

        f.Run();
        Assert.Equal(UpkeepPhase.ToTarget, f.Loop.Phase);
        Assert.Equal(8, f.Loop.Custody.Carried);
        Assert.Equal(42, f.Depot.Count(StewardFixture.Wood));

        f.Run();
        Assert.Equal(UpkeepPhase.Feeding, f.Loop.Phase);

        // One unit per tick: the work is visible in game rather than
        // instantaneous, and each unit is measured on its own.
        f.Run();
        Assert.Equal(1, f.Loop.Custody.Burned);
        Assert.Equal(7, f.Loop.Custody.Carried);

        f.AssertConserved(startingStock: 50);
        Assert.True(f.Loop.Custody.Balances);
    }

    [Fact]
    public void He_walks_to_the_depot_and_then_to_the_fire_and_never_anywhere_else()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 2f, x: 20f, z: 0f);

        f.Run(4);

        // The depot's marked position, then the fire's. Nothing sets a position;
        // the seam has no member that could.
        Assert.Contains(f.Motion.WalkedTo, point => point.Equals(new SitePoint(1f, 0f, 1f)));
        Assert.Contains(f.Motion.WalkedTo, point => point.Equals(new SitePoint(20f, 0f, 0f)));
    }

    [Fact]
    public void An_already_full_fire_costs_no_wood_at_all()
    {
        var f = new StewardFixture();

        // Ceil(9.5) == 10 == maxFuel, so vanilla refuses. The scan must know
        // that BEFORE anything is withdrawn.
        f.AddFire("full", fuel: 9.5f);

        f.Run(3);

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(50, f.Depot.Count(StewardFixture.Wood));
        Assert.Empty(f.Fires.Fed);
        Assert.Equal(
            FuelTargetStatus.AlreadyFuelled, f.Loop.LastScan.StatusOf(new FuelTargetKey("full", StewardFixture.Epoch)));
    }

    [Fact]
    public void An_empty_depot_stops_him_before_he_reserves_anything()
    {
        var f = new StewardFixture();
        f.Depot.Put(StewardFixture.Wood, 0);
        f.AddFire("fire-1", fuel: 0f);

        f.Run();

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.False(f.Loop.Reservation.IsHeld);
        Assert.Contains("empty", f.Loop.Explanation);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
    }

    [Fact]
    public void A_partial_supply_is_carried_and_the_shortfall_is_not_invented()
    {
        var f = new StewardFixture();
        f.Depot.Put(StewardFixture.Wood, 3);
        f.AddFire("fire-1", fuel: 0f);

        f.Run(3);

        Assert.Equal(3, f.Loop.Custody.Withdrawn);
        Assert.Equal(3, f.Loop.Custody.Carried);
        Assert.Equal(0, f.Depot.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 3);
    }

    [Fact]
    public void A_fire_that_stops_accepting_part_way_sends_the_rest_back_exactly()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);

        // Two units in, the fire declines. Nothing else changes.
        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);

        f.RunUntilTripEnds();

        Assert.Equal(2, f.Loop.Custody.Burned);
        Assert.Equal(8, f.Loop.Custody.Returned);
        Assert.Equal(0, f.Loop.Custody.Carried);
        Assert.Equal(0, f.Loop.Custody.Unaccounted);
        Assert.Equal(48, f.Depot.Count(StewardFixture.Wood));
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_fire_that_is_gone_when_he_arrives_sends_the_wood_back()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);

        f.Run(4);
        Assert.Equal(UpkeepPhase.Feeding, f.Loop.Phase);

        f.Fires.Gone = true;
        f.RunUntilTripEnds();

        Assert.Empty(f.Fires.Fed);
        Assert.Equal(0, f.Loop.Custody.Carried);
        Assert.Equal(10, f.Loop.Custody.Returned);
        f.AssertConserved(startingStock: 50);
    }

    [Theory]
    [InlineData("not owned here")]
    [InlineData("access denied")]
    [InlineData("cannot refill")]
    [InlineData("infinite")]
    [InlineData("outside")]
    [InlineData("stale")]
    public void A_fire_he_may_not_touch_is_never_walked_to(string why)
    {
        var f = new StewardFixture();
        switch (why)
        {
            case "not owned here": f.AddFire("f", 0f, ownedHere: false); break;
            case "access denied": f.AddFire("f", 0f, accessGranted: false); break;
            case "cannot refill": f.AddFire("f", 0f, canRefill: false); break;
            case "infinite": f.AddFire("f", 0f, infiniteFuel: true); break;
            case "outside": f.AddFire("f", 0f, x: 400f, z: 400f); break;
            case "stale": f.AddFire("f", 0f, epoch: "a-previous-session"); break;
        }

        f.Run(3);

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Empty(f.Fires.Fed);
        Assert.Equal(50, f.Depot.Count(StewardFixture.Wood));
    }

    [Fact]
    public void A_fire_that_burns_something_the_chest_does_not_stock_is_left_alone()
    {
        var f = new StewardFixture();
        f.AddFire("coal-fire", fuel: 0f, fuelName: "$item_coal");

        f.Run(2);

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(
            FuelTargetStatus.WrongFuel,
            f.Loop.LastScan.StatusOf(new FuelTargetKey("coal-fire", StewardFixture.Epoch)));
    }

    [Fact]
    public void A_depot_somebody_has_open_is_not_raced()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Depot.Available = false;

        f.Run(2);

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Contains("not reachable", f.Loop.Explanation);
        Assert.Equal(0, f.Fires.SurveyCount);
    }

    // ------------------------------------------------------------------
    // The gates, re-asked before every step rather than once per trip
    // ------------------------------------------------------------------

    [Fact]
    public void Turning_tending_off_mid_trip_stops_him_on_the_next_tick_holding_what_he_has()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(3);
        Assert.Equal(10, f.Loop.Custody.Carried);

        f.TendingEnabled = false;
        f.Run();

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Contains("TendFiresEnabled", f.Loop.Explanation);

        // He still has it, and the ledger still says so. Conservation does not
        // depend on a setting.
        Assert.Equal(10, f.Loop.Custody.Carried);
        f.AssertConserved(startingStock: 50);
    }

    // The verdicts travel as ints because xunit needs a public signature and
    // WorkAuthorityVerdict is internal, as every shared type is.
    [Theory]
    [InlineData((int)WorkAuthorityVerdict.NotHost)]
    [InlineData((int)WorkAuthorityVerdict.OtherPeersConnected)]
    [InlineData((int)WorkAuthorityVerdict.DedicatedServer)]
    [InlineData((int)WorkAuthorityVerdict.RuntimeDisabled)]
    [InlineData((int)WorkAuthorityVerdict.NoWorld)]
    [InlineData((int)WorkAuthorityVerdict.Unspecified)]
    public void Without_authority_nothing_happens_and_the_reason_is_a_sentence(int verdictValue)
    {
        var verdict = (WorkAuthorityVerdict)verdictValue;
        var f = new StewardFixture { Authority = verdict };
        f.AddFire("fire-1", fuel: 0f);

        f.Run(3);

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(WorkAuthorityPolicy.Describe(verdict), f.Loop.Explanation);
    }

    [Fact]
    public void A_body_that_is_not_here_stops_him_and_says_where_to_look()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Motion.IsPresent = false;

        f.Run();

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Contains("not here", f.Loop.Explanation);
    }

    [Fact]
    public void An_unwritable_record_stops_him_before_anything_moves()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Journal.Writable = false;

        f.Run(3);

        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(50, f.Depot.Count(StewardFixture.Wood));
        Assert.Contains("record cannot be written", f.Loop.Explanation);
    }

    // ------------------------------------------------------------------
    // Stale scope
    // ------------------------------------------------------------------

    [Fact]
    public void Re_marking_the_settlement_mid_trip_stops_him_rather_than_finishing_against_the_new_one()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 0f);
        f.Run(3);
        Assert.Equal(UpkeepPhase.ToTarget, f.Loop.Phase);

        f.Scope.Undesignate(DesignationKind.SettlementArea);
        f.Scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(100f, 0f, 100f), 20f),
            new AlwaysGranted());

        f.Run();

        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Empty(f.Fires.Fed);

        // The scope changed, not the wood. He is still holding it and the
        // ledger still says so.
        Assert.Equal(10, f.Loop.Custody.Carried);
        f.AssertConserved(startingStock: 50);
    }

    // ------------------------------------------------------------------
    // Reservation
    // ------------------------------------------------------------------

    [Fact]
    public void Exactly_one_fire_is_reserved_at_a_time_and_it_is_released_when_the_trip_ends()
    {
        var f = new StewardFixture();
        f.AddFire("near-empty", fuel: 0f, x: 4f);
        f.AddFire("nearly-full", fuel: 8f, x: 6f);

        f.Run();
        Assert.True(f.Loop.Reservation.IsHeld);
        Assert.Equal("near-empty", f.Loop.Reservation.Target.Value);

        // A second take, whatever it names, is refused while one is held.
        Assert.Equal(
            ReservationOutcome.Busy,
            f.Loop.Reservation.Take(new FuelTargetKey("nearly-full", StewardFixture.Epoch), "somebody-else", 1));

        // The trip ends; the reservation goes back before the next one can be
        // taken. Asserted at the end of THIS trip, because the Steward starts
        // another straight away and would hold one again.
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        f.RunUntilTripEnds();
        Assert.False(f.Loop.Reservation.IsHeld);
    }

    [Fact]
    public void The_actor_mode_is_held_while_he_works_and_released_when_he_is_done()
    {
        var f = new StewardFixture();
        f.AddFire("fire-1", fuel: 9.5f);

        Assert.Equal(ActorMode.Resting, f.Modes.Mode);
        f.Run();
        Assert.Equal(ActorMode.Resting, f.Modes.Mode);

        f.Fires.Replace(FakeFires.Fuelled(f.AddFire("fire-2", 0f), 0f));
        f.Run();
        Assert.Equal(ActorMode.Working, f.Modes.Mode);
        Assert.False(f.Modes.MayRetireBody);
    }

    // ------------------------------------------------------------------
    // Reporting
    // ------------------------------------------------------------------

    [Fact]
    public void A_long_standing_reason_is_said_once_rather_than_every_tick()
    {
        var f = new StewardFixture();
        f.Depot.Put(StewardFixture.Wood, 0);
        f.AddFire("fire-1", fuel: 0f);

        for (int tick = 0; tick < 60; tick++)
        {
            f.Loop.Tick(f.Tick());
            f.Now += 1f;
        }

        Assert.True(
            f.Reported.Count(line => line.Contains("empty")) <= 3,
            "the empty-chest message was repeated " +
            f.Reported.Count(line => line.Contains("empty")) + " times");
    }

    [Fact]
    public void Idle_scanning_is_throttled_rather_than_run_every_tick()
    {
        var f = new StewardFixture();
        f.AddFire("full", fuel: 10f);

        f.RunSameInstant(50);

        Assert.Equal(1, f.Fires.SurveyCount);
    }
}
