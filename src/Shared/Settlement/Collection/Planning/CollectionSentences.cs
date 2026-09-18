namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>One sentence a player can act on for every collection attention
/// reason and order state (COOP-04). A notice without a next move is the kind
/// that teaches people to ignore notices, so each one names what to do.
///
/// Presentation elsewhere (agent E's UI) may word these differently; the
/// console and the log use these.</summary>
internal static class CollectionSentences
{
    public static string Describe(CollectionAttentionReason reason)
    {
        switch (reason)
        {
            case CollectionAttentionReason.Unspecified:
                return "No problem is recorded.";
            case CollectionAttentionReason.PausedByPlayer:
                return "You paused it. Nothing is wrong: resume when you want him to carry on.";

            case CollectionAttentionReason.NoAuthority:
                return "Workers may not act here right now: the settlement runtime is off, no world is loaded, or " +
                    "this game is not the host. Turn the runtime on as the host, then resume.";
            case CollectionAttentionReason.OtherPeersConnected:
                return "Someone else is connected. Workers only work in single player or while nobody else is " +
                    "connected, for now. Resume once you are alone.";
            case CollectionAttentionReason.WorkerNotRecruited:
                return "Thorstein is not employed here. Recruit him, then resume.";
            case CollectionAttentionReason.ToolMissing:
                return "Thorstein needs his own axe and hammer before he starts. Give him the missing tool, then resume.";
            case CollectionAttentionReason.ToolBroken:
                return "One of Thorstein's tools is broken. Repair or replace it, then resume. He stays hired.";
            case CollectionAttentionReason.ToolHandoverUncertain:
                return "A tool handover was interrupted and nobody knows whose hands the tool is in. Resolve it first.";
            case CollectionAttentionReason.JournalReadOnly:
                return "The settlement record cannot be written, so nothing is moved. Check the log for why.";

            case CollectionAttentionReason.ScopeInvalid:
                return "The work area this order was given no longer exists in this world load. Cancel it and " +
                    "give a new order.";
            case CollectionAttentionReason.ScopeChanged:
                return "The work area changed after the order was given (the area was redrawn or your bed moved). " +
                    "He will not follow it. Cancel and give a new order.";
            case CollectionAttentionReason.ScopeUnloaded:
                return "The work area is not loaded, so he cannot see it. Go closer, then resume.";
            case CollectionAttentionReason.NoEligibleSources:
                return "There are no natural loose stones or branches he may take in the work area.";
            case CollectionAttentionReason.SourcesExhausted:
                return "The stones and branches here have been picked. Branches grow back with time; stones do not.";
            case CollectionAttentionReason.SourceUnreachable:
                return "He could not find a way to the stones or branches that are left.";
            case CollectionAttentionReason.SurveyIncomplete:
                return "He could not finish looking over the work area. Resume to let him look again.";

            case CollectionAttentionReason.CarryFull:
                return "He cannot carry any more. If something he picked did not fit, it is lying where he picked " +
                    "it: take it yourself or record it as lost. Make room on him, then resume.";
            case CollectionAttentionReason.NoReturnSpace:
                return "There is no room left where the materials go. Make room, then resume.";
            case CollectionAttentionReason.DestinationFull:
                return "The chest is full. He keeps what he carries. Make room in that chest, then resume.";
            case CollectionAttentionReason.DestinationUnavailable:
                return "He cannot use the chest right now: it is not loaded, in use or out of reach. He keeps what " +
                    "he carries.";
            case CollectionAttentionReason.DestinationAccessDenied:
                return "He is not allowed to use that chest. He keeps what he carries.";
            case CollectionAttentionReason.DestinationStale:
                return "The chest was chosen before the world was reloaded. Choose it again with a new order.";

            case CollectionAttentionReason.TransferUncertain:
                return "A transfer was interrupted and it is not known where those items are. Nothing is replayed. " +
                    "Check the chest and Thorstein, then resolve it.";
            case CollectionAttentionReason.ReconciliationMismatch:
                return "The record and the real inventories disagree. Nothing was changed; check them and resolve it.";
            case CollectionAttentionReason.PlayerRemovedMaterial:
                return "Some collected material was taken away. Record it as lost to continue.";
            case CollectionAttentionReason.WorkerBodyLost:
                return "Thorstein is not here: his body is missing, unloaded or not under this world's control.";
            case CollectionAttentionReason.WorkerBodyDuplicated:
                return "There are two Thorsteins. Neither is removed automatically; sort it out before he works.";

            case CollectionAttentionReason.HaulerUnavailable:
                return "Gunnar or his cart is not available for this order.";
            case CollectionAttentionReason.HaulerNeedsAttention:
                return "Gunnar needs attention before the order can continue.";
            case CollectionAttentionReason.RendezvousTimedOut:
                return "Gunnar and Thorstein could not meet in time.";
            case CollectionAttentionReason.CartLeaseLost:
                return "The cart is no longer assigned to this order.";

            default:
                return "A reason was recorded that this build does not know; that is a bug.";
        }
    }

    public static string Describe(CollectionOrderState state)
    {
        switch (state)
        {
            case CollectionOrderState.Accepted:
                return "accepted";
            case CollectionOrderState.Surveying:
                return "surveying (solo survey by Thorstein)";
            case CollectionOrderState.Collecting:
                return "collecting";
            case CollectionOrderState.WaitingForHauler:
                return "working with the hauler";
            case CollectionOrderState.Delivering:
                return "delivering";
            case CollectionOrderState.HoldingForPlayer:
                return "holding the materials for you";
            case CollectionOrderState.Paused:
                return "paused";
            case CollectionOrderState.NeedsAttention:
                return "needs attention";
            case CollectionOrderState.Completed:
                return "completed";
            case CollectionOrderState.Cancelled:
                return "cancelled";
            default:
                return "in an unknown state (a bug)";
        }
    }
}
