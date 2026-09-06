using TheConcernedCat.ConcernedTeamster.Domain.Cargo;

namespace ConcernedTeamster.Tests;

/// <summary>CT-006: manifest totals always equal the sum of known line
/// weights, ordering is deterministic and stable, the snapshot is immune to
/// source-list mutation, and unknown items are explicit markers that never
/// skew totals.</summary>
public class CargoManifestTests
{
    private static CargoEntry Stone(int count = 10) =>
        CargoEntry.Create("Stone", "$item_stone", count, 2f, 2f * count, weightKnown: true);

    private static CargoEntry Wood(int count = 50) =>
        CargoEntry.Create("Wood", "$item_wood", count, 2f, 2f * count, weightKnown: true);

    private static CargoEntry Unknown(int slot = 0) => CargoEntry.CreateUnreadable(slot);

    [Fact]
    public void Create_TotalsEqualSumOfKnownLineWeights()
    {
        CargoManifest manifest = CargoManifest.Create(
            new[] { Stone(10), Wood(50), Unknown() }, captureTimeSeconds: 5.0);

        float expected = 0f;
        foreach (CargoEntry entry in manifest.Entries)
        {
            if (entry.WeightKnown)
            {
                expected += entry.LineWeight;
            }
        }

        Assert.Equal(expected, manifest.TotalKnownWeight);
        Assert.Equal(120f, manifest.TotalKnownWeight);
        Assert.Equal(60, manifest.TotalItemCount);
        Assert.Equal(5.0, manifest.CaptureTimeSeconds);
    }

    [Fact]
    public void Create_UnknownEntries_AreCountedNotSummed()
    {
        CargoManifest manifest = CargoManifest.Create(
            new[] { Stone(1), Unknown(0), Unknown(1) }, 0d);

        Assert.Equal(2f, manifest.TotalKnownWeight);
        Assert.Equal(2, manifest.UnknownWeightEntryCount);
        Assert.True(manifest.HasUnknownWeights);
    }

    [Fact]
    public void Create_OrdersHeaviestKnownLinesFirstThenUnknownsLast()
    {
        CargoManifest manifest = CargoManifest.Create(
            new[] { Unknown(3), Stone(10), Wood(50) }, 0d);

        Assert.Equal("Wood", manifest.Entries[0].ItemId);      // 100 weight
        Assert.Equal("Stone", manifest.Entries[1].ItemId);     // 20 weight
        Assert.False(manifest.Entries[2].WeightKnown);         // marker last
    }

    [Fact]
    public void Create_TiesBreakByNameThenIdDeterministically()
    {
        CargoEntry ore = CargoEntry.Create("CopperOre", "$item_copperore", 5, 4f, 20f, true);
        CargoEntry stone = CargoEntry.Create("Stone", "$item_stone", 10, 2f, 20f, true);

        CargoManifest forward = CargoManifest.Create(new[] { ore, stone }, 0d);
        CargoManifest reversed = CargoManifest.Create(new[] { stone, ore }, 0d);

        Assert.Equal("CopperOre", forward.Entries[0].ItemId);  // "$item_c..." < "$item_s..."
        Assert.Equal(forward.Entries[0].ItemId, reversed.Entries[0].ItemId);
        Assert.Equal(forward.Entries[1].ItemId, reversed.Entries[1].ItemId);
    }

    [Fact]
    public void Create_IsImmuneToSourceMutationAfterwards()
    {
        var source = new List<CargoEntry> { Stone(10) };
        CargoManifest manifest = CargoManifest.Create(source, 0d);

        source.Add(Wood(50));
        source.Clear();

        Assert.Single(manifest.Entries);
        Assert.Equal(20f, manifest.TotalKnownWeight);
    }

    [Fact]
    public void CreateEmpty_IsAValidEmptyCart()
    {
        CargoManifest manifest = CargoManifest.CreateEmpty(7.5);

        Assert.Empty(manifest.Entries);
        Assert.Equal(0f, manifest.TotalKnownWeight);
        Assert.Equal(0, manifest.TotalItemCount);
        Assert.False(manifest.HasUnknownWeights);
        Assert.Equal(7.5, manifest.CaptureTimeSeconds);
    }

    [Fact]
    public void Entry_NameFallbackChain_TokenThenIdThenUnknown()
    {
        Assert.Equal("$item_stone", Stone().EffectiveDisplayName);

        CargoEntry noToken = CargoEntry.Create("ModdedThing", null, 1, 1f, 1f, true);
        Assert.Equal("ModdedThing", noToken.EffectiveDisplayName);

        CargoEntry nothing = CargoEntry.Create(null, null, 1, 1f, 1f, true);
        Assert.Equal("unknown item", nothing.EffectiveDisplayName);
    }

    [Fact]
    public void Entry_UnknownWeight_ZeroesWeightsRegardlessOfInput()
    {
        CargoEntry entry = CargoEntry.Create("X", "$x", 3, 99f, 297f, weightKnown: false);

        Assert.False(entry.WeightKnown);
        Assert.Equal(0f, entry.UnitWeight);
        Assert.Equal(0f, entry.LineWeight);
    }

    [Fact]
    public void Entry_UnreadableSlots_HaveUniqueStableIds()
    {
        CargoEntry marker = CargoEntry.CreateUnreadable(7);

        Assert.Equal("unreadable-slot-7", marker.ItemId);
        // The slot id doubles as the display fallback — explicit and unique,
        // never a fabricated item name.
        Assert.Equal("unreadable-slot-7", marker.EffectiveDisplayName);
        Assert.Equal(0, marker.Count);
        Assert.False(marker.WeightKnown);
    }

    // -- CT-040: scale at a generously large synthetic entry count --------
    //
    // Not a claim about vanilla's actual cart grid capacity (this repo's
    // "research, don't invent" rule applies to game constants too, and
    // Teamster's own code never hardcodes one — CargoManifest just sorts
    // whatever Inventory.GetAllItems() returns) — 200 distinct entries is
    // a deliberately generous synthetic stress input, well past any
    // realistic single-cart inventory, chosen to prove the sort/total
    // logic stays correct and fast regardless of how large a real
    // inventory could ever plausibly be.

    [Fact]
    public void Create_ManyDistinctEntries_StaysCorrectAndFast()
    {
        const int entryCount = 200;
        var entries = new List<CargoEntry>(entryCount);
        float expectedTotal = 0f;
        for (int index = 0; index < entryCount; index++)
        {
            int count = index + 1;
            float unitWeight = 1.5f;
            float lineWeight = unitWeight * count;
            entries.Add(CargoEntry.Create(
                "Item" + index.ToString("D3"), "$item_" + index, count, unitWeight, lineWeight,
                weightKnown: true));
            expectedTotal += lineWeight;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        CargoManifest manifest = CargoManifest.Create(entries, captureTimeSeconds: 0d);
        stopwatch.Stop();

        Assert.Equal(entryCount, manifest.Entries.Count);
        Assert.Equal(expectedTotal, manifest.TotalKnownWeight);

        // Heaviest-first ordering must still hold at scale: the highest
        // synthetic count (and therefore line weight) sorts first.
        Assert.Equal("Item" + (entryCount - 1).ToString("D3"), manifest.Entries[0].ItemId);

        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Composing a {entryCount}-entry manifest took {stopwatch.ElapsedMilliseconds} ms.");
    }
}
