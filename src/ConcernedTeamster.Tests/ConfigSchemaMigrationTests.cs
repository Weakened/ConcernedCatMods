using TheConcernedCat.ConcernedTeamster.Domain.Config;

namespace ConcernedTeamster.Tests;

/// <summary>CT-039: the config schema migration ladder, proven directly
/// against the decision function — Plugin.Awake's actual call site is
/// BepInEx-bound and cannot be exercised by an automated test.</summary>
public class ConfigSchemaMigrationTests
{
    [Fact]
    public void Decide_PreVersioning_MigratesWithoutChangingAnyOtherValue()
    {
        // The one real migration every existing install goes through: no
        // SchemaVersion key existed before CT-039, so BepInEx binds every
        // pre-CT-039 config to PreVersioning on first post-upgrade load.
        ConfigSchemaMigration.Plan plan = ConfigSchemaMigration.Decide(ConfigSchemaVersion.PreVersioning);

        Assert.True(plan.NeedsMigration);
        Assert.False(plan.IsFromANewerVersion);
        Assert.Contains("versioning introduced", plan.LogMessage);
    }

    [Fact]
    public void Decide_AlreadyCurrent_IsANoOp()
    {
        ConfigSchemaMigration.Plan plan = ConfigSchemaMigration.Decide(ConfigSchemaVersion.Current);

        Assert.False(plan.NeedsMigration);
        Assert.False(plan.IsFromANewerVersion);
        Assert.Null(plan.LogMessage);
    }

    [Fact]
    public void Decide_NewerThanCurrent_NeverMigratesBackward()
    {
        // A downgrade scenario: a newer mod version wrote a higher schema
        // version than this build understands. Must never guess a
        // backward migration — leave the stored version exactly as found.
        ConfigSchemaMigration.Plan plan = ConfigSchemaMigration.Decide(ConfigSchemaVersion.Current + 1);

        Assert.False(plan.NeedsMigration);
        Assert.True(plan.IsFromANewerVersion);
        Assert.Contains("newer than this build", plan.LogMessage);
    }

    [Fact]
    public void ConfigSchemaVersion_PreVersioningIsStrictlyLessThanCurrent()
    {
        // A structural guard: if a future bump of Current ever collided
        // with PreVersioning, every existing install's bootstrap migration
        // above would silently stop firing.
        Assert.True(ConfigSchemaVersion.PreVersioning < ConfigSchemaVersion.Current);
    }
}
