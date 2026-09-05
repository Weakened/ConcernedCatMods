namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>One player's observed relationship to a shared cart at one
/// instant (CT-028), reduced from read-only game state. Privacy:
/// <see cref="DisplayName"/> is only ever the in-game character name the
/// local player can already see above that player — no account id, no
/// position, nothing else, and nothing is transmitted (Teamster sends no
/// network messages; CT-026 audit).</summary>
public readonly struct CoopParticipant
{
    public CoopParticipant(
        string displayName,
        bool isLocalPlayer,
        bool isAttached,
        bool inContact,
        float motionAlignment)
    {
        DisplayName = displayName ?? string.Empty;
        IsLocalPlayer = isLocalPlayer;
        IsAttached = isAttached;
        InContact = inContact;
        MotionAlignment = motionAlignment;
    }

    /// <summary>In-game character name already visible to the local player;
    /// empty when unknown.</summary>
    public string DisplayName { get; }

    public bool IsLocalPlayer { get; }

    /// <summary>Holding the cart's pull handle (replicated, so observers see
    /// a remote puller too).</summary>
    public bool IsAttached { get; }

    /// <summary>Touching or immediately adjacent to the cart.</summary>
    public bool InContact { get; }

    /// <summary>This player's motion projected onto the cart's escape
    /// direction: positive helps the cart along, negative pushes against it,
    /// near zero is no contribution. NaN means the contribution is unknown
    /// (e.g. positions not yet sampled). Unitless and sign-only — never a
    /// force, only an observation of which way the player is moving.</summary>
    public float MotionAlignment { get; }
}
