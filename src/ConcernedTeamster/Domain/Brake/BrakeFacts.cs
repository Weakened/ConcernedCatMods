namespace TheConcernedCat.ConcernedTeamster.Domain.Brake;

/// <summary>The facts the brake lifecycle needs about one cart at one
/// instant (CT-012), gathered read-only by the adapter. A fact the adapter
/// could not establish must arrive as its fail-closed value (false /
/// float.MaxValue), never as an optimistic default.</summary>
public readonly struct BrakeFacts
{
    public BrakeFacts(
        bool capabilityOk,
        bool inWorld,
        bool cartExists,
        bool isLocalAuthority,
        bool isAttached,
        float distanceMeters)
    {
        CapabilityOk = capabilityOk;
        InWorld = inWorld;
        CartExists = cartExists;
        IsLocalAuthority = isLocalAuthority;
        IsAttached = isAttached;
        DistanceMeters = distanceMeters;
    }

    public bool CapabilityOk { get; }

    public bool InWorld { get; }

    public bool CartExists { get; }

    /// <summary>True only when the local client owns the cart under vanilla
    /// rules right now — the brake never takes or requests authority.</summary>
    public bool IsLocalAuthority { get; }

    public bool IsAttached { get; }

    public float DistanceMeters { get; }

    public static BrakeFacts Unavailable => new(
        capabilityOk: false, inWorld: false, cartExists: false,
        isLocalAuthority: false, isAttached: false, distanceMeters: float.MaxValue);
}
