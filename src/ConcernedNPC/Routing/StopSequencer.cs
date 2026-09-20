using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>What an errand is: where it starts, everywhere it has to call, and
/// where it should finish.
///
/// <b>The final destination is not a stop.</b> It is where the round ends - the
/// depot, the workbench, the fire - and it is never reordered into the middle of
/// the route. A route that drops the load off halfway and then carries on is a
/// route a player watches and does not believe.</summary>
internal readonly struct StopSequenceRequest
{
    internal StopSequenceRequest(
        NpcPoint from, IReadOnlyList<RouteStop>? stops, bool hasFinalDestination, RouteStop finalDestination)
    {
        From = from;
        Stops = stops ?? Array.Empty<RouteStop>();
        HasFinalDestination = hasFinalDestination;
        FinalDestination = finalDestination;
    }

    /// <summary>Where the NPC is standing when the round starts.</summary>
    internal NpcPoint From { get; }

    /// <summary>Everywhere it has to call, in whatever order the role happened
    /// to produce them. The order in here is not respected and is not meant to
    /// be; it is only ever a tie-break of last resort.</summary>
    internal IReadOnlyList<RouteStop> Stops { get; }

    /// <summary>Whether there is a logical end to the round.</summary>
    internal bool HasFinalDestination { get; }

    /// <summary>Where the round ends. Meaningless unless
    /// <see cref="HasFinalDestination"/>.</summary>
    internal RouteStop FinalDestination { get; }
}

/// <summary>Why a stop is not in the route.</summary>
internal enum StopDropReason
{
    /// <summary>Nobody dropped it.</summary>
    Unspecified = 0,

    /// <summary>It has no name or no place anybody could compute.</summary>
    Unusable = 1,

    /// <summary>The role says it cannot be got to.</summary>
    Unreachable = 2,

    /// <summary>A walk to it failed lately and is still being left alone. Not
    /// the same as unreachable: it will be offered again once the pause is
    /// over.</summary>
    RefusedRecently = 3,

    /// <summary>Two stops with the same name were offered; the second is not a
    /// second stop.</summary>
    Duplicate = 4,

    /// <summary>More stops were offered than one round orders, and this one was
    /// further away than the ones kept. It comes back next round: the sequence
    /// says so with <see cref="StopSequence.Truncated"/>.</summary>
    BeyondTheRound = 5,
}

/// <summary>One stop that is not in the route, and why.</summary>
internal readonly struct DroppedStop
{
    internal DroppedStop(RouteStop stop, StopDropReason reason)
    {
        Stop = stop;
        Reason = reason;
    }

    internal RouteStop Stop { get; }

    internal StopDropReason Reason { get; }
}

/// <summary>Why a sequence is what it is.</summary>
internal enum StopSequenceOutcome
{
    /// <summary>Nobody ordered anything. Never a route.</summary>
    Unspecified = 0,

    /// <summary>A route in the order it should be walked.</summary>
    Ordered = 1,

    /// <summary>Every stop offered was dropped, and there is no final
    /// destination either. Nothing to walk. <b>Not the same as "there is nothing
    /// to do"</b>: <see cref="StopSequence.Dropped"/> says what was refused and
    /// why, and a round whose stops were all refused for a pause will have them
    /// back shortly.</summary>
    NothingToVisit = 2,

    /// <summary>Nothing was offered at all.</summary>
    NothingOffered = 3,
}

/// <summary>A round, in the order it should be walked.</summary>
internal readonly struct StopSequence
{
    private readonly RouteStop[]? _stops;
    private readonly DroppedStop[]? _dropped;

    internal StopSequence(
        StopSequenceOutcome outcome,
        IReadOnlyList<RouteStop>? stops,
        IReadOnlyList<DroppedStop>? dropped,
        float lengthMetres,
        bool truncated,
        bool hasFinalDestination)
    {
        Outcome = outcome;
        LengthMetres = lengthMetres;
        Truncated = truncated;
        HasFinalDestination = hasFinalDestination;
        _stops = Copy(stops);
        _dropped = CopyDropped(dropped);
    }

    internal StopSequenceOutcome Outcome { get; }

    /// <summary>The stops in the order they should be walked, with the final
    /// destination last when there is one.</summary>
    internal IReadOnlyList<RouteStop> Stops => _stops ?? Array.Empty<RouteStop>();

    /// <summary>Everything that was offered and is not in the route, with the
    /// reason for each. <b>Carried rather than discarded</b> because "he did not
    /// go there" and "he could not go there" are different sentences, and the
    /// second one names something the player can fix.</summary>
    internal IReadOnlyList<DroppedStop> Dropped => _dropped ?? Array.Empty<DroppedStop>();

    /// <summary>How far the whole round is, from where the NPC stands, through
    /// every stop, to the end. The number the ordering is judged on.</summary>
    internal float LengthMetres { get; }

    /// <summary>Whether more stops were offered than one round orders. The rest
    /// were not lost: they are in <see cref="Dropped"/> and come back next
    /// round.</summary>
    internal bool Truncated { get; }

    /// <summary>Whether the last stop is the logical end of the round rather
    /// than a call on the way.</summary>
    internal bool HasFinalDestination { get; }

    /// <summary>Whether there is anything to walk.</summary>
    internal bool IsWalkable => Outcome == StopSequenceOutcome.Ordered && Stops.Count > 0;

    private static RouteStop[]? Copy(IReadOnlyList<RouteStop>? source)
    {
        if (source == null || source.Count == 0)
        {
            return null;
        }

        var copy = new RouteStop[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }

    private static DroppedStop[]? CopyDropped(IReadOnlyList<DroppedStop>? source)
    {
        if (source == null || source.Count == 0)
        {
            return null;
        }

        var copy = new DroppedStop[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }
}

/// <summary>Putting the stops of one round into an order a player watching would
/// call intentional.
///
/// <b>This is explicitly not a travelling-salesman solver, and the refusal is
/// the design rather than an apology.</b> The optimum over twenty stops is worth
/// a few metres and costs a search nobody has budgeted for inside a frame. An
/// NPC that hitches for a quarter of a second every few steps is worse than an
/// NPC that walks a little further, so what is built here is bounded,
/// deterministic and visibly sensible, and it says so.
///
/// <b>The four steps, in order.</b>
///
/// <i>Filter.</i> A stop with no name or no computable place is unusable; a stop
/// the role says cannot be got to is dropped; a stop a walk failed at lately is
/// left alone for its pause; a name offered twice is one stop. Everything
/// dropped is reported with its reason rather than silently disappearing.
///
/// <i>Respect priority.</i> Stops are walked in descending priority bands, and a
/// band is finished before the next one starts. Priority beats distance
/// absolutely and on purpose: a role that says one lamp matters more has already
/// weighed the walk, and a router that quietly reordered on distance would be
/// overruling it.
///
/// <i>Nearest neighbour.</i> Within a band, repeatedly walk to the nearest stop
/// not yet taken, starting from where the previous band ended. Ties break on the
/// stop's name, ordinally, which is what makes the same inputs give the same
/// route whatever order they arrived in.
///
/// <i>Cheap local improvement.</i> While a band is small, a bounded two-opt pass
/// unpicks the crossings nearest-neighbour is famous for leaving - the long
/// hop back for the one stop it walked past. Two passes at most, only strictly
/// improving moves, applied in a fixed scan order, so it is deterministic and
/// cannot cycle.
///
/// <b>Cost.</b> Nearest neighbour is <c>O(n^2)</c> distance computations in a
/// band; two-opt is <c>O(passes * n^2)</c>. Both are horizontal distances - no
/// probe, no navigation query, no allocation per candidate - and <c>n</c> is
/// capped at <see cref="MostStopsPerRound"/>, so the whole ordering is bounded
/// above by about <c>3 * MostStopsPerRound^2</c> square roots however pathological
/// the input. At the shipped cap that is a few thousand floating-point
/// operations, once per round, not once per tick.
///
/// <b>What it does not do.</b> It does not ask whether a leg is walkable - that
/// is <see cref="LocalRoutePlanner"/>, leg by leg, because a route ordered
/// against navigation queries would spend one per pair. It does not know what a
/// stop is for. And it plans one body: nothing here knows about a trailing load,
/// a hitch or a turning circle.</summary>
internal static class StopSequencer
{
    /// <summary>How many stops one round is ordered over. More than this and the
    /// nearest are kept and the rest come back next round, because the cost is
    /// quadratic and a player cannot tell forty stops from twenty-four anyway.
    /// </summary>
    internal const int MostStopsPerRound = 24;

    /// <summary>The largest band two-opt is spent on. Above this the crossings
    /// it removes are not worth the passes.</summary>
    internal const int MostStopsImproved = 12;

    /// <summary>How many two-opt passes at most. Bounded rather than "until no
    /// improvement" so the cost is a number rather than a hope.</summary>
    internal const int ImprovementPasses = 2;

    /// <summary>An improvement smaller than this is not one. Without it, float
    /// noise could make two orderings swap places forever and the same inputs
    /// would stop giving the same route.</summary>
    internal const float SmallestImprovementMetres = 0.01f;

    /// <summary>Orders one round.</summary>
    /// <param name="request">Where from, where to call, where to finish.</param>
    /// <param name="setbacks">What walks have failed lately, or null. Shared
    /// across NPCs on purpose - see <see cref="NpcWalkSetbacks"/>.</param>
    /// <param name="now">The caller's clock, in seconds, for judging which
    /// setbacks are still active. Passed in rather than read.</param>
    internal static StopSequence Order(in StopSequenceRequest request, NpcWalkSetbacks? setbacks = null, float now = 0f)
    {
        var dropped = new List<DroppedStop>();
        List<RouteStop> usable = Filter(request, setbacks, now, dropped);

        bool truncated = false;
        if (usable.Count > MostStopsPerRound)
        {
            truncated = true;
            usable = KeepNearest(request.From, usable, dropped);
        }

        bool hasFinal = request.HasFinalDestination && request.FinalDestination.IsValid;
        if (usable.Count == 0)
        {
            if (hasFinal)
            {
                var only = new List<RouteStop> { request.FinalDestination };
                return new StopSequence(
                    StopSequenceOutcome.Ordered,
                    only,
                    dropped,
                    request.From.HorizontalDistanceTo(request.FinalDestination.At),
                    truncated,
                    true);
            }

            StopSequenceOutcome empty = request.Stops.Count == 0
                ? StopSequenceOutcome.NothingOffered
                : StopSequenceOutcome.NothingToVisit;
            return new StopSequence(empty, null, dropped, 0f, truncated, false);
        }

        var ordered = new List<RouteStop>(usable.Count + 1);
        NpcPoint cursor = request.From;

        foreach (List<RouteStop> band in Bands(usable))
        {
            NearestNeighbour(cursor, band);
            if (band.Count <= MostStopsImproved)
            {
                Improve(cursor, band);
            }

            ordered.AddRange(band);
            cursor = band[band.Count - 1].At;
        }

        if (hasFinal)
        {
            ordered.Add(request.FinalDestination);
        }

        return new StopSequence(
            StopSequenceOutcome.Ordered, ordered, dropped, Length(request.From, ordered), truncated, hasFinal);
    }

    /// <summary>How far a walk from <paramref name="from"/> through
    /// <paramref name="stops"/> is, corner to corner. The one number an ordering
    /// is judged on, and the one a test compares against a pathological
    /// ordering.</summary>
    internal static float Length(NpcPoint from, IReadOnlyList<RouteStop>? stops)
    {
        if (stops == null || stops.Count == 0)
        {
            return 0f;
        }

        float total = from.HorizontalDistanceTo(stops[0].At);
        for (int index = 0; index + 1 < stops.Count; index++)
        {
            total += stops[index].At.HorizontalDistanceTo(stops[index + 1].At);
        }

        return total;
    }

    private static List<RouteStop> Filter(
        in StopSequenceRequest request, NpcWalkSetbacks? setbacks, float now, List<DroppedStop> dropped)
    {
        var usable = new List<RouteStop>(request.Stops.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (RouteStop stop in request.Stops)
        {
            if (!stop.IsValid)
            {
                dropped.Add(new DroppedStop(stop, StopDropReason.Unusable));
                continue;
            }

            if (!seen.Add(stop.Key))
            {
                dropped.Add(new DroppedStop(stop, StopDropReason.Duplicate));
                continue;
            }

            if (!stop.IsReachable)
            {
                dropped.Add(new DroppedStop(stop, StopDropReason.Unreachable));
                continue;
            }

            if (setbacks != null && setbacks.RefusesSpot(stop.At, now))
            {
                dropped.Add(new DroppedStop(stop, StopDropReason.RefusedRecently));
                continue;
            }

            usable.Add(stop);
        }

        return usable;
    }

    /// <summary>Keeps the <see cref="MostStopsPerRound"/> nearest to where the
    /// NPC stands and reports the rest as beyond this round. Nearest rather than
    /// first-offered because the order a role produced its targets in is not a
    /// preference, and nearest is the only choice a player can see the sense
    /// in.</summary>
    private static List<RouteStop> KeepNearest(NpcPoint from, List<RouteStop> usable, List<DroppedStop> dropped)
    {
        var byDistance = new List<RouteStop>(usable);
        byDistance.Sort((left, right) =>
        {
            int order = from.HorizontalDistanceTo(left.At).CompareTo(from.HorizontalDistanceTo(right.At));
            return order != 0 ? order : string.CompareOrdinal(left.Key, right.Key);
        });

        var kept = new List<RouteStop>(MostStopsPerRound);
        for (int index = 0; index < byDistance.Count; index++)
        {
            if (index < MostStopsPerRound)
            {
                kept.Add(byDistance[index]);
            }
            else
            {
                dropped.Add(new DroppedStop(byDistance[index], StopDropReason.BeyondTheRound));
            }
        }

        return kept;
    }

    /// <summary>The usable stops split into descending priority bands. A band is
    /// finished before the next one starts.</summary>
    private static List<List<RouteStop>> Bands(List<RouteStop> usable)
    {
        var sorted = new List<RouteStop>(usable);
        sorted.Sort((left, right) =>
        {
            int order = right.Priority.CompareTo(left.Priority);
            return order != 0 ? order : string.CompareOrdinal(left.Key, right.Key);
        });

        var bands = new List<List<RouteStop>>();
        int index = 0;
        while (index < sorted.Count)
        {
            var band = new List<RouteStop> { sorted[index] };
            int priority = sorted[index].Priority;
            index++;
            while (index < sorted.Count && sorted[index].Priority == priority)
            {
                band.Add(sorted[index]);
                index++;
            }

            bands.Add(band);
        }

        return bands;
    }

    /// <summary>Reorders <paramref name="band"/> in place: repeatedly the
    /// nearest stop not yet taken, from where the walk currently is. Ties break
    /// on the name, ordinally, so the same set of stops in a different input
    /// order gives the same route.</summary>
    private static void NearestNeighbour(NpcPoint cursor, List<RouteStop> band)
    {
        for (int taken = 0; taken < band.Count; taken++)
        {
            int nearest = taken;
            float best = cursor.HorizontalDistanceTo(band[taken].At);
            for (int candidate = taken + 1; candidate < band.Count; candidate++)
            {
                float distance = cursor.HorizontalDistanceTo(band[candidate].At);
                if (distance < best ||
                    (distance == best && string.CompareOrdinal(band[candidate].Key, band[nearest].Key) < 0))
                {
                    nearest = candidate;
                    best = distance;
                }
            }

            if (nearest != taken)
            {
                RouteStop swap = band[taken];
                band[taken] = band[nearest];
                band[nearest] = swap;
            }

            cursor = band[taken].At;
        }
    }

    /// <summary>The cheap local improvement: a bounded two-opt over an open
    /// path. Reversing the run between two positions is the move that undoes a
    /// crossing, and on an open path the only legs whose length changes are the
    /// one entering the run and the one leaving it - so each candidate costs
    /// four distance computations rather than a re-measure of the whole route.
    ///
    /// Only strictly improving moves are taken, by more than
    /// <see cref="SmallestImprovementMetres"/>, in a fixed scan order, for at
    /// most <see cref="ImprovementPasses"/> passes. That is what makes it
    /// terminate, and what makes the same inputs give the same route.
    ///
    /// <b>Internal rather than private so it can be tested for what it
    /// claims.</b> Nearest neighbour is good enough on most small inputs that a
    /// test going through <see cref="Order"/> would still pass with this pass
    /// deleted - which would make it unfalsifiable from outside, and a pass
    /// nothing can catch the removal of is a pass nobody should trust. Asked
    /// directly, on an order that does cross itself, it either takes the
    /// crossing out or it does not.</summary>
    internal static void Improve(NpcPoint start, List<RouteStop> band)
    {
        int count = band.Count;
        if (count < 3)
        {
            return;
        }

        for (int pass = 0; pass < ImprovementPasses; pass++)
        {
            bool improved = false;
            for (int first = 0; first + 1 < count; first++)
            {
                for (int last = first + 1; last < count; last++)
                {
                    NpcPoint before = first == 0 ? start : band[first - 1].At;
                    bool hasAfter = last + 1 < count;

                    float was = before.HorizontalDistanceTo(band[first].At);
                    float becomes = before.HorizontalDistanceTo(band[last].At);
                    if (hasAfter)
                    {
                        was += band[last].At.HorizontalDistanceTo(band[last + 1].At);
                        becomes += band[first].At.HorizontalDistanceTo(band[last + 1].At);
                    }

                    if (becomes >= was - SmallestImprovementMetres)
                    {
                        continue;
                    }

                    band.Reverse(first, (last - first) + 1);
                    improved = true;
                }
            }

            if (!improved)
            {
                return;
            }
        }
    }
}
