using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Bodies;

/// <summary>The answer to one request to construct or give up a body: what
/// happened, for which identity and kind, and why not.
///
/// <b>What it guarantees.</b> That a caller which does not look at
/// <see cref="IsGranted"/> cannot accidentally read a refusal as permission. The
/// defaulted struct is <see cref="BodyClaimStatus.Unspecified"/>, which is not a
/// grant, and there is no implicit conversion to <c>bool</c> that could quietly
/// become one.</summary>
public readonly struct BodyClaim
{
    internal BodyClaim(BodyClaimStatus status, NpcIdentity identity, NpcBodyKind kind, string holder, string reason)
    {
        Status = status;
        Identity = identity;
        Kind = kind;
        Holder = holder;
        Reason = reason;
    }

    /// <summary>What happened.</summary>
    public BodyClaimStatus Status { get; }

    /// <summary>The identity asked about.</summary>
    public NpcIdentity Identity { get; }

    /// <summary>The kind asked for.</summary>
    public NpcBodyKind Kind { get; }

    /// <summary>The holder id that was passed in.</summary>
    public string Holder { get; }

    /// <summary>Why it was refused, naming the conflict - the kind that already
    /// exists, or the holder that has the identity. Empty on a grant, so a log
    /// line never invents a problem.</summary>
    public string Reason { get; }

    /// <summary>The one question before constructing a body. True only for
    /// <see cref="BodyClaimStatus.Claimed"/> and
    /// <see cref="BodyClaimStatus.AlreadyHeld"/> - the two that mean "this
    /// holder has this body now" - and false for everything else, the defaulted
    /// struct included.</summary>
    public bool IsGranted =>
        Status == BodyClaimStatus.Claimed || Status == BodyClaimStatus.AlreadyHeld;

    public override string ToString() =>
        Reason.Length == 0
            ? Status + " " + Identity + " " + Kind
            : Status + " " + Identity + " " + Kind + ": " + Reason;
}
