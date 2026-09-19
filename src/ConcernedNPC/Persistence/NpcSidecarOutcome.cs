namespace TheConcernedCat.ConcernedNPC.Persistence;

/// <summary>What happened when a sidecar file was read.
///
/// Three values and not a boolean, because "there is no file" and "there is a
/// file this build could not read" lead to opposite actions: the first is a new
/// player and may be written over freely, and the second must never be, because
/// the file that could not be read is the only copy of whatever it holds.
/// </summary>
internal enum NpcSidecarOutcome
{
    Unspecified = 0,

    /// <summary>No file. Not an error - it is what a first run looks
    /// like.</summary>
    Missing = 1,

    /// <summary>The file was read. What its lines mean is the caller's
    /// business; this library never interprets them.</summary>
    Read = 2,

    /// <summary>A file exists and could not be read. Nothing was deleted, and
    /// nothing may be written over it until it has been moved
    /// aside.</summary>
    Unreadable = 3,
}
