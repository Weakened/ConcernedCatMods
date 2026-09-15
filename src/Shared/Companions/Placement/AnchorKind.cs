namespace TheConcernedCat.Companions.Placement;

/// <summary>Where a companion's home position came from.
///
/// Only two sources qualify, and the list is closed on purpose. A portal
/// arrival, a death spot, or wherever the player happens to be standing are all
/// places a player passes through, not places they live; treating any of them
/// as home would drag the companion across the map behind an ordinary journey.</summary>
internal enum AnchorKind
{
    /// <summary>No valid anchor. The companion is not placed and nothing is
    /// guessed.</summary>
    None = 0,

    /// <summary>The character's claimed, still-valid bed.</summary>
    ClaimedBed = 1,

    /// <summary>The world's default start location, used when no bed is
    /// claimed or the claimed bed is gone.</summary>
    DefaultSpawn = 2,
}
