using System;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.Companions.Definitions;

/// <summary>Everything the shared layer knows about one companion.
///
/// Note what is absent: no display name, no dialogue, no appearance, no prefab.
/// Those belong to the product that owns the companion, in its own localization
/// and content files. This layer carries identity and localization keys so that
/// two products can ship completely different characters without either one
/// knowing the other exists.</summary>
internal sealed class CompanionDefinition
{
    public CompanionDefinition(
        ProductId product, CompanionId companion, QuestId introductionQuest, string nameKey)
    {
        if (product.IsEmpty)
        {
            throw new ArgumentException("A companion needs an owning product.", nameof(product));
        }

        if (companion.IsEmpty)
        {
            throw new ArgumentException("A companion needs an identity.", nameof(companion));
        }

        if (introductionQuest.IsEmpty)
        {
            throw new ArgumentException("A companion needs an introduction quest.", nameof(introductionQuest));
        }

        if (string.IsNullOrEmpty(nameKey))
        {
            throw new ArgumentException("A companion needs a localization key for its name.", nameof(nameKey));
        }

        Product = product;
        Companion = companion;
        IntroductionQuest = introductionQuest;
        NameKey = nameKey;
    }

    public ProductId Product { get; }
    public CompanionId Companion { get; }

    /// <summary>The quest whose completion grants this product's features.</summary>
    public QuestId IntroductionQuest { get; }

    /// <summary>Localization key for the displayed name. The key is stable;
    /// the text is translated.</summary>
    public string NameKey { get; }

    public override string ToString()
    {
        return Product.Value + "/" + Companion.Value;
    }
}
