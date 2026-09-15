using TheConcernedCat.Companions.Definitions;
using TheConcernedCat.Companions.Identity;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>The extension contract, proved with two synthetic products.
///
/// These are the tests that stand in for a second shipping mod. They show that
/// a second product can adopt the shared layer, that it collides with the first
/// nowhere, and that neither can reach the other's companions, quests, or
/// unlock state.</summary>
public class CompanionRegistryTests
{
    [Fact]
    public void TwoSyntheticProductsRegisterAlongsideEachOther()
    {
        var registry = new CompanionRegistry();

        Assert.Equal(RegistrationOutcome.Registered, registry.Register(SyntheticProduct.Alpha()));
        Assert.Equal(RegistrationOutcome.Registered, registry.Register(SyntheticProduct.Beta()));
        Assert.Equal(2, registry.ProductCount);
    }

    [Fact]
    public void IdenticalCompanionAndQuestSlugsInDifferentProductsDoNotCollide()
    {
        // Both synthetic products deliberately use "guide" and "introduction".
        SyntheticProduct alpha = SyntheticProduct.Alpha();
        SyntheticProduct beta = SyntheticProduct.Beta();

        Assert.Equal(alpha.Companion.Companion, beta.Companion.Companion);
        Assert.Equal(alpha.Quest.Quest, beta.Quest.Quest);

        var registry = new CompanionRegistry();
        registry.Register(alpha);
        registry.Register(beta);

        Assert.True(registry.TryGetCompanion(alpha.Id, alpha.Companion.Companion, out CompanionDefinition first));
        Assert.True(registry.TryGetCompanion(beta.Id, beta.Companion.Companion, out CompanionDefinition second));

        Assert.NotSame(first, second);
        Assert.Equal(alpha.Id, first.Product);
        Assert.Equal(beta.Id, second.Product);
    }

    [Fact]
    public void OneProductCannotReachAnothersDefinitions()
    {
        var registry = new CompanionRegistry();
        SyntheticProduct alpha = SyntheticProduct.Alpha();
        registry.Register(alpha);

        var beta = new ProductId("synthetic-beta");

        // Beta is not registered at all, so nothing of alpha's is reachable
        // through beta's identity even though the slugs match exactly.
        Assert.False(registry.TryGetCompanion(beta, alpha.Companion.Companion, out _));
        Assert.False(registry.TryGetQuest(beta, alpha.Quest.Quest, out _));
        Assert.False(registry.TryGetIntroductionQuest(beta, alpha.Companion.Companion, out _));
        Assert.False(registry.TryGetProduct(beta, out _));
    }

    [Fact]
    public void RegisteringTheSameProductTwiceIsRefused()
    {
        var registry = new CompanionRegistry();
        registry.Register(SyntheticProduct.Alpha());

        Assert.Equal(RegistrationOutcome.DuplicateProduct, registry.Register(SyntheticProduct.Alpha()));
        Assert.Equal(1, registry.ProductCount);
    }

    [Fact]
    public void ACompanionOwnedByAnotherProductIsRefused()
    {
        var owner = new ProductId("synthetic-alpha");
        var foreignProduct = new ProductId("synthetic-beta");
        var companion = new CompanionId("guide");
        var quest = new QuestId("introduction");

        var inconsistent = new StubProduct(
            owner,
            new[] { new CompanionDefinition(foreignProduct, companion, quest, "k") },
            new[] { new QuestDefinition(owner, quest, companion, "n", "e") });

        Assert.Equal(
            RegistrationOutcome.InconsistentDefinitions, new CompanionRegistry().Register(inconsistent));
    }

    [Fact]
    public void AQuestIntroducingAnUnownedCompanionIsRefused()
    {
        // The first step towards a cross-product unlock, refused outright.
        var owner = new ProductId("synthetic-alpha");
        var quest = new QuestId("introduction");

        var inconsistent = new StubProduct(
            owner,
            new[] { new CompanionDefinition(owner, new CompanionId("guide"), quest, "k") },
            new[] { new QuestDefinition(owner, quest, new CompanionId("stranger"), "n", "e") });

        Assert.Equal(
            RegistrationOutcome.InconsistentDefinitions, new CompanionRegistry().Register(inconsistent));
    }

    [Fact]
    public void ACompanionWhoseIntroductionQuestIsMissingIsRefused()
    {
        var owner = new ProductId("synthetic-alpha");
        var companion = new CompanionId("guide");

        var inconsistent = new StubProduct(
            owner,
            new[] { new CompanionDefinition(owner, companion, new QuestId("absent"), "k") },
            new[] { new QuestDefinition(owner, new QuestId("introduction"), companion, "n", "e") });

        Assert.Equal(
            RegistrationOutcome.InconsistentDefinitions, new CompanionRegistry().Register(inconsistent));
    }

    [Fact]
    public void DuplicateCompanionsWithinOneProductAreRefused()
    {
        var owner = new ProductId("synthetic-alpha");
        var companion = new CompanionId("guide");
        var quest = new QuestId("introduction");

        var duplicated = new StubProduct(
            owner,
            new[]
            {
                new CompanionDefinition(owner, companion, quest, "k"),
                new CompanionDefinition(owner, companion, quest, "k2"),
            },
            new[] { new QuestDefinition(owner, quest, companion, "n", "e") });

        Assert.Equal(
            RegistrationOutcome.DuplicateCompanion, new CompanionRegistry().Register(duplicated));
    }

    [Fact]
    public void DuplicateQuestsWithinOneProductAreRefused()
    {
        var owner = new ProductId("synthetic-alpha");
        var companion = new CompanionId("guide");
        var quest = new QuestId("introduction");

        var duplicated = new StubProduct(
            owner,
            new[] { new CompanionDefinition(owner, companion, quest, "k") },
            new[]
            {
                new QuestDefinition(owner, quest, companion, "n", "e"),
                new QuestDefinition(owner, quest, companion, "n2", "e2"),
            });

        Assert.Equal(
            RegistrationOutcome.DuplicateQuest, new CompanionRegistry().Register(duplicated));
    }

    [Fact]
    public void ARejectedRegistrationLeavesNothingBehind()
    {
        var registry = new CompanionRegistry();
        var owner = new ProductId("synthetic-alpha");
        var companion = new CompanionId("guide");
        var quest = new QuestId("introduction");

        registry.Register(new StubProduct(
            owner,
            new[] { new CompanionDefinition(owner, companion, quest, "k") },
            new[] { new QuestDefinition(owner, quest, new CompanionId("stranger"), "n", "e") }));

        Assert.Equal(0, registry.ProductCount);
        Assert.False(registry.TryGetCompanion(owner, companion, out _));

        // The identity is still free, so the product can fix itself and retry.
        Assert.Equal(RegistrationOutcome.Registered, registry.Register(SyntheticProduct.Alpha()));
    }

    [Fact]
    public void AProductWithoutAnIdentityIsRefused()
    {
        var invalid = new StubProduct(
            default, Array.Empty<CompanionDefinition>(), Array.Empty<QuestDefinition>());

        Assert.Equal(RegistrationOutcome.InvalidProduct, new CompanionRegistry().Register(invalid));
    }

    [Fact]
    public void IntroductionQuestLookupStaysWithinTheProduct()
    {
        var registry = new CompanionRegistry();
        SyntheticProduct alpha = SyntheticProduct.Alpha();
        SyntheticProduct beta = SyntheticProduct.Beta();
        registry.Register(alpha);
        registry.Register(beta);

        Assert.True(registry.TryGetIntroductionQuest(
            alpha.Id, alpha.Companion.Companion, out QuestDefinition alphaQuest));
        Assert.True(registry.TryGetIntroductionQuest(
            beta.Id, beta.Companion.Companion, out QuestDefinition betaQuest));

        Assert.Equal(alpha.Id, alphaQuest.Product);
        Assert.Equal(beta.Id, betaQuest.Product);
        Assert.NotSame(alphaQuest, betaQuest);
    }

    private sealed class StubProduct : ICompanionProduct
    {
        public StubProduct(
            ProductId id,
            IReadOnlyList<CompanionDefinition> companions,
            IReadOnlyList<QuestDefinition> quests)
        {
            Id = id;
            Companions = companions;
            Quests = quests;
        }

        public ProductId Id { get; }
        public IReadOnlyList<CompanionDefinition> Companions { get; }
        public IReadOnlyList<QuestDefinition> Quests { get; }
    }
}
