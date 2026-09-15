using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.Companions.Definitions;

/// <summary>Holds the products registered in one process and answers lookups
/// about them.
///
/// The registry exists to make one guarantee testable: products are isolated.
/// Identities are always keyed by product, so two products may use the same
/// companion or quest slug without colliding; nothing registered by one product
/// is reachable through another product's identity; and a definition that names
/// a foreign product is refused outright rather than quietly rehomed.
///
/// Registration never throws on a conflict. A product that fails to register
/// gets an outcome it can log and continue from - one product's mistake must
/// not take down a process that another product is also running in.</summary>
internal sealed class CompanionRegistry
{
    private readonly Dictionary<string, ICompanionProduct> _products =
        new Dictionary<string, ICompanionProduct>(StringComparer.Ordinal);
    private readonly Dictionary<string, CompanionDefinition> _companions =
        new Dictionary<string, CompanionDefinition>(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestDefinition> _quests =
        new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);

    public int ProductCount => _products.Count;

    public RegistrationOutcome Register(ICompanionProduct product)
    {
        if (product == null)
        {
            throw new ArgumentNullException(nameof(product));
        }

        ProductId id = product.Id;
        if (id.IsEmpty)
        {
            return RegistrationOutcome.InvalidProduct;
        }

        if (_products.ContainsKey(id.Value))
        {
            return RegistrationOutcome.DuplicateProduct;
        }

        IReadOnlyList<CompanionDefinition> companions = product.Companions ?? new CompanionDefinition[0];
        IReadOnlyList<QuestDefinition> quests = product.Quests ?? new QuestDefinition[0];

        // Validate the whole product before mutating anything, so a rejected
        // registration leaves no partial state behind.
        var companionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (CompanionDefinition companion in companions)
        {
            if (!companion.Product.Equals(id))
            {
                return RegistrationOutcome.InconsistentDefinitions;
            }

            if (!companionKeys.Add(companion.Companion.Value))
            {
                return RegistrationOutcome.DuplicateCompanion;
            }
        }

        var questKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuestDefinition quest in quests)
        {
            if (!quest.Product.Equals(id))
            {
                return RegistrationOutcome.InconsistentDefinitions;
            }

            if (!questKeys.Add(quest.Quest.Value))
            {
                return RegistrationOutcome.DuplicateQuest;
            }

            if (!companionKeys.Contains(quest.Companion.Value))
            {
                // A quest introducing a companion this product does not own
                // would be the first step towards a cross-product unlock.
                return RegistrationOutcome.InconsistentDefinitions;
            }
        }

        foreach (CompanionDefinition companion in companions)
        {
            if (!questKeys.Contains(companion.IntroductionQuest.Value))
            {
                return RegistrationOutcome.InconsistentDefinitions;
            }
        }

        _products.Add(id.Value, product);
        foreach (CompanionDefinition companion in companions)
        {
            _companions.Add(CompanionKey(id, companion.Companion), companion);
        }

        foreach (QuestDefinition quest in quests)
        {
            _quests.Add(QuestKey(id, quest.Quest), quest);
        }

        return RegistrationOutcome.Registered;
    }

    public bool TryGetProduct(ProductId product, out ICompanionProduct registered)
    {
        return _products.TryGetValue(product.Value, out registered!);
    }

    /// <summary>Looks up a companion <i>within one product</i>. Passing another
    /// product's identity never finds it, which is the isolation guarantee in
    /// its smallest form.</summary>
    public bool TryGetCompanion(ProductId product, CompanionId companion, out CompanionDefinition definition)
    {
        return _companions.TryGetValue(CompanionKey(product, companion), out definition!);
    }

    public bool TryGetQuest(ProductId product, QuestId quest, out QuestDefinition definition)
    {
        return _quests.TryGetValue(QuestKey(product, quest), out definition!);
    }

    /// <summary>The introduction quest for a product's companion, if the
    /// product owns both.</summary>
    public bool TryGetIntroductionQuest(
        ProductId product, CompanionId companion, out QuestDefinition definition)
    {
        definition = null!;
        return TryGetCompanion(product, companion, out CompanionDefinition found)
            && TryGetQuest(product, found.IntroductionQuest, out definition);
    }

    private static string CompanionKey(ProductId product, CompanionId companion)
    {
        return product.Value + "/" + companion.Value;
    }

    private static string QuestKey(ProductId product, QuestId quest)
    {
        return product.Value + "/" + quest.Value;
    }
}
