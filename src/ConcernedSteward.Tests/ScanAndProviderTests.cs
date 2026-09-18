using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain.Interop;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Choosing which fire to tend: bounded, deterministic, and never a
/// function of where the player happens to be standing.</summary>
public sealed class TargetScanTests
{
    private const string Wood = "$item_wood";
    private const string Epoch = "epoch-1";

    private static readonly Designation Settlement =
        new Designation(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 64f, null);

    private static readonly SitePoint Depot = new SitePoint(0f, 0f, 0f);

    private static readonly string[] Stocked = { Wood };

    [Fact]
    public void The_emptiest_fire_is_served_first_whatever_its_capacity()
    {
        FuelTargetScan scan = FuelTargetSelector.Scan(
            new[]
            {
                Fire("big-and-half-full", fuel: 20f, maxFuel: 100f),
                Fire("small-and-nearly-out", fuel: 1f, maxFuel: 10f),
                Fire("comfortable", fuel: 8f),
            },
            Settlement, Depot, Stocked, Epoch, UpkeepLimits.Default);

        Assert.Equal("small-and-nearly-out", scan.Next!.Value.Key.Value);
    }

    [Fact]
    public void Ties_are_broken_by_distance_from_the_depot_not_from_the_player()
    {
        // The depot is a designated fixed point. Ordering by distance to the
        // player would reshuffle the queue every time somebody walked across
        // their own base, and "deterministic" would be true of the sort and
        // false of the result.
        FuelTargetScan scan = FuelTargetSelector.Scan(
            new[]
            {
                Fire("far", fuel: 2f, x: 40f),
                Fire("near", fuel: 2f, x: 3f),
                Fire("middling", fuel: 2f, x: 20f),
            },
            Settlement, Depot, Stocked, Epoch, UpkeepLimits.Default);

        Assert.Equal(
            new[] { "near", "middling", "far" },
            scan.Considered.Select(v => v.Target.Key.Value).ToArray());
    }

    [Fact]
    public void Two_identical_fires_are_ordered_by_key_so_the_result_is_total()
    {
        var targets = new[] { Fire("b", 2f, x: 5f), Fire("a", 2f, x: 5f), Fire("c", 2f, x: 5f) };

        // The same answer whichever order the engine enumerated them in.
        FuelTargetScan first = FuelTargetSelector.Scan(
            targets, Settlement, Depot, Stocked, Epoch, UpkeepLimits.Default);
        FuelTargetScan second = FuelTargetSelector.Scan(
            targets.Reverse().ToArray(), Settlement, Depot, Stocked, Epoch, UpkeepLimits.Default);

        Assert.Equal(new[] { "a", "b", "c" }, first.Considered.Select(v => v.Target.Key.Value));
        Assert.Equal(
            first.Considered.Select(v => v.Target.Key.Value),
            second.Considered.Select(v => v.Target.Key.Value));
    }

    [Fact]
    public void A_scan_never_looks_at_more_than_its_bound_and_says_when_it_stopped_short()
    {
        var limits = new UpkeepLimits(
            maxTargetsScanned: 4, maxUnitsPerTrip: 10, maxConcurrentJobs: 1,
            scanIntervalSeconds: 15f, phaseLimitSeconds: 90f, maxFailuresPerPhase: 3);

        var many = new List<FuelTargetObservation>();
        for (int index = 0; index < 40; index++)
        {
            many.Add(Fire("fire-" + index.ToString("00"), fuel: index, x: 5f));
        }

        FuelTargetScan scan = FuelTargetSelector.Scan(
            many, Settlement, Depot, Stocked, Epoch, limits);

        Assert.Equal(4, scan.Examined);
        Assert.Equal(40, scan.Offered);
        Assert.True(scan.Truncated);
    }

    [Fact]
    public void The_bound_is_applied_after_the_sort_so_the_neediest_fires_survive_it()
    {
        var limits = new UpkeepLimits(
            maxTargetsScanned: 2, maxUnitsPerTrip: 10, maxConcurrentJobs: 1,
            scanIntervalSeconds: 15f, phaseLimitSeconds: 90f, maxFailuresPerPhase: 3);

        // The two that need it most are last in the list the engine handed
        // over. Truncating before sorting would drop exactly those.
        FuelTargetScan scan = FuelTargetSelector.Scan(
            new[]
            {
                Fire("full-a", 9f), Fire("full-b", 9f), Fire("empty-a", 0f), Fire("empty-b", 1f),
            },
            Settlement, Depot, Stocked, limits: limits, epoch: Epoch);

        Assert.Equal(
            new[] { "empty-a", "empty-b" },
            scan.Considered.Select(v => v.Target.Key.Value).ToArray());
    }

    [Fact]
    public void Nothing_offered_is_an_empty_scan_rather_than_a_world_sweep()
    {
        Assert.Equal(0, FuelTargetSelector.Scan(
            null, Settlement, Depot, Stocked, Epoch, UpkeepLimits.Default).Examined);
        Assert.Equal(0, FuelTargetSelector.Scan(
            Array.Empty<FuelTargetObservation>(), Settlement, Depot, Stocked, Epoch,
            UpkeepLimits.Default).Examined);
    }

    [Fact]
    public void No_settlement_marked_makes_every_fire_out_of_scope()
    {
        FuelTargetScan scan = FuelTargetSelector.Scan(
            new[] { Fire("fire", 0f) }, null, Depot, Stocked, Epoch, UpkeepLimits.Default);

        Assert.Null(scan.Next);
        Assert.Equal(FuelTargetStatus.OutsideSettlement, scan.Considered[0].Status);
    }

    [Fact]
    public void An_unknown_stock_list_makes_every_fire_ineligible_rather_than_eligible()
    {
        // The fail-closed direction: a chest that could not be read stocks
        // nothing as far as this is concerned.
        FuelTargetScan scan = FuelTargetSelector.Scan(
            new[] { Fire("fire", 0f) }, Settlement, Depot, null, Epoch, UpkeepLimits.Default);

        Assert.Null(scan.Next);
        Assert.Equal(FuelTargetStatus.WrongFuel, scan.Considered[0].Status);
    }

    [Theory]
    [InlineData(0f, 10f, true)]
    [InlineData(9f, 10f, true)]
    [InlineData(9.000001f, 10f, false)]
    [InlineData(9.5f, 10f, false)]
    [InlineData(10f, 10f, false)]
    public void Vanillas_ceiling_comparison_is_reproduced_exactly(
        float fuel, float maxFuel, bool accepts)
    {
        // Writing this as fuel < maxFuel would make the Steward fetch a log for
        // a fire that then refuses it -- not a crash, not in a log, and the
        // wood quietly ends up in the wrong place.
        Assert.Equal(accepts, FuelMath.AcceptsOneUnit(fuel, maxFuel));
    }

    [Fact]
    public void Filling_is_counted_by_stepping_the_way_vanilla_does_not_by_dividing()
    {
        Assert.Equal(10, FuelMath.UnitsToFill(0f, 10f, 10));
        Assert.Equal(1, FuelMath.UnitsToFill(9f, 10f, 10));

        // Dividing would say "0.6 missing, fetch one". Vanilla's CEILING says
        // this fire is full and will refuse, and a Steward who fetched for it
        // would walk a log across the settlement for nothing.
        Assert.Equal(0, FuelMath.UnitsToFill(9.4f, 10f, 10));
        Assert.Equal(0, FuelMath.UnitsToFill(9.5f, 10f, 10));

        // The partial unit is real, but only where capacity is not a whole
        // number: at 10 of 10.5 vanilla accepts and the clamp adds 0.5.
        Assert.Equal(1, FuelMath.UnitsToFill(10f, 10.5f, 10));

        // And it is bounded: an unbounded count is a number this layer promised
        // not to produce.
        Assert.Equal(3, FuelMath.UnitsToFill(0f, 100f, 3));
    }

    [Fact]
    public void The_expected_gain_is_what_vanillas_clamp_would_actually_add()
    {
        Assert.Equal(1f, FuelMath.ExpectedGain(0f, 10f), 3);
        Assert.Equal(1f, FuelMath.ExpectedGain(9f, 10f), 3);
        Assert.Equal(0f, FuelMath.ExpectedGain(9.5f, 10f), 3);

        // A non-integral capacity gives a partial unit at the top, which is why
        // the loop measures instead of predicting.
        Assert.Equal(0.5f, FuelMath.ExpectedGain(10f, 10.5f), 3);
    }

    private static FuelTargetObservation Fire(
        string key, float fuel, float maxFuel = 10f, float x = 5f) =>
        new FuelTargetObservation(
            new FuelTargetKey(key, Epoch), new SitePoint(x, 0f, 0f), Wood, fuel, maxFuel,
            canRefill: true, infiniteFuel: false, ownedHere: true, accessGranted: true);
}

/// <summary>The optional bulk-restock provider: what happens when it is not
/// there, is too old, or speaks something else.
///
/// <b>The point of all of these is that the answer changes nothing.</b> Bulk
/// restocking is not built; the probe exists so the shape of "the other mod is
/// absent or different" is settled while there is nothing behind it. In every
/// case below the Steward carries his own wood exactly as before.</summary>
public sealed class RestockProviderTests
{
    [Fact]
    public void No_hauler_installed_is_an_ordinary_answer_and_not_a_fault()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(RestockProviderLookup.NotFound);

        Assert.Equal(CapabilityStatus.Absent, discovery.Status);
        Assert.False(discovery.IsAvailable);
        Assert.Contains("carries his own wood", discovery.LogLine);
    }

    [Fact]
    public void A_hauler_older_than_the_floor_is_refused_rather_than_tried()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => RestockProviderLookup.Detected(new Version(1, 0, 4), Endpoint()));

        Assert.Equal(CapabilityStatus.VersionTooLow, discovery.Status);
        Assert.Contains("older than", discovery.LogLine);
    }

    [Fact]
    public void A_version_the_registry_could_not_state_is_below_the_floor_never_a_guess()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => RestockProviderLookup.Detected(null, Endpoint()));

        Assert.Equal(CapabilityStatus.VersionTooLow, discovery.Status);
        Assert.Contains("unknown version", discovery.LogLine);
    }

    [Fact]
    public void A_hauler_publishing_a_different_contract_major_is_a_mismatch_not_a_crash()
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major + 1)] = Endpoint()
                [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major)],
        };

        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => RestockProviderLookup.Detected(RestockProviderGate.FloorVersion, map));

        Assert.Equal(CapabilityStatus.MajorMismatch, discovery.Status);
        Assert.False(discovery.IsAvailable);
    }

    [Fact]
    public void A_hauler_publishing_something_unrecognisable_is_a_failed_probe()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => RestockProviderLookup.Detected(
                RestockProviderGate.FloorVersion, "not a capability map", "wrong shape"));

        Assert.Equal(CapabilityStatus.ProbeFailed, discovery.Status);
        Assert.Contains("wrong shape", discovery.LogLine);
    }

    [Fact]
    public void A_probe_that_throws_is_caught_and_answered()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => throw new InvalidOperationException("the registry exploded"));

        Assert.Equal(CapabilityStatus.ProbeFailed, discovery.Status);
        Assert.False(discovery.IsAvailable);
    }

    [Fact]
    public void A_matching_hauler_is_noticed_and_still_changes_nothing_in_this_build()
    {
        RestockDiscovery discovery = RestockProviderGate.Evaluate(
            () => RestockProviderLookup.Detected(new Version(2, 0, 0), Endpoint()));

        Assert.Equal(CapabilityStatus.Available, discovery.Status);
        Assert.True(discovery.IsAvailable);
        Assert.Contains("not built yet", discovery.LogLine);
    }

    [Fact]
    public void An_absent_or_mismatched_hauler_never_gates_the_fire_tending()
    {
        // The evidence that matters most: whatever the probe says, the trip is
        // identical. Nothing in UpkeepLoop consults the discovery at all.
        var without = new StewardFixture();
        without.AddFire("fire-1", fuel: 0f);
        without.Fires.Scripted.Enqueue(FeedOutcome.Declined);
        without.RunUntilTripEnds();

        Assert.Equal(1, without.Loop.TripsCompleted);
        Assert.Equal(50, without.Depot.Count(StewardFixture.Wood));
        without.AssertConserved(startingStock: 50);
    }

    private static Dictionary<string, object> Endpoint()
    {
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> handler =
            _ => new Dictionary<string, string>(StringComparer.Ordinal);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major)] = handler,
        };
    }
}
