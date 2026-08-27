namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Sharing intent of an entity. Until collaborative sync ships
/// (v0.6) this is stored intent only; everything behaves as Private.</summary>
internal enum AtlasScope
{
    Private = 1,
    Table = 2,
    Server = 3,
}
