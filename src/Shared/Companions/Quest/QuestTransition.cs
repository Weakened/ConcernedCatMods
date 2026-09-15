namespace TheConcernedCat.Companions.Quest;

/// <summary>A request to advance a companion introduction quest. Requests are
/// total and idempotent: asking for progress that is already met is a success
/// that changes nothing, never an error and never a second reward.</summary>
internal enum QuestTransition
{
    /// <summary>The collectible has been presented to the player.</summary>
    Discover = 0,

    /// <summary>The player examined the collectible.</summary>
    Collect = 1,

    /// <summary>The introduction has started.</summary>
    BeginIntroduction = 2,

    /// <summary>The player welcomed the companion.</summary>
    Welcome = 3,

    /// <summary>The player explicitly chose tools only.</summary>
    Skip = 4,
}
