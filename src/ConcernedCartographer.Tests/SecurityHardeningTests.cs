using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>SEC-1.0-001: the sync receive path must survive hostile
/// envelopes — decompression bombs, absurd revisions, non-finite floats,
/// memory-hostile strings, and HUD markup injection.</summary>
public class SecurityHardeningTests
{
    // --- Bounded decompression (finding 1) ---

    [Fact]
    public void Compression_RoundtripsOrdinaryPayloads()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("PINS\nsome row\nROUTES\nanother row\n");
        byte[] compressed = AtlasCompression.Compress(payload);

        Assert.True(AtlasCompression.TryDecompress(compressed, 4_000_000, out byte[] restored));
        Assert.Equal(payload, restored);
    }

    [Fact]
    public void Decompression_AbortsOnBombInsteadOfAllocating()
    {
        // 32 MB of zeros gzips down far below the compressed-size cap —
        // a classic bomb shape that passes the envelope check.
        byte[] bomb = AtlasCompression.Compress(new byte[32_000_000]);

        Assert.True(bomb.Length < 320_000);
        Assert.False(AtlasCompression.TryDecompress(bomb, 4_000_000, out _));
    }

    [Fact]
    public void Decompression_HonorsTheExactCapBoundary()
    {
        byte[] payload = new byte[1000];
        new Random(7).NextBytes(payload);
        byte[] compressed = AtlasCompression.Compress(payload);

        Assert.True(AtlasCompression.TryDecompress(compressed, 1000, out byte[] restored));
        Assert.Equal(payload, restored);
        Assert.False(AtlasCompression.TryDecompress(compressed, 999, out _));
    }

    [Fact]
    public void Decompression_RejectsCorruptStreams()
    {
        Assert.False(AtlasCompression.TryDecompress(new byte[] { 1, 2, 3, 4, 5 }, 4_000_000, out _));
    }

    // --- Revision sanity (finding 2) ---

    [Fact]
    public void PinCodec_RejectsAbsurdRevisions()
    {
        var pin = new AtlasPin(AtlasId.NewPin()) { Revision = AtlasLimits.MaxRevision };
        Assert.Equal(0, PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) }).MalformedRows);

        pin.Revision = AtlasLimits.MaxRevision + 1;
        PinCodec.ParseResult result = PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) });
        Assert.Empty(result.Pins);
        Assert.Equal(1, result.MalformedRows);
    }

    [Fact]
    public void RouteCodec_RejectsAbsurdRevisions()
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())) { Revision = AtlasLimits.MaxRevision + 1 };
        route.Points.Add(new RoadPoint(1f, 2f, 3f));

        RouteCodec.ParseResult result = RouteCodec.Parse(RouteCodec.SerializeRoute(route));
        Assert.Empty(result.Routes);
        Assert.True(result.MalformedRows > 0);
    }

    // --- Non-finite floats (finding 3) ---

    [Fact]
    public void PinCodec_RejectsNonFinitePositions()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            Position = new RoadPoint(float.NaN, 10f, 20f),
        };

        PinCodec.ParseResult result = PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) });
        Assert.Empty(result.Pins);
        Assert.Equal(1, result.MalformedRows);
    }

    [Fact]
    public void PinCodec_RejectsNonFiniteSizeScale()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            SizeScale = float.PositiveInfinity,
        };

        PinCodec.ParseResult result = PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) });
        Assert.Empty(result.Pins);
        Assert.Equal(1, result.MalformedRows);
    }

    [Fact]
    public void RouteCodec_SkipsNonFinitePoints()
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())) { Revision = 1 };
        route.Points.Add(new RoadPoint(1f, 2f, 3f));
        route.Points.Add(new RoadPoint(float.NegativeInfinity, 2f, 3f));

        RouteCodec.ParseResult result = RouteCodec.Parse(RouteCodec.SerializeRoute(route));
        AtlasRoute parsed = Assert.Single(result.Routes);
        Assert.Single(parsed.Points);
        Assert.Equal(1, result.MalformedRows);
    }

    [Fact]
    public void RoadCodec_RejectsNonFiniteRows()
    {
        var stroke = new RoadStroke(Guid.NewGuid(), RoadKind.Dirt, RoadObservationSource.Traversal);
        stroke.Points.Add(new RoadPoint(float.NaN, 0f, 0f));

        RoadAtlasCodec.ParseResult result = RoadAtlasCodec.Parse(RoadAtlasCodec.Serialize(new[] { stroke }));
        Assert.Empty(result.Strokes);
        Assert.Equal(1, result.MalformedRows);
    }

    // --- Field-length caps (finding 4) ---

    [Fact]
    public void PinCodec_TruncatesOversizedStrings()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            Name = new string('n', 5000),
            Category = new string('c', 5000),
            IconId = new string('i', 5000),
            Notes = new string('x', 50_000),
        };
        for (int i = 0; i < 200; i++)
        {
            pin.Tags.Add($"tag{i}-{new string('t', 200)}");
        }

        AtlasPin parsed = Assert.Single(PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) }).Pins);
        Assert.Equal(AtlasLimits.MaxNameLength, parsed.Name.Length);
        Assert.Equal(AtlasLimits.MaxCategoryLength, parsed.Category.Length);
        Assert.Equal(AtlasLimits.MaxIconIdLength, parsed.IconId.Length);
        Assert.Equal(AtlasLimits.MaxNotesLength, parsed.Notes.Length);
        Assert.Equal(AtlasLimits.MaxTags, parsed.Tags.Count);
        Assert.All(parsed.Tags, tag => Assert.True(tag.Length <= AtlasLimits.MaxTagLength));
    }

    [Fact]
    public void RouteCodec_TruncatesOversizedStrings()
    {
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 1,
            Name = new string('n', 5000),
            Notes = new string('x', 50_000),
        };
        route.Points.Add(new RoadPoint(1f, 2f, 3f));

        AtlasRoute parsed = Assert.Single(RouteCodec.Parse(RouteCodec.SerializeRoute(route)).Routes);
        Assert.Equal(AtlasLimits.MaxNameLength, parsed.Name.Length);
        Assert.Equal(AtlasLimits.MaxNotesLength, parsed.Notes.Length);
    }

    [Fact]
    public void ReasonableStrings_SurviveUntouched()
    {
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            Name = "Silver vein at the twin peaks",
            Notes = "Bring a wishbone and a strong pickaxe.",
        };
        pin.Tags.Add("mining");

        AtlasPin parsed = Assert.Single(PinCodec.Parse(new[] { PinCodec.SerializeRow(pin) }).Pins);
        Assert.Equal(pin.Name, parsed.Name);
        Assert.Equal(pin.Notes, parsed.Notes);
        Assert.Equal(pin.Tags, parsed.Tags);
    }

    // --- Deletion names in the preview (finding 5) ---

    [Fact]
    public void SyncPlan_ListsDeletionNamesUpToTheCap()
    {
        var plan = new SyncPlan();
        for (int i = 0; i < 12; i++)
        {
            plan.TombstonePins.Add(new AtlasPin(AtlasId.NewPin()) { Name = $"Pin {i}" });
        }

        plan.TombstoneRoutes.Add(new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid())) { Name = "Old ferry line" });

        List<string> names = plan.DeletionNames(10);
        Assert.Equal(10, names.Count);
        Assert.Contains("pin \"Pin 0\"", names);

        List<string> all = plan.DeletionNames(100);
        Assert.Equal(13, all.Count);
        Assert.Contains("route \"Old ferry line\"", all);
    }

    [Fact]
    public void SyncPlan_FallsBackToIdsForNamelessDeletions()
    {
        var plan = new SyncPlan();
        var pin = new AtlasPin(AtlasId.NewPin());
        plan.TombstonePins.Add(pin);

        string entry = Assert.Single(plan.DeletionNames(10));
        Assert.Contains(pin.Id.ToString(), entry);
    }

    // --- Display sanitization (finding 6) ---

    [Theory]
    [InlineData("<size=999>HUGE</size>", "HUGE")]
    [InlineData("<color=red>Red</color> Viking", "Red Viking")]
    [InlineData("plain name", "plain name")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeDisplay_StripsMarkup(string? input, string expected)
    {
        Assert.Equal(expected, AtlasText.SanitizeDisplay(input, 24));
    }

    [Fact]
    public void SanitizeDisplay_StripsControlCharactersAndCapsLength()
    {
        Assert.Equal("ab", AtlasText.SanitizeDisplay("a\u0000\u0007b\n", 24));
        Assert.Equal(24, AtlasText.SanitizeDisplay(new string('x', 500), 24).Length);
    }
}
