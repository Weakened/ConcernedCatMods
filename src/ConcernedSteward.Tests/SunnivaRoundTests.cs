using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Quest;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Speech;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>#382's acceptance list, proved on the path the plugin actually
/// runs: the shared job driver (<c>sharedDriver: true</c>), the shipped carry,
/// and the thresholds a tick carries. Each test names the property it pins, and
/// each was planted against (the handoff records which plant each one caught).
/// </summary>
public sealed class SunnivaRoundTests
{
    private const string Wood = StewardFixture.Wood;
    private const string Resin = "$item_resin";
    private static readonly SitePoint DepotPoint = new SitePoint(1f, 0f, 1f);

    private static StewardFixture Camp(int carry = UpkeepLimits.ShippedCarryPerTrip) =>
        new StewardFixture(limits: UpkeepLimits.WithCarry(carry), sharedDriver: true);

    private static int Withdrawals(StewardFixture f) =>
        f.Journal.Receipts.Count(r => r.Step == UpkeepStep.Withdraw && r.Moved > 0);

    /// <summary>The order the receipts were written in, one letter a step:
    /// W withdraw, F feed, D deposit.</summary>
    private static string Shape(StewardFixture f) =>
        new string(f.Journal.Receipts.Select(r =>
            r.Step == UpkeepStep.Withdraw ? 'W' : r.Step == UpkeepStep.Feed ? 'F' : 'D').ToArray());

    private static void RunUntilTargeting(StewardFixture f, string key, int cap = 60)
    {
        for (int step = 0; step < cap; step++)
        {
            if (f.Loop.Phase == UpkeepPhase.ToTarget && f.Loop.CurrentTarget.Value == key)
            {
                return;
            }

            f.Run();
        }

        throw new Xunit.Sdk.XunitException(
            "she never set out for " + key + "; she is in " + f.Loop.Phase + " (" + f.Loop.Explanation + ")");
    }

    // ------------------------------------------------------------------
    // One provisioning trip, one manifest
    // ------------------------------------------------------------------

    [Fact]
    public void Five_low_lights_are_one_trip_to_the_chest_at_the_shipped_carry()
    {
        StewardFixture f = Camp();
        for (int index = 0; index < 5; index++)
        {
            f.AddFire("light-" + index, fuel: 1f, x: 4f + (index * 4f), z: 0f);
        }

        f.RunUntilTripEnds(cap: 120);

        Assert.Equal(1, Withdrawals(f));
        Assert.Equal(1, f.Motion.WalkedTo.Count(point => point.Equals(DepotPoint)));
        Assert.Equal(5, f.Fires.Fed.Distinct(StringComparer.Ordinal).Count());

        // Never chest, light, chest, light: every withdrawal precedes every feed.
        Assert.Matches("^W+F+D*$", Shape(f));
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void The_one_withdrawal_is_the_sum_of_what_every_stop_needs()
    {
        StewardFixture f = Camp();
        f.AddFire("nine", fuel: 1f, x: 4f, z: 0f, secondsPerUnit: 60f);   // 9 to reach 10
        f.AddFire("five", fuel: 5f, x: 8f, z: 0f, secondsPerUnit: 30f);   // 5
        f.AddFire("two", fuel: 8f, x: 12f, z: 0f, secondsPerUnit: 30f);   // 2

        f.RunUntilTripEnds(cap: 80);

        UpkeepReceipt withdrawal = Assert.Single(f.Journal.Receipts, r => r.Step == UpkeepStep.Withdraw);
        Assert.Equal(16, withdrawal.Moved);
        Assert.Equal(16, f.Loop.Custody.Burned);
        Assert.Equal(10f, f.Fires.FuelOf("nine"));
        Assert.Equal(10f, f.Fires.FuelOf("five"));
        Assert.Equal(10f, f.Fires.FuelOf("two"));
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void Full_lights_are_never_walked_to_or_fetched_for()
    {
        StewardFixture f = Camp();
        f.AddFire("full-a", fuel: 10f, x: 4f, z: 0f);
        f.AddFire("full-b", fuel: 10f, x: 8f, z: 0f);
        f.AddFire("low", fuel: 1f, x: 12f, z: 0f);

        f.RunUntilTripEnds(cap: 60);

        Assert.All(f.Fires.Fed, key => Assert.Equal("low", key));
        Assert.Equal(9, f.Loop.Custody.Withdrawn);
        Assert.DoesNotContain(f.Motion.WalkedTo, point => point.Equals(new SitePoint(4f, 0f, 0f)));
        Assert.DoesNotContain(f.Motion.WalkedTo, point => point.Equals(new SitePoint(8f, 0f, 0f)));
    }

    [Fact]
    public void The_round_burns_what_the_most_urgent_light_burns_and_no_kind_of_light_starves()
    {
        // The emptiest piece is a hearth with an hour of wood left; the torch
        // beside it has three minutes. Choosing the round's fuel by fuel level
        // picked the hearth, found nothing due, and left the torch to go out.
        StewardFixture f = Camp();
        f.Depot.Put(Resin, 50);
        f.AddFire("slow-hearth", fuel: 2f, x: 4f, z: 0f, secondsPerUnit: 1800f);
        f.AddFire("torch", fuel: 3f, maxFuel: 4f, x: 8f, z: 0f, fuelName: Resin, secondsPerUnit: 60f);

        f.RunUntilTripEnds(cap: 60);

        Assert.Contains("torch", f.Fires.Fed);
        Assert.DoesNotContain("slow-hearth", f.Fires.Fed);
        Assert.Equal(49, f.Depot.Count(Resin));
        Assert.Equal(50, f.Depot.Count(Wood));
    }

    [Fact]
    public void The_thresholds_a_tick_carries_are_the_ones_she_works_to()
    {
        StewardFixture shipped = Camp();
        shipped.AddFire("nine-minutes", fuel: 9f, x: 4f, z: 0f, secondsPerUnit: 60f);
        shipped.Run(6);
        Assert.Empty(shipped.Fires.Fed);

        StewardFixture configured = Camp();
        configured.Thresholds = MaintenanceThresholds.FromSettings(600f, 1200f, 10, out string? refusal);
        Assert.Null(refusal);
        configured.AddFire("nine-minutes", fuel: 9f, x: 4f, z: 0f, secondsPerUnit: 60f);
        configured.RunUntilTripEnds(cap: 40);
        Assert.Equal(new[] { "nine-minutes" }, configured.Fires.Fed);
    }

    // ------------------------------------------------------------------
    // Revalidation at each stop
    // ------------------------------------------------------------------

    [Fact]
    public void A_light_destroyed_while_she_walks_is_skipped_and_its_share_goes_back()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        f.AddFire("destroyed", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "destroyed");
        f.Fires.Remove("destroyed");
        f.RunUntilTripEnds(cap: 40);

        Assert.DoesNotContain("destroyed", f.Fires.Fed);
        Assert.Equal(9, f.Loop.Custody.Returned);
        RoundOutcome outcome = Assert.NotNull(f.Loop.LastOutcome);
        Assert.Equal(1, outcome.Serviced);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(0, outcome.Refused);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_light_a_player_refilled_while_she_walked_is_skipped_and_reconciled_as_such()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        FuelTargetObservation refilled = f.AddFire("refilled", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "refilled");
        f.Fires.Replace(FakeFires.Fuelled(refilled, 10f));
        f.RunUntilTripEnds(cap: 40);

        Assert.DoesNotContain("refilled", f.Fires.Fed);
        Assert.Equal(9, f.Loop.Custody.Returned);
        RoundOutcome outcome = Assert.NotNull(f.Loop.LastOutcome);
        Assert.Equal(1, outcome.Skipped);
        Assert.True(outcome.IsComplete);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_ward_that_goes_up_while_she_walks_is_refused_said_and_never_called_done()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        FuelTargetObservation warded = f.AddFire("warded", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "warded");
        f.Fires.Replace(new FuelTargetObservation(
            warded.Key, warded.Position, warded.FuelItemName, warded.Fuel, warded.MaxFuel,
            warded.CanRefill, warded.InfiniteFuel, warded.OwnedHere, accessGranted: false,
            warded.SecondsPerUnit, warded.IsLit));
        f.RunUntilTripEnds(cap: 40);

        Assert.DoesNotContain("warded", f.Fires.Fed);
        RoundOutcome outcome = Assert.NotNull(f.Loop.LastOutcome);
        Assert.Equal(1, outcome.Refused);
        Assert.False(outcome.IsComplete);
        Assert.True(outcome.HasUnfinishedWork);
        Assert.Contains("she may not touch", outcome.Describe());
        Assert.Contains(f.Spoken, line => line.Contains("not mine to touch", StringComparison.Ordinal));
        Assert.Equal(9, f.Loop.Custody.Returned);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_ward_over_a_later_stop_found_before_she_sets_out_for_it_is_still_reconciled_as_refused()
    {
        // The shared driver revalidates a stop before dispatching it, so a ward
        // that goes up while she is still at the first light is caught there
        // and the stop never reaches this loop. The books must still say she
        // may not touch it - not that it needed nothing, and not that she
        // failed to reach it.
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        FuelTargetObservation later = f.AddFire("later", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "first");
        f.Fires.Replace(new FuelTargetObservation(
            later.Key, later.Position, later.FuelItemName, later.Fuel, later.MaxFuel,
            later.CanRefill, later.InfiniteFuel, later.OwnedHere, accessGranted: false,
            later.SecondsPerUnit, later.IsLit));
        f.RunUntilTripEnds(cap: 40);

        Assert.DoesNotContain("later", f.Fires.Fed);
        RoundOutcome outcome = Assert.NotNull(f.Loop.LastOutcome);
        Assert.Equal(1, outcome.Serviced);
        Assert.Equal(1, outcome.Refused);
        Assert.Equal(0, outcome.NotReached);
        Assert.False(outcome.IsComplete);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_low_light_she_may_not_touch_is_reported_rather_than_left_to_go_out_in_silence()
    {
        StewardFixture f = Camp();
        f.AddFire("warded-low", fuel: 1f, x: 4f, z: 0f, accessGranted: false);
        f.AddFire("warded-full", fuel: 10f, x: 6f, z: 0f, accessGranted: false);
        f.AddFire("warded-outside", fuel: 1f, x: 400f, z: 400f, accessGranted: false);
        f.AddFire("hers", fuel: 1f, x: 8f, z: 0f);

        f.RunUntilTripEnds(cap: 60);

        Assert.Equal(new[] { "hers" }, f.Fires.Fed.Distinct().ToArray());
        Assert.Contains("1 light(s)", f.Loop.Attention);
        Assert.Single(f.Spoken, line => line.Contains("not mine to touch", StringComparison.Ordinal));
        Assert.Contains(f.Spoken, line => line.Contains("One light here is", StringComparison.Ordinal));
    }

    [Fact]
    public void A_light_that_stops_taking_fuel_part_way_is_serviced_with_what_it_took_and_the_rest_returns()
    {
        StewardFixture f = Camp();
        f.AddFire("only", fuel: 1f, x: 4f, z: 0f);
        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);

        f.RunUntilTripEnds(cap: 40);

        Assert.Equal(2, f.Loop.Custody.Burned);
        Assert.Equal(7, f.Loop.Custody.Returned);
        Assert.Equal(48, f.Depot.Count(Wood));
        f.AssertConserved(startingStock: 50);
    }

    // ------------------------------------------------------------------
    // No free fuel
    // ------------------------------------------------------------------

    [Fact]
    public void A_shortage_that_was_met_and_comes_back_is_said_again_at_once()
    {
        StewardFixture f = Camp();
        FuelTargetObservation torch = f.AddFire("torch", fuel: 1f, x: 4f, z: 0f, fuelName: Resin);

        f.Run();
        Assert.Single(f.Spoken, line => line.Contains("I need", StringComparison.Ordinal));

        // A player fills it by hand: nothing is short any more.
        f.Fires.Replace(FakeFires.Fuelled(torch, 10f));
        f.Run();

        // It burns down again well inside the cooldown, and the chest still has
        // no resin: that is a new occasion, and it is said.
        f.Fires.Replace(FakeFires.Fuelled(torch, 1f));
        f.Run();
        Assert.True(f.Now < StewardVoice.DefaultCooldownSeconds);
        Assert.Equal(2, f.Spoken.Count(line => line.Contains("I need", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_carry_smaller_than_one_light_still_tends_it_because_the_light_is_held_to_the_carry()
    {
        StewardFixture f = Camp(carry: 5);
        f.Thresholds = MaintenanceThresholds.FromSettings(
            300f, 1200f, 10, out string? why, carryPerTrip: 5);
        Assert.NotNull(why);
        f.AddFire("campfire", fuel: 1f, x: 4f, z: 0f);

        f.RunUntilTripEnds(cap: 40);

        Assert.Equal(5, f.Loop.Custody.Burned);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_reload_forgets_the_last_worlds_round()
    {
        StewardFixture f = Camp();
        f.AddFire("only", fuel: 1f, x: 4f, z: 0f);
        f.RunUntilTripEnds(cap: 40);
        Assert.NotNull(f.Loop.LastOutcome);

        f.Loop.OnWorldLoaded(f.Pack, Wood);

        Assert.Null(f.Loop.LastOutcome);
        Assert.Equal(string.Empty, f.Loop.Attention);
    }

    [Fact]
    public void Ten_torches_and_no_resin_is_a_sentence_on_screen_that_names_the_resin()
    {
        StewardFixture f = Camp();
        for (int index = 0; index < 10; index++)
        {
            f.AddFire("torch-" + index, fuel: 1f, x: 2f + index, z: 0f, fuelName: Resin);
        }

        f.Run(4);

        Assert.Empty(f.Fires.Fed);
        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(50, f.Depot.Count(Wood));
        string said = Assert.Single(f.Spoken);
        Assert.Contains("I need 90 " + Resin, said);
        Assert.Contains("10 lights", said);
        Assert.Contains("She needs 90 " + Resin, f.Loop.Explanation);
    }

    [Fact]
    public void Half_the_fuel_is_half_a_round_she_says_what_is_missing_and_she_does_not_spin()
    {
        StewardFixture f = Camp();
        f.Depot.Put(Wood, 10);
        f.AddFire("a", fuel: 1f, x: 4f, z: 0f);
        f.AddFire("b", fuel: 1f, x: 8f, z: 0f);
        f.AddFire("c", fuel: 1f, x: 12f, z: 0f);

        f.RunUntilTripEnds(cap: 40);

        // Whole or nothing: one light filled, two waiting, and the shortfall is
        // measured against everything the settlement wants.
        Assert.Equal(9, f.Loop.Custody.Burned);
        RoundOutcome outcome = Assert.NotNull(f.Loop.LastOutcome);
        Assert.Equal(2, outcome.DeferredForMaterial);
        Assert.False(outcome.IsComplete);
        Assert.False(outcome.AnotherRoundWouldHelp);
        Assert.Contains(f.Spoken, line => line.Contains("I need 17 " + Wood, StringComparison.Ordinal));

        // And the shortage is said once per cooldown, not every scan, while it
        // lasts - and not silenced for ever either. It was first said on the
        // first tick, at time zero.
        int before = f.Loop.Custody.Withdrawn;
        while (f.Now < StewardVoice.DefaultCooldownSeconds - 20f)
        {
            f.Run();
        }

        Assert.Equal(before, f.Loop.Custody.Withdrawn);
        Assert.Single(f.Spoken, line => line.Contains("I need", StringComparison.Ordinal));

        while (f.Now < StewardVoice.DefaultCooldownSeconds + 40f)
        {
            f.Run();
        }

        Assert.Equal(2, f.Spoken.Count(line => line.Contains("I need", StringComparison.Ordinal)));
        Assert.Equal(before, f.Loop.Custody.Withdrawn);
        f.AssertConserved(startingStock: 10);
    }

    // ------------------------------------------------------------------
    // Interruption, reload, custody
    // ------------------------------------------------------------------

    [Fact]
    public void Switching_tending_off_halfway_keeps_every_unit_and_nothing_is_replayed()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        f.AddFire("second", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "second");
        int fedBefore = f.Fires.Fed.Count;
        Assert.Equal(9, f.Loop.Custody.Carried);

        f.TendingEnabled = false;
        f.Run(3);
        Assert.Equal(UpkeepPhase.Idle, f.Loop.Phase);
        Assert.Equal(9, f.Loop.Custody.Carried);
        Assert.Equal(fedBefore, f.Fires.Fed.Count);

        f.TendingEnabled = true;
        f.RunUntilTripEnds(cap: 40);
        Assert.Equal(9, f.Loop.Custody.Returned);
        Assert.Equal(0, f.Loop.Custody.Carried);
        f.AssertConserved(startingStock: 50);
    }

    [Fact]
    public void A_reload_halfway_round_returns_the_measured_load_and_the_next_round_finishes_only_what_is_left()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        f.AddFire("second", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "second");
        Assert.Equal(9, f.Fires.Fed.Count(key => key == "first"));
        Assert.Equal(9, f.Loop.Custody.Carried);

        f.Loop.OnWorldLoaded(f.Pack, Wood);
        f.RunUntilTripEnds(cap: 40);
        Assert.Equal(9, f.Loop.Custody.Returned);
        Assert.Equal(41, f.Depot.Count(Wood));

        f.RunUntilTripEnds(cap: 60);
        Assert.Equal(9, f.Fires.Fed.Count(key => key == "first"));
        Assert.Equal(9, f.Fires.Fed.Count(key => key == "second"));
        Assert.Equal(10f, f.Fires.FuelOf("first"));
        Assert.Equal(10f, f.Fires.FuelOf("second"));
        Assert.Equal(32, f.Depot.Count(Wood));
    }

    [Fact]
    public void Across_skips_destruction_and_a_refusing_fire_nothing_is_duplicated_and_nothing_is_lost()
    {
        StewardFixture f = Camp();
        f.AddFire("a", fuel: 1f, x: 4f, z: 0f);
        f.AddFire("b", fuel: 1f, x: 8f, z: 0f);
        f.AddFire("c", fuel: 1f, x: 12f, z: 0f);
        for (int unit = 0; unit < 9; unit++)
        {
            f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        }

        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Accepted);
        f.Fires.Scripted.Enqueue(FeedOutcome.Declined);

        RunUntilTargeting(f, "b");
        f.Fires.Remove("b");
        f.RunUntilTripEnds(cap: 80);

        float gained = (f.Fires.FuelOf("a") - 1f) + (f.Fires.FuelOf("c") - 1f);
        Assert.Equal(f.Loop.Custody.Burned, (int)gained);
        Assert.Equal(11, f.Loop.Custody.Burned);
        Assert.Equal(27, f.Loop.Custody.Withdrawn);
        Assert.Equal(16, f.Loop.Custody.Returned);
        Assert.Equal(0, f.Loop.Custody.Unaccounted);
        f.AssertConserved(startingStock: 50);
    }

    // ------------------------------------------------------------------
    // Approved containers only
    // ------------------------------------------------------------------

    [Fact]
    public void With_the_chest_unmarked_she_takes_from_nothing_at_all()
    {
        StewardFixture f = Camp();
        f.AddFire("low", fuel: 1f, x: 4f, z: 0f);
        f.Scope.Undesignate(DesignationKind.SupplyContainer);

        f.Run(5);

        Assert.Equal(0, f.Loop.Custody.Withdrawn);
        Assert.Equal(50, f.Depot.Count(Wood));
        Assert.Empty(f.Fires.Fed);
    }

    [Fact]
    public void Surplus_goes_back_to_the_marked_chest_or_stays_in_her_hands_and_is_never_dropped()
    {
        StewardFixture f = Camp();
        f.AddFire("first", fuel: 1f, x: 4f, z: 0f);
        FuelTargetObservation refilled = f.AddFire("refilled", fuel: 1f, x: 8f, z: 0f);

        RunUntilTargeting(f, "refilled");
        f.Fires.Replace(FakeFires.Fuelled(refilled, 10f));
        f.Depot.Available = false;
        f.Run(6);

        Assert.Equal(9, f.Loop.Custody.Carried);
        Assert.Equal(9, f.Pack.Count(Wood));
        Assert.Equal(0, f.Loop.Custody.Unaccounted);

        f.Depot.Available = true;
        f.RunUntilTripEnds(cap: 40);
        Assert.Equal(0, f.Pack.Count(Wood));
        Assert.Equal(41, f.Depot.Count(Wood));
        f.AssertConserved(startingStock: 50);
    }

    // ------------------------------------------------------------------
    // One world's quest is that world's
    // ------------------------------------------------------------------

    [Fact]
    public void A_flint_and_steel_found_in_one_world_is_not_found_in_another()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);
        var worldA = new SettlementScope(0xA1L, new SettlementId("home"));
        var worldB = new SettlementScope(0xB2L, new SettlementId("home"));

        Assert.Null(store.Save(
            worldA, IntroductionStage.Unmet, IntroductionStage.Unmet,
            Array.Empty<Designation>(), Array.Empty<UpkeepIntent>(),
            recordedLoss: 0, fuelItemName: string.Empty,
            questStage: StewardQuestStage.Found, questFurthest: StewardQuestStage.Found));

        Assert.Equal(StewardQuestStage.Found, store.Load(worldA).QuestFurthest);
        RecordLoadReport other = store.Load(worldB);
        Assert.Equal(StewardQuestStage.Unstarted, other.QuestStage);
        Assert.Equal(StewardQuestStage.Unstarted, other.QuestFurthest);
    }
}

/// <summary>What she says, and when she says it again.</summary>
public sealed class StewardVoiceTests
{
    [Fact]
    public void A_subject_is_said_once_per_cooldown_whatever_its_latest_wording()
    {
        var said = new List<string>();
        var voice = new StewardVoice((line, weight) => said.Add(line), cooldownSeconds: 100f);

        Assert.True(voice.Say("short:$item_resin", "I need 40", 0f));
        Assert.False(voice.Say("short:$item_resin", "I need 41", 50f));
        Assert.True(voice.Say("short:$item_wood", "I need 9 wood", 50f));
        Assert.True(voice.Say("short:$item_resin", "I need 42", 150f));

        Assert.Equal(new[] { "I need 40", "I need 9 wood", "I need 42" }, said);
    }

    [Fact]
    public void A_subject_that_stopped_being_true_is_said_at_once_when_it_returns()
    {
        var said = new List<string>();
        var voice = new StewardVoice((line, weight) => said.Add(line), cooldownSeconds: 100f);

        voice.Say("untouchable", "first", 0f);
        voice.Forget("untouchable");
        voice.Say("untouchable", "again", 1f);

        Assert.Equal(new[] { "first", "again" }, said);
    }

    [Fact]
    public void Story_beats_are_never_throttled_and_only_their_first_line_may_be_important()
    {
        var said = new List<(string, SpeechWeight)>();
        var voice = new StewardVoice((line, weight) => said.Add((line, weight)));

        voice.Narrate(StewardQuestSentences.LinesFor(StewardQuestStage.Found), SpeechWeight.Important);
        voice.Narrate(StewardQuestSentences.LinesFor(StewardQuestStage.Found), SpeechWeight.Important);

        Assert.Equal(4, said.Count);
        Assert.Equal((StewardQuestSentences.Discovery, SpeechWeight.Important), said[0]);
        Assert.Equal(SpeechWeight.Aside, said[1].Item2);
    }

    [Fact]
    public void A_screen_that_throws_loses_the_line_and_nothing_else()
    {
        var voice = new StewardVoice((line, weight) => throw new InvalidOperationException("no hud"));

        Assert.False(voice.Say("stopped", "she stopped", 0f));
        voice.Narrate(new[] { "a", "b" });
        Assert.Equal(0, voice.LinesSpoken);
    }

    [Fact]
    public void Every_upkeep_sentence_names_her_and_names_the_fuel_by_vanillas_own_token()
    {
        var shortfall = new RoundManifest(new[] { new RoundManifestLine("$item_resin", 40) });

        string line = UpkeepSpeech.Short(shortfall, 10);

        Assert.StartsWith(StewardRole.DisplayNameFallbackCapitalised + ":", line);
        Assert.Contains("I need 40 $item_resin", line);
        Assert.Equal("short:$item_resin", UpkeepSpeech.ShortSubject(shortfall));
        Assert.StartsWith(StewardRole.DisplayNameFallbackCapitalised, UpkeepSpeech.Untouchable(2));
        Assert.StartsWith(StewardRole.DisplayNameFallbackCapitalised, UpkeepSpeech.Stopped("x"));
    }
}

/// <summary>Configuration that cannot make her loop, and a carry that cannot
/// be a typing slip.</summary>
public sealed class StewardConfigurationTests
{
    [Fact]
    public void Thresholds_that_would_make_her_loop_are_refused_in_words_and_the_shipped_ones_used()
    {
        MaintenanceThresholds loop = MaintenanceThresholds.FromSettings(600f, 300f, 10, out string? why);

        Assert.NotNull(why);
        Assert.Contains("RefuelToSeconds", why);
        Assert.Equal(MaintenanceThresholds.Default.RefuelBelowSeconds, loop.RefuelBelowSeconds);
        Assert.Equal(MaintenanceThresholds.Default.RefuelToSeconds, loop.RefuelToSeconds);

        MaintenanceThresholds.FromSettings(0f, 300f, 10, out why);
        Assert.Contains("RefuelBelowSeconds", why);
        MaintenanceThresholds.FromSettings(100f, 300f, 0, out why);
        Assert.Contains("MostUnitsPerLight", why);
    }

    [Fact]
    public void No_light_may_need_more_than_one_trip_carries()
    {
        MaintenanceThresholds held = MaintenanceThresholds.FromSettings(
            300f, 1200f, 10, out string? why, carryPerTrip: 5);

        Assert.Equal(5, held.MostUnitsPerLight);
        Assert.Contains("CarryPerTrip", why);

        MaintenanceThresholds fits = MaintenanceThresholds.FromSettings(
            300f, 1200f, 10, out why, carryPerTrip: 50);
        Assert.Null(why);
        Assert.Equal(10, fits.MostUnitsPerLight);
    }

    [Fact]
    public void Sensible_thresholds_are_used_exactly()
    {
        MaintenanceThresholds used = MaintenanceThresholds.FromSettings(900f, 3600f, 4, out string? why);

        Assert.Null(why);
        Assert.Equal(900f, used.RefuelBelowSeconds);
        Assert.Equal(3600f, used.RefuelToSeconds);
        Assert.Equal(4, used.MostUnitsPerLight);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(100000, UpkeepLimits.MostUnitsPerTripAllowed)]
    public void The_carry_is_clamped_and_everything_else_is_the_shipped_limits(int asked, int carried)
    {
        UpkeepLimits limits = UpkeepLimits.WithCarry(asked);

        Assert.Equal(carried, limits.MaxUnitsPerTrip);
        Assert.Equal(UpkeepLimits.Default.MaxTargetsScanned, limits.MaxTargetsScanned);
        Assert.Equal(UpkeepLimits.Default.ScanIntervalSeconds, limits.ScanIntervalSeconds);
        Assert.Equal(1, limits.MaxConcurrentJobs);
    }

    [Fact]
    public void The_shipped_carry_is_one_vanilla_stack_so_five_low_lights_fit_one_trip()
    {
        Assert.Equal(50, UpkeepLimits.ShippedCarryPerTrip);
        Assert.True(UpkeepLimits.ShippedCarryPerTrip >= 5 * MaintenanceThresholds.Default.MostUnitsPerLight);
    }

    [Fact]
    public void The_owner_chosen_hair_is_vanillas_own_token_for_Matronly()
    {
        // Read from the installed 1.0.14 build's localization table:
        // "customization_hair12","Matronly". A token, never a guessed prefab.
        Assert.Equal("$customization_hair12", StewardLooks.DefaultHairStyle);
    }
}

/// <summary>The reconciler's treatment of a stop she may not touch.</summary>
public sealed class RefusedStopTests
{
    [Fact]
    public void A_refused_stop_is_neither_owed_nor_done_and_is_named()
    {
        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 32f, null);
        var lights = new[]
        {
            Lights.Light("a", fuel: 1f),
            Lights.Light("b", fuel: 1f, x: 8f),
        };
        RoundPlan round = MaintenanceRound.Prepare(
            lights, settlement, new[] { Lights.Chest("chest", contents: (Lights.Wood, 50)) }, Lights.Epoch,
            MaintenanceThresholds.Default);

        RoundOutcome outcome = RoundReconciler.Close(
            round,
            new[]
            {
                new StopResult(round.Stops[0].Light.Key, StopOutcome.Serviced, 9),
                new StopResult(round.Stops[1].Light.Key, StopOutcome.Refused, 0),
            },
            new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 18) }),
            leftForAnotherRound: 0);

        Assert.Equal(1, outcome.Refused);
        Assert.True(outcome.Outstanding.IsEmpty);
        Assert.False(outcome.IsComplete);
        Assert.True(outcome.HasUnfinishedWork);
        Assert.False(outcome.AnotherRoundWouldHelp);
        Assert.Contains("1 she may not touch", outcome.Describe());
    }

    [Fact]
    public void Only_low_lit_lights_inside_the_settlement_count_as_untouchable()
    {
        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 32f, null);
        var lights = new[]
        {
            Lights.Light("warded-low", fuel: 1f, accessGranted: false),
            Lights.Light("warded-full", fuel: 10f, accessGranted: false),
            Lights.Light("warded-out", fuel: 1f, x: 400f, accessGranted: false),
            Lights.Light("warded-unlit", fuel: 1f, accessGranted: false, lit: false),
            Lights.Light("not-ours-low", fuel: 1f, ownedHere: false),
            Lights.Light("hers", fuel: 1f),
        };
        List<LightNeed> judged = LightNeeds.Assess(
            lights, settlement, Lights.Epoch, MaintenanceThresholds.Default);

        Assert.Equal(2, LightNeeds.LowButUntouchable(judged, settlement, MaintenanceThresholds.Default));
        Assert.Equal(0, LightNeeds.LowButUntouchable(judged, null, MaintenanceThresholds.Default));
    }
}

/// <summary>Production reachability: the pieces above have callers in the
/// shipped plugin, not only in this suite.</summary>
public sealed class SunnivaReachabilityTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { ProductSources.Product }.Concat(path).ToArray()));

    [Fact]
    public void The_runtime_gives_her_a_voice_that_reaches_the_screen_and_hands_it_to_the_loop()
    {
        string runtime = ProductSources.WithoutComments(Source("Runtime", "StewardRuntime.cs"));

        Assert.Contains("new StewardVoice(", runtime);
        Assert.Contains("HudSpeech.Say(line, weight)", runtime);
        Assert.Contains("_settings.SpeakOnScreen.Value", runtime);
        Assert.Contains("_npc, _voice)", runtime);
        Assert.Contains("_voice.Narrate(", runtime);
        Assert.Contains("CurrentThresholds()", runtime);
        Assert.Contains("out string? refusal, _loop.Limits.MaxUnitsPerTrip)", runtime);
        Assert.Contains("UpkeepLimits.WithCarry(_settings.CarryPerTrip.Value)", runtime);
        Assert.Contains("hairItem: _settings.HairStyle.Value", runtime);
    }

    [Fact]
    public void The_loop_reconciles_through_the_reconciler_and_says_shortages_through_the_voice()
    {
        string loop = ProductSources.WithoutComments(Source("Domain", "Upkeep", "UpkeepLoop.cs"));

        Assert.Contains("RoundReconciler.Close(", loop);
        Assert.Contains("UpkeepSpeech.Short(", loop);
        Assert.Contains("LightNeeds.LowButUntouchable(", loop);
        Assert.Contains("tick.Thresholds", loop);
        Assert.DoesNotContain("MaintenanceThresholds.Default)", loop);
    }

    [Fact]
    public void The_screen_is_reached_by_name_so_an_appended_parameter_cannot_break_the_plugin()
    {
        foreach (string path in ProductSources.Files(ProductSources.Product))
        {
            string code = ProductSources.WithoutComments(File.ReadAllText(path));
            Assert.DoesNotContain(".ShowMessage(", code);
            Assert.DoesNotContain(".Message(MessageHud", code);
        }

        string hud = ProductSources.WithoutComments(Source("Runtime", "HudSpeech.cs"));
        Assert.Contains("\"ShowMessage\"", hud);
    }

    [Fact]
    public void She_moves_in_only_inside_the_ground_marked_as_home()
    {
        string runtime = ProductSources.WithoutComments(Source("Runtime", "StewardRuntime.cs"));
        int ensure = runtime.IndexOf("private string EnsureBody(", StringComparison.Ordinal);
        int guard = runtime.IndexOf("home.Contains(", ensure, StringComparison.Ordinal);
        int spawn = runtime.IndexOf("StewardWorkerPrefab.Spawn(", ensure, StringComparison.Ordinal);
        Assert.True(ensure > 0, "EnsureBody is gone");

        Assert.True(guard > 0, "the settlement check is gone");
        Assert.True(spawn > guard, "the body is spawned before the settlement is checked");
    }

    [Fact]
    public void Recruitment_is_refused_from_outside_before_any_stage_or_beat_is_recorded()
    {
        string runtime = ProductSources.WithoutComments(Source("Runtime", "StewardRuntime.cs"));
        int recruit = runtime.IndexOf("internal string Recruit()", StringComparison.Ordinal);
        int guard = runtime.IndexOf("!PlayerIsInsideSettlement()", recruit, StringComparison.Ordinal);
        int advance = runtime.IndexOf("_introduction.Advance(", recruit, StringComparison.Ordinal);
        int end = runtime.IndexOf("private string MoveIn(", StringComparison.Ordinal);
        string recruitBody = runtime.Substring(recruit, end - recruit);

        Assert.True(recruit > 0 && guard > recruit, "Recruit no longer asks where the player stands");
        Assert.True(advance > guard, "a stage is recorded before the settlement check");
        Assert.Contains("_census.Verdict != StewardCensusVerdict.Present && !PlayerIsInsideSettlement()", recruitBody);

        // The move-in beat is never said inside Recruit itself, only by MoveIn,
        // and MoveIn says it only for a body that is present - after EnsureBody.
        Assert.DoesNotContain("StewardQuestStage.Settled", recruitBody);
        Assert.Contains("MoveIn(EnsureBody(", recruitBody);
        string moveIn = runtime.Substring(end);
        int present = moveIn.IndexOf("StewardCensusVerdict.Present", StringComparison.Ordinal);
        int beat = moveIn.IndexOf("AdvanceQuest(StewardQuestStage.Settled)", StringComparison.Ordinal);
        Assert.True(present > 0 && beat > present, "the move-in beat is not gated on a present body");
    }
}
