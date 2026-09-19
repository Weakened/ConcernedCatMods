using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>What Gunnar may collect, and - mostly - what he may not (#381).
///
/// Every exclusion the issue names has a test here, and each one is written as
/// the look-alike that would get past a weaker rule rather than as a restatement
/// of the rule. A capability test ("has something takeable and gives Stone")
/// admits the hoe-placed stone and the timer-grown rock; the tests below are the
/// two of those, plus the ones a player would actually be upset about.</summary>
public sealed class GunnarCollectionEligibilityTests
{
    private static CollectableFacts Stone(
        string? prefab = GunnarCollectionAllowlist.LooseStonePrefab,
        int takeables = 1,
        IReadOnlyList<string>? forbidden = null,
        bool bearsFood = false,
        string? yield = GunnarCollectionAllowlist.StoneItemPrefab,
        int amount = 1,
        bool extraDropsEmpty = true,
        float respawn = 0f,
        bool hide = false,
        bool creator = false,
        bool taken = false,
        bool visible = true,
        bool takeableNow = true,
        bool marked = false,
        bool validObject = true) =>
        new CollectableFacts(
            validObject, prefab, takeables, forbidden, bearsFood, yield != null, yield, amount,
            extraDropsEmpty, respawn, hide, creator, taken, visible, takeableNow, marked);

    private static CollectableFacts Branch(
        float respawn = 240f,
        bool hide = true,
        bool bearsFood = false,
        IReadOnlyList<string>? forbidden = null,
        bool marked = false) =>
        new CollectableFacts(
            true, GunnarCollectionAllowlist.BranchPrefab, 1, forbidden, bearsFood, true,
            GunnarCollectionAllowlist.WoodItemPrefab, 1, true, respawn, hide, false, false, true, true, marked);

    [Fact]
    public void A_natural_loose_stone_is_eligible()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(Stone());

        Assert.True(verdict.IsEligible);
        Assert.Equal(CollectableKind.LooseStone, verdict.Kind);
        Assert.Equal(GunnarCollectionAllowlist.StoneItemPrefab, verdict.Yields);
    }

    [Fact]
    public void A_natural_branch_is_eligible_and_gives_wood()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(Branch());

        Assert.True(verdict.IsEligible);
        Assert.Equal(GunnarCollectionAllowlist.WoodItemPrefab, verdict.Yields);
    }

    [Fact]
    public void A_stone_a_player_placed_is_theirs()
    {
        // The hoe-placed look-alike: the same takeable component, the same
        // yield, and a creator recorded on it. Caught twice over - by the
        // prefab name and by the creator - and the second is what makes the
        // first not load-bearing on its own.
        CollectableVerdict byName = GunnarTargetPredicate.Classify(Stone(prefab: "Placeable_Stone"));
        CollectableVerdict byCreator = GunnarTargetPredicate.Classify(Stone(creator: true));

        Assert.Equal(CollectionClause.Identity, byName.FailedClause);
        Assert.Equal(CollectionClause.Provenance, byCreator.FailedClause);
    }

    [Fact]
    public void A_rock_that_grows_on_a_timer_is_not_a_resource()
    {
        // The procreation-born look-alike wears a different network prefab and
        // regrows; either fact alone refuses it.
        CollectableVerdict byName = GunnarTargetPredicate.Classify(Stone(prefab: "Pickable_HardRockOffspring"));
        CollectableVerdict byRespawn = GunnarTargetPredicate.Classify(Stone(respawn: 30f));

        Assert.Equal(CollectionClause.Identity, byName.FailedClause);
        Assert.Equal(CollectionClause.Yield, byRespawn.FailedClause);
    }

    [Fact]
    public void A_food_bearing_plant_is_refused_and_the_refusal_says_so()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(Branch(bearsFood: true));

        Assert.Equal(CollectionClause.FoodBearing, verdict.FailedClause);
        Assert.Contains("food", GunnarTargetPredicate.Describe(verdict.FailedClause), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_container_in_the_work_area_is_not_a_resource()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(
            Stone(forbidden: new[] { "a container" }));

        Assert.Equal(CollectionClause.Shape, verdict.FailedClause);
        Assert.Contains("a container", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pile_a_player_dropped_is_not_a_resource()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(
            Stone(forbidden: new[] { "a dropped item pile" }));

        Assert.Equal(CollectionClause.Shape, verdict.FailedClause);
    }

    [Fact]
    public void Something_invisible_is_a_timer_and_not_a_resource()
    {
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(
            new CollectableFacts(
                true, GunnarCollectionAllowlist.BranchPrefab, 1, null, false, true,
                GunnarCollectionAllowlist.WoodItemPrefab, 1, true, 240f, true, false, false,
                visible: false, takeableNow: true, markedByPlayer: false));

        Assert.Equal(CollectionClause.State, verdict.FailedClause);
        Assert.Contains("timer", verdict.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_exhausted_source_is_still_the_right_kind_of_thing()
    {
        // The distinction that lets a survey say "this area is picked clean"
        // rather than "there was never anything here".
        CollectableVerdict verdict = GunnarTargetPredicate.Classify(Stone(taken: true));

        Assert.Equal(CollectionClause.State, verdict.FailedClause);
        Assert.False(verdict.IsEligible);
        Assert.True(verdict.IsCollectableKind);
    }

    [Fact]
    public void A_patched_yield_fails_closed()
    {
        Assert.Equal(CollectionClause.Yield, GunnarTargetPredicate.Classify(Stone(amount: 12)).FailedClause);
        Assert.Equal(CollectionClause.Yield, GunnarTargetPredicate.Classify(Stone(yield: "Coins")).FailedClause);
        Assert.Equal(
            CollectionClause.Yield,
            GunnarTargetPredicate.Classify(Stone(extraDropsEmpty: false)).FailedClause);
    }

    [Fact]
    public void A_composite_wearing_two_takeable_components_is_refused()
    {
        Assert.Equal(CollectionClause.Shape, GunnarTargetPredicate.Classify(Stone(takeables: 2)).FailedClause);
    }

    [Fact]
    public void Nothing_and_no_world_object_are_both_refusals_rather_than_crashes()
    {
        Assert.Equal(CollectionClause.Identity, GunnarTargetPredicate.Classify(null).FailedClause);
        Assert.Equal(CollectionClause.Identity, GunnarTargetPredicate.Classify(Stone(validObject: false)).FailedClause);
    }

    [Fact]
    public void A_players_mark_widens_which_candidates_are_looked_at_never_which_rules_apply()
    {
        // A marked chest is still a chest. The mark changes what he is told to
        // look at, and nothing about what he is allowed to take.
        CollectableVerdict markedChest = GunnarTargetPredicate.Classify(
            Stone(forbidden: new[] { "a container" }, marked: true));
        CollectableVerdict markedStone = GunnarTargetPredicate.Classify(Stone(marked: true));

        Assert.Equal(CollectionClause.Shape, markedChest.FailedClause);
        Assert.True(markedStone.IsEligible);
        Assert.Equal(CollectableKind.MarkedResource, markedStone.Kind);
    }

    [Fact]
    public void Felling_is_not_in_this_slice_so_no_kind_claims_it()
    {
        // A guard on the scope decision rather than on behaviour: if a later
        // leaf adds a fellable kind, this test is what makes somebody go and
        // read why felling was held back (an RPC send out of Gunnar's runtime,
        // which the shipped worker-runtime scope audit refuses).
        var kinds = new List<CollectableKind>();
        foreach (CollectableKind kind in Enum.GetValues(typeof(CollectableKind)))
        {
            kinds.Add(kind);
        }

        Assert.Equal(
            new[]
            {
                CollectableKind.Unspecified,
                CollectableKind.LooseStone,
                CollectableKind.Branch,
                CollectableKind.MarkedResource,
            },
            kinds);
    }

    // ------------------------------------------------------------------
    // The place
    // ------------------------------------------------------------------

    private static CollectableSite Site(
        bool owned = true,
        bool inArea = true,
        WardStanding ward = WardStanding.Granted,
        LocationStanding location = LocationStanding.Outside,
        bool fits = true) =>
        new CollectableSite(owned, inArea, ward, location, fits);

    [Fact]
    public void A_place_that_refuses_nothing_refuses_nothing()
    {
        Assert.Equal(CollectionSiteClause.Unspecified, GunnarTargetPredicate.CheckSite(Site()));
    }

    [Fact]
    public void Outside_the_work_area_is_a_refusal_and_never_a_wider_search()
    {
        CollectionSiteClause clause = GunnarTargetPredicate.CheckSite(Site(inArea: false));

        Assert.Equal(CollectionSiteClause.OutsideWorkArea, clause);
        Assert.Contains("does not look further", GunnarTargetPredicate.Describe(clause), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_unknown_refuses()
    {
        Assert.Equal(
            CollectionSiteClause.InsideLocation,
            GunnarTargetPredicate.CheckSite(Site(location: LocationStanding.Unknown)));
        Assert.Equal(
            CollectionSiteClause.WardUnknown,
            GunnarTargetPredicate.CheckSite(Site(ward: WardStanding.Unknown)));
        Assert.Equal(CollectionSiteClause.NotOwnedHere, GunnarTargetPredicate.CheckSite(Site(owned: false)));
    }

    [Fact]
    public void A_defaulted_site_refuses_because_nobody_asked_anything()
    {
        Assert.Equal(CollectionSiteClause.NotOwnedHere, GunnarTargetPredicate.CheckSite(default));
    }

    [Fact]
    public void Somebody_elses_ward_and_a_built_place_each_refuse_by_name()
    {
        Assert.Equal(
            CollectionSiteClause.WardDenied,
            GunnarTargetPredicate.CheckSite(Site(ward: WardStanding.Denied)));
        Assert.Equal(
            CollectionSiteClause.InsideLocation,
            GunnarTargetPredicate.CheckSite(Site(location: LocationStanding.Inside)));
    }

    [Fact]
    public void Carry_room_is_the_last_thing_asked_because_it_is_the_one_that_changes()
    {
        // Everything else about a place is fixed; how much he is carrying is
        // not. Asking it last means a refusal names the thing a player can do
        // nothing about before it names the thing that fixes itself.
        Assert.Equal(CollectionSiteClause.OverCarryLimit, GunnarTargetPredicate.CheckSite(Site(fits: false)));
        Assert.Equal(
            CollectionSiteClause.WardDenied,
            GunnarTargetPredicate.CheckSite(Site(ward: WardStanding.Denied, fits: false)));
    }

    [Fact]
    public void Every_clause_and_every_site_clause_has_a_sentence()
    {
        foreach (CollectionClause clause in Enum.GetValues(typeof(CollectionClause)))
        {
            Assert.NotEmpty(GunnarTargetPredicate.Describe(clause));
            Assert.DoesNotContain("bug", GunnarTargetPredicate.Describe(clause), StringComparison.Ordinal);
        }

        foreach (CollectionSiteClause clause in Enum.GetValues(typeof(CollectionSiteClause)))
        {
            Assert.NotEmpty(GunnarTargetPredicate.Describe(clause));
            Assert.DoesNotContain("bug", GunnarTargetPredicate.Describe(clause), StringComparison.Ordinal);
        }
    }
}
