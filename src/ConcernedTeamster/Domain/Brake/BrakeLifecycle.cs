using TheConcernedCat.ConcernedTeamster.Domain.Authority;

namespace TheConcernedCat.ConcernedTeamster.Domain.Brake;

/// <summary>The parking brake's entire decision logic (CT-012), pure and
/// exhaustively testable. Invariants:
///
/// - Engage happens only through an explicit toggle request, only when every
///   eligibility fact holds (capability, in-world, cart exists, local
///   vanilla authority, not attached, within reach) — never automatically.
/// - While engaged, every tick re-checks the facts and ANY failure releases:
///   world exit, capability loss, cart gone, authority lost, cart grabbed,
///   player walked away. There is no reachable state that keeps the brake
///   engaged without its lifecycle owner confirming it.
/// - One brake at a time; toggling a different cart is refused until the
///   engaged one is released.
///
/// The physics itself lives behind the adapter seam; this class only decides.
/// Callers apply the physics first and then confirm with
/// <see cref="MarkEngaged"/>/<see cref="MarkReleased"/> so a failed physics
/// call can never leave the state machine believing the brake holds.</summary>
public sealed class BrakeLifecycle
{
    /// <summary>Maximum distance at which the engage control works.</summary>
    public const float EngageMaxDistanceMeters = 5f;

    /// <summary>Walking beyond this releases the brake automatically.</summary>
    public const float AutoReleaseDistanceMeters = 12f;

    public string? EngagedCartId { get; private set; }

    public bool IsEngaged => EngagedCartId is not null;

    /// <summary>Decides what an explicit toggle press on a cart does.</summary>
    public BrakeAction EvaluateToggle(string cartId, BrakeFacts facts, out string reason)
    {
        if (EngagedCartId is not null)
        {
            if (EngagedCartId == cartId)
            {
                reason = "released by player";
                return BrakeAction.Release;
            }

            reason = "another cart is already braked; release it first";
            return BrakeAction.None;
        }

        if (!facts.CapabilityOk)
        {
            reason = "cart capability is unavailable";
            return BrakeAction.None;
        }

        if (!facts.InWorld)
        {
            reason = "no local player";
            return BrakeAction.None;
        }

        if (!facts.CartExists)
        {
            reason = "cart no longer exists";
            return BrakeAction.None;
        }

        if (!CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, facts.Authority))
        {
            reason = "this client does not control the cart";
            return BrakeAction.None;
        }

        if (facts.IsAttached)
        {
            reason = "cart is being pulled; detach first";
            return BrakeAction.None;
        }

        if (facts.DistanceMeters > EngageMaxDistanceMeters)
        {
            reason = "too far from the cart";
            return BrakeAction.None;
        }

        reason = "engaged by player";
        return BrakeAction.Engage;
    }

    /// <summary>Re-checks an engaged brake against fresh facts; any failed
    /// fact releases with its reason. No-op while released.</summary>
    public BrakeAction EvaluateTick(BrakeFacts facts, out string reason)
    {
        if (EngagedCartId is null)
        {
            reason = string.Empty;
            return BrakeAction.None;
        }

        if (!facts.InWorld)
        {
            reason = "left the world";
            return BrakeAction.Release;
        }

        if (!facts.CapabilityOk)
        {
            reason = "cart capability lost";
            return BrakeAction.Release;
        }

        if (!facts.CartExists)
        {
            reason = "cart no longer exists";
            return BrakeAction.Release;
        }

        if (!CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, facts.Authority))
        {
            reason = "cart authority moved to another client";
            return BrakeAction.Release;
        }

        if (facts.IsAttached)
        {
            reason = "cart was grabbed";
            return BrakeAction.Release;
        }

        if (facts.DistanceMeters > AutoReleaseDistanceMeters)
        {
            reason = "player left the cart behind";
            return BrakeAction.Release;
        }

        reason = string.Empty;
        return BrakeAction.None;
    }

    /// <summary>Confirms the physics engaged. Called only after the adapter
    /// succeeded.</summary>
    public void MarkEngaged(string cartId)
    {
        EngagedCartId = cartId;
    }

    /// <summary>Confirms release. Always safe to call; the state machine
    /// never stays engaged past this point even if the physics restore had
    /// nothing left to restore (destroyed cart).</summary>
    public void MarkReleased()
    {
        EngagedCartId = null;
    }
}
