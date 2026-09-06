namespace TheConcernedCat.ConcernedTeamster.Domain.Config;

/// <summary>The config schema version ladder (CT-039). Bump
/// <see cref="Current"/> and add a matching step to
/// <see cref="ConfigSchemaMigration"/> whenever a future change to
/// <c>TeamsterSettings</c> genuinely needs one (a renamed, removed, or
/// restructured key) — not preemptively for changes that don't.</summary>
public static class ConfigSchemaVersion
{
    /// <summary>The version every install predating CT-039 is at, including
    /// ones with no schema-version key in their <c>.cfg</c> file at all —
    /// BepInEx binds a config key absent from an existing file to whatever
    /// default this leaf declares, so every pre-CT-039 upgrade genuinely
    /// reads as this value on its first post-upgrade load.</summary>
    public const int PreVersioning = 0;

    /// <summary>The current schema version.</summary>
    public const int Current = 1;
}
