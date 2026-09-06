namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>One entry in the compatibility registry (CT-036): a mod
/// Teamster has researched and has a documented policy for. The registry is
/// the only place that names a specific mod GUID — feature code queries the
/// evaluated <see cref="ModDetectionResult"/>s, never a GUID literal.</summary>
public sealed class KnownModProbe
{
    public KnownModProbe(string guid, string displayName, CompatibilityPolicy policy, string description)
    {
        Guid = guid;
        DisplayName = displayName;
        Policy = policy;
        Description = description;
    }

    /// <summary>The mod's BepInEx plugin GUID — never invented; only added
    /// after verifying it against the mod's actual shipped metadata.</summary>
    public string Guid { get; }

    /// <summary>Human-readable name for logs and the compatibility panel.</summary>
    public string DisplayName { get; }

    public CompatibilityPolicy Policy { get; }

    /// <summary>What this policy means in practice, shown to the player
    /// (for example "cart mass readings may not reflect this mod's changes").</summary>
    public string Description { get; }
}
