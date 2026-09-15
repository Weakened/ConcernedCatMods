using System.Collections.Generic;
using TheConcernedCat.Companions.Definitions;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>Concerned Cartographer's answer to the shared companion layer
/// (CC-NPC-003): one product, one companion, one introduction quest.
///
/// This is the whole of the product's registration surface. Everything else the
/// companion layer needs — the scope, the legacy answer, the anchor — is asked
/// for through interfaces, so this file stays a plain list of identities and
/// localization keys with no game types anywhere near it.</summary>
internal sealed class CartographerCompanions : ICompanionProduct
{
    public const string ProductSlug = "concerned-cartographer";
    public const string HulgiSlug = "hulgi";
    public const string BrokenCompassSlug = "broken-compass";

    public static readonly ProductId Product = new ProductId(ProductSlug);
    public static readonly CompanionId Hulgi = new CompanionId(HulgiSlug);
    public static readonly QuestId BrokenCompass = new QuestId(BrokenCompassSlug);

    public static readonly CompanionDefinition HulgiDefinition = new CompanionDefinition(
        Product, Hulgi, BrokenCompass, "companion.hulgi.name");

    public static readonly QuestDefinition BrokenCompassDefinition = new QuestDefinition(
        Product, BrokenCompass, Hulgi, "companion.compass.name", "companion.compass.examine");

    public static readonly CartographerCompanions Instance = new CartographerCompanions();

    private readonly CompanionDefinition[] _companions = { HulgiDefinition };
    private readonly QuestDefinition[] _quests = { BrokenCompassDefinition };

    private CartographerCompanions()
    {
    }

    public ProductId Id => Product;

    public IReadOnlyList<CompanionDefinition> Companions => _companions;

    public IReadOnlyList<QuestDefinition> Quests => _quests;
}
