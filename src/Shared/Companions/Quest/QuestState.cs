namespace TheConcernedCat.Companions.Quest;

/// <summary>The persisted stage of a companion introduction quest.
///
/// The numeric values are part of the sidecar format and must never be
/// renumbered; new stages are appended. Ordering is meaningful for every
/// value except <see cref="Skipped"/>, which is a completion that deliberately
/// has no companion attached.</summary>
internal enum QuestState
{
    /// <summary>Nothing has happened yet. The collectible may be presented.</summary>
    Unstarted = 0,

    /// <summary>The collectible has been presented to this character.</summary>
    Discovered = 1,

    /// <summary>The collectible was examined and its memento journaled.</summary>
    Collected = 2,

    /// <summary>The introduction is open, or was closed part-way through.
    /// Closing the dialogue defers completion and lands here, so the next
    /// interaction resumes rather than restarts.</summary>
    IntroPending = 3,

    /// <summary>Completed with the companion welcomed.</summary>
    Recruited = 4,

    /// <summary>Completed by an explicit tools-only choice. The product
    /// features unlock exactly as they do for <see cref="Recruited"/>; only
    /// the companion is absent, and the player may still welcome them
    /// later.</summary>
    Skipped = 5,
}
