namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>What the registry did with a piece it was told about.
///
/// An enum rather than a <c>bool</c> because three of these are ordinary and
/// two are worth reporting, and a caller that cannot tell them apart cannot
/// explain why a wall it built is not in camp.</summary>
internal enum CampPieceOutcome
{
    /// <summary>Unsaid. Never returned.</summary>
    Unspecified = 0,

    /// <summary>Taken into the registry. Camp will be re-surveyed.</summary>
    Admitted = 1,

    /// <summary>Already known, at the same place. Nothing changed and nothing
    /// is wrong: this is what a re-scan of an area looks like.</summary>
    AlreadyKnown = 2,

    /// <summary>Known, and it has moved. Taken, and camp will be re-surveyed -
    /// which happens because a role may name the same object before and after
    /// the game moves it.</summary>
    Moved = 3,

    /// <summary>Not a member: a terrain edit of any kind, an unrelated natural
    /// object, or something nobody classified. <b>Not stored</b>, so it costs
    /// nothing in every later survey.</summary>
    Excluded = 4,

    /// <summary>Named in another world load. Refused: the name points at
    /// something else now.</summary>
    StaleEpoch = 5,

    /// <summary>No name, no real position, or no world load. Refused.</summary>
    Malformed = 6,
}
