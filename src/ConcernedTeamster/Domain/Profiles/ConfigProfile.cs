namespace TheConcernedCat.ConcernedTeamster.Domain.Profiles;

/// <summary>A documented preset bundle of Teamster settings (CT-034). Picking
/// one is explicit (the player changes the config value) and reversible
/// (picking a different profile re-applies its own values; individual
/// settings stay independently editable afterward).</summary>
public enum ConfigProfile
{
    Minimal,
    Standard,
    EverythingObservational,
}
