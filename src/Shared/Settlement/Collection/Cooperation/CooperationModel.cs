using System;
using TheConcernedCat.Interop.Haul;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Where one cooperative delivery run stands (COOP-02). Zero is
/// unspecified.</summary>
internal enum CooperationPhase
{
    Unspecified = 0,

    /// <summary>Handshake with the haul provider; the lease, the cart and the
    /// destination are checked before anything is asked of Gunnar.</summary>
    Connecting = 1,

    /// <summary>Gunnar is bringing the cart to the rendezvous. Thorstein keeps
    /// collecting, or waits at the rendezvous once his load is ready.</summary>
    Staging = 2,

    /// <summary>The cart waits, still, at the rendezvous while Thorstein
    /// collects toward the next checkpoint.</summary>
    AwaitingLoad = 3,

    /// <summary>Thorstein walks to the waiting cart.</summary>
    ApproachingCart = 4,

    /// <summary>The cart is held still and Thorstein's load moves into it
    /// through the custody executor.</summary>
    Loading = 5,

    /// <summary>Gunnar pulls the loaded cart to the delivery container while
    /// Thorstein walks there with him.</summary>
    Hauling = 6,

    /// <summary>The cart is held still at the destination and its order
    /// material moves into the container, directly or carried across.</summary>
    Unloading = 7,

    /// <summary>Everything requested is delivered: Gunnar is released to
    /// detach and park.</summary>
    Completing = 8,

    /// <summary>Terminal.</summary>
    Completed = 9,

    /// <summary>The provider or the cart was lost: transfers have stopped and
    /// the cart's custody location is being reconciled before anything else.
    /// </summary>
    Reconciling = 10,

    /// <summary>Holding with a reason (or at the player's request); resumes
    /// from Connecting.</summary>
    Paused = 11,

    /// <summary>Stopped with one actionable reason and retained evidence.
    /// </summary>
    NeedsAttention = 12,

    /// <summary>Terminal: ended by the player. Not a refund.</summary>
    Cancelled = 13,
}

/// <summary>What the order's owner (the collection loop) should do after a
/// tick. Mirrors Foreman's <c>CooperativeStep</c> plus Cancelled.</summary>
internal enum CooperationStep
{
    Unspecified = 0,

    /// <summary>The cooperative run needs Thorstein now: walking to the cart,
    /// loading, hauling, unloading.</summary>
    Working = 1,

    /// <summary>Thorstein should collect; the cart is staging or waiting.
    /// </summary>
    CollectMore = 2,

    /// <summary>Everything requested is delivered.</summary>
    Delivered = 3,

    Paused = 4,

    NeedsAttention = 5,

    Cancelled = 6,
}

/// <summary>Where the run's pieces were at one tick, for the order owner, the
/// panel and the log.</summary>
internal sealed class CooperationTick
{
    public CooperationTick(
        CooperationStep step, CooperationPhase phase, CollectionAttentionReason reason, string detail, int planRevision)
    {
        if (step == CooperationStep.Unspecified || phase == CooperationPhase.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "A tick names its step and phase.");
        }

        Step = step;
        Phase = phase;
        Reason = reason;
        Detail = detail ?? string.Empty;
        PlanRevision = planRevision;
    }

    public CooperationStep Step { get; }

    public CooperationPhase Phase { get; }

    /// <summary>The one actionable reason while Paused or NeedsAttention;
    /// Unspecified otherwise (and for a pause the player asked for).</summary>
    public CollectionAttentionReason Reason { get; }

    /// <summary>Evidence and specifics for the reason, for the panel and log.
    /// </summary>
    public string Detail { get; }

    /// <summary>Increments on every phase, reason or rendezvous change, so a
    /// reader can tell the plan it saw is stale.</summary>
    public int PlanRevision { get; }
}

/// <summary>Why cooperation cannot be offered right now (COOP-03), for the
/// panel's participant row.</summary>
internal enum CooperationAvailability
{
    Unspecified = 0,
    Available = 1,
    ProviderNotProbed = 2,
    ProviderAbsent = 3,
    ProviderTooOld = 4,
    ProviderIncompatible = 5,
    ProviderNotAnswering = 6,
    NoWorldOnProvider = 7,
    HaulingNotAllowed = 8,
    OtherPeersConnected = 9,
    GunnarUnavailable = 10,
    NoCartAssigned = 11,
    CartNotUpright = 12,
    GunnarBusy = 13,
    GunnarNeedsAttention = 14,
}

/// <summary>Maps what the haul provider reports onto the collection order's
/// own attention vocabulary. Kept in one place so a reason is translated the
/// same way whether it arrived as a refusal or as a haul's attention.</summary>
internal static class CooperationReasons
{
    /// <summary>A refusal that only means "this rendezvous point is not one a
    /// loaded cart can reach": try the next point instead of stopping.</summary>
    public static bool IsRouteRefusal(HaulWireReason reason)
    {
        switch (reason)
        {
            case HaulWireReason.NoRoute:
            case HaulWireReason.TooSteep:
            case HaulWireReason.TooNarrow:
            case HaulWireReason.ForbiddenDoor:
            case HaulWireReason.Water:
            case HaulWireReason.UnsupportedGap:
            case HaulWireReason.OutsideLoadedArea:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The order's reason for a haul that stopped or was refused.
    /// </summary>
    public static CollectionAttentionReason ForHaul(HaulWireReason reason)
    {
        switch (reason)
        {
            case HaulWireReason.CartDestroyed:
            case HaulWireReason.CartUnloaded:
            case HaulWireReason.OwnershipLost:
            case HaulWireReason.LeaseInvalidated:
            case HaulWireReason.NoLease:
            case HaulWireReason.EpochMismatch:
                return CollectionAttentionReason.CartLeaseLost;
            case HaulWireReason.OtherPeersConnected:
                return CollectionAttentionReason.OtherPeersConnected;
            case HaulWireReason.RendezvousTimedOut:
                return CollectionAttentionReason.RendezvousTimedOut;
            case HaulWireReason.NoAuthority:
            case HaulWireReason.WorkerUnavailable:
            case HaulWireReason.HaulBusy:
            case HaulWireReason.Unspecified:
                return CollectionAttentionReason.HaulerUnavailable;
            default:
                return CollectionAttentionReason.HaulerNeedsAttention;
        }
    }

    /// <summary>What the loop should do with a provider haul phase it did not
    /// ask for.</summary>
    public static bool IsProviderEnded(HaulWirePhase phase) =>
        phase == HaulWirePhase.NeedsAttention || phase == HaulWirePhase.Paused ||
        phase == HaulWirePhase.Unassigned;

    /// <summary>Phases in which Gunnar is working on some haul and cannot take
    /// a new one.</summary>
    public static bool IsBusy(HaulWirePhase phase)
    {
        switch (phase)
        {
            case HaulWirePhase.Approaching:
            case HaulWirePhase.Hitching:
            case HaulWirePhase.Pulling:
            case HaulWirePhase.Stopping:
            case HaulWirePhase.Unloading:
            case HaulWirePhase.Detaching:
            case HaulWirePhase.Recovering:
                return true;
            default:
                return false;
        }
    }
}
