namespace TheConcernedCat.Companions.Unlock;

/// <summary>The resolved answer to "may this character use the product's added
/// features, and should anything be offered to them".
///
/// Feature access and companion presentation are separate fields on purpose. A
/// returning player is unlocked immediately and can still meet the companion
/// afterwards; a player who hides the companion keeps every feature. Nothing
/// here depends on an actor existing in the world.</summary>
internal readonly struct UnlockDecision
{
    public UnlockDecision(
        bool isUnlocked,
        UnlockReason reason,
        bool shouldPersistGrant,
        bool shouldPresentIntroduction,
        bool shouldOfferToolsOnly)
    {
        IsUnlocked = isUnlocked;
        Reason = reason;
        ShouldPersistGrant = shouldPersistGrant;
        ShouldPresentIntroduction = shouldPresentIntroduction;
        ShouldOfferToolsOnly = shouldOfferToolsOnly;
    }

    /// <summary>Whether the product's added affordances are available.</summary>
    public bool IsUnlocked { get; }

    public UnlockReason Reason { get; }

    /// <summary>True when this grant was decided from evidence outside the
    /// sidecar and should be written down now, so that later sessions never
    /// have to re-derive it from evidence that may have moved.</summary>
    public bool ShouldPersistGrant { get; }

    /// <summary>Whether the collectible and introduction should be offered.
    /// Independent of <see cref="IsUnlocked"/>: a grandfathered player is
    /// unlocked and may still be offered the story.</summary>
    public bool ShouldPresentIntroduction { get; }

    /// <summary>Whether a visible tools-only choice should be offered. Always
    /// available while the introduction is unfinished, so nobody is ever
    /// obliged to complete a story to use the tools.</summary>
    public bool ShouldOfferToolsOnly { get; }
}
