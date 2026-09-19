namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>A person's answer to an uncertain transfer: which end the material
/// is actually at. The only way an uncertain transfer ever settles - this
/// library will not guess, and nothing in it ever settles one on a timer.
/// </summary>
internal enum NpcTransferSide
{
    /// <summary>Unsaid. Never an answer.</summary>
    Unspecified = 0,

    /// <summary>It never left. The units stay where they were.</summary>
    Source = 1,

    /// <summary>It arrived. The stated number of units moved; anything short of
    /// the intended count stays at the source.</summary>
    Destination = 2,
}
