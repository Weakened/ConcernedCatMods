namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>How one nearby player is affecting a shared cart (CT-028),
/// inferred from observed read-only state only — never from any force
/// Teamster applies (it applies none). Purely descriptive: it explains who
/// is helping and who is in the way, and grants nobody a newton.</summary>
public enum CoopEffort
{
    /// <summary>Not attached, not in contact, or contributing no motion — a
    /// bystander for this cart right now.</summary>
    Idle,

    /// <summary>Pulling the handle, or pushing in the cart's escape
    /// direction — effort that moves the haul along.</summary>
    Helping,

    /// <summary>In contact and pushing against the cart's escape direction —
    /// effort working against the haul.</summary>
    Hindering,

    /// <summary>In contact but the contribution cannot be read (unknown
    /// motion) — stated plainly rather than guessed.</summary>
    Unclear,
}
