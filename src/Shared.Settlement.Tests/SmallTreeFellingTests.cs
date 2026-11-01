using System;
using System.Linq;
using TheConcernedCat.Workers.Felling;

namespace Shared.Settlement.Tests;

/// <summary>D19 (owner decision 2026-10-31, #282): which objects a worker may
/// fell, where and when, and how a hit's result is read. Values are the ones
/// read from Valheim 1.0.21's bundle; a change to them must be a change to the
/// decision, so they are pinned here exactly.</summary>
public sealed class SmallTreeFellingTests
{
    private static SmallTreeFacts Tree(
        string prefab = "Beech_small1", int destructibles = 1, bool plant = false, bool piece = false,
        bool wear = false, bool pickable = false, int tier = 0, float health = 20f, bool valid = true) =>
        new(valid, prefab, destructibles, plant, piece, wear, pickable, tier, health);

    private static FellSiteFacts Site(
        bool enabled = true, bool authority = true, bool explicitArea = true, bool inside = true, bool owned = true,
        bool ward = true, float player = 40f, float piece = 40f, float worker = 1f, bool axe = true, int axeTier = 0,
        int treeTier = 0) =>
        new(enabled, authority, explicitArea, inside, owned, ward, player, piece, worker, axe, axeTier, treeTier);

    [Fact]
    public void TheAllowlistIsExactlyTheFourSmallTreesReadFromTheGame()
    {
        Assert.Equal(
            new[] { "Beech_small1", "Beech_small2", "FirTree_small", "FirTree_small_dead" },
            SmallTreeFelling.Allowlist.Select(kind => kind.Prefab).ToArray());
        Assert.All(SmallTreeFelling.Allowlist, kind => Assert.Equal(0, kind.MinToolTier));
        Assert.Equal(new[] { 20f, 20f, 40f, 40f }, SmallTreeFelling.Allowlist.Select(kind => kind.Health).ToArray());
        Assert.Equal(15f, SmallTreeFelling.ClearanceMetres);
    }

    [Theory]
    [InlineData("Beech1")] // a full-size tree (TreeBase): decided, unused
    [InlineData("FirTree")]
    [InlineData("Birch1")]
    [InlineData("Bush01")]
    [InlineData("Beech_Sapling")]
    [InlineData("RaspberryBush")]
    [InlineData("SnowFirTree_small")] // small, but not on the owner's list
    [InlineData("YggaShoot_small1")]
    [InlineData("beech_small1")] // case matters: identity is the exact prefab name
    [InlineData(null)]
    public void AnythingNotOnTheListIsRefused(string? prefab)
    {
        Assert.Equal(SmallTreeRefusal.NotAllowlisted, SmallTreeFelling.Classify(Tree(prefab!)));
    }

    [Fact]
    public void EachAllowlistedTreeIsEligibleAsTheGameShipsIt()
    {
        foreach (SmallTreeKind kind in SmallTreeFelling.Allowlist)
        {
            Assert.Equal(SmallTreeRefusal.Eligible, SmallTreeFelling.Classify(Tree(kind.Prefab, health: kind.Health)));
        }
    }

    [Fact]
    public void CultivationFoodShapeConfigurationAndStateEachRefuse_InThatOrder()
    {
        Assert.Equal(SmallTreeRefusal.NotAllowlisted, SmallTreeFelling.Classify(Tree(valid: false)));
        Assert.Equal(SmallTreeRefusal.WrongShape, SmallTreeFelling.Classify(Tree(destructibles: 2, plant: true)));
        Assert.Equal(SmallTreeRefusal.WrongShape, SmallTreeFelling.Classify(Tree(destructibles: 0)));
        Assert.Equal(SmallTreeRefusal.Cultivated, SmallTreeFelling.Classify(Tree(plant: true, pickable: true)));
        Assert.Equal(SmallTreeRefusal.Cultivated, SmallTreeFelling.Classify(Tree(piece: true)));
        Assert.Equal(SmallTreeRefusal.Cultivated, SmallTreeFelling.Classify(Tree(wear: true)));
        Assert.Equal(SmallTreeRefusal.Food, SmallTreeFelling.Classify(Tree(pickable: true, tier: 3)));
        Assert.Equal(SmallTreeRefusal.ConfigurationChanged, SmallTreeFelling.Classify(Tree(tier: 1, health: 0f)));
        Assert.Equal(SmallTreeRefusal.Gone, SmallTreeFelling.Classify(Tree(health: 0f)));
        Assert.Equal(SmallTreeRefusal.Gone, SmallTreeFelling.Classify(Tree(health: float.NaN)));
    }

    [Fact]
    public void ASiteIsAllowedOnlyWhenEveryRuleHolds()
    {
        Assert.Equal(FellSiteRefusal.Allowed, SmallTreeFelling.CheckSite(Site(), 2f));
    }

    [Fact]
    public void SiteRulesRefuseInTheirOrder()
    {
        Assert.Equal(FellSiteRefusal.FeatureOff, SmallTreeFelling.CheckSite(Site(enabled: false, authority: false, explicitArea: false), 2f));
        Assert.Equal(FellSiteRefusal.NoAuthority, SmallTreeFelling.CheckSite(Site(authority: false, explicitArea: false), 2f));
        Assert.Equal(FellSiteRefusal.NoExplicitArea, SmallTreeFelling.CheckSite(Site(explicitArea: false, inside: false), 2f));
        Assert.Equal(FellSiteRefusal.OutsideArea, SmallTreeFelling.CheckSite(Site(inside: false, owned: false), 2f));
        Assert.Equal(FellSiteRefusal.NotOwnedHere, SmallTreeFelling.CheckSite(Site(owned: false, ward: false), 2f));
        Assert.Equal(FellSiteRefusal.WardDenied, SmallTreeFelling.CheckSite(Site(ward: false, player: 1f), 2f));
        Assert.Equal(FellSiteRefusal.PlayerTooClose, SmallTreeFelling.CheckSite(Site(player: 14.99f, piece: 1f), 2f));
        Assert.Equal(FellSiteRefusal.BuiltPieceTooClose, SmallTreeFelling.CheckSite(Site(piece: 14.99f, worker: 9f), 2f));
        Assert.Equal(FellSiteRefusal.WorkerOutOfReach, SmallTreeFelling.CheckSite(Site(worker: 2.01f, axe: false), 2f));
        Assert.Equal(FellSiteRefusal.NoUsableAxe, SmallTreeFelling.CheckSite(Site(axe: false), 2f));
        Assert.Equal(FellSiteRefusal.AxeTierTooLow, SmallTreeFelling.CheckSite(Site(axeTier: 0, treeTier: 1), 2f));
    }

    [Fact]
    public void ClearancesAreInclusiveAtFifteenMetresAndMeasurementsThatAreNotNumbersRefuse()
    {
        Assert.Equal(FellSiteRefusal.Allowed, SmallTreeFelling.CheckSite(Site(player: 15f, piece: 15f, worker: 2f), 2f));
        Assert.Equal(FellSiteRefusal.Allowed, SmallTreeFelling.CheckSite(Site(player: float.PositiveInfinity, piece: float.PositiveInfinity), 2f));
        Assert.Equal(FellSiteRefusal.PlayerTooClose, SmallTreeFelling.CheckSite(Site(player: float.NaN), 2f));
        Assert.Equal(FellSiteRefusal.BuiltPieceTooClose, SmallTreeFelling.CheckSite(Site(piece: float.NaN), 2f));
        Assert.Equal(FellSiteRefusal.WorkerOutOfReach, SmallTreeFelling.CheckSite(Site(worker: float.NaN), 2f));
    }

    // ------------------------------------------------- conservation: reading a hit

    [Fact]
    public void AFellNeedsTheTreeGoneAndItsOwnDropsSeen()
    {
        Assert.Equal(FellOutcome.Felled, SmallTreeFelling.Read(20f, goneAfter: true, 0f, newDropsAtTree: 3, threw: false));

        // Gone with nothing spawned is never credited as a fell.
        Assert.Equal(FellOutcome.Uncertain, SmallTreeFelling.Read(20f, goneAfter: true, 0f, newDropsAtTree: 0, threw: false));
    }

    [Fact]
    public void AStandingTreeIsAHitANoEffectOrUncertain_NeverAFell()
    {
        Assert.Equal(FellOutcome.Hit, SmallTreeFelling.Read(40f, false, 15f, 0, false));
        Assert.Equal(FellOutcome.NoEffect, SmallTreeFelling.Read(40f, false, 40f, 0, false));
        Assert.Equal(FellOutcome.Uncertain, SmallTreeFelling.Read(40f, false, 15f, 0, threw: true));
        Assert.Equal(FellOutcome.Uncertain, SmallTreeFelling.Read(40f, false, 15f, 2, false)); // drops from a standing tree
        Assert.Equal(FellOutcome.Uncertain, SmallTreeFelling.Read(40f, false, 50f, 0, false)); // health went up
        Assert.Equal(FellOutcome.Uncertain, SmallTreeFelling.Read(40f, false, float.NaN, 0, false));
    }

    [Fact]
    public void EveryRefusalHasAPlayerSentence()
    {
        foreach (SmallTreeRefusal refusal in Enum.GetValues(typeof(SmallTreeRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(SmallTreeFelling.Describe(refusal)));
        }

        foreach (FellSiteRefusal refusal in Enum.GetValues(typeof(FellSiteRefusal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(SmallTreeFelling.Describe(refusal)));
        }
    }
}
