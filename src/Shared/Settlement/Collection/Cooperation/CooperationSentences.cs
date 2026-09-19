using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>One sentence a player can act on for every value the collection
/// order and the cooperative run can show (COOP-04: "one actionable reason").
///
/// Every enum value has a sentence, and the panel shows the sentence, never the
/// enum name. They live here, beside the states they explain, so a new value
/// cannot ship without one - a test walks all of them.</summary>
internal static class CooperationSentences
{
    public static string For(CollectionAttentionReason reason)
    {
        switch (reason)
        {
            case CollectionAttentionReason.Unspecified:
                return "Nothing needs your attention.";
            case CollectionAttentionReason.NoAuthority:
                return "Work is not allowed here: it runs on your own world, as the host, with nobody else connected.";
            case CollectionAttentionReason.OtherPeersConnected:
                return "Someone else is connected. Workers only run in single player or while nobody else is connected.";
            case CollectionAttentionReason.WorkerNotRecruited:
                return "Thorstein has not been recruited yet.";
            case CollectionAttentionReason.ToolMissing:
                return "Thorstein is missing a tool he was issued. Give him an axe and a hammer again.";
            case CollectionAttentionReason.ToolBroken:
                return "One of Thorstein's tools is broken. Replace it to carry on.";
            case CollectionAttentionReason.ToolHandoverUncertain:
                return "A tool handover could not be confirmed. Settle it before he works again.";
            case CollectionAttentionReason.JournalReadOnly:
                return "The settlement record cannot be written, so nothing is moved. See the log for the file.";
            case CollectionAttentionReason.ScopeInvalid:
                return "The work area is not usable. Mark a harvest area, or let him use the camp circle.";
            case CollectionAttentionReason.ScopeChanged:
                return "The work area was chosen in an earlier world load, or has changed since. " +
                    "Run cf_collect rebind to set it again.";
            case CollectionAttentionReason.ScopeUnloaded:
                return "The work area is outside loaded ground. Go back there and it continues.";
            case CollectionAttentionReason.NoEligibleSources:
                return "No loose stones or branches he may take were found in the area.";
            case CollectionAttentionReason.SourcesExhausted:
                return "The area has no more loose stones or branches to take.";
            case CollectionAttentionReason.SourceUnreachable:
                return "He cannot find a way to what is left in the area.";
            case CollectionAttentionReason.SurveyIncomplete:
                return "He could not look over the whole area, so what is there is not known yet.";
            case CollectionAttentionReason.CarryFull:
                return "Thorstein cannot carry any more.";
            case CollectionAttentionReason.NoReturnSpace:
                return "There is nowhere to put what he has: the cart and the chest have no room.";
            case CollectionAttentionReason.DestinationFull:
                return "The chest is full. Empty it, and what is waiting goes in.";
            case CollectionAttentionReason.DestinationUnavailable:
                return "The chest cannot be used right now. Pick it again, or choose another one.";
            case CollectionAttentionReason.DestinationAccessDenied:
                return "A ward or a lock keeps him out of that chest.";
            case CollectionAttentionReason.DestinationStale:
                return "The chest was chosen in an earlier world load. Look at it and choose it again.";
            case CollectionAttentionReason.TransferUncertain:
                return "A transfer could not be confirmed. Nothing is credited or replayed until you settle it.";
            case CollectionAttentionReason.ReconciliationMismatch:
                return "The record and what is actually there disagree. Settle it before he works again.";
            case CollectionAttentionReason.PlayerRemovedMaterial:
                return "Material the record was following is gone. Tell it what happened.";
            case CollectionAttentionReason.WorkerBodyLost:
                return "Thorstein is not here.";
            case CollectionAttentionReason.WorkerBodyDuplicated:
                return "Two bodies carry Thorstein's name. Neither is removed on its own; see the log.";
            case CollectionAttentionReason.HaulerUnavailable:
                return "Gunnar cannot haul right now. Check Concerned Teamster's Gunnar panel.";
            case CollectionAttentionReason.HaulerNeedsAttention:
                return "Gunnar needs your attention. Check Concerned Teamster's Gunnar panel.";
            case CollectionAttentionReason.RendezvousTimedOut:
                return "The two of them waited too long to meet. Nothing moved; start them again when the way is clear.";
            case CollectionAttentionReason.CartLeaseLost:
                return "The cart is no longer Gunnar's to pull. Anything in it stays in it.";
            case CollectionAttentionReason.PausedByPlayer:
                return "You paused this order.";
            default:
                return "Something needs your attention; see the log.";
        }
    }

    public static string For(CollectionOrderState state)
    {
        switch (state)
        {
            case CollectionOrderState.Accepted:
                return "Accepted";
            case CollectionOrderState.Surveying:
                return "Looking over the area";
            case CollectionOrderState.Collecting:
                return "Collecting";
            case CollectionOrderState.WaitingForHauler:
                return "With Gunnar and the cart";
            case CollectionOrderState.Delivering:
                return "Delivering";
            case CollectionOrderState.HoldingForPlayer:
                return "Holding it for you";
            case CollectionOrderState.Paused:
                return "Paused";
            case CollectionOrderState.NeedsAttention:
                return "Needs your attention";
            case CollectionOrderState.Completed:
                return "Done";
            case CollectionOrderState.Cancelled:
                return "Cancelled";
            default:
                return "No order";
        }
    }

    public static string For(CooperationPhase phase)
    {
        switch (phase)
        {
            case CooperationPhase.Connecting:
                return "checking Gunnar and his cart";
            case CooperationPhase.Staging:
                return "bringing the cart to the meeting point";
            case CooperationPhase.AwaitingLoad:
                return "waiting at the meeting point";
            case CooperationPhase.ApproachingCart:
                return "walking to the cart";
            case CooperationPhase.Loading:
                return "loading the cart";
            case CooperationPhase.Hauling:
                return "hauling to the chest";
            case CooperationPhase.Unloading:
                return "unloading at the chest";
            case CooperationPhase.Completing:
                return "parking the cart";
            case CooperationPhase.Completed:
                return "finished";
            case CooperationPhase.Reconciling:
                return "checking what is in the cart";
            case CooperationPhase.Paused:
                return "paused";
            case CooperationPhase.NeedsAttention:
                return "stopped";
            case CooperationPhase.Cancelled:
                return "cancelled";
            default:
                return "not started";
        }
    }

    public static string For(CooperationAvailability availability)
    {
        switch (availability)
        {
            case CooperationAvailability.Available:
                return "Gunnar and his cart are ready.";
            case CooperationAvailability.ProviderNotProbed:
                return "Concerned Teamster has not been looked for yet.";
            case CooperationAvailability.ProviderAbsent:
                return "Concerned Teamster is not installed, so Thorstein collects on his own.";
            case CooperationAvailability.ProviderTooOld:
                return "Concerned Teamster is too old to haul for Thorstein. Update it.";
            case CooperationAvailability.ProviderIncompatible:
                return "This Concerned Teamster cannot haul for Thorstein. One of the two mods needs an update.";
            case CooperationAvailability.ProviderNotAnswering:
                return "Concerned Teamster is not answering.";
            case CooperationAvailability.NoWorldOnProvider:
                return "Concerned Teamster has no world loaded.";
            case CooperationAvailability.HaulingNotAllowed:
                return "Gunnar's hauling is off, or this is not a world he may work in.";
            case CooperationAvailability.OtherPeersConnected:
                return "Someone else is connected, so Gunnar will not haul.";
            case CooperationAvailability.GunnarUnavailable:
                return "Gunnar cannot work right now.";
            case CooperationAvailability.NoCartAssigned:
                return "No cart is assigned to Gunnar. Assign one in Concerned Teamster's Gunnar panel.";
            case CooperationAvailability.CartNotUpright:
                return "Gunnar's cart is not upright.";
            case CooperationAvailability.GunnarBusy:
                return "Gunnar is busy with another haul.";
            case CooperationAvailability.GunnarNeedsAttention:
                return "Gunnar is stopped and needs your attention.";
            default:
                return "Gunnar's help cannot be offered right now.";
        }
    }

    /// <summary>Why work is refused here, in the panel's words.</summary>
    public static string For(WorkAuthorityVerdict verdict) => WorkAuthorityPolicy.Describe(verdict);

    /// <summary>Where an order collects, for the panel and the preview.</summary>
    public static string ForScope(WorkScope scope)
    {
        if (scope == null)
        {
            return "No work area yet.";
        }

        string where = scope.Source == WorkScopeSource.HarvestDesignation
            ? "your harvest area"
            : "the camp circle on " + (scope.AnchorDescription.Length > 0 ? scope.AnchorDescription : "your respawn point");
        return "Area: " + where + ", " + Round(scope.RadiusMetres) + " m around " + Describe(scope.Centre) + ".";
    }

    private static string Describe(SitePoint point) =>
        "(" + Round(point.X) + ", " + Round(point.Z) + ")";

    private static string Round(float value) =>
        value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
