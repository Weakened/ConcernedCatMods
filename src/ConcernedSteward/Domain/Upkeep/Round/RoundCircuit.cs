using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>The order one round is walked in (#382: "route an efficient
/// circuit"), expressed as the routing bands Concerned NPC's planner walks: it
/// finishes a higher band before a lower one, seeds and fills each trip from the
/// highest band left, and orders by distance only inside a band.
///
/// <b>The rule.</b>
/// <list type="number">
/// <item>Urgency decides <i>which</i> lights are in the round: every light under
/// <c>RefuelBelowSeconds</c> is a stop, and when fuel is short the most urgent are
/// the ones funded (<see cref="MaintenanceRound"/>, unchanged).</item>
/// <item>Urgency decides <i>which trip</i> a light is in: the stops, most urgent
/// first, are packed greedily into trips of what she can carry, and each trip gets
/// a band range above every later trip's.</item>
/// <item>Inside a trip, every stop shares one band, so the planner walks them as a
/// short circuit (nearest neighbour, then a bounded two-opt) instead of in strict
/// urgency order, which zigzags.</item>
/// <item>A stop that might not last until its trip is over is promoted above the
/// trip's circuit, and promoted stops keep urgency order among themselves. "Might
/// not last" compares the light's time left with the <i>end</i> of its trip on a
/// deliberately slow estimate - every earlier trip, a walk back to the chest and a
/// collection between trips, <see cref="WalkMetresPerSecond"/>,
/// <see cref="SecondsPerStop"/> at every stop and <see cref="StartAllowanceSeconds"/>
/// to reach the chest at all - so neither the circuit's own reordering nor the
/// two-opt pass can bring a light later than the estimate. Promotion is repeated
/// with the promoted stops walked first until nothing new is promoted.</item>
/// </list>
///
/// So "closest to going out first" still means what a player would want - nothing
/// is left to go out for the sake of a shorter walk - without the walk order being
/// a one-second-wide sort of how much each light has left. Revalidation at each
/// stop is untouched. Game-free and deterministic: ties break on the light's key.
///
/// <b>It relies on the planner keeping these trips.</b> Concerned NPC's partitioner
/// seeds each trip from the highest band left and, below the seed's band, tops it up
/// from the highest band that still fits before the nearest; with every trip packed
/// full, that rebuilds exactly these trips.
///
/// <b>Stated limits.</b> The estimate is an estimate: a light already about to go
/// out when the round is planned can go out anyway; promotion makes it the first
/// thing she walks to, not a guarantee. The planner walks at most eight trips a
/// round (later ones, the least urgent, wait for the next round) and orders at most
/// twenty-four stops in a round, keeping the nearest; in a settlement with more, a
/// light beyond them waits for the next round.</summary>
internal static class RoundCircuit
{
    /// <summary>A slow walk, on purpose: overestimating her time to a light can
    /// only promote it, never leave it to go out.</summary>
    internal const float WalkMetresPerSecond = 1.0f;

    /// <summary>Time spent at each stop before she moves on, generously.</summary>
    internal const float SecondsPerStop = 5f;

    /// <summary>Time allowed for reaching the chest and collecting, before a trip.
    /// </summary>
    internal const float StartAllowanceSeconds = 30f;

    /// <summary>The routing band of each stop, in the order given.</summary>
    /// <param name="chest">The chest every trip starts at and returns to.</param>
    /// <param name="stops">The round's stops, <b>most urgent first</b> (as
    /// <see cref="RoundPlan.Stops"/> is).</param>
    /// <param name="unitsPerTrip">What she can carry at once; zero or less means
    /// everything in one trip.</param>
    internal static int[] Bands(SitePoint chest, IReadOnlyList<LightNeed> stops, int unitsPerTrip)
    {
        if (stops == null)
        {
            throw new ArgumentNullException(nameof(stops));
        }

        var bands = new int[stops.Count];
        List<List<int>> trips = Trips(stops, unitsPerTrip);
        int span = stops.Count + 2;
        float elapsed = StartAllowanceSeconds;

        for (int trip = 0; trip < trips.Count; trip++)
        {
            List<int> members = trips[trip];
            int floor = (trips.Count - trip) * span;
            var promoted = new List<int>();

            float end;
            while (true)
            {
                end = elapsed + TripSeconds(chest, stops, promoted, members, out _);
                var newly = new List<int>();
                foreach (int stop in members)
                {
                    if (!promoted.Contains(stop) && stops[stop].SecondsLeft <= end)
                    {
                        newly.Add(stop);
                    }
                }

                if (newly.Count == 0)
                {
                    break;
                }

                promoted.AddRange(newly);

                // Urgency order: the stops arrive most urgent first.
                promoted.Sort();
            }

            foreach (int stop in members)
            {
                bands[stop] = floor;
            }

            for (int rank = 0; rank < promoted.Count; rank++)
            {
                bands[promoted[rank]] = floor + promoted.Count - rank;
            }

            // Back to the chest from the trip's farthest stop: the planner's own
            // order may end the trip anywhere in it, so this is the bound.
            float back = 0f;
            foreach (int stop in members)
            {
                back = Math.Max(back, stops[stop].Light.Position.HorizontalDistanceTo(chest));
            }

            elapsed = end + (back / WalkMetresPerSecond) + StartAllowanceSeconds;
        }

        return bands;
    }

    /// <summary>The trips, packed first-fit in urgency order: each trip starts
    /// with the most urgent stop left and takes every later stop that still fits
    /// in her hands. So every trip is full - nothing left over could join it - and
    /// the planner, which may top a trip up from a lower band, has nothing to top it
    /// up with that would make the trip longer than estimated. Indices into the
    /// stops given.</summary>
    internal static List<List<int>> Trips(IReadOnlyList<LightNeed> stops, int unitsPerTrip)
    {
        var trips = new List<List<int>>();
        var left = new List<int>();
        for (int index = 0; index < stops.Count; index++)
        {
            left.Add(index);
        }

        while (left.Count > 0)
        {
            var current = new List<int> { left[0] };
            int carried = Math.Max(0, stops[left[0]].Units);
            left.RemoveAt(0);
            for (int index = 0; index < left.Count;)
            {
                int units = Math.Max(0, stops[left[index]].Units);
                if (unitsPerTrip <= 0 || carried + units <= unitsPerTrip)
                {
                    current.Add(left[index]);
                    carried += units;
                    left.RemoveAt(index);
                    continue;
                }

                index++;
            }

            current.Sort();
            trips.Add(current);
        }

        return trips;
    }

    /// <summary>Total walking distance of visiting the stops in the given order
    /// from the chest, for comparing orders.</summary>
    internal static float Length(SitePoint chest, IReadOnlyList<SitePoint> order)
    {
        float total = 0f;
        SitePoint at = chest;
        foreach (SitePoint place in order)
        {
            total += at.HorizontalDistanceTo(place);
            at = place;
        }

        return total;
    }

    /// <summary>How long one trip takes on the slow estimate: the promoted stops
    /// in urgency order, then the rest by nearest neighbour.</summary>
    private static float TripSeconds(
        SitePoint chest, IReadOnlyList<LightNeed> stops, List<int> promoted, List<int> members, out SitePoint last)
    {
        float metres = 0f;
        SitePoint at = chest;
        foreach (int stop in promoted)
        {
            metres += at.HorizontalDistanceTo(stops[stop].Light.Position);
            at = stops[stop].Light.Position;
        }

        var rest = new List<int>();
        foreach (int stop in members)
        {
            if (!promoted.Contains(stop))
            {
                rest.Add(stop);
            }
        }

        while (rest.Count > 0)
        {
            int best = 0;
            for (int index = 1; index < rest.Count; index++)
            {
                float here = at.HorizontalDistanceTo(stops[rest[index]].Light.Position);
                float there = at.HorizontalDistanceTo(stops[rest[best]].Light.Position);
                if (here < there || (here == there
                    && string.CompareOrdinal(stops[rest[index]].Light.Key.Value, stops[rest[best]].Light.Key.Value) < 0))
                {
                    best = index;
                }
            }

            metres += at.HorizontalDistanceTo(stops[rest[best]].Light.Position);
            at = stops[rest[best]].Light.Position;
            rest.RemoveAt(best);
        }

        last = at;
        return (metres / WalkMetresPerSecond) + (members.Count * SecondsPerStop);
    }
}
