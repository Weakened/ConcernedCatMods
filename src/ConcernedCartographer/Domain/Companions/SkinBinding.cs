namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>How a skinned customization mesh should be attached to a body.
/// </summary>
internal enum SkinBindingMode
{
    /// <summary>Take the body's bone array verbatim, in the body's own order.
    /// What the game does, and the only thing that is right for a mesh authored
    /// against the player skeleton.</summary>
    AdoptBodySkeleton = 0,

    /// <summary>Match bone by bone on name, keeping the mesh's own ordering.
    /// For a mesh that cannot be handed the body's array because it does not
    /// have the body's bone count.</summary>
    MatchByName = 1,

    /// <summary>Neither is possible. The mesh is left alone rather than
    /// half-bound.</summary>
    Refuse = 2,
}

/// <summary>Which way to bind a skinned customization mesh, given nothing but
/// the two counts.
///
/// This is one line of arithmetic with a metre of consequence behind it, which
/// is why it is here rather than inline in the adapter. Unity skins a mesh by
/// INDEX: vertex weights name bone slot 17, and bindpose 17 is the matrix that
/// undoes slot 17's rest transform. A vanilla hair or armour mesh is authored
/// against the player skeleton, so its slot 17 means the player skeleton's slot
/// 17 and the correct bone array is the body's, untouched.
///
/// Matching those slots up by NAME instead keeps the mesh's own ordering. Where
/// the two orders agree the result is byte-identical and nothing is wrong -
/// which is why Hulgi's beard landed correctly for a week while his hair, with
/// the same bone count, the same bone names and a complete match, rendered 0.99
/// m away at the height an unanimated bind pose puts a head (#305).</summary>
internal static class SkinBinding
{
    public static SkinBindingMode Decide(int bindposeCount, int bodyBoneCount)
    {
        if (bindposeCount <= 0 || bodyBoneCount <= 0)
        {
            return SkinBindingMode.Refuse;
        }

        // Equality is the whole test. Unity requires bones.Length ==
        // bindposes.Length, so a mesh with the body's count is one the body's
        // array fits - and for a vanilla customization item that is not a
        // coincidence, it is the contract the artist authored against.
        return bindposeCount == bodyBoneCount
            ? SkinBindingMode.AdoptBodySkeleton
            : SkinBindingMode.MatchByName;
    }
}
