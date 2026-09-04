using TheConcernedCat.ConcernedTeamster.Domain.Cargo;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-007: the manifest presenter's sort matrix is deterministic
/// for every column and direction (unknown markers pinned last, canonical
/// tiebreak), filtering is case-insensitive and clears cleanly, and
/// empty/no-match/stale/no-manifest states render explicitly. Localizer
/// failures degrade to raw tokens without losing rows.</summary>
public class CargoManifestPresenterTests
{
    private static CargoEntry Entry(string id, string token, int count, float unit, bool known = true)
    {
        return CargoEntry.Create(id, token, count, unit, unit * count, known);
    }

    private static CargoManifest SampleManifest(double captureTime = 100.0)
    {
        return CargoManifest.Create(new[]
        {
            Entry("Stone", "$item_stone", 30, 2f),        // line 60
            Entry("Wood", "$item_wood", 50, 2f),          // line 100
            Entry("CopperOre", "$item_copperore", 5, 4f), // line 20
            CargoEntry.CreateUnreadable(9),
        }, captureTime);
    }

    private static CargoManifestViewModel Present(
        CargoManifest? manifest,
        CargoSortColumn column = CargoSortColumn.LineWeight,
        bool descending = true,
        string? filter = null,
        double now = 100.0,
        Func<string, string>? localize = null)
    {
        return CargoManifestPresenter.Present(manifest, column, descending, filter, now, localize);
    }

    private static string[] Names(CargoManifestViewModel viewModel)
    {
        var names = new string[viewModel.Rows.Count];
        for (int index = 0; index < names.Length; index++)
        {
            names[index] = viewModel.Rows[index].Name;
        }

        return names;
    }

    // -- sort matrix ---------------------------------------------------

    [Theory]
    [InlineData(CargoSortColumn.LineWeight, true, new[] { "$item_wood", "$item_stone", "$item_copperore" })]
    [InlineData(CargoSortColumn.LineWeight, false, new[] { "$item_copperore", "$item_stone", "$item_wood" })]
    [InlineData(CargoSortColumn.Name, false, new[] { "$item_copperore", "$item_stone", "$item_wood" })]
    [InlineData(CargoSortColumn.Name, true, new[] { "$item_wood", "$item_stone", "$item_copperore" })]
    [InlineData(CargoSortColumn.Count, true, new[] { "$item_wood", "$item_stone", "$item_copperore" })]
    [InlineData(CargoSortColumn.Count, false, new[] { "$item_copperore", "$item_stone", "$item_wood" })]
    [InlineData(CargoSortColumn.UnitWeight, true, new[] { "$item_copperore", "$item_wood", "$item_stone" })]
    [InlineData(CargoSortColumn.UnitWeight, false, new[] { "$item_wood", "$item_stone", "$item_copperore" })]
    public void Present_SortMatrix_IsDeterministic(
        CargoSortColumn column, bool descending, string[] expectedKnownOrder)
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(), column, descending);

        string[] names = Names(viewModel);
        Assert.Equal(4, names.Length);
        Assert.Equal(expectedKnownOrder[0], names[0]);
        Assert.Equal(expectedKnownOrder[1], names[1]);
        Assert.Equal(expectedKnownOrder[2], names[2]);
        // The unreadable marker is pinned last under every sort.
        Assert.Equal("unreadable-slot-9", names[3]);
        Assert.Equal("?", viewModel.Rows[3].LineWeightText);
    }

    [Fact]
    public void Present_UnitWeightTies_FallBackToCanonicalOrderStably()
    {
        // Stone and Wood share unit weight 2.0; canonical manifest order
        // (line weight desc) puts Wood (100) before Stone (60), and the
        // canonical tiebreak preserves that relative order in BOTH sort
        // directions — that is what makes the secondary order stable.
        CargoManifestViewModel ascending = Present(SampleManifest(), CargoSortColumn.UnitWeight, false);
        Assert.Equal(new[] { "$item_wood", "$item_stone" }, new[] { Names(ascending)[0], Names(ascending)[1] });
        Assert.Equal("$item_copperore", Names(ascending)[2]);

        CargoManifestViewModel descending = Present(SampleManifest(), CargoSortColumn.UnitWeight, true);
        Assert.Equal(new[] { "$item_copperore", "$item_wood", "$item_stone" },
            new[] { Names(descending)[0], Names(descending)[1], Names(descending)[2] });
    }

    // -- filter --------------------------------------------------------

    [Fact]
    public void Present_Filter_MatchesCaseInsensitively()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(), filter: "STONE");

        Assert.Equal(CargoManifestState.Live, viewModel.State);
        string[] names = Names(viewModel);
        Assert.Single(names);
        Assert.Equal("$item_stone", names[0]);
    }

    [Fact]
    public void Present_Filter_AppliesToLocalizedNames()
    {
        CargoManifestViewModel viewModel = Present(
            SampleManifest(), filter: "copper ore",
            localize: token => token == "$item_copperore" ? "Copper ore" : token.Replace("$item_", ""));

        Assert.Single(viewModel.Rows);
        Assert.Equal("Copper ore", viewModel.Rows[0].Name);
    }

    [Fact]
    public void Present_FilterNoMatch_RendersExplicitStateWithWholeCartTotals()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(), filter: "obsidian");

        Assert.Equal(CargoManifestState.NoMatch, viewModel.State);
        Assert.Equal("No items match \"obsidian\".", viewModel.Message);
        Assert.Empty(viewModel.Rows);
        // Totals always describe the whole cart, not the filtered view.
        Assert.Equal("Total weight: 180.0 (+1 unknown) · 85 items", viewModel.TotalLine);
    }

    [Fact]
    public void Present_ClearingTheFilter_RestoresEveryRow()
    {
        Assert.Single(Present(SampleManifest(), filter: "stone").Rows);
        Assert.Equal(4, Present(SampleManifest(), filter: "").Rows.Count);
        Assert.Equal(4, Present(SampleManifest(), filter: "   ").Rows.Count);
        Assert.Equal(4, Present(SampleManifest(), filter: null).Rows.Count);
    }

    // -- states --------------------------------------------------------

    [Fact]
    public void Present_NullManifest_SaysNoContainer()
    {
        CargoManifestViewModel viewModel = Present(null);

        Assert.Equal(CargoManifestState.NoManifest, viewModel.State);
        Assert.Equal("No cart container available.", viewModel.Message);
        Assert.Empty(viewModel.Rows);
        Assert.Equal(string.Empty, viewModel.TotalLine);
    }

    [Fact]
    public void Present_EmptyManifest_SaysCartIsEmpty()
    {
        CargoManifestViewModel viewModel = Present(CargoManifest.CreateEmpty(100.0));

        Assert.Equal(CargoManifestState.Empty, viewModel.State);
        Assert.Equal("Cart is empty.", viewModel.Message);
        Assert.Equal("Total weight: 0.0 · 0 items", viewModel.TotalLine);
        Assert.Equal("Captured 0.0 s ago", viewModel.FreshnessLine);
    }

    [Fact]
    public void Present_OldCapture_IsVisiblyStale()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(100.0), now: 103.5);

        Assert.Equal(CargoManifestState.Stale, viewModel.State);
        Assert.Equal("STALE — captured 3.5 s ago", viewModel.FreshnessLine);
        Assert.Equal(4, viewModel.Rows.Count);
    }

    [Fact]
    public void Present_FreshCapture_IsLive()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(100.0), now: 102.9);

        Assert.Equal(CargoManifestState.Live, viewModel.State);
        Assert.Equal("Captured 2.9 s ago", viewModel.FreshnessLine);
    }

    // -- rows and localization ----------------------------------------

    [Fact]
    public void Present_Rows_FormatEveryColumnInvariantly()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(), CargoSortColumn.LineWeight, true);

        CargoRowViewModel wood = viewModel.Rows[0];
        Assert.Equal("$item_wood", wood.Name);
        Assert.Equal("50", wood.CountText);
        Assert.Equal("2.0", wood.UnitWeightText);
        Assert.Equal("100.0", wood.LineWeightText);
    }

    [Fact]
    public void Present_LocalizerThrows_FallsBackToRawTokenPerRow()
    {
        CargoManifestViewModel viewModel = Present(
            SampleManifest(),
            localize: token => token == "$item_wood"
                ? throw new InvalidOperationException("broken localizer")
                : token.ToUpperInvariant());

        Assert.Equal(4, viewModel.Rows.Count);
        Assert.Contains(Names(viewModel), name => name == "$item_wood");
        Assert.Contains(Names(viewModel), name => name == "$ITEM_STONE");
    }

    [Fact]
    public void Present_LocalizerReturningEmpty_KeepsRawToken()
    {
        CargoManifestViewModel viewModel = Present(SampleManifest(), localize: _ => "");

        Assert.Contains(Names(viewModel), name => name == "$item_wood");
    }
}
