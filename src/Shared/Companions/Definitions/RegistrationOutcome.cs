namespace TheConcernedCat.Companions.Definitions;

/// <summary>Result of offering a product to the registry.</summary>
internal enum RegistrationOutcome
{
    Registered = 0,

    /// <summary>A product with this identity is already registered.</summary>
    DuplicateProduct = 1,

    /// <summary>Two companions in this product share an identity.</summary>
    DuplicateCompanion = 2,

    /// <summary>Two quests in this product share an identity.</summary>
    DuplicateQuest = 3,

    /// <summary>A companion or quest declared an owning product other than the
    /// one registering it, or a companion's introduction quest is missing from
    /// the same product. Cross-product wiring is the one thing this layer must
    /// never allow, so it is refused rather than repaired.</summary>
    InconsistentDefinitions = 4,

    /// <summary>The product declared no identity.</summary>
    InvalidProduct = 5,
}
