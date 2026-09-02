using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

public class QuickPinSuggesterTests
{
    [Theory]
    [InlineData("Portal", "portal_wood(Clone)", "vanilla:portal", "Travel")]
    [InlineData("Copper deposit", "rock4_copper(Clone)", "cc:resource", "Resources")]
    [InlineData("Burial chamber", "crypt2(Clone)", "cc:danger", "Danger")]
    [InlineData("Workbench", "piece_workbench(Clone)", "vanilla:hammer", "Work")]
    [InlineData("Beehive", "beehive(Clone)", "cc:farm", "Base")]
    [InlineData("Something odd", "weird_prefab(Clone)", "vanilla:dot", "Explore")]
    public void KeywordRules_MapToIconAndCategory(string hover, string prefab, string expectedIcon, string expectedCategory)
    {
        QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(hover, prefab);
        Assert.Equal(expectedIcon, suggestion.IconId);
        Assert.Equal(expectedCategory, suggestion.Category);
        Assert.Equal(hover, suggestion.Name);
    }

    [Fact]
    public void HoverName_KeepsOnlyTheFirstLine_WithoutMarkup()
    {
        // Interaction prompts live on later hover lines; they must never
        // leak into a pin name (RC10 feedback 15).
        string name = QuickPinSuggester.CleanName("Silver vein\n<color=yellow>[E] Mine</color>", "rock_silver(Clone)");
        Assert.Equal("Silver vein", name);
    }

    [Theory]
    [InlineData(null, "TreasureChest_meadows(Clone)", "Treasure Chest Meadows")]
    [InlineData("", "portal_wood(Clone)", "Portal Wood")]
    [InlineData("$piece_workbench", "piece_workbench(Clone)", "Workbench")]
    [InlineData(null, "RaspberryBush(Clone)", "Raspberry Bush")]
    [InlineData(null, null, "Marked object")]
    public void PrefabFallback_HumanizesClonesUnderscoresCaseAndTokens(string? hover, string? prefab, string expected)
    {
        // RC11 blockers 11/14: prefab fallbacks go through the shared
        // humanizer — "Raspberry Bush", never "Raspberrybush".
        Assert.Equal(expected, QuickPinSuggester.CleanName(hover, prefab));
    }

    [Theory]
    [InlineData("Collider (1)")]
    [InlineData("Collider")]
    [InlineData("collider2")]
    [InlineData("trigger")]
    [InlineData("mesh")]
    [InlineData("Cube")]
    [InlineData("LOD1")]
    [InlineData("snappoint (3)")]
    [InlineData("attach")]
    [InlineData("GameObject (7)")]
    [InlineData("(Clone)")]
    [InlineData("  ")]
    public void TechnicalEngineNames_AreNeverPinNames(string technical)
    {
        // THE RC10 feedback 15 regression: hovering a chest's collider
        // child must name the pin after the chest's prefab, not the
        // collider.
        QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(
            hoverName: null,
            new[] { technical, "TreasureChest_meadows(Clone)", "SomeRoot" });

        Assert.Equal("Treasure Chest Meadows", suggestion.Name);
        Assert.True(QuickPinSuggester.IsTechnicalName(technical));
    }

    [Fact]
    public void AllCandidatesTechnical_FallsBackToFriendlyName()
    {
        QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(
            hoverName: null, new[] { "Collider (1)", "mesh", (string?)null });

        Assert.Equal(QuickPinSuggester.FallbackName, suggestion.Name);
    }

    [Fact]
    public void TechnicalChildName_StillFeedsKeywordMatching_ThroughDeeperCandidates()
    {
        QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(
            hoverName: null, new[] { "Collider (1)", "portal_wood(Clone)" });

        Assert.Equal("vanilla:portal", suggestion.IconId);
        Assert.Equal("Portal Wood", suggestion.Name);
    }

    [Fact]
    public void LocalizedHoverName_OutranksEveryObjectName()
    {
        QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(
            "Treasure chest", new[] { "Collider (1)", "TreasureChest_meadows(Clone)" });

        Assert.Equal("Treasure chest", suggestion.Name);
    }
}
