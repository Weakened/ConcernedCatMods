using System.Collections.Generic;
using TheConcernedCat.Companions.Dialogue;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>CC-NPC-005: Hulgi's catalogue, and the rotation that serves it.
///
/// Two of these are content rules rather than code rules — every line has real
/// text, and no line leaks a place the player has not been — and they are
/// tested because a writer adding a line months from now is exactly who needs
/// to be told.</summary>
public sealed class HulgiDialogueTests
{
    private static readonly DialogueCatalog Catalog = HulgiDialogue.BuildCatalog();

    private static DialogueContext Knowing(params string[] biomes)
    {
        return new DialogueContext(biomes);
    }

    // ------------------------------------------------------------------
    // The catalogue itself
    // ------------------------------------------------------------------

    [Fact]
    public void Catalogue_MeetsTheTwentyFourLineRequirement()
    {
        Assert.True(
            Catalog.Count >= 24,
            $"#269 requires at least 24 varied lines; the catalogue has {Catalog.Count}.");
    }

    [Fact]
    public void Catalogue_CoversEveryRequiredCategory()
    {
        foreach (DialogueCategory category in new[]
        {
            DialogueCategory.CatAndHome,
            DialogueCategory.Travel,
            DialogueCategory.Weather,
            DialogueCategory.BiomeTip,
            DialogueCategory.Greeting,
        })
        {
            Assert.True(
                Catalog.CountIn(category) >= 4,
                $"{category} has only {Catalog.CountIn(category)} lines; each category needs enough " +
                "to rotate without repeating.");
        }
    }

    [Fact]
    public void Catalogue_EveryLineHasRealAuthoredText()
    {
        // AtlasStrings.Get falls back to the key itself for an unknown key, so
        // "the text equals the key" is exactly the missing-content failure.
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
        foreach (DialogueLine line in Catalog.Lines)
        {
            string text = AtlasStrings.Get(line.Key);
            Assert.False(
                text == line.Key,
                $"Dialogue line {line.Key} has no English text in AtlasStrings.");
            Assert.True(text.Length > 12, $"Dialogue line {line.Key} is suspiciously short.");
        }
    }

    [Fact]
    public void Catalogue_LinesAreShortEnoughForAHudToast()
    {
        // The brief asks for short lines, not cinematic ones, and they are
        // delivered as a HUD message rather than a panel.
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
        foreach (DialogueLine line in Catalog.Lines)
        {
            Assert.True(
                AtlasStrings.Get(line.Key).Length <= 160,
                $"Dialogue line {line.Key} is too long for a HUD message.");
        }
    }

    [Fact]
    public void Catalogue_OnlyBiomeTipsAreGated()
    {
        foreach (DialogueLine line in Catalog.Lines)
        {
            if (line.Category == DialogueCategory.BiomeTip)
            {
                Assert.NotNull(line.RequiredBiome);
            }
            else
            {
                Assert.Null(line.RequiredBiome);
            }
        }
    }

    [Fact]
    public void Catalogue_NamesNoBossAndNoItem()
    {
        // The one content rule with no recovery: a spoiler cannot be taken
        // back. Nothing in the catalogue may name a boss or tell the player
        // what to bring.
        AtlasStrings.LoadOverrides(new Dictionary<string, string>());
        string[] forbidden =
        {
            "eikthyr", "elder", "bonemass", "moder", "yagluth", "queen", "fader",
            "trophy", "summon", "altar", "weak to", "weakness", "vulnerable",
        };

        foreach (DialogueLine line in Catalog.Lines)
        {
            string text = AtlasStrings.Get(line.Key).ToLowerInvariant();
            foreach (string word in forbidden)
            {
                Assert.False(
                    text.Contains(word),
                    $"Dialogue line {line.Key} mentions \"{word}\"; companion lines must not spoil " +
                    "progression or name a boss.");
            }
        }
    }

    // ------------------------------------------------------------------
    // The spoiler gate
    // ------------------------------------------------------------------

    [Fact]
    public void APlayerWhoHasBeenNowhereHearsNoPlaceSpecificLine()
    {
        var rotation = new DialogueRotation(Catalog);

        for (int i = 0; i < 60; i++)
        {
            Assert.True(rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line));
            Assert.Null(line.RequiredBiome);
        }
    }

    [Fact]
    public void APlayerOnlyHearsAboutPlacesTheyHaveBeen()
    {
        var rotation = new DialogueRotation(Catalog);
        DialogueContext context = Knowing(HulgiDialogue.Meadows, HulgiDialogue.Swamp);

        var heard = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            Assert.True(rotation.TryNext(null, context, out DialogueLine line));
            if (line.RequiredBiome != null)
            {
                heard.Add(line.RequiredBiome);
            }
        }

        Assert.Subset(
            new HashSet<string> { HulgiDialogue.Meadows, HulgiDialogue.Swamp }, heard);
    }

    [Fact]
    public void BiomeMatchingIsCaseInsensitive()
    {
        // The audit could confirm m_knownBiome is a HashSet<string> but not how
        // this build spells its contents, so the comparison must not care.
        var rotation = new DialogueRotation(Catalog);
        DialogueContext context = Knowing("mEaDoWs");

        bool heardMeadows = false;
        for (int i = 0; i < 200 && !heardMeadows; i++)
        {
            rotation.TryNext(DialogueCategory.BiomeTip, context, out DialogueLine line);
            heardMeadows = line?.RequiredBiome == HulgiDialogue.Meadows;
        }

        Assert.True(heardMeadows);
    }

    [Fact]
    public void ACategoryWithNothingEligibleReportsFailureRatherThanSayingSomethingElse()
    {
        var rotation = new DialogueRotation(Catalog);

        Assert.False(
            rotation.TryNext(DialogueCategory.BiomeTip, DialogueContext.Empty, out DialogueLine _));
    }

    // ------------------------------------------------------------------
    // Rotation — including review finding F9
    // ------------------------------------------------------------------

    [Fact]
    public void ALineDoesNotComeBackWhileTheWindowHolds()
    {
        var rotation = new DialogueRotation(Catalog, suppressionWindow: 8);
        var recent = new List<string>();

        for (int i = 0; i < 40; i++)
        {
            Assert.True(rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line));
            Assert.DoesNotContain(line.Key, recent);
            recent.Add(line.Key);
            if (recent.Count > 8)
            {
                recent.RemoveAt(0);
            }
        }
    }

    [Fact]
    public void F9_MixingASmallCategoryWithTheWholeCatalogueStillReachesTheTail()
    {
        // The finding: the cursor used to be folded into the eligible list's
        // length, so one call for a five-line category restarted the
        // whole-catalogue walk near the front every time and the back of the
        // catalogue was only ever reached when the recency window pushed it
        // that far.
        var rotation = new DialogueRotation(Catalog, suppressionWindow: 4);
        var seen = new HashSet<string>();
        DialogueContext context = DialogueContext.Empty;

        for (int i = 0; i < 400; i++)
        {
            rotation.TryNext(DialogueCategory.Greeting, context, out DialogueLine greeting);
            seen.Add(greeting.Key);

            Assert.True(rotation.TryNext(null, context, out DialogueLine any));
            seen.Add(any.Key);
        }

        foreach (DialogueLine line in Catalog.Lines)
        {
            if (line.RequiredBiome != null)
            {
                continue;
            }

            Assert.True(
                seen.Contains(line.Key),
                $"{line.Key} was never reached when small-category and whole-catalogue requests " +
                "were interleaved.");
        }
    }

    [Fact]
    public void F9_TwoCharactersStillDivergeAfterACategoryScopedCall()
    {
        // The second half of the finding: folding the cursor into a small
        // category's range discarded the per-character starting offset, which
        // is the only thing making two characters hear a different order.
        var first = new DialogueRotation(Catalog, startOffset: 0);
        var second = new DialogueRotation(Catalog, startOffset: 17);
        DialogueContext context = DialogueContext.Empty;

        first.TryNext(DialogueCategory.Greeting, context, out DialogueLine _);
        second.TryNext(DialogueCategory.Greeting, context, out DialogueLine _);

        bool diverged = false;
        for (int i = 0; i < 6 && !diverged; i++)
        {
            first.TryNext(null, context, out DialogueLine a);
            second.TryNext(null, context, out DialogueLine b);
            diverged = a.Key != b.Key;
        }

        Assert.True(diverged, "Two characters heard an identical order after a category-scoped call.");
    }

    [Fact]
    public void ASmallEligibleSetReleasesSuppressionRatherThanRunningOutOfWords()
    {
        // A window wider than the category. He must still say something.
        var rotation = new DialogueRotation(Catalog, suppressionWindow: 100);

        for (int i = 0; i < 30; i++)
        {
            Assert.True(
                rotation.TryNext(DialogueCategory.Greeting, DialogueContext.Empty, out DialogueLine _));
        }
    }

    [Fact]
    public void ResetClearsTheWindowForANewSession()
    {
        var rotation = new DialogueRotation(Catalog);
        rotation.TryNext(null, DialogueContext.Empty, out DialogueLine _);
        Assert.NotEmpty(rotation.RecentKeys);

        rotation.Reset();

        Assert.Empty(rotation.RecentKeys);
    }
}
