using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.Companions.Definitions;

/// <summary>What a product supplies to the shared companion layer.
///
/// This is the whole extension contract. A product implements it with its own
/// constants, compiles this shared source into its own assembly, and gains the
/// quest, persistence, unlock, placement, and dialogue machinery without
/// referencing - or even knowing about - any other product.</summary>
internal interface ICompanionProduct
{
    /// <summary>This product's identity. Must be unique across every product
    /// registered in one process.</summary>
    ProductId Id { get; }

    /// <summary>The companions this product owns. Identities need only be
    /// unique within the product: two products may each ship a <c>guide</c>.</summary>
    IReadOnlyList<CompanionDefinition> Companions { get; }

    /// <summary>The introduction quests this product owns.</summary>
    IReadOnlyList<QuestDefinition> Quests { get; }
}
