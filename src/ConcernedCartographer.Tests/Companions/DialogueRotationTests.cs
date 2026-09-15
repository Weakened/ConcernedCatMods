using TheConcernedCat.Companions.Dialogue;

namespace ConcernedCartographer.Tests.Companions;

public class DialogueRotationTests
{
    private static DialogueCatalog Catalog(int count, DialogueCategory category = DialogueCategory.Travel)
    {
        var lines = new List<DialogueLine>();
        for (int index = 0; index < count; index++)
        {
            lines.Add(new DialogueLine("line." + index, category));
        }

        return new DialogueCatalog(lines);
    }

    [Fact]
    public void DoesNotRepeatWithinTheSuppressionWindow()
    {
        var rotation = new DialogueRotation(Catalog(12), suppressionWindow: 8);
        var said = new List<string>();

        for (int turn = 0; turn < 8; turn++)
        {
            Assert.True(rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line));
            said.Add(line.Key);
        }

        Assert.Equal(said.Count, said.Distinct().Count());
    }

    [Fact]
    public void WalksTheWholeCatalogueBeforeComingBackAround()
    {
        var rotation = new DialogueRotation(Catalog(6), suppressionWindow: 5);
        var said = new List<string>();

        for (int turn = 0; turn < 6; turn++)
        {
            rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line);
            said.Add(line.Key);
        }

        Assert.Equal(6, said.Distinct().Count());
    }

    [Fact]
    public void KeepsTalkingWhenTheWindowIsLargerThanTheCatalogue()
    {
        // A small category must not make the companion mute. Saying something a
        // little sooner than ideal is better than running out of words.
        var rotation = new DialogueRotation(Catalog(3), suppressionWindow: 10);

        for (int turn = 0; turn < 30; turn++)
        {
            Assert.True(rotation.TryNext(null, DialogueContext.Empty, out _));
        }
    }

    [Fact]
    public void NeverSaysTheSameLineTwiceInARowWithMoreThanOneAvailable()
    {
        var rotation = new DialogueRotation(Catalog(2), suppressionWindow: 8);
        string? previous = null;

        for (int turn = 0; turn < 20; turn++)
        {
            rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line);
            Assert.NotEqual(previous, line.Key);
            previous = line.Key;
        }
    }

    [Fact]
    public void ASingleLineCatalogueStillWorks()
    {
        var rotation = new DialogueRotation(Catalog(1), suppressionWindow: 4);

        Assert.True(rotation.TryNext(null, DialogueContext.Empty, out DialogueLine first));
        Assert.True(rotation.TryNext(null, DialogueContext.Empty, out DialogueLine second));
        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void AnEmptyCatalogueStaysSilentInsteadOfThrowing()
    {
        var rotation = new DialogueRotation(new DialogueCatalog(Array.Empty<DialogueLine>()));

        Assert.False(rotation.TryNext(null, DialogueContext.Empty, out _));
    }

    [Fact]
    public void DifferentStartOffsetsBeginAtDifferentLines()
    {
        // Two characters should not hear the catalogue in the same order, while
        // each one stays reproducible.
        var first = new DialogueRotation(Catalog(10), startOffset: 0);
        var second = new DialogueRotation(Catalog(10), startOffset: 5);

        first.TryNext(null, DialogueContext.Empty, out DialogueLine a);
        second.TryNext(null, DialogueContext.Empty, out DialogueLine b);

        Assert.NotEqual(a.Key, b.Key);
    }

    [Fact]
    public void TheSameOffsetAlwaysProducesTheSameSequence()
    {
        static List<string> Run()
        {
            var rotation = new DialogueRotation(Catalog(7), startOffset: 3);
            var said = new List<string>();
            for (int turn = 0; turn < 7; turn++)
            {
                rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line);
                said.Add(line.Key);
            }

            return said;
        }

        Assert.Equal(Run(), Run());
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-7)]
    [InlineData(int.MaxValue)]
    public void AnyCallerSuppliedOffsetIsAccepted(int offset)
    {
        // The offset is a hash, not a validated index.
        var rotation = new DialogueRotation(Catalog(5), startOffset: offset);

        Assert.True(rotation.TryNext(null, DialogueContext.Empty, out _));
    }

    [Fact]
    public void FiltersToTheRequestedCategory()
    {
        var catalog = new DialogueCatalog(new[]
        {
            new DialogueLine("cat.1", DialogueCategory.CatAndHome),
            new DialogueLine("travel.1", DialogueCategory.Travel),
            new DialogueLine("weather.1", DialogueCategory.Weather),
        });

        var rotation = new DialogueRotation(catalog);
        Assert.True(rotation.TryNext(DialogueCategory.Weather, DialogueContext.Empty, out DialogueLine line));
        Assert.Equal("weather.1", line.Key);
    }

    [Fact]
    public void NeverMentionsAPlaceTheCharacterHasNotSeen()
    {
        var catalog = new DialogueCatalog(new[]
        {
            new DialogueLine("tip.swamp", DialogueCategory.BiomeTip, requiredBiome: "Swamp"),
            new DialogueLine("tip.plains", DialogueCategory.BiomeTip, requiredBiome: "Plains"),
        });

        var known = new DialogueContext(new[] { "Swamp" });
        var rotation = new DialogueRotation(catalog);

        for (int turn = 0; turn < 10; turn++)
        {
            Assert.True(rotation.TryNext(DialogueCategory.BiomeTip, known, out DialogueLine line));
            Assert.Equal("tip.swamp", line.Key);
        }
    }

    [Fact]
    public void SaysNothingRatherThanSpoilingWhenNoBiomeIsKnown()
    {
        var catalog = new DialogueCatalog(new[]
        {
            new DialogueLine("tip.swamp", DialogueCategory.BiomeTip, requiredBiome: "Swamp"),
        });

        Assert.False(new DialogueRotation(catalog)
            .TryNext(DialogueCategory.BiomeTip, DialogueContext.Empty, out _));
    }

    [Fact]
    public void UnknownProgressionIsTreatedAsKnowingNothing()
    {
        // When progression cannot be read, the failure mode of guessing is a
        // spoiler, so the empty context is the safe default.
        Assert.Equal(0, DialogueContext.Empty.KnownBiomeCount);
        Assert.False(DialogueContext.Empty.KnowsBiome("Swamp"));
        Assert.False(new DialogueContext(null).KnowsBiome("Meadows"));
    }

    [Fact]
    public void KnownBiomesAreMatchedCaseInsensitivelyAndIgnoreBlanks()
    {
        var context = new DialogueContext(new[] { "Swamp", "", "  " });

        Assert.True(context.KnowsBiome("swamp"));
        Assert.True(context.KnowsBiome("SWAMP"));
        Assert.Equal(2, context.KnownBiomeCount);
        Assert.False(context.KnowsBiome(null));
    }

    [Fact]
    public void UngatedLinesAreAlwaysAllowed()
    {
        var line = new DialogueLine("always", DialogueCategory.Travel);

        Assert.Null(line.RequiredBiome);
        Assert.True(DialogueContext.Empty.Allows(line));
    }

    [Fact]
    public void ABlankRequiredBiomeMeansUngatedRatherThanImpossible()
    {
        Assert.Null(new DialogueLine("k", DialogueCategory.Travel, requiredBiome: "").RequiredBiome);
    }

    [Fact]
    public void ACatalogueRejectsDuplicateKeys()
    {
        // A duplicate would be said twice as often as everything else while the
        // suppression window believed otherwise.
        Assert.Throws<ArgumentException>(() => new DialogueCatalog(new[]
        {
            new DialogueLine("same", DialogueCategory.Travel),
            new DialogueLine("same", DialogueCategory.Weather),
        }));
    }

    [Fact]
    public void ALineRequiresALocalizationKey()
    {
        Assert.Throws<ArgumentException>(() => new DialogueLine("", DialogueCategory.Travel));
    }

    [Fact]
    public void CatalogueReportsCategoryCoverage()
    {
        var catalog = new DialogueCatalog(new[]
        {
            new DialogueLine("cat.1", DialogueCategory.CatAndHome),
            new DialogueLine("cat.2", DialogueCategory.CatAndHome),
            new DialogueLine("tip.1", DialogueCategory.BiomeTip, requiredBiome: "Swamp"),
        });

        Assert.Equal(2, catalog.CountIn(DialogueCategory.CatAndHome));
        Assert.Equal(1, catalog.CountIn(DialogueCategory.BiomeTip));
        Assert.Equal(0, catalog.CountIn(DialogueCategory.Weather));

        // Only CatAndHome is sayable to someone who has been nowhere.
        Assert.Equal(new[] { DialogueCategory.CatAndHome }, catalog.UnconditionalCategories());
    }

    [Fact]
    public void ResetClearsTheRecencyWindow()
    {
        var rotation = new DialogueRotation(Catalog(4));
        rotation.TryNext(null, DialogueContext.Empty, out _);
        Assert.NotEmpty(rotation.RecentKeys);

        rotation.Reset();
        Assert.Empty(rotation.RecentKeys);
    }

    [Fact]
    public void RejectsANegativeSuppressionWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DialogueRotation(Catalog(4), suppressionWindow: -1));
    }
}
