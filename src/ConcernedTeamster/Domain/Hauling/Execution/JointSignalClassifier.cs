using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>What one frame's signals say about a haul Gunnar believes is
/// hitched (CONTRACTS.md §2.5).</summary>
internal readonly struct SignalVerdict
{
    private SignalVerdict(
        bool healthy,
        HaulAttentionReason reason,
        bool releaseJoint,
        bool pause,
        LeaseInvalidation invalidation,
        string detail,
        bool stillHeld)
    {
        Healthy = healthy;
        Reason = reason;
        ReleaseJoint = releaseJoint;
        Pause = pause;
        Invalidation = invalidation;
        Detail = detail ?? string.Empty;
        _stillHeld = stillHeld;
    }

    private readonly bool _stillHeld;

    public bool Healthy { get; }

    /// <summary>Why control ends; unspecified only when healthy.</summary>
    public HaulAttentionReason Reason { get; }

    /// <summary>Call the cart's own detach now. False only when the joint is
    /// someone else's, or when keeping it is what holds a leaning cart.</summary>
    public bool ReleaseJoint { get; }

    /// <summary>Paused rather than NeedsAttention (a peer connected, D3).
    /// </summary>
    public bool Pause { get; }

    /// <summary>The lease ends for this reason (D6); unspecified keeps it.
    /// </summary>
    public LeaseInvalidation Invalidation { get; }

    public bool InvalidatesLease => Invalidation != LeaseInvalidation.Unspecified;

    /// <summary>Control ends but Gunnar's joint stays, because the joint is
    /// what holds the cart (a hard lean). Repeating this verdict while a person
    /// is already asked changes nothing.</summary>
    public bool StillHeld => !Healthy && !ReleaseJoint && _stillHeld;

    public string Detail { get; }

    public static SignalVerdict Fine() =>
        new SignalVerdict(true, HaulAttentionReason.Unspecified, false, false, LeaseInvalidation.Unspecified, string.Empty, false);

    public static SignalVerdict End(
        HaulAttentionReason reason,
        bool releaseJoint,
        LeaseInvalidation invalidation,
        string detail,
        bool pause = false,
        bool stillHeld = false)
    {
        if (reason == HaulAttentionReason.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Ending control needs a reason.");
        }

        return new SignalVerdict(false, reason, releaseJoint, pause, invalidation, detail, stillHeld && !releaseJoint);
    }
}

/// <summary>Classifies the per-frame signals of a hitched haul into the one
/// reason control ends, whether to release the joint, and whether the lease
/// ends (CONTRACTS.md §2.5, §3.3; DECISIONS.md D4, D6).
///
/// <b>Order.</b> Authority first, because without it nothing else may continue;
/// then the body, because a joint without its puller is the one state that must
/// never last a physics step; then the cart itself; then the joint. A vanished
/// joint is attributed from the evidence vanilla leaves behind: ownership moved
/// (vanilla detaches every non-owner), the local player now holds a joint
/// (vanilla's attach detaches every loaded cart first) or is pointing at the
/// cart (the owner's own Use toggles the joint off), the cart tipped past
/// vanilla's own 0.1, and otherwise the hitch broke.
///
/// Nothing here re-hitches. Every ending is final for the attempt.</summary>
internal static class JointSignalClassifier
{
    public static SignalVerdict Classify(
        WorkAuthorityVerdict authority,
        CartObservation cart,
        PullerBodyFacts puller,
        HaulLimits limits)
    {
        if (authority == WorkAuthorityVerdict.OtherPeersConnected)
        {
            return SignalVerdict.End(
                HaulAttentionReason.OtherPeersConnected,
                releaseJoint: true,
                LeaseInvalidation.Unspecified,
                "a peer connected; Gunnar lets go and waits",
                pause: true);
        }

        if (authority != WorkAuthorityVerdict.Granted)
        {
            return SignalVerdict.End(
                HaulAttentionReason.AuthorityLost,
                releaseJoint: true,
                LeaseInvalidation.AuthorityLost,
                "work authority is " + authority);
        }

        if (!puller.Present || puller.Faulted || puller.Dead)
        {
            return SignalVerdict.End(
                HaulAttentionReason.WorkerBodyLost,
                releaseJoint: true,
                LeaseInvalidation.WorkerBodyLost,
                puller.Faulted ? "Gunnar's worker faulted" : puller.Dead ? "Gunnar's body died" : "Gunnar's body is gone");
        }

        if (!cart.Resolved)
        {
            return cart.RecordExists
                ? SignalVerdict.End(HaulAttentionReason.CartUnloaded, releaseJoint: true, LeaseInvalidation.CartUnloaded, "the cart unloaded")
                : SignalVerdict.End(HaulAttentionReason.CartDestroyed, releaseJoint: true, LeaseInvalidation.CartDestroyed, "the cart was destroyed");
        }

        if (!cart.HasJoint)
        {
            if (!cart.IsOwner)
            {
                return SignalVerdict.End(
                    HaulAttentionReason.OwnershipLost,
                    releaseJoint: true,
                    LeaseInvalidation.OwnershipLost,
                    "the cart's ownership moved away and vanilla detached it");
            }

            if (cart.LocalPlayerHasJoint)
            {
                return SignalVerdict.End(
                    HaulAttentionReason.PlayerTookOver,
                    releaseJoint: true,
                    LeaseInvalidation.Unspecified,
                    "the player grabbed a cart, which detaches every other cart");
            }

            if (cart.LocalPlayerHoveringCart)
            {
                return SignalVerdict.End(
                    HaulAttentionReason.PlayerTookOver,
                    releaseJoint: true,
                    LeaseInvalidation.Unspecified,
                    "the joint came off while the player was using the cart");
            }

            if (!(cart.UpDot >= HaulExecutionLimits.VanillaTipUpDot))
            {
                return SignalVerdict.End(
                    HaulAttentionReason.CartTipped,
                    releaseJoint: true,
                    LeaseInvalidation.Unspecified,
                    "the cart tipped over and vanilla let go");
            }

            return SignalVerdict.End(
                HaulAttentionReason.JointBroke,
                releaseJoint: true,
                LeaseInvalidation.Unspecified,
                "the hitch came apart (break force or detach distance)");
        }

        if (!cart.JointConnectedToPuller)
        {
            // Someone else's joint now: never released by Gunnar.
            return cart.JointConnectedToLocalPlayer
                ? SignalVerdict.End(HaulAttentionReason.PlayerTookOver, releaseJoint: false, LeaseInvalidation.Unspecified, "the player is pulling the cart")
                : SignalVerdict.End(HaulAttentionReason.JointBroke, releaseJoint: false, LeaseInvalidation.Unspecified, "the cart's joint is connected to another body");
        }

        if (!cart.IsOwner)
        {
            return SignalVerdict.End(
                HaulAttentionReason.OwnershipLost,
                releaseJoint: true,
                LeaseInvalidation.OwnershipLost,
                "the cart's ownership moved away");
        }

        if (cart.BrakeEngaged || cart.RootFrozen)
        {
            return SignalVerdict.End(
                HaulAttentionReason.BrakeEngaged,
                releaseJoint: true,
                LeaseInvalidation.Unspecified,
                "a brake froze the cart while it was hitched");
        }

        if (!(cart.UpDot >= limits.MinUprightDot))
        {
            // Leaning hard but still held: the joint is what holds it, so it
            // stays until a person decides; vanilla lets go at 0.1 anyway.
            return SignalVerdict.End(
                HaulAttentionReason.CartTipped,
                releaseJoint: false,
                LeaseInvalidation.Unspecified,
                FormattableString.Invariant($"the cart is leaning (up axis {cart.UpDot:0.###})"),
                stillHeld: true);
        }

        return SignalVerdict.Fine();
    }
}
