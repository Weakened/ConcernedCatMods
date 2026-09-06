namespace TheConcernedCat.ConcernedTeamster.Domain.Profiles;

/// <summary>Decides whether a profile's values should be (re)applied to the
/// individual settings (CT-034): only when the active profile differs from
/// the last one actually applied. This makes applying a profile idempotent
/// by construction — feeding the same active profile through this twice
/// applies once and then no-ops — while still letting the player edit
/// individual settings afterward without the mod overwriting them again on
/// every subsequent restart.</summary>
public static class ProfileTransition
{
    public static bool ShouldApply(ConfigProfile active, ConfigProfile lastApplied)
    {
        return active != lastApplied;
    }
}
