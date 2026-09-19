using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>The round loop: the thing that reads what a plan did not cover
/// (#381).
///
/// A batch planner that honestly reports the work it left behind is only half a
/// fix. Without a loop that acts on the report, a large job stops after one
/// round and looks, from the player's side, exactly like the defect the honest
/// report was written to close. These tests are about the other half.</summary>
public sealed class GunnarCollectionRoundTests
{
    private static readonly CollectionLimits Limits = CollectionLimits.Default;

    [Fact]
    public void A_job_bigger_than_one_batch_is_worked_over_several_rounds()
    {
        // Twelve stones and room for four: three rounds, three trips to the
        // chest, and a job that is finished only when the area is clear.
        var world = new FakeWorld(Stones(12), carryKilograms: 8f);

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.AreaCleared, report.Stopped);
        Assert.False(report.NeedsAnotherRound);
        Assert.Equal(12, report.StopsServiced);
        Assert.Equal(12, report.Ledger.AcquiredOf("Stone"));
        Assert.Equal(12, report.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.True(report.RoundsRun >= 3);
    }

    [Fact]
    public void He_travels_to_the_chest_once_a_batch_and_never_once_an_item()
    {
        var world = new FakeWorld(Stones(12), carryKilograms: 8f);

        RoundReport report = CollectionRound.Run(world, Limits);

        // Four stones a trip, so at most one deposit per round and certainly
        // not one per stone.
        Assert.True(world.Deposits <= report.RoundsRun,
            "deposits " + world.Deposits + " exceeded rounds " + report.RoundsRun);
        Assert.True(world.Deposits < report.StopsServiced);
    }

    [Fact]
    public void A_round_that_covered_everything_still_looks_once_more_before_saying_it_is_finished()
    {
        var world = new FakeWorld(Stones(2), carryKilograms: 100f);

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.AreaCleared, report.Stopped);
        Assert.True(world.Surveys >= 2, "a job that never looked twice cannot know it is finished");
    }

    [Fact]
    public void A_job_that_will_not_end_stops_at_the_round_ceiling_and_says_so()
    {
        // A world that grows a new stone for every one he takes. He is allowed
        // to be beaten by it; he is not allowed to stand there forever.
        var world = new FakeWorld(Stones(4), carryKilograms: 8f) { Regrows = true };

        RoundReport report = CollectionRound.Run(world, Limits.With(mostRoundsPerJob: 5));

        Assert.Equal(CollectionStopReason.RoundCeilingReached, report.Stopped);
        Assert.True(report.NeedsAnotherRound);
        Assert.Equal(5, report.RoundsRun);
    }

    [Fact]
    public void Nothing_reachable_ends_in_needs_attention_and_never_in_a_portal()
    {
        // Every stop refuses to be reached. Bounded local recovery is the
        // worker's business and has already happened by the time it answers
        // Unreachable; what this loop does about it is stop and say so.
        var world = new FakeWorld(Stones(6), carryKilograms: 100f) { Answer = StopOutcome.Unreachable };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.NoRouteOnFoot, report.Stopped);
        Assert.True(report.NeedsAnotherRound);
        Assert.Equal(0, report.StopsServiced);
        Assert.Contains("teleport", report.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CollectionRound.BarrenRoundsBeforeStopping, report.RoundsRun);
    }

    [Fact]
    public void A_stop_that_vanished_while_he_walked_costs_the_round_nothing()
    {
        var world = new FakeWorld(Stones(3), carryKilograms: 100f);
        world.GoneKeys.Add("s0");

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.AreaCleared, report.Stopped);
        Assert.Equal(2, report.StopsServiced);
        Assert.Equal(1, report.StopsSkipped);
        Assert.Equal(2, report.Ledger.AcquiredOf("Stone"));
    }

    [Fact]
    public void A_destination_that_refuses_stops_the_job_with_the_material_still_on_him()
    {
        var world = new FakeWorld(Stones(3), carryKilograms: 100f) { Destination = DepositOutcome.Refused };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.DestinationUnavailable, report.Stopped);
        Assert.Equal(3, report.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(0, report.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.True(report.Ledger.IsConserved);
    }

    [Fact]
    public void A_deposit_nobody_could_measure_stops_the_job_and_writes_nothing_off()
    {
        var world = new FakeWorld(Stones(3), carryKilograms: 100f) { Destination = DepositOutcome.Uncertain };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.DestinationUnavailable, report.Stopped);
        Assert.Equal(3, report.Ledger.At("Stone", CargoPlace.Carried));
        Assert.Equal(0, report.Ledger.At("Stone", CargoPlace.Lost));
        Assert.True(report.Ledger.IsConserved);
    }

    [Fact]
    public void An_area_that_cannot_be_read_refuses_and_never_falls_back_to_some_other_radius()
    {
        var world = new FakeWorld(Stones(3), carryKilograms: 100f) { AreaGone = true };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.AreaUnavailable, report.Stopped);
        Assert.Equal(0, report.StopsServiced);
        Assert.Equal(1, report.RoundsRun);
    }

    [Fact]
    public void A_cancelled_order_puts_down_what_it_has_and_stops()
    {
        var world = new FakeWorld(Stones(20), carryKilograms: 8f);
        int calls = 0;

        RoundReport report = CollectionRound.Run(world, Limits, () => ++calls > 6);

        Assert.Equal(CollectionStopReason.Cancelled, report.Stopped);
        Assert.True(report.Ledger.IsConserved);
    }

    [Fact]
    public void Material_is_conserved_after_every_single_step()
    {
        var world = new FakeWorld(Stones(9), carryKilograms: 6f) { CheckConservation = true };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.True(report.Ledger.IsConserved);
        Assert.Equal(report.Ledger.Acquired, report.Ledger.TotalEverywhere);
    }

    // ------------------------------------------------------------------
    // The cart
    // ------------------------------------------------------------------

    [Fact]
    public void A_destroyed_cart_stops_the_haul_preserves_the_accounting_and_invents_nothing()
    {
        var world = new FakeWorld(Stones(6), carryKilograms: 4f)
        {
            Cart = new CartCapacity(assigned: true, freeSlots: 2, slotStackSize: 50),
            DestroyCartAfterStops = 3,
        };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.CartDestroyed, report.Stopped);
        Assert.True(report.NeedsAnotherRound);
        Assert.True(report.Ledger.IsConserved);
        // Nothing replaced what it held, and nothing was re-acquired to make
        // the job whole: every unit he ever took is still accounted for exactly
        // once, and what the cart was carrying is on the ground where it stood.
        Assert.Equal(report.Ledger.Acquired, report.Ledger.TotalEverywhere);
        Assert.Equal(0, report.Ledger.At("Stone", CargoPlace.Delivered));
        Assert.Equal(0, report.Ledger.At("Stone", CargoPlace.InCart));
        Assert.True(report.Ledger.At("Stone", CargoPlace.SpilledFromCart) > 0,
            "the cart was holding something when it was destroyed, and it has to be somewhere");
    }

    [Fact]
    public void Without_a_cart_assigned_a_missing_cart_is_not_an_event()
    {
        var world = new FakeWorld(Stones(3), carryKilograms: 100f) { CartExists = false };

        RoundReport report = CollectionRound.Run(world, Limits);

        Assert.Equal(CollectionStopReason.AreaCleared, report.Stopped);
    }

    [Fact]
    public void There_is_no_verb_anywhere_in_the_seam_that_could_move_him_instantly()
    {
        // "No portals, on foot or with a cart, and never to recover a stuck
        // route." Enforced by shape rather than by discipline: the only thing
        // the round loop can ask for that changes where he is standing is Go,
        // which takes a stop and walks. There is no position, no destination,
        // no warp and no teleport on this seam, so a later change that wanted
        // one would have to widen an interface somebody reads.
        var names = new List<string>();
        foreach (System.Reflection.MemberInfo member in typeof(ICollectionWorker).GetMembers())
        {
            names.Add(member.Name);
        }

        Assert.Equal(
            new[]
            {
                "get_Where", "Survey", "ReadCarry", "ReadCart", "Go", "DepositAll", "CartStillExists", "Where",
            },
            names);
        Assert.All(names, name =>
        {
            Assert.DoesNotContain("teleport", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("portal", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("warp", name, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Where_he_is_can_be_read_and_never_written()
    {
        System.Reflection.PropertyInfo where = typeof(ICollectionWorker).GetProperty("Where")!;

        Assert.True(where.CanRead);
        Assert.False(where.CanWrite);
    }

    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    private static List<CollectionCandidate> Stones(int count)
    {
        var stones = new List<CollectionCandidate>();
        for (int index = 0; index < count; index++)
        {
            stones.Add(new CollectionCandidate(
                "s" + index,
                new CollectionPoint(index + 1, 0f, 0f),
                CollectableVerdict.Eligible(CollectableKind.LooseStone),
                CollectionSiteClause.Unspecified,
                "Stone",
                1,
                2f));
        }

        return stones;
    }

    /// <summary>A world Gunnar can work in, with every knob a test needs to
    /// break it.</summary>
    private sealed class FakeWorld : ICollectionWorker
    {
        private readonly List<CollectionCandidate> _standing;
        private readonly float _carryKilograms;
        private int _grown;
        private float _carried;

        public FakeWorld(List<CollectionCandidate> standing, float carryKilograms)
        {
            _standing = standing;
            _carryKilograms = carryKilograms;
        }

        public HashSet<string> GoneKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

        public StopOutcome Answer { get; set; } = StopOutcome.Taken;

        public DepositOutcome Destination { get; set; } = DepositOutcome.Deposited;

        public CartCapacity Cart { get; set; } = CartCapacity.None;

        public bool CartExists { get; set; } = true;

        public int DestroyCartAfterStops { get; set; } = int.MaxValue;

        public bool AreaGone { get; set; }

        public bool Regrows { get; set; }

        public bool CheckConservation { get; set; }

        public int Surveys { get; private set; }

        public int Deposits { get; private set; }

        public int Stops { get; private set; }

        public CollectionPoint Where { get; private set; }

        public CollectionSurvey Survey()
        {
            Surveys++;
            if (AreaGone)
            {
                return CollectionSurvey.Unavailable("the work area was deleted");
            }

            return new CollectionSurvey(SurveyOutcome.Finished, new List<CollectionCandidate>(_standing));
        }

        public CarryFacts ReadCarry() => new CarryFacts(_carryKilograms, 0f, _carried, beltEquipped: true);

        public CartCapacity ReadCart() => Cart;

        public StopResult Go(CollectionStop stop)
        {
            Stops++;
            Where = stop.Candidate.Where;
            if (Stops >= DestroyCartAfterStops)
            {
                CartExists = false;
            }

            if (GoneKeys.Contains(stop.Candidate.Key))
            {
                _standing.RemoveAll(candidate => candidate.Key == stop.Candidate.Key);
                return StopResult.Did(StopOutcome.Gone);
            }

            if (Answer != StopOutcome.Taken)
            {
                return StopResult.Did(Answer);
            }

            _standing.RemoveAll(candidate => candidate.Key == stop.Candidate.Key);

            // What does not fit on his back goes into the cart, exactly as it
            // would in the world, so the ledger knows where every unit is.
            CargoPlace landed = CargoPlace.Carried;
            if (_carried + stop.Candidate.Kilograms > _carryKilograms && Cart.Assigned)
            {
                landed = CargoPlace.InCart;
            }
            else
            {
                _carried += stop.Candidate.Kilograms;
            }

            if (Regrows)
            {
                _grown++;
                _standing.Add(new CollectionCandidate(
                    "grown" + _grown,
                    new CollectionPoint(20f + _grown, 0f, 0f),
                    CollectableVerdict.Eligible(CollectableKind.LooseStone),
                    CollectionSiteClause.Unspecified,
                    "Stone",
                    1,
                    2f));
            }

            return StopResult.Took(landed);
        }

        public DepositResult DepositAll(IReadOnlyList<KeyValuePair<string, int>> carried)
        {
            Deposits++;
            if (Destination == DepositOutcome.Refused || Destination == DepositOutcome.Uncertain)
            {
                return new DepositResult(Destination, null, "the chest would not take it");
            }

            _carried = 0f;
            return new DepositResult(DepositOutcome.Deposited, carried);
        }

        public bool CartStillExists() => CartExists;
    }
}
