namespace TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

/// <summary>The member shapes the startup capability probe can verify
/// (CT-002). Kinds are added only when an adapter needs them, so the probe
/// never advertises verification it cannot perform.</summary>
public enum GameMemberKind
{
    InstanceField,
    StaticField,
    InstanceMethod,
    InstanceProperty,
}
