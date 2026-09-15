namespace TheConcernedCat.Companions.Unlock;

/// <summary>Why a product's added features are, or are not, available.
///
/// The value is recorded in the sidecar when a grant is persisted, so a support
/// question about "why can I use this without meeting anyone" has an answer in
/// the data rather than a guess. Values are part of the sidecar format: never
/// renumber them.</summary>
internal enum UnlockReason
{
    /// <summary>Not unlocked. The introduction is still the way in.</summary>
    NotUnlocked = 0,

    /// <summary>The introduction was finished, by welcome or by an explicit
    /// tools-only choice.</summary>
    QuestCompleted = 1,

    /// <summary>This character and world already had product data from before
    /// the introduction shipped.</summary>
    ExistingUserData = 2,

    /// <summary>Prior use was suggested but not proven, and the benefit of the
    /// doubt went to the player.</summary>
    AmbiguousLegacyEvidence = 3,

    /// <summary>The companion data could not be read or was written by a newer
    /// build. Access is preserved rather than revoked on the strength of a file
    /// this build could not interpret.</summary>
    DataUnreadable = 4,

    /// <summary>The player asked for tools without the story.</summary>
    ToolsOnlyPreference = 5,

    /// <summary>A grant recorded in an earlier session. Once written, it is
    /// never re-evaluated: this is what makes access monotonic across changes
    /// to beds, deaths, settings, and other mods.</summary>
    PreviouslyGranted = 6,
}
