namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>How a managed pin entered the atlas. Foreign and system pins
/// are never managed and therefore never carry a source.</summary>
internal enum AtlasPinSource
{
    Managed = 1,
    AdoptedVanilla = 2,
    Generated = 3,
    Imported = 4,
}
