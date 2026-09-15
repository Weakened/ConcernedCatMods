namespace TheConcernedCat.Companions.Persistence;

/// <summary>How a companion sidecar read turned out.
///
/// None of these values ever means "delete the file and start over". A player
/// whose sidecar is unreadable must keep whatever access they already had and
/// get an actionable notice; silently wiping progress, or locking someone out
/// of tools they have been using, are both worse than a stale file.</summary>
internal enum SidecarLoadOutcome
{
    /// <summary>No sidecar exists yet. Normal for a fresh character.</summary>
    Missing = 0,

    /// <summary>Parsed cleanly.</summary>
    Loaded = 1,

    /// <summary>Parsed, but some rows were malformed and skipped. Valid rows
    /// were kept.</summary>
    LoadedWithSkippedRows = 2,

    /// <summary>The file belongs to a different product, world, or character.
    /// It is never merged and never overwritten: the most likely cause is a
    /// copied profile, and the real data may still be wanted.</summary>
    ScopeMismatch = 3,

    /// <summary>Written by a newer schema than this build understands. Treated
    /// as read-only so a downgrade cannot destroy it.</summary>
    UnsupportedSchema = 4,

    /// <summary>Unreadable: no recognizable header, or an IO error. The caller
    /// quarantines rather than deletes.</summary>
    Corrupt = 5,
}

/// <summary>Reading of load outcomes that more than one caller needs.</summary>
internal static class SidecarLoadOutcomes
{
    /// <summary>True when the outcome is itself evidence that this character has
    /// used the product before.
    ///
    /// A file that exists but cannot be fully read is still a file somebody's
    /// game wrote. Treating that as a fresh player would re-run the
    /// introduction and, worse, withdraw features they have been using - so any
    /// outcome except "no file" and "a clean read" counts as prior use.</summary>
    public static bool IndicatesPriorData(SidecarLoadOutcome outcome)
    {
        switch (outcome)
        {
            case SidecarLoadOutcome.LoadedWithSkippedRows:
            case SidecarLoadOutcome.ScopeMismatch:
            case SidecarLoadOutcome.UnsupportedSchema:
            case SidecarLoadOutcome.Corrupt:
                return true;
            default:
                return false;
        }
    }
}
