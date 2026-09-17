using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Whether a cooperative order can be offered or resumed now (COOP-03):
/// Teamster present and compatible, Gunnar allowed and available, a cart
/// assigned, upright and not busy. Reads only (<c>hello</c>, <c>describeLease</c>);
/// never changes anything on either side.</summary>
internal static class CooperationAvailabilityProbe
{
    public static CooperationAvailability Evaluate(HaulClient client, float now, out CollectionAttentionReason reason, out string detail)
    {
        reason = CollectionAttentionReason.HaulerUnavailable;
        switch (client.DiscoveryStatus)
        {
            case CapabilityStatus.Available:
                break;
            case CapabilityStatus.Absent:
                detail = "Concerned Teamster is not installed.";
                return CooperationAvailability.ProviderAbsent;
            case CapabilityStatus.VersionTooLow:
                detail = "Concerned Teamster is too old to haul for Thorstein.";
                return CooperationAvailability.ProviderTooOld;
            case CapabilityStatus.Unspecified:
                detail = "Concerned Teamster has not been looked for yet.";
                return CooperationAvailability.ProviderNotProbed;
            default:
                detail = "This Concerned Teamster cannot haul for Thorstein (" + client.DiscoveryStatus + ").";
                return CooperationAvailability.ProviderIncompatible;
        }

        HaulCall<HelloReply> hello = client.Hello(now);
        if (!hello.Succeeded)
        {
            detail = "Concerned Teamster is not answering.";
            return CooperationAvailability.ProviderNotAnswering;
        }

        HelloReply handshake = hello.Reply!;
        if (handshake.ProviderEpoch == System.Guid.Empty)
        {
            detail = "Concerned Teamster has no world loaded.";
            return CooperationAvailability.NoWorldOnProvider;
        }

        if (handshake.Authority == WorkAuthorityVerdict.OtherPeersConnected)
        {
            reason = CollectionAttentionReason.OtherPeersConnected;
            detail = WorkAuthorityPolicy.Describe(handshake.Authority);
            return CooperationAvailability.OtherPeersConnected;
        }

        if (handshake.Authority != WorkAuthorityVerdict.Granted)
        {
            detail = "Gunnar may not haul: " + WorkAuthorityPolicy.Describe(handshake.Authority);
            return CooperationAvailability.HaulingNotAllowed;
        }

        if (!handshake.WorkerAvailable)
        {
            detail = "Gunnar cannot work right now.";
            return CooperationAvailability.GunnarUnavailable;
        }

        if (!handshake.HasLease)
        {
            detail = "No cart is assigned to Gunnar.";
            return CooperationAvailability.NoCartAssigned;
        }

        HaulCall<DescribeLeaseReply> lease = client.DescribeLease(now);
        if (!lease.Succeeded)
        {
            detail = lease.Outcome == HaulCallOutcome.Refused ? "No cart is assigned to Gunnar." : "Gunnar's cart could not be read.";
            return lease.Outcome == HaulCallOutcome.Refused
                ? CooperationAvailability.NoCartAssigned
                : CooperationAvailability.ProviderNotAnswering;
        }

        DescribeLeaseReply described = lease.Reply!;
        if (!described.CartUpright)
        {
            reason = CollectionAttentionReason.HaulerNeedsAttention;
            detail = "Gunnar's cart is not upright.";
            return CooperationAvailability.CartNotUpright;
        }

        if (described.Phase == HaulWirePhase.NeedsAttention || described.Phase == HaulWirePhase.Paused)
        {
            reason = CollectionAttentionReason.HaulerNeedsAttention;
            detail = "Gunnar is stopped; see his panel in Concerned Teamster.";
            return CooperationAvailability.GunnarNeedsAttention;
        }

        if (CooperationReasons.IsBusy(described.Phase))
        {
            detail = "Gunnar is busy with a haul.";
            return CooperationAvailability.GunnarBusy;
        }

        reason = CollectionAttentionReason.Unspecified;
        detail = "Gunnar and his cart are ready.";
        return CooperationAvailability.Available;
    }
}
