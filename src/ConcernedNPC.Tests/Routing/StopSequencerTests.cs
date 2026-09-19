using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The route the player watches. Not the shortest one - a bounded
/// heuristic that looks intentional and gives the same answer twice.</summary>
public sealed class StopSequencerTests
{
    /// <summary>The same stops in a different order give the same route, corner
    /// for corner.
    ///
    /// <b>Why this is the first test.</b> Everything else in the pipeline rests
    /// on a plan being a pure function of its inputs: an interruption decides
    /// between carrying on and planning again by comparing a fresh plan against
    /// the one it was following, and a router whose answer depended on the order
    /// a dictionary happened to enumerate would make that comparison
    /// meaningless.</summary>
    [Fact]
    public void Identical_inputs_give_an_identical_route()
    {
        List<RouteStop> stops = Scatter(17);
        var shuffled = new List<RouteStop>(stops);
        Rotate(shuffled, 7);
        shuffled.Reverse();

        StopSequence first = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), stops, false, default));
        StopSequence second = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), shuffled, false, default));

        Assert.Equal(Keys(first), Keys(second));
        Assert.Equal(first.LengthMetres, second.LengthMetres, 4);

        // And again, to catch anything that depends on how many times it has
        // been asked.
        StopSequence third = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), stops, false, default));
        Assert.Equal(Keys(first), Keys(third));
    }

    /// <summary>The route is materially better than the order the stops arrived
    /// in, on an input built to be as bad as possible.
    ///
    /// <b>The pathological input</b> is a ring of stops offered alternately from
    /// opposite sides, which is the order a survey that walks a grid actually
    /// produces. Walked as given it crosses the ring every time. The bar here is
    /// deliberately generous - half the pathological length - because the claim
    /// being made is "visibly sensible", not "optimal", and a tight bound would
    /// be a test of the heuristic's luck rather than of its worth.</summary>
    [Fact]
    public void The_route_is_materially_better_than_a_pathological_ordering()
    {
        var ring = new List<RouteStop>();
        const int count = 16;
        for (int index = 0; index < count; index++)
        {
            // Alternate sides: 0, 8, 1, 9, 2, 10 ... round a circle of radius 40.
            int around = ((index % 2) * (count / 2)) + (index / 2);
            double angle = around * 2.0 * Math.PI / count;
            ring.Add(new RouteStop(
                "s" + index, At((float)(40.0 * Math.Cos(angle)), (float)(40.0 * Math.Sin(angle))), 0, true));
        }

        NpcPoint from = At(40f, 0f);
        float pathological = StopSequencer.Length(from, ring);
        StopSequence ordered = StopSequencer.Order(new StopSequenceRequest(from, ring, false, default));

        Assert.Equal(count, ordered.Stops.Count);
        Assert.True(
            ordered.LengthMetres < pathological * 0.5f,
            "the ordered route was " + ordered.LengthMetres + " m against " + pathological + " m as offered");

        // The circumference is the best any route round a ring can do, so this
        // says the heuristic is close to it rather than merely better than
        // terrible.
        Assert.True(ordered.LengthMetres < 2f * (float)Math.PI * 40f * 1.2f);
    }

    /// <summary>Unreachable stops are dropped before the route is built, not
    /// discovered by walking into them - and the route says which were dropped
    /// and why.</summary>
    [Fact]
    public void Unreachable_and_unusable_stops_are_dropped_with_a_reason()
    {
        var stops = new List<RouteStop>
        {
            new RouteStop("good", At(10f, 0f), 0, true),
            new RouteStop("unreachable", At(20f, 0f), 0, false),
            new RouteStop(string.Empty, At(30f, 0f), 0, true),
            new RouteStop("nowhere", new NpcPoint(float.NaN, 0f, 0f), 0, true),
            new RouteStop("good", At(40f, 0f), 0, true),
        };

        StopSequence sequence = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), stops, false, default));

        Assert.Equal(new[] { "good" }, Keys(sequence));
        Assert.Equal(4, sequence.Dropped.Count);
        Assert.Equal(StopDropReason.Unreachable, Reason(sequence, "unreachable"));
        Assert.Equal(StopDropReason.Unusable, Reason(sequence, "nowhere"));
        Assert.Equal(StopDropReason.Duplicate, Reason(sequence, "good"));
    }

    /// <summary>A stop a walk failed at lately is left alone for its pause, and
    /// comes back afterwards. Not the same as unreachable, and the reason says
    /// so.</summary>
    [Fact]
    public void A_stop_a_walk_failed_at_lately_is_left_alone_and_comes_back()
    {
        var setbacks = new NpcWalkSetbacks();
        setbacks.Remember(At(20f, 0f), now: 0f);
        var stops = new List<RouteStop>
        {
            new RouteStop("a", At(10f, 0f), 0, true),
            new RouteStop("b", At(20f, 0f), 0, true),
        };
        var request = new StopSequenceRequest(At(0f, 0f), stops, false, default);

        StopSequence during = StopSequencer.Order(request, setbacks, 30f);
        Assert.Equal(new[] { "a" }, Keys(during));
        Assert.Equal(StopDropReason.RefusedRecently, Reason(during, "b"));

        StopSequence after = StopSequencer.Order(request, setbacks, NpcWalkSetbacks.FirstPauseSeconds + 1f);
        Assert.Equal(new[] { "a", "b" }, Keys(after));
    }

    /// <summary>Priority beats distance absolutely: a high-priority stop on the
    /// far side is visited before a low-priority one underfoot.
    ///
    /// <b>Why absolutely and not as a weight.</b> A role that says one lamp
    /// matters more has already weighed the walk against the urgency. A router
    /// that quietly traded the two would be overruling it with a constant
    /// nobody chose.</summary>
    [Fact]
    public void Priority_is_respected_across_the_whole_round()
    {
        // The names run the other way from the priorities on purpose: an
        // ordering that fell back on the tie-break would come out alphabetical,
        // which here is exactly the wrong answer, so the test cannot pass by
        // accident.
        var stops = new List<RouteStop>
        {
            new RouteStop("a underfoot", At(1f, 0f), 0, true),
            new RouteStop("b nearby", At(2f, 0f), 0, true),
            new RouteStop("y far but urgent", At(90f, 0f), 5, true),
            new RouteStop("z also urgent", At(92f, 0f), 5, true),
        };

        StopSequence sequence = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), stops, false, default));

        Assert.Equal(
            new[] { "y far but urgent", "z also urgent", "b nearby", "a underfoot" }, Keys(sequence));

        // And a middle band goes in the middle, so it is bands rather than
        // "the highest first and then whatever".
        var three = new List<RouteStop>
        {
            new RouteStop("a low", At(1f, 0f), 0, true),
            new RouteStop("b middle", At(50f, 0f), 3, true),
            new RouteStop("c high", At(90f, 0f), 9, true),
        };

        Assert.Equal(
            new[] { "c high", "b middle", "a low" },
            Keys(StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), three, false, default))));
    }

    /// <summary>The round ends where it is supposed to end, and the end is never
    /// reordered into the middle.</summary>
    [Fact]
    public void The_round_ends_at_its_logical_destination()
    {
        var stops = new List<RouteStop>
        {
            new RouteStop("a", At(10f, 0f), 0, true),
            new RouteStop("b", At(20f, 0f), 0, true),
        };
        var depot = new RouteStop("depot", At(1f, 0f), 0, true);

        StopSequence sequence = StopSequencer.Order(
            new StopSequenceRequest(At(0f, 0f), stops, true, depot));

        Assert.Equal(new[] { "a", "b", "depot" }, Keys(sequence));
        Assert.True(sequence.HasFinalDestination);

        // Even with nothing to call at, the walk home is still a walk.
        StopSequence home = StopSequencer.Order(
            new StopSequenceRequest(At(0f, 0f), new List<RouteStop>(), true, depot));
        Assert.Equal(new[] { "depot" }, Keys(home));
    }

    /// <summary>More stops than one round orders keeps the nearest and says the
    /// rest are for next time. Nothing is lost.</summary>
    [Fact]
    public void More_stops_than_one_round_orders_come_back_next_round()
    {
        List<RouteStop> stops = Scatter(StopSequencer.MostStopsPerRound + 6);

        StopSequence sequence = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), stops, false, default));

        Assert.True(sequence.Truncated);
        Assert.Equal(StopSequencer.MostStopsPerRound, sequence.Stops.Count);
        Assert.Equal(6, sequence.Dropped.Count);
        foreach (DroppedStop dropped in sequence.Dropped)
        {
            Assert.Equal(StopDropReason.BeyondTheRound, dropped.Reason);
        }
    }

    /// <summary>Nothing to visit is its own answer, and it is not the same as
    /// nothing having been offered.</summary>
    [Fact]
    public void Nothing_to_visit_is_not_the_same_as_nothing_offered()
    {
        Assert.Equal(
            StopSequenceOutcome.NothingOffered,
            StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), null, false, default)).Outcome);

        var refused = new List<RouteStop> { new RouteStop("a", At(10f, 0f), 0, false) };
        StopSequence sequence = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), refused, false, default));

        Assert.Equal(StopSequenceOutcome.NothingToVisit, sequence.Outcome);
        Assert.False(sequence.IsWalkable);
        Assert.Single(sequence.Dropped);
    }

    /// <summary>The cheap local improvement earns its place: given an order that
    /// crosses itself, it takes the crossing out.
    ///
    /// <b>Why this asks the pass directly instead of going through
    /// <see cref="StopSequencer.Order"/>.</b> Because nearest neighbour is good
    /// enough on most small inputs that a test going the long way round would
    /// pass with the improvement deleted, and a test that passes either way
    /// tests nothing. Here the input is the crossing order itself - two opposite
    /// corners of a square in the wrong sequence - and the pass either turns it
    /// into the four sides or it does not.</summary>
    [Fact]
    public void The_local_improvement_takes_out_a_crossing()
    {
        NpcPoint from = At(-1f, 5f);
        var crossing = new List<RouteStop>
        {
            new RouteStop("a", At(0f, 0f), 0, true),
            new RouteStop("c", At(10f, 10f), 0, true),
            new RouteStop("b", At(10f, 0f), 0, true),
            new RouteStop("d", At(0f, 10f), 0, true),
        };

        float before = StopSequencer.Length(from, crossing);
        StopSequencer.Improve(from, crossing);
        float after = StopSequencer.Length(from, crossing);

        Assert.True(after < before, "the pass left the route at " + after + " m, from " + before + " m");
        Assert.Equal(new[] { "a", "b", "c", "d" }, KeysOf(crossing));
        Assert.Equal(from.HorizontalDistanceTo(At(0f, 0f)) + 30f, after, 3);
    }

    /// <summary>The improvement never makes a route longer, on every scatter and
    /// every rotation of it. A local search that could lengthen would be a
    /// player watching an NPC wander further after thinking about it.</summary>
    [Fact]
    public void The_local_improvement_never_lengthens_a_route()
    {
        for (int size = 3; size <= StopSequencer.MostStopsImproved; size++)
        {
            List<RouteStop> stops = Scatter(size);
            for (int rotation = 0; rotation < size; rotation++)
            {
                var order = new List<RouteStop>(stops);
                Rotate(order, rotation);
                NpcPoint from = At(rotation * 3f, -20f);

                float before = StopSequencer.Length(from, order);
                StopSequencer.Improve(from, order);
                float after = StopSequencer.Length(from, order);

                Assert.True(after <= before + 0.001f);
                Assert.Equal(size, order.Count);
            }
        }
    }

    /// <summary>The improvement is bounded and terminates however unpleasant the
    /// input, and the answer is still the same twice.</summary>
    [Fact]
    public void The_improvement_terminates_on_stops_that_are_all_in_one_place()
    {
        var heap = new List<RouteStop>();
        for (int index = 0; index < StopSequencer.MostStopsImproved; index++)
        {
            heap.Add(new RouteStop("s" + index, At(5f, 5f), 0, true));
        }

        StopSequence first = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), heap, false, default));
        heap.Reverse();
        StopSequence second = StopSequencer.Order(new StopSequenceRequest(At(0f, 0f), heap, false, default));

        Assert.Equal(StopSequencer.MostStopsImproved, first.Stops.Count);
        Assert.Equal(Keys(first), Keys(second));
    }

    private static List<RouteStop> Scatter(int count)
    {
        var stops = new List<RouteStop>(count);
        for (int index = 0; index < count; index++)
        {
            // Deterministic and irregular: a lattice walked at an angle.
            float x = ((index * 37) % 71) - 35f;
            float z = ((index * 53) % 67) - 33f;
            stops.Add(new RouteStop("s" + index, At(x, z), 0, true));
        }

        return stops;
    }

    private static void Rotate(List<RouteStop> stops, int by)
    {
        for (int turn = 0; turn < by; turn++)
        {
            RouteStop first = stops[0];
            stops.RemoveAt(0);
            stops.Add(first);
        }
    }

    private static string[] Keys(StopSequence sequence)
    {
        var keys = new string[sequence.Stops.Count];
        for (int index = 0; index < sequence.Stops.Count; index++)
        {
            keys[index] = sequence.Stops[index].Key;
        }

        return keys;
    }

    private static string[] KeysOf(IReadOnlyList<RouteStop> stops)
    {
        var keys = new string[stops.Count];
        for (int index = 0; index < stops.Count; index++)
        {
            keys[index] = stops[index].Key;
        }

        return keys;
    }

    private static StopDropReason Reason(StopSequence sequence, string key)
    {
        foreach (DroppedStop dropped in sequence.Dropped)
        {
            if (dropped.Stop.Key == key)
            {
                return dropped.Reason;
            }
        }

        return StopDropReason.Unspecified;
    }

    private static NpcPoint At(float x, float z) => new NpcPoint(x, 0f, z);
}
