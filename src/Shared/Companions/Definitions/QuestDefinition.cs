using System;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.Companions.Definitions;

/// <summary>One introduction quest: which companion it introduces, and the
/// localization keys for the collectible that starts it.</summary>
internal sealed class QuestDefinition
{
    public QuestDefinition(
        ProductId product,
        QuestId quest,
        CompanionId companion,
        string collectibleNameKey,
        string collectibleExamineKey)
    {
        if (product.IsEmpty)
        {
            throw new ArgumentException("A quest needs an owning product.", nameof(product));
        }

        if (quest.IsEmpty)
        {
            throw new ArgumentException("A quest needs an identity.", nameof(quest));
        }

        if (companion.IsEmpty)
        {
            throw new ArgumentException("A quest needs the companion it introduces.", nameof(companion));
        }

        if (string.IsNullOrEmpty(collectibleNameKey))
        {
            throw new ArgumentException(
                "A quest needs a localization key for its collectible.", nameof(collectibleNameKey));
        }

        if (string.IsNullOrEmpty(collectibleExamineKey))
        {
            throw new ArgumentException(
                "A quest needs a localization key for examining its collectible.",
                nameof(collectibleExamineKey));
        }

        Product = product;
        Quest = quest;
        Companion = companion;
        CollectibleNameKey = collectibleNameKey;
        CollectibleExamineKey = collectibleExamineKey;
    }

    public ProductId Product { get; }
    public QuestId Quest { get; }
    public CompanionId Companion { get; }

    /// <summary>Localization key for the collectible's hover name.</summary>
    public string CollectibleNameKey { get; }

    /// <summary>Localization key for the text shown when the collectible is
    /// examined.</summary>
    public string CollectibleExamineKey { get; }

    public override string ToString()
    {
        return Product.Value + "/" + Quest.Value;
    }
}
