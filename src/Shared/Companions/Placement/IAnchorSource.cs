namespace TheConcernedCat.Companions.Placement;

/// <summary>Supplies the companion's home position from the game.
///
/// Implementations must return <see cref="AnchorKind.None"/> rather than a
/// fallback whenever the answer is not actually known - during a scene
/// transition, before the player object exists, or when a bed lookup fails. A
/// deferred companion is a small absence; a companion anchored to a guess can
/// end up inside a wall or on the wrong continent.</summary>
internal interface IAnchorSource
{
    /// <summary>Resolves the current anchor. Returns false when no valid anchor
    /// is available yet.</summary>
    bool TryGetAnchor(out CompanionAnchor anchor);
}
