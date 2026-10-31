using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>The order one provisioned tour is walked in (#382: "route an
/// efficient circuit"), expressed as the routing bands Concerned NPC's planner
/// walks.
///
/// <b>The rule.</b>
/// <list type="number">
/// <item>Urgency decides <i>which</i> lights are in the round: every light under
/// <c>RefuelBelowSeconds</c> is a stop, and when fuel is short the most urgent are
/// the ones funded (<see cref="MaintenanceRound"/>, unchanged).</item>
/// <item>Within the tour, every stop shares one band, so the planner walks them as a
/// short circuit from the chest (nearest neighbour, then a bounded two-opt) instead
/// of in strict urgency order, which zigzags.</item>
/// <item>A stop that would go out before she reached it on that circuit is promoted
/// to a band of its own, walked first. "Would go out" is judged on a deliberately
/// pessimistic walk of the same circuit: nearest neighbour from the chest, a return
/// to the chest whenever the next stop no longer fits in her hands, a slow
/// <see cref="WalkMetresPerSecond"/> and <see cref="SecondsPerStop"/> spent at every
/// stop before it.</item>
/// </list>
///
/// So "closest to going out first" still means what a player would want - nothing
/// starves for the sake of a shorter walk - without the walk order being a
/// one-second-wide sort of how much each light has left. Revalidation at each stop
/// is untouched. Game-free and deterministic: ties break on the light's key.</summary>
internal static class RoundCircuit
{
    /// <summary>The band every stop of the tour shares.</summary>
    internal const int InTour = 1;

    /// <summary>The band of a stop that would go out before the circuit reached it.
    /// </summary>
    internal const int WouldGoOut = 2;

    /// <summary>A slow walk, on purpose: overestimating her time to a light can only
    /// promote it, never let it starve.</summary>
    internal const float WalkMetresPerSecond = 1.0f;

    /// <summary>Time spent at each stop before she moves on, generously.</summary>
    internal const float SecondsPerStop = 5f;

    /// <summary>The routing band of each stop, in the order given.</summary>
    /// <param name="chest">Where every trip starts and returns to.</param>
    /// <param name="stops">The round's stops.</param>
    /// <param name="unitsPerTrip">What she can carry at once; zero or less means
    /// everything in one trip.</param>
    internal static int[] Bands(SitePoint chest, IReadOnlyList<LightNeed> stops, int unitsPerTrip)
    {
        if (stops == null)
        {
            throw new ArgumentNullException(nameof(stops));
        }

        var bands = new int[stops.Count];
        for (int index = 0; index < bands.Length; index++)
        {
            bands[index] = InTour;
        }

        foreach (Arrival arrival in Walk(chest, stops, unitsPerTrip))
        {
            if (stops[arrival.Stop].SecondsLeft <= arrival.Seconds)
            {
                bands[arrival.Stop] = WouldGoOut;
            }
        }

        return bands;
    }

    /// <summary>The pessimistic walk the promotion is judged on: when each stop is
    /// reached, in visiting order.</summary>
    internal static IReadOnlyList<Arrival> Walk(SitePoint chest, IReadOnlyList<LightNeed> stops, int unitsPerTrip)
    {
        var arrivals = new List<Arrival>(stops.Count);
        var left = new List<int>(stops.Count);
        for (int index = 0; index < stops.Count; index++)
        {
            left.Add(index);
        }

        SitePoint at = chest;
        float elapsed = 0f;
        int carried = 0;
        while (left.Count > 0)
        {
            int next = Nearest(stops, left, at, carried, unitsPerTrip);
            if (next < 0)
            {
                // Nothing left fits in her hands: back to the chest for the next trip.
                elapsed += at.HorizontalDistanceTo(chest) / WalkMetresPerSecond;
                at = chest;
                carried = 0;
                continue;
            }

            int stop = left[next];
            left.RemoveAt(next);
            SitePoint place = stops[stop].Light.Position;
            elapsed += at.HorizontalDistanceTo(place) / WalkMetresPerSecond;
            arrivals.Add(new Arrival(stop, elapsed));
            elapsed += SecondsPerStop;
            carried += Math.Max(0, stops[stop].Units);
            at = place;
        }

        return arrivals;
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

    private static int Nearest(IReadOnlyList<LightNeed> stops, List<int> left, SitePoint at, int carried, int unitsPerTrip)
    {
        int best = -1;
        float bestDistance = 0f;
        for (int index = 0; index < left.Count; index++)
        {
            LightNeed stop = stops[left[index]];
            bool fits = unitsPerTrip <= 0 || carried == 0 || carried + Math.Max(0, stop.Units) <= unitsPerTrip;
            if (!fits)
            {
                continue;
            }

            float distance = at.HorizontalDistanceTo(stop.Light.Position);
            if (best < 0 || distance < bestDistance
                || (distance == bestDistance
                    && string.CompareOrdinal(stop.Light.Key.Value, stops[left[best]].Light.Key.Value) < 0))
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>When the pessimistic walk reaches one stop.</summary>
    internal readonly struct Arrival
    {
        internal Arrival(int stop, float seconds)
        {
            Stop = stop;
            Seconds = seconds;
        }

        /// <summary>Index into the stops given.</summary>
        public int Stop { get; }

        /// <summary>Seconds after leaving the chest.</summary>
        public float Seconds { get; }
    }
}
