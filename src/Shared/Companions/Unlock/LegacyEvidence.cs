namespace TheConcernedCat.Companions.Unlock;

/// <summary>How confident a product is that this character used it before the
/// companion introduction existed.
///
/// The three values exist because the honest answer is often the middle one. A
/// product may find a config file but no per-character data, or data whose
/// ownership it cannot pin down. Treating that as "no" would take tools away
/// from someone who has been using them, so ambiguity is resolved in the
/// player's favour and the grant is written down immediately.</summary>
internal enum LegacyEvidence
{
    /// <summary>Nothing suggests prior use. This is the fresh-player path.</summary>
    None = 0,

    /// <summary>Something suggests prior use but it is not conclusive.</summary>
    Ambiguous = 1,

    /// <summary>Prior use is established: this character and world already have
    /// product data from before the introduction shipped.</summary>
    Present = 2,
}
