using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class PinCodecTests
{
    private static AtlasPin FullPin()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 7,
            CreatedUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Utc),
            Name = "Harbor\twith tab",
            IconId = "cc:harbor",
            Category = "Travel",
            ColorArgb = unchecked((int)0xFF3366CC),
            SizeScale = 1.25f,
            Notes = "line one\nline two, with comma\r100% done",
            Status = AtlasPinStatus.InProgress,
            Checked = true,
            Scope = AtlasScope.Table,
            Source = AtlasPinSource.AdoptedVanilla,
            Archived = true,
            Deleted = true,
            DeletedUtc = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            Position = new RoadPoint(-1234.5f, 31.25f, 987.75f),
        };
        pin.Tags.AddRange(new[] { "port", "iron, tin" });
        return pin;
    }

    [Fact]
    public void Roundtrip_PreservesEveryField()
    {
        AtlasPin original = FullPin();
        PinCodec.ParseResult result = PinCodec.Parse(PinCodec.Serialize(new[] { original }));

        Assert.Equal(0, result.MalformedRows);
        AtlasPin parsed = Assert.Single(result.Pins);
        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.Revision, parsed.Revision);
        Assert.Equal(original.CreatedUtc, parsed.CreatedUtc);
        Assert.Equal(original.ModifiedUtc, parsed.ModifiedUtc);
        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.IconId, parsed.IconId);
        Assert.Equal(original.Category, parsed.Category);
        Assert.Equal(original.ColorArgb, parsed.ColorArgb);
        Assert.Equal(original.SizeScale, parsed.SizeScale);
        Assert.Equal(original.Notes, parsed.Notes);
        Assert.Equal(original.Tags, parsed.Tags);
        Assert.Equal(original.Status, parsed.Status);
        Assert.Equal(original.Checked, parsed.Checked);
        Assert.Equal(original.Scope, parsed.Scope);
        Assert.Equal(original.Source, parsed.Source);
        Assert.Equal(original.Archived, parsed.Archived);
        Assert.Equal(original.Deleted, parsed.Deleted);
        Assert.Equal(original.DeletedUtc, parsed.DeletedUtc);
        Assert.Equal(original.Position, parsed.Position);
    }

    [Fact]
    public void EmptyOptionalFields_Roundtrip()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
        };

        PinCodec.ParseResult result = PinCodec.Parse(PinCodec.Serialize(new[] { pin }));

        AtlasPin parsed = Assert.Single(result.Pins);
        Assert.Null(parsed.ColorArgb);
        Assert.Null(parsed.DeletedUtc);
        Assert.Empty(parsed.Tags);
        Assert.Equal("", parsed.Name);
    }

    [Fact]
    public void JournalReplay_HighestRevisionWins_RegardlessOfOrder()
    {
        AtlasPin pin = FullPin();
        AtlasPin older = pin.Clone();
        older.Revision = 3;
        older.Name = "Older";
        AtlasPin newest = pin.Clone();
        newest.Revision = 12;
        newest.Name = "Newest";

        var lines = new List<string> { PinCodec.Header };
        lines.Add(PinCodec.SerializeRow(newest));
        lines.Add(PinCodec.SerializeRow(older));
        lines.Add(PinCodec.SerializeRow(pin));

        PinCodec.ParseResult result = PinCodec.Parse(lines);

        AtlasPin winner = Assert.Single(result.Pins);
        Assert.Equal("Newest", winner.Name);
        Assert.Equal(12, winner.Revision);
        Assert.Equal(2, result.SupersededRows);
        Assert.Equal(0, result.MalformedRows);
    }

    [Fact]
    public void TruncatedTrailingJournalLine_LosesOnlyThatRow()
    {
        AtlasPin first = FullPin();
        var second = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 2,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Name = "Survivor",
        };

        string truncated = PinCodec.SerializeRow(second).Substring(0, 40);
        var lines = new List<string>
        {
            PinCodec.Header,
            PinCodec.SerializeRow(first),
            PinCodec.SerializeRow(second),
            truncated,
        };

        PinCodec.ParseResult result = PinCodec.Parse(lines);

        Assert.Equal(1, result.MalformedRows);
        Assert.Equal(2, result.Pins.Count);
    }

    [Theory]
    [InlineData("not a pin row at all")]
    [InlineData("cc:pin:00000000000000000000000000000000\t0\t1\t1\tn\ti\tc\t\t1\t\t\t0\t0\t1\t1\t0\t0\t\t1\t2\t3\t1")]
    [InlineData("cc:route:00000000000000000000000000000000\t1\t1\t1\tn\ti\tc\t\t1\t\t\t0\t0\t1\t1\t0\t0\t\t1\t2\t3\t1")]
    [InlineData("cc:pin:00000000000000000000000000000000\t1\t1\t1\tn\ti\tc\tbadcolor\t1\t\t\t0\t0\t1\t1\t0\t0\t\t1\t2\t3\t1")]
    [InlineData("cc:pin:00000000000000000000000000000000\t1\t1\t1\tn\ti\tc\t\t1\t\t\t9\t0\t1\t1\t0\t0\t\t1\t2\t3\t1")]
    public void MalformedRows_AreCountedAndSkipped(string badRow)
    {
        var lines = new List<string> { PinCodec.Header, PinCodec.SerializeRow(FullPin()), badRow };
        PinCodec.ParseResult result = PinCodec.Parse(lines);

        Assert.Equal(1, result.MalformedRows);
        Assert.Single(result.Pins);
    }
}

public class IconRegistryTests
{
    [Fact]
    public void KnownIds_ResolveToStableDefinitions()
    {
        Assert.True(IconRegistry.TryResolve("vanilla:house", out var house));
        Assert.Equal(1, house.VanillaType);
        Assert.True(IconRegistry.TryResolve("vanilla:portal", out var portal));
        Assert.Equal(6, portal.VanillaType);
    }

    [Fact]
    public void UnknownId_FallsBackWithoutChangingIdentity()
    {
        Assert.False(IconRegistry.TryResolve("mod:mystery", out var fallback));
        Assert.Equal(IconRegistry.DefaultIconId, fallback.Id);
        Assert.Equal(IconRegistry.FallbackVanillaType, IconRegistry.ResolveVanillaType("mod:mystery"));

        // The stored identity is what the pin keeps, not the fallback.
        var pin = new AtlasPin(AtlasId.NewPin()) { IconId = "mod:mystery" };
        Assert.Equal("mod:mystery", pin.IconId);
    }

    [Fact]
    public void VanillaTypeMapping_RoundtripsForAdoption()
    {
        Assert.Equal("vanilla:fire", IconRegistry.FromVanillaType(0));
        Assert.Equal("vanilla:house", IconRegistry.FromVanillaType(1));
        Assert.Equal("vanilla:dot", IconRegistry.FromVanillaType(3));
        Assert.Equal("vanilla:portal", IconRegistry.FromVanillaType(6));
        Assert.Equal(IconRegistry.DefaultIconId, IconRegistry.FromVanillaType(99));
    }

    [Fact]
    public void AllIds_AreUniqueAndNamespaced()
    {
        var seen = new HashSet<string>();
        foreach (var definition in IconRegistry.All)
        {
            Assert.True(seen.Add(definition.Id));
            Assert.Contains(":", definition.Id);
        }
    }

    [Fact]
    public void Search_MatchesKeywordsAndEmptyQueryReturnsAll()
    {
        Assert.Equal(IconRegistry.All.Count, IconRegistry.Search("").Count);
        var results = IconRegistry.Search("port");
        Assert.Contains(results, definition => definition.Id == "vanilla:portal");
        Assert.Contains(results, definition => definition.Id == "cc:harbor");
    }

    [Fact]
    public void Rc8_MinimumUsefulCcSet_ShipsDistinctSprites()
    {
        string[] required =
        {
            "cc:road", "cc:harbor", "cc:resource", "cc:danger", "cc:farm",
            "cc:mine", "cc:fishing", "cc:camp", "cc:travel", "cc:trader",
            "cc:dungeon", "cc:objective",
        };

        var spriteKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in required)
        {
            Assert.True(IconRegistry.TryResolve(id, out var definition), $"missing {id}");
            Assert.True(definition.HasCustomSprite, $"{id} must ship its own sprite");
            Assert.True(spriteKeys.Add(definition.SpriteKey), $"{id} reuses sprite {definition.SpriteKey}");
        }
    }

    [Fact]
    public void EveryCcIcon_HasItsOwnSprite_SoPlacementCanNeverDegradeToADot()
    {
        // RC10 feedback 12: the immediate-sprite path renders the chosen
        // cc:* visual from the first naming frame. That only holds if every
        // present AND future cc:* entry ships a sprite key.
        foreach (var definition in IconRegistry.All)
        {
            if (definition.Id.StartsWith("cc:", StringComparison.Ordinal))
            {
                Assert.True(definition.HasCustomSprite,
                    $"{definition.Id} must define a SpriteKey; without one a palette placement " +
                    "renders its vanilla fallback (the Dot bug).");
            }
        }
    }

    [Fact]
    public void Rc8_VanillaIcons_KeepVanillaRendering()
    {
        foreach (string id in new[] { "vanilla:fire", "vanilla:house", "vanilla:hammer", "vanilla:dot", "vanilla:portal" })
        {
            Assert.True(IconRegistry.TryResolve(id, out var definition));
            Assert.False(definition.HasCustomSprite);
        }
    }

    [Fact]
    public void Rc8_EveryDefinition_KeepsAVanillaFallbackType()
    {
        foreach (var definition in IconRegistry.All)
        {
            Assert.InRange(definition.VanillaType, 0, 12);
        }
    }
}
