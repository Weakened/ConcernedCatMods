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
    public void HoverName_MarkupAndNewlines_AreStripped()
    {
        string name = QuickPinSuggester.CleanName("Silver vein\n<color=yellow>[E] Mine</color>", "rock_silver(Clone)");
        Assert.Equal("Silver vein[E] Mine", name.Replace("  ", " ").Trim());
    }

    [Theory]
    [InlineData(null, "TreasureChest_meadows(Clone)", "Treasurechest meadows")]
    [InlineData("", "portal_wood(Clone)", "Portal wood")]
    [InlineData("$piece_workbench", "piece_workbench(Clone)", "Piece workbench")]
    [InlineData(null, null, "Marked spot")]
    public void PrefabFallback_CleansClonesUnderscoresAndTokens(string? hover, string? prefab, string expected)
    {
        Assert.Equal(expected, QuickPinSuggester.CleanName(hover, prefab));
    }
}
