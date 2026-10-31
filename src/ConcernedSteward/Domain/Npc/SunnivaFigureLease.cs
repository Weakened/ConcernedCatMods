using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedSteward.Domain.Npc;

/// <summary>The lease on Sunniva's presentation figure, held through Concerned
/// NPC's one arbiter beside the worker lease her adoption already holds
/// (<see cref="StewardNpcAdoption.ClaimBody"/>).
///
/// <b>Never both.</b> The arbiter refuses a figure while her worker lease is
/// held and a worker claim while the figure's is held
/// (<c>RefusedOtherKindExists</c>). So the runtime gives this up - after taking
/// the figure down - before it asks for the worker lease, and only asks for this
/// one after the worker lease has gone. Getting the order wrong would not make
/// two bodies; it would make the hand-over fail.
///
/// <b>The figure's lifetime is shorter than this lease, never longer.</b> The
/// figure is built only after <see cref="Holds"/> is true and taken down before
/// anything here can release it. This class never touches a game object.
/// Game-free, so the ordering is tested against the real library.</summary>
internal sealed class SunnivaFigureLease
{
    /// <summary>Who holds the presentation lease: the figure at home.</summary>
    internal const string Holder = "steward/presentation";

    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;
    private BodyLease? _lease;

    internal SunnivaFigureLease(NpcRoleRegistry registry, NpcIdentity identity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (identity.IsEmpty)
        {
            throw new ArgumentException("Sunniva's identity is required.", nameof(identity));
        }

        _identity = identity;
    }

    /// <summary>The figure may exist.</summary>
    internal bool Holds => _lease != null && _lease.IsActive;

    /// <summary>The last refusal the arbiter gave, for the status line.</summary>
    internal string LastRefusal { get; private set; } = string.Empty;

    /// <summary>Asks for the figure's lease. True when it is held afterwards.</summary>
    internal bool TryClaim()
    {
        if (Holds)
        {
            LastRefusal = string.Empty;
            return true;
        }

        _lease = null;
        BodyClaim claim = _registry.TryClaimBody(_identity, NpcBodyKind.Presentation, Holder);
        if (claim.IsGranted && claim.Lease != null)
        {
            _lease = claim.Lease;
            LastRefusal = string.Empty;
            return true;
        }

        LastRefusal = claim.Status + ": " + claim.Reason;
        return false;
    }

    /// <summary>Gives the lease up. Idempotent. The caller has already taken
    /// the figure down.</summary>
    internal void Release()
    {
        BodyLease? lease = _lease;
        _lease = null;
        lease?.Dispose();
    }

    /// <summary>The world has gone; every lease died with it in the arbiter.
    /// </summary>
    internal void Forget()
    {
        _lease = null;
        LastRefusal = string.Empty;
    }
}
