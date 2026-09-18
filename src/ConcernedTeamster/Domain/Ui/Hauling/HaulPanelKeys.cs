using TheConcernedCat.ConcernedTeamster.Domain.Hauling;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;

/// <summary>Catalog keys for Gunnar's panel (CT-032: no user-facing English in
/// the presentation layer; the sentences live in TeamsterStrings and are
/// translatable). Every phase and every attention reason has its own key, so a
/// player is never shown an enum name - and a reason added without a sentence
/// fails the panel's own test.</summary>
internal static class HaulPanelKeys
{
    public const string Button = "haul.button";
    public const string Title = "haul.title";
    public const string Assign = "haul.assign";
    public const string Confirm = "haul.confirm";
    public const string Release = "haul.release";
    public const string ReleaseConfirm = "haul.releaseconfirm";
    public const string Here = "haul.here";
    public const string Start = "haul.start";
    public const string Stop = "haul.stop";
    public const string Detach = "haul.detach";
    public const string Close = "haul.close";
    public const string HaulingOff = "haul.off";
    public const string SeamGone = "haul.seamgone";
    public const string Authority = "haul.authority";
    public const string Lease = "haul.lease";
    public const string NoLease = "haul.nolease";
    public const string Hovered = "haul.hovered";
    public const string NoHovered = "haul.nohovered";
    public const string Destination = "haul.destination";
    public const string NoDestination = "haul.nodestination";
    public const string State = "haul.state";
    public const string Reason = "haul.reason";
    public const string OrderDriven = "haul.order";

    /// <summary>The word for a phase, as a player reads it.</summary>
    public static string ForPhase(HaulPhase phase)
    {
        switch (phase)
        {
            case HaulPhase.Unassigned:
                return "haul.phase.unassigned";
            case HaulPhase.Ready:
                return "haul.phase.ready";
            case HaulPhase.Approaching:
                return "haul.phase.approaching";
            case HaulPhase.Hitching:
                return "haul.phase.hitching";
            case HaulPhase.Pulling:
                return "haul.phase.pulling";
            case HaulPhase.Stopping:
                return "haul.phase.stopping";
            case HaulPhase.Waiting:
                return "haul.phase.waiting";
            case HaulPhase.Unloading:
                return "haul.phase.unloading";
            case HaulPhase.Detaching:
                return "haul.phase.detaching";
            case HaulPhase.Paused:
                return "haul.phase.paused";
            case HaulPhase.NeedsAttention:
                return "haul.phase.needsattention";
            case HaulPhase.Recovering:
                return "haul.phase.recovering";
            default:
                return "haul.phase.unassigned";
        }
    }

    /// <summary>The one sentence for an attention reason (CONTRACTS.md §2.3:
    /// presentation is agent E's, refusals are agent A's).</summary>
    public static string ForReason(HaulAttentionReason reason)
    {
        switch (reason)
        {
            case HaulAttentionReason.NoRoute:
                return "haul.reason.noroute";
            case HaulAttentionReason.TooSteep:
                return "haul.reason.toosteep";
            case HaulAttentionReason.TooNarrow:
                return "haul.reason.toonarrow";
            case HaulAttentionReason.ForbiddenDoor:
                return "haul.reason.forbiddendoor";
            case HaulAttentionReason.Water:
                return "haul.reason.water";
            case HaulAttentionReason.UnsupportedGap:
                return "haul.reason.unsupportedgap";
            case HaulAttentionReason.OutsideLoadedArea:
                return "haul.reason.outsideloadedarea";
            case HaulAttentionReason.Wedged:
                return "haul.reason.wedged";
            case HaulAttentionReason.JointBroke:
                return "haul.reason.jointbroke";
            case HaulAttentionReason.CartTipped:
                return "haul.reason.carttipped";
            case HaulAttentionReason.PlayerTookOver:
                return "haul.reason.playertookover";
            case HaulAttentionReason.BrakeEngaged:
                return "haul.reason.brakeengaged";
            case HaulAttentionReason.OwnershipLost:
                return "haul.reason.ownershiplost";
            case HaulAttentionReason.CartDestroyed:
                return "haul.reason.cartdestroyed";
            case HaulAttentionReason.CartUnloaded:
                return "haul.reason.cartunloaded";
            case HaulAttentionReason.AuthorityLost:
                return "haul.reason.authoritylost";
            case HaulAttentionReason.OtherPeersConnected:
                return "haul.reason.otherpeersconnected";
            case HaulAttentionReason.HitchFailed:
                return "haul.reason.hitchfailed";
            case HaulAttentionReason.ApproachTimedOut:
                return "haul.reason.approachtimedout";
            case HaulAttentionReason.RendezvousTimedOut:
                return "haul.reason.rendezvoustimedout";
            case HaulAttentionReason.UnsafeParking:
                return "haul.reason.unsafeparking";
            case HaulAttentionReason.WorkerBodyLost:
                return "haul.reason.workerbodylost";
            case HaulAttentionReason.LeaseInvalidated:
                return "haul.reason.leaseinvalidated";
            case HaulAttentionReason.WorkerBodyDuplicated:
                return "haul.reason.workerbodyduplicated";
            case HaulAttentionReason.PausedByPlayer:
                return "haul.reason.pausedbyplayer";
            default:
                return "haul.reason.noroute";
        }
    }
}
