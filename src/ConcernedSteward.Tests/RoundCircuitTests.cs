using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>#382 "route an efficient circuit": the bands one tour is walked in.
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

    [Fact]
    public void A_tour_with_time_to_spare_is_one_band()
    {
        LightNeed[] stops = Stops(
            Lights.Light("a", fuel: 2f, x: -14f),
            Lights.Light("b", fuel: 2.5f, x: 18f),
            Lights.Light("c", fuel: 3f, x: 2f));

        int[] bands = RoundCircuit.Bands(Chest, stops, 50);

        Assert.All(bands, band => Assert.Equal(RoundCircuit.InTour, band));
    }

    [Fact]
    public void Only_the_light_that_would_go_out_on_the_way_is_promoted()
    {
        LightNeed[] stops = Stops(
            Lights.Light("near", fuel: 0.5f, x: 18f),
            Lights.Light("far", fuel: 0.5f, x: -40f),
            Lights.Light("mid", fuel: 3f, x: 2f));

        int[] bands = RoundCircuit.Bands(Chest, stops, 50);
        string Band(string key) => bands[System.Array.FindIndex(stops, s => s.Light.Key.Value == key)].ToString();

        // "near" is reached in two seconds with thirty to spare; "far" is reached
        // after about a minute with thirty left.
        Assert.Equal(RoundCircuit.InTour.ToString(), Band("near"));
        Assert.Equal(RoundCircuit.WouldGoOut.ToString(), Band("far"));
        Assert.Equal(RoundCircuit.InTour.ToString(), Band("mid"));
    }

    [Fact]
    public void A_return_to_the_chest_between_trips_counts_towards_going_out()
    {
        // Two lights that each fill her hands. With room for both, neither is
        // promoted; with room for one, the walk the promotion is judged on goes back
        // to the chest before the second, so the second is reached later.
        LightNeed[] stops = Stops(
            Lights.Light("first", fuel: 0.5f, x: 10f),
            Lights.Light("second", fuel: 0.8f, x: 0f));

        Assert.All(RoundCircuit.Bands(Chest, stops, 0), band => Assert.Equal(RoundCircuit.InTour, band));

        var arrivals = RoundCircuit.Walk(Chest, stops, stops.Max(stop => stop.Units)).ToArray();
        Assert.Equal(2, arrivals.Length);
        Assert.True(arrivals[1].Seconds > arrivals[0].Seconds + 20f, "no return trip was counted");
    }

    [Fact]
    public void The_same_lights_in_any_order_give_the_same_walk()
    {
        LightNeed[] stops = Stops(
            Lights.Light("a", fuel: 2f, x: -14f),
            Lights.Light("b", fuel: 2.5f, x: 6f),
            Lights.Light("c", fuel: 3f, x: 6f, z: 0f));
        LightNeed[] reversed = stops.Reverse().ToArray();

        string[] Order(LightNeed[] given) =>
            RoundCircuit.Walk(Chest, given, 50).Select(arrival => given[arrival.Stop].Light.Key.Value).ToArray();

        Assert.Equal(Order(stops), Order(reversed));
    }
}
