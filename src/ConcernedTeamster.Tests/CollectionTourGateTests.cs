using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>Whether a collection tour may start (#381), how big one load is, and
/// the area it may not look beyond.</summary>
public sealed class CollectionTourGateTests
{
    private static TourRequest Request(
        bool feature = true,
        bool world = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seam = true,
        bool unwritable = false,
        bool unreadable = false,
        bool worker = true,
        bool busy = false,
        bool cart = false,
        bool area = true,
        bool chest = true,
        bool marked = true,
        int capacity = 50) =>
        new TourRequest(feature, world, authority, seam, unwritable, unreadable, worker, busy, cart, area, chest,
            marked, capacity);

    [Fact]
    public void Everything_in_place_starts()
    {
        Assert.Equal(TourRefusal.None, CollectionTourGate.Evaluate(Request()));
    }

    [Fact]
    public void A_defaulted_request_is_refused()
    {
        Assert.Equal(TourRefusal.FeatureOff, CollectionTourGate.Evaluate(default));
    }

    [Fact]
    public void Each_clause_refuses_on_its_own()
    {
        Assert.Equal(TourRefusal.FeatureOff, CollectionTourGate.Evaluate(Request(feature: false)));
        Assert.Equal(TourRefusal.WorldIsGoingAway, CollectionTourGate.Evaluate(Request(world: false)));
        Assert.Equal(TourRefusal.WorkRefused, CollectionTourGate.Evaluate(Request(authority: WorkAuthorityVerdict.OtherPeersConnected)));
        Assert.Equal(TourRefusal.SeamUnavailable, CollectionTourGate.Evaluate(Request(seam: false)));
        Assert.Equal(TourRefusal.WorkerRecordUnwritable, CollectionTourGate.Evaluate(Request(unwritable: true)));
        Assert.Equal(TourRefusal.WorkerRecordUnreadable, CollectionTourGate.Evaluate(Request(unreadable: true)));
        Assert.Equal(TourRefusal.NoWorker, CollectionTourGate.Evaluate(Request(worker: false)));
        Assert.Equal(TourRefusal.AlreadyWorking, CollectionTourGate.Evaluate(Request(busy: true)));
        Assert.Equal(TourRefusal.CartAssigned, CollectionTourGate.Evaluate(Request(cart: true)));
        Assert.Equal(TourRefusal.NoArea, CollectionTourGate.Evaluate(Request(area: false)));
        Assert.Equal(TourRefusal.NoDestination, CollectionTourGate.Evaluate(Request(chest: false)));
        Assert.Equal(TourRefusal.DestinationNotMarked, CollectionTourGate.Evaluate(Request(marked: false)));
        Assert.Equal(TourRefusal.NoCapacity, CollectionTourGate.Evaluate(Request(capacity: 0)));
    }

    [Fact]
    public void No_authority_other_than_granted_starts_a_tour()
    {
        foreach (WorkAuthorityVerdict verdict in Enum.GetValues(typeof(WorkAuthorityVerdict)))
        {
            TourRefusal refusal = CollectionTourGate.Evaluate(Request(authority: verdict));
            Assert.Equal(verdict == WorkAuthorityVerdict.Granted ? TourRefusal.None : TourRefusal.WorkRefused, refusal);
        }
    }

    [Fact]
    public void A_record_he_cannot_trust_is_never_described_as_him_being_absent()
    {
        // The falsehood §6c of GUNNAR_COLLECTION.md fixed twice: a Gunnar
        // standing there told he is not here.
        Assert.DoesNotContain("not here",
            CollectionTourGate.Describe(CollectionTourGate.Evaluate(Request(unwritable: true, worker: false)), WorkAuthorityVerdict.Granted),
            StringComparison.Ordinal);
        Assert.DoesNotContain("not here",
            CollectionTourGate.Describe(CollectionTourGate.Evaluate(Request(unreadable: true, worker: false)), WorkAuthorityVerdict.Granted),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_refusal_has_a_sentence_of_its_own_and_none_offers_a_nearest_chest()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TourRefusal refusal in Enum.GetValues(typeof(TourRefusal)))
        {
            string said = CollectionTourGate.Describe(refusal, WorkAuthorityVerdict.Granted);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said, StringComparison.Ordinal);
            Assert.True(seen.Add(said) || refusal == TourRefusal.WorkRefused, refusal + " shares a sentence");
        }

        Assert.Contains("never looks for the nearest", CollectionTourGate.Describe(TourRefusal.NoDestination, WorkAuthorityVerdict.Granted),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // One load
    // ------------------------------------------------------------------

    [Fact]
    public void A_load_is_the_smaller_of_what_he_can_lift_and_what_he_has_room_for()
    {
        CollectionLimits limits = CollectionLimits.Default.Validate();

        // 300 base + 150 belt, nothing else held, 2 kg a unit: 225 by weight;
        // 32 free slots of 50: 1600 by room.
        Assert.Equal(225, CollectionCapacity.UnitsPerLoad(new CarryFacts(300f, 150f, 0f, true), limits, 2f, 32, 50));

        // Room is the limit: 3 free slots of 50.
        Assert.Equal(150, CollectionCapacity.UnitsPerLoad(new CarryFacts(300f, 150f, 0f, true), limits, 2f, 3, 50));

        // What he holds that is not going in the chest comes off the weight.
        Assert.Equal(200, CollectionCapacity.UnitsPerLoad(new CarryFacts(300f, 150f, 50f, true), limits, 2f, 32, 50));
    }

    [Fact]
    public void A_mixed_load_holds_a_slot_back_for_every_material_after_the_first()
    {
        CollectionLimits limits = CollectionLimits.Default.Validate();
        var carry = new CarryFacts(300f, 150f, 0f, true);

        // Three free slots of fifty: one material fills all three, two materials
        // need a slot each, so only two are free for the bulk of the load.
        Assert.Equal(150, CollectionCapacity.UnitsPerLoad(carry, limits, 2f, 3, 50, materials: 1));
        Assert.Equal(100, CollectionCapacity.UnitsPerLoad(carry, limits, 2f, 3, 50, materials: 2));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(carry, limits, 2f, 1, 50, materials: 2));
    }

    [Fact]
    public void Every_unreadable_number_is_a_load_of_nothing()
    {
        CollectionLimits limits = CollectionLimits.Default.Validate();
        var carry = new CarryFacts(300f, 150f, 0f, true);
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(carry, limits, 0f, 32, 50));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(carry, limits, float.NaN, 32, 50));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(carry, limits, 2f, 0, 50));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(carry, limits, 2f, 32, 0));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(new CarryFacts(float.NaN, 0f, 0f, false), limits, 2f, 32, 50));
        Assert.Equal(0, CollectionCapacity.UnitsPerLoad(new CarryFacts(300f, 0f, 400f, false), limits, 2f, 32, 50));
    }

    // ------------------------------------------------------------------
    // The area
    // ------------------------------------------------------------------

    [Fact]
    public void The_area_is_clamped_to_the_work_area_bounds_and_never_widened()
    {
        Assert.True(CollectionArea.TryCreate(new CollectionPoint(10f, 2f, 10f), 500f, 1, out CollectionArea? big, out _));
        Assert.Equal(CollectionArea.LargestRadiusMetres, big!.BoundingRadiusMetres);

        Assert.True(CollectionArea.TryCreate(new CollectionPoint(10f, 2f, 10f), 0.5f, 2, out CollectionArea? small, out _));
        Assert.Equal(CollectionArea.SmallestRadiusMetres, small!.BoundingRadiusMetres);

        Assert.True(CollectionArea.TryCreate(new CollectionPoint(10f, 2f, 10f), float.NaN, 3, out CollectionArea? unread, out _));
        Assert.Equal(CollectionArea.DefaultRadiusMetres, unread!.BoundingRadiusMetres);
    }

    [Fact]
    public void Containment_is_flat_distance_from_where_the_player_stood()
    {
        Assert.True(CollectionArea.TryCreate(new CollectionPoint(0f, 0f, 0f), 10f, 1, out CollectionArea? area, out _));
        Assert.True(area!.Contains(new CollectionPoint(6f, 50f, 8f)));
        Assert.False(area.Contains(new CollectionPoint(8f, 0f, 8f)));
        Assert.False(area.Contains(new NpcPoint(float.NaN, 0f, 0f)));
    }

    [Fact]
    public void A_centre_that_is_not_a_place_sets_no_area()
    {
        Assert.False(CollectionArea.TryCreate(new CollectionPoint(float.NaN, 0f, 0f), 10f, 1, out CollectionArea? area, out string said));
        Assert.Null(area);
        Assert.Contains("no area was set", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_area_set_is_a_different_revision()
    {
        CollectionArea.TryCreate(new CollectionPoint(0f, 0f, 0f), 10f, 1, out CollectionArea? first, out _);
        CollectionArea.TryCreate(new CollectionPoint(0f, 0f, 0f), 10f, 2, out CollectionArea? second, out _);
        Assert.NotEqual(first!.Revision, second!.Revision);
    }
}
