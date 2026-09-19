using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Arranging a settlement's lights exactly, including the states a
/// live game makes hard to reach: a brazier somebody switched off, a piece that
/// burns nothing, a torch under a ward, a hearth a player filled while she was
/// walking to it.</summary>
internal static class Lights
{
    internal const string Epoch = "world-1";
    internal const string Wood = "$item_wood";
    internal const string Resin = "$item_resin";

    /// <summary>Sixty seconds a unit, so the shipped threshold of five minutes
    /// is exactly five units and every number in these tests can be read
    /// without arithmetic.</summary>
    internal const float SecondsPerUnit = 60f;

    internal static Designation Settlement(float radius = 64f) =>
        new Designation(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), radius, null);

    internal static FuelTargetObservation Light(
        string key,
        float fuel,
        float maxFuel = 10f,
        string item = Wood,
        float secondsPerUnit = SecondsPerUnit,
        float x = 0f,
        float z = 0f,
        bool lit = true,
        bool canRefill = true,
        bool infiniteFuel = false,
        bool ownedHere = true,
        bool accessGranted = true,
        string epoch = Epoch) =>
        new FuelTargetObservation(
            new FuelTargetKey(key, epoch),
            new SitePoint(x, 0f, z),
            item,
            fuel,
            maxFuel,
            canRefill,
            infiniteFuel,
            ownedHere,
            accessGranted,
            secondsPerUnit,
            lit);

    internal static SupplySighting Chest(
        string key,
        SupplyAccess access = SupplyAccess.Both,
        float x = 20f,
        float z = 0f,
        params (string item, int units)[] contents) =>
        new SupplySighting(
            key,
            "the supply chest",
            new SitePoint(x, 0f, z),
            access,
            contents.Select(entry => new SupplyLine(entry.item, entry.units)));
}

/// <summary>What one planned maintenance round is, and what it refuses to be.
/// </summary>
public sealed class MaintenanceRoundTests
{
    private static readonly MaintenanceThresholds Shipped = MaintenanceThresholds.Default;

    // ------------------------------------------------------------------
    // The one the whole issue is about
    // ------------------------------------------------------------------

    /// <summary>Five low torches. The loop this replaces would have walked to
    /// the chest five times, because it worked out what it needed one fire at a
    /// time and never had the total in front of it.</summary>
    [Fact]
    public void Five_low_lights_are_one_storage_trip_rather_than_five()
    {
        var lights = new List<FuelTargetObservation>();
        for (int index = 0; index < 5; index++)
        {
            // One unit left: one minute of light, well under the five-minute
            // threshold, and nine units short of the twenty-minute top-up which
            // its capacity caps at ten.
            lights.Add(Lights.Light("torch-" + index, fuel: 1f, x: index * 4f));
        }

        var chests = new[]
        {
            Lights.Chest("depot", contents: new[] { (Lights.Wood, 90) }),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(), chests, Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);
        Assert.Equal(5, round.Stops.Count);

        // The whole round is ONE requirement, not five.
        RoundManifestLine only = Assert.Single(round.Manifest.Lines);
        Assert.Equal(Lights.Wood, only.Item);
        Assert.Equal(45, only.Units);

        // And with room for the whole of it, that is one visit to the chest.
        Assert.Equal(1, round.StorageTripsNeeded(unitsPerTrip: 50));

        // The failure this replaces, stated as arithmetic so it cannot come
        // back quietly: fetching per light is five visits for the same work.
        Assert.Equal(5, round.Stops.Count);
    }

    [Fact]
    public void A_round_bigger_than_one_load_takes_the_fewest_trips_it_can_and_not_one_a_light()
    {
        var lights = new List<FuelTargetObservation>();
        for (int index = 0; index < 5; index++)
        {
            lights.Add(Lights.Light("torch-" + index, fuel: 1f, x: index * 4f));
        }

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 90) }) },
            Lights.Epoch, Shipped);

        // Forty-five units at twenty a load is three, not five.
        Assert.Equal(3, round.StorageTripsNeeded(unitsPerTrip: 20));
    }

    // ------------------------------------------------------------------
    // The manifest
    // ------------------------------------------------------------------

    [Fact]
    public void The_manifest_is_every_stops_need_added_up_by_item()
    {
        var lights = new[]
        {
            Lights.Light("hearth", fuel: 2f, maxFuel: 10f, item: Lights.Wood),
            Lights.Light("torch-a", fuel: 0f, maxFuel: 4f, item: Lights.Resin, x: 5f),
            Lights.Light("torch-b", fuel: 1f, maxFuel: 4f, item: Lights.Resin, x: 9f),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[]
            {
                Lights.Chest("depot", contents: new[] { (Lights.Wood, 50), (Lights.Resin, 50) }),
            },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);

        // Wood: the hearth wants eight. Resin: four plus three.
        Assert.Equal(8, round.Manifest.UnitsOf(Lights.Wood));
        Assert.Equal(7, round.Manifest.UnitsOf(Lights.Resin));
        Assert.Equal(15, round.Manifest.TotalUnits);

        // Deterministic order, so two identical settlements plan identically.
        Assert.Equal(
            new[] { Lights.Resin, Lights.Wood },
            round.Manifest.Lines.Select(line => line.Item).ToArray());
    }

    [Fact]
    public void The_manifest_counts_only_stops_and_never_the_lights_that_were_excluded()
    {
        var lights = new[]
        {
            Lights.Light("low", fuel: 1f),
            Lights.Light("full", fuel: 10f, x: 5f),
            Lights.Light("off", fuel: 0f, lit: false, x: 9f),
            Lights.Light("eternal", fuel: 0f, secondsPerUnit: 0f, x: 13f),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(9, round.Manifest.TotalUnits);
        Assert.Single(round.Stops);
    }

    // ------------------------------------------------------------------
    // Which lights are excluded, and why
    // ------------------------------------------------------------------

    [Fact]
    public void A_light_with_burning_time_left_is_not_a_stop()
    {
        // Six units at a minute each is six minutes, over the five-minute
        // threshold. Not full, and not due: this is what "prioritise what is
        // closest to going out rather than topping everything to full" means.
        LightNeed need = LightNeeds.Assess(
            Lights.Light("hearth", fuel: 6f), Lights.Settlement(),
            Lights.Epoch, Shipped);

        Assert.Equal(FuelTargetStatus.NotDue, need.Status);
        Assert.False(need.IsAStop);
        Assert.Equal(0, need.Units);
    }

    [Fact]
    public void A_full_light_is_not_a_stop_even_when_its_burn_rate_is_brutal()
    {
        // Full, and ten seconds a unit means it is under the threshold anyway.
        // Vanilla refuses a unit when Ceil(fuel) >= m_maxFuel, so walking there
        // would be a walk for nothing.
        LightNeed need = LightNeeds.Assess(
            Lights.Light("brazier", fuel: 10f, maxFuel: 10f, secondsPerUnit: 10f),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(FuelTargetStatus.AlreadyFuelled, need.Status);
        Assert.False(need.IsAStop);
    }

    [Fact]
    public void A_light_that_burns_nothing_is_never_urgent()
    {
        LightNeed need = LightNeeds.Assess(
            Lights.Light("decorative", fuel: 0f, secondsPerUnit: 0f),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(FuelTargetStatus.NeverConsumes, need.Status);
        Assert.Equal(float.PositiveInfinity, need.SecondsLeft);
    }

    [Fact]
    public void A_light_a_player_switched_off_is_never_the_one_closest_to_going_out()
    {
        // Empty and off. Without the state read this would out-rank every real
        // fire in the settlement, forever.
        LightNeed need = LightNeeds.Assess(
            Lights.Light("brazier", fuel: 0f, lit: false),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(FuelTargetStatus.NotLit, need.Status);
        Assert.False(need.IsAStop);
    }

    /// <summary>The expected verdict is passed as its number rather than as the
    /// enum: <c>FuelTargetStatus</c> is internal to the product, and a public
    /// xUnit theory cannot take an internal parameter type. The numbers are the
    /// enum's own and are stable, which is a second reason to write them out.
    /// </summary>
    [Theory]
    [InlineData(false, true, true, false, 3)]   // NotOwnedHere
    [InlineData(true, false, true, false, 7)]   // AccessDenied
    [InlineData(true, true, false, false, 4)]   // CannotRefill
    [InlineData(true, true, true, true, 5)]     // InfiniteFuel
    public void The_permission_and_capability_refusals_still_come_first(
        bool owned, bool access, bool canRefill, bool infinite, int expected)
    {
        LightNeed need = LightNeeds.Assess(
            Lights.Light(
                "light", fuel: 0f, ownedHere: owned, accessGranted: access,
                canRefill: canRefill, infiniteFuel: infinite),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(expected, (int)need.Status);
        Assert.False(need.IsAStop);
    }

    [Fact]
    public void A_light_outside_the_marked_settlement_is_never_a_stop()
    {
        LightNeed need = LightNeeds.Assess(
            Lights.Light("far", fuel: 0f, x: 500f),
            Lights.Settlement(radius: 32f), Lights.Epoch, Shipped);

        Assert.Equal(FuelTargetStatus.OutsideSettlement, need.Status);
    }

    [Fact]
    public void Nothing_marked_means_nothing_in_scope_and_never_everywhere()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("low", fuel: 0f) }, null,
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.NoScope, round.Verdict);
        Assert.Empty(round.Stops);
    }

    // ------------------------------------------------------------------
    // Urgency
    // ------------------------------------------------------------------

    [Fact]
    public void The_light_closest_to_going_out_is_first_whatever_its_fuel_reads()
    {
        // The torch reads HIGHER than the hearth and has less time left, because
        // it burns four times as fast. A queue ordered by fuel level would send
        // her to the wrong one.
        var lights = new[]
        {
            Lights.Light("hearth", fuel: 2f, secondsPerUnit: 60f),   // 120s
            Lights.Light("torch", fuel: 3f, secondsPerUnit: 15f, x: 5f), // 45s
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        Assert.Equal("torch", round.Stops[0].Light.Key.Value);
        Assert.Equal("hearth", round.Stops[1].Light.Key.Value);
        Assert.True(round.Stops[0].Priority > round.Stops[1].Priority);
    }

    [Fact]
    public void Two_lights_with_the_same_time_left_are_ordered_by_a_tie_break_that_is_total()
    {
        var lights = new[]
        {
            Lights.Light("b", fuel: 1f, x: 1f),
            Lights.Light("a", fuel: 1f, x: 2f),
        };

        RoundPlan first = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);
        RoundPlan second = MaintenanceRound.Prepare(
            lights.Reverse().ToArray(), Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(
            first.Stops.Select(stop => stop.Light.Key.Value).ToArray(),
            second.Stops.Select(stop => stop.Light.Key.Value).ToArray());
        Assert.Equal("a", first.Stops[0].Light.Key.Value);
    }

    // ------------------------------------------------------------------
    // No free fuel
    // ------------------------------------------------------------------

    [Fact]
    public void Ten_torches_and_no_resin_is_a_refusal_that_names_resin()
    {
        var lights = new List<FuelTargetObservation>();
        for (int index = 0; index < 10; index++)
        {
            lights.Add(Lights.Light(
                "torch-" + index, fuel: 0f, maxFuel: 4f, item: Lights.Resin, x: index * 3f));
        }

        // A chest full of wood. Not one unit of what the torches burn.
        var chests = new[]
        {
            Lights.Chest("depot", contents: new[] { (Lights.Wood, 500) }),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(), chests, Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.ShortOfMaterial, round.Verdict);
        Assert.Contains(Lights.Resin, round.Describe());
        Assert.Equal(40, round.Shortfall.UnitsOf(Lights.Resin));
        Assert.Equal(0, round.Shortfall.UnitsOf(Lights.Wood));
    }

    /// <summary>Half the wood for two lights is half a round, not no round.
    ///
    /// <b>The alternative was considered and rejected.</b> Refusing the whole
    /// round because one light cannot be paid for would leave a settlement dark
    /// that had the material to light half of it, and the player's fix — put
    /// more wood in the chest — is the same either way. What must never happen
    /// is that the half-round reports itself finished, which is what
    /// <see cref="RoundPlan.DeferredForMaterial"/> exists to stop.</summary>
    [Fact]
    public void Half_the_material_is_half_a_round_and_the_rest_is_counted()
    {
        var lights = new[]
        {
            Lights.Light("urgent", fuel: 0f),
            Lights.Light("less-urgent", fuel: 2f, x: 5f),
        };

        // Ten units. The empty one wants ten; the other wants eight.
        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 10) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);

        // The most urgent one, taken whole rather than both taken half.
        LightNeed only = Assert.Single(round.Stops);
        Assert.Equal("urgent", only.Light.Key.Value);
        Assert.Equal(10, round.Manifest.UnitsOf(Lights.Wood));

        // And the other is counted, so nothing can call this a finished
        // settlement.
        Assert.Equal(1, round.DeferredForMaterial);
        Assert.Equal(8, round.Shortfall.UnitsOf(Lights.Wood));
        Assert.Contains("1 more will have to wait", round.Describe());
    }

    [Fact]
    public void A_round_it_cannot_pay_for_at_all_is_refused_before_a_single_step()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 3) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.ShortOfMaterial, round.Verdict);
        Assert.Empty(round.Stops);
        Assert.Equal(1, round.DeferredForMaterial);
        Assert.Equal(7, round.Shortfall.UnitsOf(Lights.Wood));
    }

    [Fact]
    public void A_deposit_only_container_is_not_a_source_however_full_it_is()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(),
            new[]
            {
                Lights.Chest("bin", SupplyAccess.Deposit, contents: new[] { (Lights.Wood, 500) }),
            },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.NoSupply, round.Verdict);
    }

    [Fact]
    public void Material_spread_over_two_approved_chests_is_not_a_shortage()
    {
        var chests = new[]
        {
            Lights.Chest("north", contents: new[] { (Lights.Wood, 4) }),
            Lights.Chest("south", x: -20f, contents: new[] { (Lights.Wood, 6) }),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(), chests,
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);
        Assert.Equal(10, round.Manifest.UnitsOf(Lights.Wood));
    }

    /// <summary>She does the wood fire and says what the torch needs.
    ///
    /// The one-fire-at-a-time loop answers this case with "it burns something
    /// the chest does not stock" and stops there. A round that totalled the
    /// settlement first can do better: the torch is a light that needs fuel, it
    /// is counted, and what it needs is named.</summary>
    [Fact]
    public void A_light_burning_something_no_approved_chest_stocks_is_still_counted_and_named()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[]
            {
                Lights.Light("wood-fire", fuel: 0f),
                Lights.Light("resin-torch", fuel: 0f, maxFuel: 4f, item: Lights.Resin, x: 5f),
            },
            Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);
        Assert.Equal("wood-fire", Assert.Single(round.Stops).Light.Key.Value);
        Assert.Equal(1, round.DeferredForMaterial);
        Assert.Equal(4, round.Shortfall.UnitsOf(Lights.Resin));
        Assert.Contains(Lights.Resin, round.Describe());
    }

    // ------------------------------------------------------------------
    // Revalidation, at the stop
    // ------------------------------------------------------------------

    [Fact]
    public void A_light_the_player_refilled_while_she_walked_is_skipped()
    {
        LightNeed planned = LightNeeds.Assess(
            Lights.Light("hearth", fuel: 1f), Lights.Settlement(),
            Lights.Epoch, Shipped);
        Assert.True(planned.IsAStop);

        LightVerdict verdict = LightRevalidation.Look(
            planned,
            Lights.Light("hearth", fuel: 10f),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(LightVerdict.AlreadyDone, verdict);
        Assert.False(LightRevalidation.MeansGo(verdict));

        // And it is settled, so the round is not left owing anything for it.
        Assert.True(LightRevalidation.IsSettled(verdict));
    }

    [Fact]
    public void A_destroyed_light_is_skipped_and_the_fuel_stays_carried()
    {
        LightNeed planned = LightNeeds.Assess(
            Lights.Light("torch", fuel: 0f), Lights.Settlement(),
            Lights.Epoch, Shipped);

        LightVerdict verdict = LightRevalidation.Look(
            planned, null, Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(LightVerdict.Gone, verdict);
        Assert.True(LightRevalidation.IsSettled(verdict));
    }

    [Fact]
    public void A_light_that_is_not_where_it_was_is_deferred_rather_than_acted_on()
    {
        LightNeed planned = LightNeeds.Assess(
            Lights.Light("torch", fuel: 0f), Lights.Settlement(),
            Lights.Epoch, Shipped);

        LightVerdict verdict = LightRevalidation.Look(
            planned, Lights.Light("torch", fuel: 0f, x: 4f),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(LightVerdict.Moved, verdict);

        // Deferred is NOT settled: the light is still wanted, so the round ends
        // owing it and comes back for it.
        Assert.False(LightRevalidation.IsSettled(verdict));
    }

    [Fact]
    public void A_ward_that_went_up_while_she_walked_refuses_the_stop_rather_than_finishing_it()
    {
        LightNeed planned = LightNeeds.Assess(
            Lights.Light("torch", fuel: 0f), Lights.Settlement(),
            Lights.Epoch, Shipped);

        LightVerdict verdict = LightRevalidation.Look(
            planned, Lights.Light("torch", fuel: 0f, accessGranted: false),
            Lights.Settlement(), Lights.Epoch, Shipped);

        Assert.Equal(LightVerdict.Refused, verdict);
        Assert.False(LightRevalidation.IsSettled(verdict));
    }

    // ------------------------------------------------------------------
    // Closing the books
    // ------------------------------------------------------------------

    [Fact]
    public void Fuel_she_did_not_use_is_reported_as_surplus_and_goes_to_a_deposit_container()
    {
        var lights = new[]
        {
            Lights.Light("a", fuel: 0f),
            Lights.Light("b", fuel: 0f, x: 5f),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[]
            {
                Lights.Chest("take-only", SupplyAccess.Take, x: 30f, contents: new[] { (Lights.Wood, 50) }),
                Lights.Chest("bin", SupplyAccess.Both, x: 8f, contents: new[] { (Lights.Wood, 1) }),
            },
            Lights.Epoch, Shipped);

        Assert.Equal(RoundVerdict.Prepared, round.Verdict);
        Assert.Equal(20, round.Manifest.TotalUnits);

        // She fetched the twenty and one light turned out not to need its ten.
        var results = new[]
        {
            new StopResult(round.Stops[0].Light.Key, StopOutcome.Serviced, 10),
            new StopResult(round.Stops[1].Light.Key, StopOutcome.Skipped, 0),
        };

        RoundOutcome outcome = RoundReconciler.Close(
            round, results,
            new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 20) }),
            leftForAnotherRound: 0);

        Assert.Equal(10, outcome.Surplus.UnitsOf(Lights.Wood));
        Assert.True(outcome.IsComplete);

        Assert.True(RoundReconciler.TryChooseDeposit(
            round.Sources, new SitePoint(5f, 0f, 0f), out SupplySighting chosen));
        Assert.Equal("bin", chosen.Key);
    }

    [Fact]
    public void With_nothing_approved_for_deposits_she_keeps_carrying_it_rather_than_dropping_it()
    {
        var sources = new[]
        {
            Lights.Chest("take-only", SupplyAccess.Take, contents: new[] { (Lights.Wood, 50) }),
        };

        Assert.False(RoundReconciler.TryChooseDeposit(
            sources, new SitePoint(0f, 0f, 0f), out _));
    }

    [Fact]
    public void A_skipped_stop_is_not_owed_and_a_failed_one_is()
    {
        var lights = new[]
        {
            Lights.Light("skipped", fuel: 0f),
            Lights.Light("failed", fuel: 0f, x: 5f),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        var results = round.Stops
            .Select(stop => new StopResult(
                stop.Light.Key,
                stop.Light.Key.Value == "skipped" ? StopOutcome.Skipped : StopOutcome.Failed,
                0))
            .ToArray();

        RoundOutcome outcome = RoundReconciler.Close(
            round, results, RoundManifest.Empty, leftForAnotherRound: 0);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(1, outcome.Failed);
        Assert.Equal(10, outcome.Outstanding.UnitsOf(Lights.Wood));
        Assert.False(outcome.IsComplete);
        Assert.True(outcome.HasUnfinishedWork);
    }

    [Fact]
    public void A_stop_nobody_reported_on_is_taken_as_never_reached_rather_than_as_done()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        RoundOutcome outcome = RoundReconciler.Close(
            round, Array.Empty<StopResult>(), RoundManifest.Empty, leftForAnotherRound: 0);

        Assert.Equal(1, outcome.NotReached);
        Assert.False(outcome.IsComplete);
    }

    // ------------------------------------------------------------------
    // LeftForAnotherRound: something reads it, and something acts on it
    // ------------------------------------------------------------------

    [Fact]
    public void A_round_that_covered_part_of_the_settlement_is_never_reported_as_finished()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        // Every step of the plan came off perfectly.
        var results = new[]
        {
            new StopResult(round.Stops[0].Light.Key, StopOutcome.Serviced, 10),
        };

        RoundOutcome outcome = RoundReconciler.Close(
            round, results,
            new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 10) }),
            leftForAnotherRound: 4);

        // ...and it is still not a finished job.
        Assert.False(outcome.IsComplete);
        Assert.True(outcome.HasUnfinishedWork);
        Assert.Equal(4, outcome.LeftForAnotherRound);
        Assert.Contains("4 more light(s)", outcome.Describe());
    }

    /// <summary>The acting, not only the saying. A round that left lights behind
    /// goes straight back out; one that covered everything waits.</summary>
    [Fact]
    public void A_round_that_left_lights_behind_goes_straight_back_out()
    {
        RoundPlan round = MaintenanceRound.Prepare(
            new[] { Lights.Light("a", fuel: 0f) }, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 50) }) },
            Lights.Epoch, Shipped);

        var done = new[] { new StopResult(round.Stops[0].Light.Key, StopOutcome.Serviced, 10) };
        RoundManifest fetched = new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 10) });

        RoundOutcome covered = RoundReconciler.Close(round, done, fetched, leftForAnotherRound: 0);
        RoundOutcome partial = RoundReconciler.Close(round, done, fetched, leftForAnotherRound: 4);

        Assert.Equal(15f, covered.NextRoundDelaySeconds(15f));
        Assert.Equal(0f, partial.NextRoundDelaySeconds(15f));
    }

    /// <summary>The other half of the rule, and the one a loop gets wrong.
    ///
    /// A settlement short of wood has unfinished work for as long as it is short
    /// of wood. Going straight back out answers nothing — the chests hold what
    /// they hold and the next plan reaches the same conclusion — so she waits,
    /// and the fix is a player putting wood in a chest. A runtime keyed on
    /// "is there work left" alone plans a round every tick, for ever.</summary>
    [Fact]
    public void A_round_that_ran_out_of_material_waits_instead_of_spinning()
    {
        var lights = new[]
        {
            Lights.Light("paid-for", fuel: 0f),
            Lights.Light("waiting", fuel: 0f, x: 5f),
        };

        RoundPlan round = MaintenanceRound.Prepare(
            lights, Lights.Settlement(),
            new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 10) }) },
            Lights.Epoch, Shipped);

        Assert.Equal(1, round.DeferredForMaterial);

        RoundOutcome outcome = RoundReconciler.Close(
            round,
            new[] { new StopResult(round.Stops[0].Light.Key, StopOutcome.Serviced, 10) },
            new RoundManifest(new[] { new RoundManifestLine(Lights.Wood, 10) }),
            leftForAnotherRound: 0);

        // The fact: there is a light in this settlement that wants fuel.
        Assert.True(outcome.HasUnfinishedWork);
        Assert.False(outcome.IsComplete);

        // The decision: another round would achieve nothing, so she waits.
        Assert.False(outcome.AnotherRoundWouldHelp);
        Assert.Equal(15f, outcome.NextRoundDelaySeconds(15f));
        Assert.Contains("waiting on fuel", outcome.Describe());
    }

    // ------------------------------------------------------------------
    // The arithmetic underneath
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 10f, 10f, 10, 10)]
    [InlineData(2f, 10f, 10f, 10, 8)]
    [InlineData(2f, 10f, 5f, 10, 3)]
    [InlineData(9.5f, 10f, 10f, 10, 0)]   // vanilla refuses: Ceil(9.5) >= 10
    [InlineData(9f, 10f, 10f, 10, 1)]
    [InlineData(0f, 10f, 10f, 3, 3)]      // the per-light cap
    [InlineData(4f, 10f, 2f, 10, 0)]      // already past the target
    [InlineData(0f, 10f, 40f, 10, 10)]    // a target past capacity is capacity
    public void Topping_up_to_a_target_uses_vanillas_own_arithmetic(
        float fuel, float maxFuel, float target, int ceiling, int expected)
    {
        Assert.Equal(expected, FuelMath.UnitsToReach(fuel, maxFuel, target, ceiling));
    }

    [Fact]
    public void A_piece_that_never_burns_has_all_the_time_there_is()
    {
        Assert.Equal(float.PositiveInfinity, MaintenanceThresholds.SecondsLeft(5f, 0f));
        Assert.Equal(float.PositiveInfinity, MaintenanceThresholds.SecondsLeft(0f, 0f));
        Assert.Equal(300f, MaintenanceThresholds.SecondsLeft(5f, 60f));
    }

    [Fact]
    public void A_threshold_that_would_make_her_loop_is_refused_rather_than_shipped()
    {
        // Servicing a light up to less than the level that made it worth
        // servicing means it wants servicing the moment it is serviced.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MaintenanceThresholds(300f, 200f, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MaintenanceThresholds(0f, 600f, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MaintenanceThresholds(300f, 600f, 0));
    }

    [Fact]
    public void The_thresholds_are_configurable_and_change_what_she_walks_to()
    {
        FuelTargetObservation light = Lights.Light("hearth", fuel: 6f);

        // Shipped: six minutes left, over the five-minute threshold.
        Assert.False(LightNeeds
            .Assess(light, Lights.Settlement(), Lights.Epoch, Shipped)
            .IsAStop);

        // A player who wants her fussier says so.
        var fussy = new MaintenanceThresholds(600f, 1200f, 10);
        LightNeed need = LightNeeds.Assess(
            light, Lights.Settlement(), Lights.Epoch, fussy);
        Assert.True(need.IsAStop);
        Assert.Equal(4, need.Units);
    }
}
