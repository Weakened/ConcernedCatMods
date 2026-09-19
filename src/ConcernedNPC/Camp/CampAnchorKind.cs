namespace TheConcernedCat.ConcernedNPC.Camp;

/// <summary>What a camp anchor was derived from, and - because the values are
/// ordered - how much it is worth.
///
/// <b>The order is the rule.</b> A lower value outranks a higher one, so the
/// preference the owner stated ("the player's bed; else the settlement; else
/// the world's start, until there is a home") is the enum's own order rather
/// than a chain of <c>if</c>s somewhere that can be reordered by accident.
///
/// <b>What is deliberately absent: a fourth kind.</b> There is no
/// "nearest structure", no "biggest cluster", no "wherever the NPC happens to
/// be standing". Every one of those silently adopts somebody else's ruin,
/// somebody else's village or a hut the player put up once to survive a storm,
/// and an NPC whose camp moved there takes its whole boundary with it. If none
/// of the three below can be had, camp is unknown, and unknown is a state this
/// library is willing to be in.</summary>
internal enum CampAnchorKind
{
    /// <summary>No anchor. Camp is unknown, and nothing is surveyed: an NPC
    /// with no anchor stays where it is rather than adopting a place.</summary>
    None = 0,

    /// <summary>The player's current respawn or home bed. The best answer,
    /// because the player chose it by sleeping in it.</summary>
    PlayerBed = 1,

    /// <summary>The settlement's established anchor, where a role keeps one.
    /// </summary>
    Settlement = 2,

    /// <summary>The world's start. <b>A temporary fallback before a residence
    /// exists</b>, never a home: a camp anchored here says so, and the first
    /// bed or settlement anchor replaces it.</summary>
    WorldSpawn = 3,
}
