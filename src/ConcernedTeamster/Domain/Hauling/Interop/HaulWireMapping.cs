using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;

/// <summary>Gunnar's domain values as they travel in <c>concernedcat.haul/1</c>.
///
/// Written as explicit switches rather than casts: the phase and reason enums
/// are pinned name for name today, but a value added to Teamster's side without
/// a contract revision must fail loudly here (the provider answers
/// ProviderError), not travel under whatever wire value shares its number.
/// </summary>
internal static class HaulWireMapping
{
    public static bool TryToWire(HaulPhase phase, out HaulWirePhase wire)
    {
        switch (phase)
        {
            case HaulPhase.Unassigned:
                wire = HaulWirePhase.Unassigned;
                return true;
            case HaulPhase.Ready:
                wire = HaulWirePhase.Ready;
                return true;
            case HaulPhase.Approaching:
                wire = HaulWirePhase.Approaching;
                return true;
            case HaulPhase.Hitching:
                wire = HaulWirePhase.Hitching;
                return true;
            case HaulPhase.Pulling:
                wire = HaulWirePhase.Pulling;
                return true;
            case HaulPhase.Stopping:
                wire = HaulWirePhase.Stopping;
                return true;
            case HaulPhase.Waiting:
                wire = HaulWirePhase.Waiting;
                return true;
            case HaulPhase.Unloading:
                wire = HaulWirePhase.Unloading;
                return true;
            case HaulPhase.Detaching:
                wire = HaulWirePhase.Detaching;
                return true;
            case HaulPhase.Paused:
                wire = HaulWirePhase.Paused;
                return true;
            case HaulPhase.NeedsAttention:
                wire = HaulWirePhase.NeedsAttention;
                return true;
            case HaulPhase.Recovering:
                wire = HaulWirePhase.Recovering;
                return true;
            default:
                wire = HaulWirePhase.Unspecified;
                return false;
        }
    }

    /// <summary>A haul's attention reason on the wire. Unspecified stays
    /// Unspecified ("no reason"); every real reason has a wire value, and
    /// <c>AuthorityLost</c> travels as the wire's <c>NoAuthority</c>.</summary>
    public static HaulWireReason ToWire(HaulAttentionReason reason)
    {
        switch (reason)
        {
            case HaulAttentionReason.NoRoute:
                return HaulWireReason.NoRoute;
            case HaulAttentionReason.TooSteep:
                return HaulWireReason.TooSteep;
            case HaulAttentionReason.TooNarrow:
                return HaulWireReason.TooNarrow;
            case HaulAttentionReason.ForbiddenDoor:
                return HaulWireReason.ForbiddenDoor;
            case HaulAttentionReason.Water:
                return HaulWireReason.Water;
            case HaulAttentionReason.UnsupportedGap:
                return HaulWireReason.UnsupportedGap;
            case HaulAttentionReason.OutsideLoadedArea:
                return HaulWireReason.OutsideLoadedArea;
            case HaulAttentionReason.Wedged:
                return HaulWireReason.Wedged;
            case HaulAttentionReason.JointBroke:
                return HaulWireReason.JointBroke;
            case HaulAttentionReason.CartTipped:
                return HaulWireReason.CartTipped;
            case HaulAttentionReason.PlayerTookOver:
                return HaulWireReason.PlayerTookOver;
            case HaulAttentionReason.BrakeEngaged:
                return HaulWireReason.BrakeEngaged;
            case HaulAttentionReason.OwnershipLost:
                return HaulWireReason.OwnershipLost;
            case HaulAttentionReason.CartDestroyed:
                return HaulWireReason.CartDestroyed;
            case HaulAttentionReason.CartUnloaded:
                return HaulWireReason.CartUnloaded;
            case HaulAttentionReason.AuthorityLost:
                return HaulWireReason.NoAuthority;
            case HaulAttentionReason.OtherPeersConnected:
                return HaulWireReason.OtherPeersConnected;
            case HaulAttentionReason.HitchFailed:
                return HaulWireReason.HitchFailed;
            case HaulAttentionReason.ApproachTimedOut:
                return HaulWireReason.ApproachTimedOut;
            case HaulAttentionReason.RendezvousTimedOut:
                return HaulWireReason.RendezvousTimedOut;
            case HaulAttentionReason.UnsafeParking:
                return HaulWireReason.UnsafeParking;
            case HaulAttentionReason.WorkerBodyLost:
                return HaulWireReason.WorkerBodyLost;
            case HaulAttentionReason.LeaseInvalidated:
                return HaulWireReason.LeaseInvalidated;
            default:
                return HaulWireReason.Unspecified;
        }
    }

    /// <summary>Why a mutation cannot be served while authority is not
    /// granted: a connected peer is its own, actionable reason; everything else
    /// is "no authority here".</summary>
    public static HaulWireReason RefusalFor(WorkAuthorityVerdict verdict) =>
        verdict == WorkAuthorityVerdict.OtherPeersConnected
            ? HaulWireReason.OtherPeersConnected
            : HaulWireReason.NoAuthority;
}
