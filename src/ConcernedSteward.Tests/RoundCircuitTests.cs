using System;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>#382 "route an efficient circuit": the bands one round is walked in.
/// </summary>
public sealed class RoundCircuitTests
{
    private static readonly SitePoint Chest = new SitePoint(20f, 0f, 0f);

    private static LightNeed[] Stops(params Domain.Upkeep.FuelTargetObservation[] lights) =>
        MaintenanceRound.Prepare(
                lights,
                Lights.Settlement(),
                new[] { Lights.Chest("depot", contents: new[] { (Lights.Wood, 500) }) },
                Lights.Epoch,
                MaintenanceThresholds.Default)
            .Stops.ToArray();

    private static int BandOf(LightNeed[] stops, int[] bands, string key) =>
        bands[Array.FindIndex(stops, stop => stop.Light.Key.Value == key)];

    [Fact]
    public void A_tour_with_time_to_spare_is_one_band()
    {
        LightNeed[] stops = Stops(
            Lights.Light("a", fuel: 2f, x: -14f),
            Lights.Light("b", fuel: 2.5f, x: 18f),
            Lights.Light("c", fuel: 3f, x: 2f));

        int[] bands = RoundCircuit.Bands(Chest, stops, 50);

        Assert.Single(bands.Distinct());
    }

    [Fact]
    public void Only_the_light_that_might_not_last_the_trip_is_promoted()
    {
        LightNeed[] stops = Stops(
            Lights.Light("near", fuel: 4f, x: 18f),
            Lights.Light("far", fuel: 0.5f, x: -40f),
            Lights.Light("mid", fuel: 4.5f, x: 2f));

        int[] bands = RoundCircuit.Bands(Chest, stops, 50);

        Assert.True(BandOf(stops, bands, "far") > BandOf(stops, bands, "near"));
        Assert.Equal(BandOf(stops, bands, "near"), BandOf(stops, bands, "mid"));
    }

    [Fact]
    public void Promoted_lights_keep_urgency_order()
    {
        LightNeed[] stops = Stops(
            Lights.Light("ten-seconds", fuel: 1f / 6f, x: -30f),
            Lights.Light("a-minute", fuel: 1f, x: 18f));

        int[] bands = RoundCircuit.Bands(Chest, stops, 50);

        Assert.True(BandOf(stops, bands, "ten-seconds") > BandOf(stops, bands, "a-minute"));
    }

    [Fact]
    public void Every_trip_is_above_every_later_one_and_urgency_fills_the_first()
    {
        LightNeed[] stops = Stops(
            Lights.Light("urgent", fuel: 2f, x: -40f),
            Lights.Light("c28", fuel: 2.5f, x: 28f),
            Lights.Light("c36", fuel: 3f, x: 36f));
        int perLight = stops.Max(stop => stop.Units);

        var trips = RoundCircuit.Trips(stops, perLight);
        int[] bands = RoundCircuit.Bands(Chest, stops, perLight);

        Assert.Equal(3, trips.Count);
        Assert.Equal("urgent", stops[trips[0][0]].Light.Key.Value);
        Assert.True(BandOf(stops, bands, "urgent") > BandOf(stops, bands, "c28"));
        Assert.True(BandOf(stops, bands, "c28") > BandOf(stops, bands, "c36"));
    }

    [Fact]
    public void A_return_to_the_chest_between_trips_can_promote_a_later_light()
    {
        // In one trip nothing is promoted. With room for one light at a time, the
        // less urgent light goes on the second trip and waits for a walk back to the
        // chest and a second collection, which it does not have time for, so it is
        // promoted within its own trip.
        LightNeed[] stops = Stops(
            Lights.Light("later", fuel: 1.5f, x: 10f),
            Lights.Light("sooner", fuel: 1.3f, x: 0f));

        Assert.Single(RoundCircuit.Bands(Chest, stops, 0).Distinct());

        int perLight = stops.Max(stop => stop.Units);
        Assert.Equal(2, RoundCircuit.Trips(stops, perLight).Count);
        int[] apart = RoundCircuit.Bands(Chest, stops, perLight);

        int floorOfSecondTrip = 1 * (stops.Length + 2);
        Assert.True(BandOf(stops, apart, "later") > floorOfSecondTrip, "the late light was not promoted");
        Assert.True(BandOf(stops, apart, "sooner") > BandOf(stops, apart, "later"), "trip order lost");
    }

    [Fact]
    public void Trips_are_full_so_the_planner_has_nothing_to_add()
    {
        // Packed first-fit: a small later stop joins an earlier trip it fits in.
        LightNeed[] stops = Stops(
            Lights.Light("big-a", fuel: 0.1f, x: 0f),
            Lights.Light("big-b", fuel: 0.5f, x: 4f),
            Lights.Light("small", fuel: 4.5f, maxFuel: 6f, x: 8f));

        int small = Array.FindIndex(stops, stop => stop.Light.Key.Value == "small");
        int bigA = Array.FindIndex(stops, stop => stop.Light.Key.Value == "big-a");
        int capacity = stops[bigA].Units + stops[small].Units;

        var trips = RoundCircuit.Trips(stops, capacity);

        Assert.Equal(2, trips.Count);
        Assert.Contains(small, trips[0]);
    }

    [Fact]
    public void The_same_lights_give_the_same_bands()
    {
        LightNeed[] stops = Stops(
            Lights.Light("a", fuel: 2f, x: -14f),
            Lights.Light("b", fuel: 2.5f, x: 6f),
            Lights.Light("c", fuel: 0.2f, x: 6f));

        Assert.Equal(RoundCircuit.Bands(Chest, stops, 50), RoundCircuit.Bands(Chest, stops, 50));
    }
}
