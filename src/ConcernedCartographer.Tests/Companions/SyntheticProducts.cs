using TheConcernedCat.Companions.Definitions;
using TheConcernedCat.Companions.Identity;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>Two products that exist only in this test assembly.
///
/// The shared companion layer promises that any product can adopt it without
/// touching another product, so that promise is proved against invented
/// products rather than against a real one. Nothing here names a shipping or
/// planned mod: the second product's identity is deliberately a placeholder,
/// because the real name of the next Concerned Cat mod is an open question for
/// its owner and inventing one in test fixtures is how invented names end up
/// shipping.
///
/// The two are built to collide wherever collision is legal - the same
/// companion slug, the same quest slug - so the tests can show that scoping by
/// product is what keeps them apart.</summary>
internal sealed class SyntheticProduct : ICompanionProduct
{
    private SyntheticProduct(
        ProductId id, CompanionDefinition companion, QuestDefinition quest)
    {
        Id = id;
        Companions = new[] { companion };
        Quests = new[] { quest };
    }

    public ProductId Id { get; }
    public IReadOnlyList<CompanionDefinition> Companions { get; }
    public IReadOnlyList<QuestDefinition> Quests { get; }

    public CompanionDefinition Companion => Companions[0];
    public QuestDefinition Quest => Quests[0];

    /// <summary>Builds a product whose companion and quest slugs are the same
    /// regardless of product, so every test that passes is passing because of
    /// product scoping and not because the slugs happened to differ.</summary>
    public static SyntheticProduct Create(
        string productSlug,
        string companionSlug = "guide",
        string questSlug = "introduction")
    {
        var product = new ProductId(productSlug);
        var companion = new CompanionId(companionSlug);
        var quest = new QuestId(questSlug);

        return new SyntheticProduct(
            product,
            new CompanionDefinition(product, companion, quest, productSlug + ".companion.name"),
            new QuestDefinition(
                product,
                quest,
                companion,
                productSlug + ".collectible.name",
                productSlug + ".collectible.examine"));
    }

    /// <summary>First synthetic product.</summary>
    public static SyntheticProduct Alpha() => Create("synthetic-alpha");

    /// <summary>Second synthetic product. Same companion and quest slugs as
    /// <see cref="Alpha"/> on purpose.</summary>
    public static SyntheticProduct Beta() => Create("synthetic-beta");
}
