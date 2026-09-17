using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Designations;

namespace Shared.Settlement.Tests;

/// <summary>The natural-source allowlist of CONTRACTS.md §6 (D10) as a decision
/// table over the audited look-alikes and exclusions (PICKUP_SEAM_AUDIT.md §1.9,
/// §4.1, §4.2, §4.5).</summary>
public sealed class CollectionPredicateTests
{
    // Cases are keyed by name: the test signatures stay public while the facts
    // and clauses they carry are the layer's internal types.

    private static readonly Dictionary<string, (NaturalSourceFacts Facts, SourceClause Clause)> Audited = new()
    {
        // The two look-alikes that decide the predicate (§4.2).
        ["hoe-placed Placeable_Stone"] = (
            SourceFacts.Make("Placeable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "Piece", "WearNTear" }, creator: 12345L),
            SourceClause.C1Identity),
        ["procreation-born Pickable_HardRockOffspring"] = (
            SourceFacts.Make("Pickable_HardRockOffspring", "Stone", "$item_stone", 0f, false, tag: "spawned"),
            SourceClause.C1Identity),

        // Look-alikes that yield something else (§4.1).
        ["Pickable_StoneRock"] = (SourceFacts.Make("Pickable_StoneRock", "StoneRock", "$item_stonerock", 0f, false), SourceClause.C1Identity),
        ["Pickable_Branch_Snow"] = (SourceFacts.Make("Pickable_Branch_Snow", "Frostwood", "$item_frostwood", 240f, true), SourceClause.C1Identity),
        ["Pickable_Flint"] = (SourceFacts.Make("Pickable_Flint", "Flint", "$item_flint", 240f, true), SourceClause.C1Identity),
        ["Pickable_Tin"] = (SourceFacts.Make("Pickable_Tin", "TinOre", "$item_tinore", 0f, false), SourceClause.C1Identity),
        ["Pickable_Obsidian"] = (SourceFacts.Make("Pickable_Obsidian", "Obsidian", "$item_obsidian", 0f, false), SourceClause.C1Identity),
        ["Pickable_Meteorite"] = (SourceFacts.Make("Pickable_Meteorite", "IronOre", "$item_ironore", 0f, false, amount: 6), SourceClause.C1Identity),

        // Food, forage and crops.
        ["Pickable_Mushroom"] = (SourceFacts.Make("Pickable_Mushroom", "Mushroom", "$item_mushroomcommon", 240f, true), SourceClause.C1Identity),
        ["Pickable_Dandelion"] = (SourceFacts.Make("Pickable_Dandelion", "Dandelion", "$item_dandelion", 240f, true), SourceClause.C1Identity),
        ["RaspberryBush"] = (SourceFacts.Make("RaspberryBush", "Raspberry", "$item_raspberries", 300f, true, forbidden: new[] { "Destructible" }), SourceClause.C1Identity),
        ["Pickable_Barley"] = (SourceFacts.Make("Pickable_Barley", "Barley", "$item_barley", 0f, false), SourceClause.C1Identity),
        ["Pickable_Carrot"] = (SourceFacts.Make("Pickable_Carrot", "Carrot", "$item_carrot", 0f, false), SourceClause.C1Identity),

        // Treasure, stands, remains and random pickables.
        ["Pickable_SurtlingCoreStand"] = (SourceFacts.Make("Pickable_SurtlingCoreStand", "SurtlingCore", "$item_surtlingcore", 0f, true), SourceClause.C1Identity),
        ["Pickable_ForestCryptRemains01"] = (SourceFacts.Make("Pickable_ForestCryptRemains01", "BoneFragments", "$item_bonefragments", 0f, false), SourceClause.C1Identity),
        ["Pickable_Item (PickableItem)"] = (SourceFacts.Make("Pickable_Item", null, null, 0f, false, pickables: 0, forbidden: new[] { "PickableItem" }), SourceClause.C1Identity),
        ["Pickable_MeatPile"] = (SourceFacts.Make("Pickable_MeatPile", "RawMeat", "$item_meat_raw", 0f, false, extraDropsEmpty: false), SourceClause.C1Identity),

        // No network object at all.
        ["no valid network view"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, valid: false), SourceClause.C1Identity),
    };

    private static readonly Dictionary<string, (NaturalSourceFacts Facts, SourceClause Clause)> Renamed = new()
    {
        // A mod renaming a look-alike to an allowlisted name is still caught by
        // an independent clause: the name is necessary, never sufficient.
        ["Placeable_Stone renamed Pickable_Stone"] = (
            SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "Piece", "WearNTear" }, creator: 12345L),
            SourceClause.C2Shape),
        ["Pickable_HardRockOffspring renamed Pickable_Stone"] = (
            SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, tag: "spawned"),
            SourceClause.C2bTag),
        ["placed stone without its piece but with its creator"] = (
            SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, creator: 12345L),
            SourceClause.C4Provenance),
        ["Pickable_Branch_Snow renamed Pickable_Branch"] = (
            SourceFacts.Make("Pickable_Branch", "Frostwood", "$item_frostwood", 240f, true),
            SourceClause.C3Yield),
        ["Pickable_StoneRock renamed Pickable_Stone"] = (
            SourceFacts.Make("Pickable_Stone", "StoneRock", "$item_stonerock", 0f, false),
            SourceClause.C3Yield),
        ["Pickable_Flint renamed Pickable_Stone"] = (
            SourceFacts.Make("Pickable_Stone", "Flint", "$item_flint", 240f, true),
            SourceClause.C3Yield),
        ["crop renamed Pickable_Branch"] = (
            SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, forbidden: new[] { "Plant", "Destructible" }),
            SourceClause.C2Shape),
    };

    private static readonly Dictionary<string, (NaturalSourceFacts Facts, SourceClause Clause)> Clauses = new()
    {
        ["two Pickables"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, pickables: 2), SourceClause.C2Shape),
        ["no Pickable"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, pickables: 0), SourceClause.C2Shape),
        ["an ItemDrop inside"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "ItemDrop" }), SourceClause.C2Shape),
        ["a Container inside"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, forbidden: new[] { "Container" }), SourceClause.C2Shape),
        ["an ItemStand inside"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "ItemStand" }), SourceClause.C2Shape),
        ["Procreation"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "Procreation" }), SourceClause.C2Shape),
        ["a Character"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, forbidden: new[] { "Character" }), SourceClause.C2Shape),
        ["no tag"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, tag: null), SourceClause.C2bTag),
        ["yield without ItemDrop"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, yieldHasItemDrop: false), SourceClause.C3Yield),
        ["no yield prefab"] = (SourceFacts.Make("Pickable_Stone", null, null, 0f, false), SourceClause.C3Yield),
        ["right prefab, wrong shared name"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_roundlog", 240f, true), SourceClause.C3Yield),
        ["a stone yielding Wood"] = (SourceFacts.Make("Pickable_Stone", "Wood", "$item_wood", 0f, false), SourceClause.C3Yield),
        ["patched amount 2"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, amount: 2), SourceClause.C3Configuration),
        ["extra drops"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, extraDropsEmpty: false), SourceClause.C3Configuration),
        ["aggravates"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, aggravate: 5f), SourceClause.C3Configuration),
        ["a respawning stone"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 60f, false), SourceClause.C3Configuration),
        ["a stone with a hide object"] = (SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, true), SourceClause.C3Configuration),
        ["a branch that never respawns"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 0f, true), SourceClause.C3Configuration),
        ["a branch without a hide object"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, false), SourceClause.C3Configuration),
        ["a creator"] = (SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, creator: -7L), SourceClause.C4Provenance),
    };

    public static TheoryData<string> AuditedNames() => Names(Audited);

    public static TheoryData<string> RenamedNames() => Names(Renamed);

    public static TheoryData<string> ClauseNames() => Names(Clauses);

    private static TheoryData<string> Names(Dictionary<string, (NaturalSourceFacts, SourceClause)> cases)
    {
        var names = new TheoryData<string>();
        foreach (string name in cases.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    [Fact]
    public void TheTwoVanillaSourcesAreEligibleAndYieldTheGamesOwnItems()
    {
        NaturalSourceVerdict stone = NaturalSourcePredicate.Classify(SourceFacts.VanillaStone());
        NaturalSourceVerdict branch = NaturalSourcePredicate.Classify(SourceFacts.VanillaBranch());

        Assert.True(stone.IsEligible, stone.ToString());
        Assert.Equal(NaturalSourceKind.LooseStone, stone.Kind);
        Assert.Equal(CollectedResource.Stone, stone.Yields);
        Assert.True(branch.IsEligible, branch.ToString());
        Assert.Equal(NaturalSourceKind.Branch, branch.Kind);
        Assert.Equal(CollectedResource.Wood, branch.Yields);
    }

    [Theory]
    [MemberData(nameof(AuditedNames))]
    public void EveryAuditedLookAlikeAndExclusionIsRefused(string name)
    {
        (NaturalSourceFacts facts, SourceClause expected) = Audited[name];
        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(facts);

        Assert.False(verdict.IsEligible, name);
        Assert.False(verdict.IsNatural, name);
        Assert.Equal(expected, verdict.FailedClause);
        Assert.Equal(NaturalSourceKind.Unspecified, verdict.Kind);
    }

    [Theory]
    [MemberData(nameof(RenamedNames))]
    public void ALookAlikeRenamedToAnAllowlistedPrefabIsStillRefusedByAnIndependentClause(string name)
    {
        (NaturalSourceFacts facts, SourceClause expected) = Renamed[name];
        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(facts);

        Assert.False(verdict.IsEligible, name);
        Assert.Equal(expected, verdict.FailedClause);
    }

    [Theory]
    [MemberData(nameof(ClauseNames))]
    public void EachClauseRefusesOnItsOwn(string name)
    {
        (NaturalSourceFacts facts, SourceClause expected) = Clauses[name];
        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(facts);

        Assert.False(verdict.IsNatural, name);
        Assert.Equal(expected, verdict.FailedClause);
    }

    [Theory]
    [InlineData(true, 1, true, "already picked")]
    [InlineData(false, 0, true, "disabled")]
    [InlineData(false, 2, true, "disabled")]
    [InlineData(false, 1, false, "not pickable now")]
    public void AnExhaustedOrDisabledSourceIsNaturalButNotEligible(bool picked, int enabled, bool canBePicked, string detail)
    {
        NaturalSourceVerdict verdict = NaturalSourcePredicate.Classify(
            SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, picked: picked, enabled: enabled, canBePicked: canBePicked));

        Assert.False(verdict.IsEligible);
        Assert.True(verdict.IsNatural);
        Assert.Equal(SourceClause.C5State, verdict.FailedClause);
        Assert.Equal(NaturalSourceKind.Branch, verdict.Kind);
        Assert.Equal(detail, verdict.Detail);
    }

    [Fact]
    public void ClausesAreCheckedInContractOrder()
    {
        // Wrong on every clause at once: the first one is reported.
        NaturalSourceFacts everythingWrong = SourceFacts.Make(
            "Placeable_Stone", "Flint", "$item_flint", 60f, true, pickables: 2, forbidden: new[] { "Piece" },
            tag: "spawned", amount: 3, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C1Identity, NaturalSourcePredicate.Classify(everythingWrong).FailedClause);

        NaturalSourceFacts fromC2 = SourceFacts.Make(
            "Pickable_Stone", "Flint", "$item_flint", 60f, true, pickables: 2, tag: "spawned", amount: 3, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C2Shape, NaturalSourcePredicate.Classify(fromC2).FailedClause);

        NaturalSourceFacts fromC2b = SourceFacts.Make(
            "Pickable_Stone", "Flint", "$item_flint", 60f, true, tag: "spawned", amount: 3, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C2bTag, NaturalSourcePredicate.Classify(fromC2b).FailedClause);

        NaturalSourceFacts fromC3 = SourceFacts.Make(
            "Pickable_Stone", "Flint", "$item_flint", 60f, true, amount: 3, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C3Yield, NaturalSourcePredicate.Classify(fromC3).FailedClause);

        NaturalSourceFacts fromC3Configuration = SourceFacts.Make(
            "Pickable_Stone", "Stone", "$item_stone", 60f, true, amount: 3, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C3Configuration, NaturalSourcePredicate.Classify(fromC3Configuration).FailedClause);

        NaturalSourceFacts fromC4 = SourceFacts.Make("Pickable_Stone", "Stone", "$item_stone", 0f, false, creator: 1L, picked: true);
        Assert.Equal(SourceClause.C4Provenance, NaturalSourcePredicate.Classify(fromC4).FailedClause);

        Assert.Equal(SourceClause.C1Identity, NaturalSourcePredicate.Classify(null!).FailedClause);
    }

    [Fact]
    public void TheForbiddenComponentListIsTheContractsList()
    {
        Assert.Equal(
            new[] { "Piece", "WearNTear", "Plant", "Destructible", "ItemDrop", "Container", "ItemStand", "Procreation", "Character", "PickableItem" },
            NaturalSourceAllowlist.ForbiddenComponentNames);
    }

    private static SourceSiteFacts Site(
        bool owned = true, bool inScope = true, bool interior = false, bool location = false,
        AreaAccess ward = AreaAccess.Granted, float distance = 1f, bool capacity = true, bool player = true) =>
        new SourceSiteFacts(owned, inScope, interior, location, ward, distance, capacity, player);

    [Fact]
    public void SiteClausesRefuseInTheOrderAPlayerWouldFixThem()
    {
        const float Reach = 2f;
        Assert.Equal(SiteRefusal.Unspecified, NaturalSourcePredicate.CheckSite(Site(), Reach));
        Assert.Equal(SiteRefusal.NoLocalPlayer, NaturalSourcePredicate.CheckSite(Site(player: false, owned: false), Reach));
        Assert.Equal(SiteRefusal.NotOwned, NaturalSourcePredicate.CheckSite(Site(owned: false, inScope: false), Reach));
        Assert.Equal(SiteRefusal.OutsideScope, NaturalSourcePredicate.CheckSite(Site(inScope: false, interior: true), Reach));
        Assert.Equal(SiteRefusal.Interior, NaturalSourcePredicate.CheckSite(Site(interior: true, location: true), Reach));
        Assert.Equal(SiteRefusal.InsideLocation, NaturalSourcePredicate.CheckSite(Site(location: true, ward: AreaAccess.Denied), Reach));
        Assert.Equal(SiteRefusal.WardDenied, NaturalSourcePredicate.CheckSite(Site(ward: AreaAccess.Denied, distance: 9f), Reach));
        Assert.Equal(SiteRefusal.WardUnknown, NaturalSourcePredicate.CheckSite(Site(ward: AreaAccess.Unavailable), Reach));
        Assert.Equal(SiteRefusal.OutOfReach, NaturalSourcePredicate.CheckSite(Site(distance: 2.01f, capacity: false), Reach));
        Assert.Equal(SiteRefusal.OutOfReach, NaturalSourcePredicate.CheckSite(Site(distance: float.NaN), Reach));
        Assert.Equal(SiteRefusal.Unspecified, NaturalSourcePredicate.CheckSite(Site(distance: 2f), Reach));
        Assert.Equal(SiteRefusal.CarryCapacity, NaturalSourcePredicate.CheckSite(Site(capacity: false), Reach));
    }

    [Fact]
    public void ASurveyKeepsExhaustedInaccessibleAndUnknownApart()
    {
        NaturalSourceVerdict eligible = NaturalSourcePredicate.Classify(SourceFacts.VanillaStone());
        NaturalSourceVerdict exhausted = NaturalSourcePredicate.Classify(
            SourceFacts.Make("Pickable_Branch", "Wood", "$item_wood", 240f, true, picked: true, canBePicked: false));
        NaturalSourceVerdict lookAlike = NaturalSourcePredicate.Classify(
            SourceFacts.Make("Pickable_HardRockOffspring", "Stone", "$item_stone", 0f, false, tag: "spawned"));

        Assert.Equal(SourceAvailability.Available, NaturalSourcePredicate.SurveyAvailability(eligible, Site()));
        Assert.Equal(SourceAvailability.Exhausted, NaturalSourcePredicate.SurveyAvailability(exhausted, Site()));
        Assert.Equal(SourceAvailability.Inaccessible, NaturalSourcePredicate.SurveyAvailability(eligible, Site(interior: true)));
        Assert.Equal(SourceAvailability.Inaccessible, NaturalSourcePredicate.SurveyAvailability(eligible, Site(location: true)));
        Assert.Equal(SourceAvailability.Inaccessible, NaturalSourcePredicate.SurveyAvailability(eligible, Site(ward: AreaAccess.Denied)));
        Assert.Equal(SourceAvailability.Unknown, NaturalSourcePredicate.SurveyAvailability(eligible, Site(ward: AreaAccess.Unavailable)));
        Assert.Equal(SourceAvailability.Unknown, NaturalSourcePredicate.SurveyAvailability(eligible, Site(owned: false)));
        Assert.Null(NaturalSourcePredicate.SurveyAvailability(eligible, Site(inScope: false)));
        Assert.Null(NaturalSourcePredicate.SurveyAvailability(lookAlike, Site()));
    }

    [Fact]
    public void EverySiteRefusalHasASentence()
    {
        foreach (SiteRefusal refusal in Enum.GetValues(typeof(SiteRefusal)))
        {
            string sentence = NaturalSourcePredicate.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("bug", sentence);
        }
    }
}
