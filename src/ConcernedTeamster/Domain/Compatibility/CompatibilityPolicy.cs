namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>What Teamster does when a known mod is detected (CT-036).
/// Policies are data attached to a <see cref="KnownModProbe"/>, never a
/// branch in feature code — adding a mod means adding a registry entry.</summary>
public enum CompatibilityPolicy
{
    /// <summary>No Teamster behavior changes; noted for transparency only.</summary>
    Coexist,

    /// <summary>A specific, documented Teamster behavior changes because
    /// this mod is present (for example: a reading is labeled unavailable
    /// under altered physics rather than shown as vanilla truth).</summary>
    Adapt,

    /// <summary>No behavior changes, but the player is told about a known
    /// limitation or conflict so a surprising reading isn't mistaken for a
    /// bug.</summary>
    Warn,
}
