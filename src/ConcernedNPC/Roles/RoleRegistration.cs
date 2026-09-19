namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>The answer to one registration attempt: what happened, to which
/// identity, and - when it failed - which fact was wrong, in words a role author
/// can act on without a debugger.
///
/// <b>Why a struct and not just the enum.</b> The registry this is shaped from
/// returned a bare <c>InconsistentDefinitions</c> and left the author to find
/// which of their definitions was inconsistent by reading the registry's source.
/// That is affordable while nobody calls it and expensive the moment four
/// products do, so the outcome carries its reason.
///
/// <b>Why it is returned and never thrown.</b> Four products register into one
/// process at load. A role that registers badly must fail alone: the other three
/// carry on, and the bad one has something it can log.</summary>
public readonly struct RoleRegistration
{
    internal RoleRegistration(RoleRegistrationStatus status, NpcIdentity identity, string reason)
    {
        Status = status;
        Identity = identity;
        Reason = reason;
    }

    /// <summary>What happened. Never <see cref="RoleRegistrationStatus.Unspecified"/>
    /// from a call that returned.</summary>
    public RoleRegistrationStatus Status { get; }

    /// <summary>The identity the role declared, as far as it could be read.
    /// Empty when the role was null or its identity unreadable, which is exactly
    /// when a log line needs to say so.</summary>
    public NpcIdentity Identity { get; }

    /// <summary>Why it failed, naming the offending fact. Empty on success.
    /// </summary>
    public string Reason { get; }

    /// <summary>The single question a caller asks before going on to build
    /// anything. False for every status but
    /// <see cref="RoleRegistrationStatus.Registered"/>, including the defaulted
    /// struct.</summary>
    public bool IsRegistered => Status == RoleRegistrationStatus.Registered;

    public override string ToString() =>
        IsRegistered
            ? Status + " " + Identity
            : Status + " " + Identity + ": " + Reason;
}
