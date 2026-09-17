using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;

/// <summary>Teamster's side of <c>concernedcat.haul/1</c> (CONTRACTS.md §3,
/// DECISIONS.md D7): turns string requests from another product into calls on
/// Gunnar's <see cref="IHaulService"/>, and the service's answers back into
/// strings.
///
/// It orchestrates nothing and moves nothing. It adds exactly what a boundary
/// between separately built products needs and the service deliberately does
/// not have: request validation, request-id idempotence, provider-epoch and
/// revision staleness, the protocol's legality rules (one haul per Gunnar, a
/// transfer hold only at a still, waiting cart), and an absolute promise never
/// to throw across the boundary - every exception becomes ProviderError.
///
/// Main thread only, like the service it wraps: the consumer calls it from its
/// own plugin's update.</summary>
internal sealed class HaulProvider
{
    private readonly Func<IHaulService?> _serviceSource;
    private readonly HaulRequestLedger _ledger;

    public HaulProvider(Func<IHaulService?> serviceSource, string providerVersion, int ledgerCapacity = HaulRequestLedger.DefaultCapacity)
    {
        _serviceSource = serviceSource ?? throw new ArgumentNullException(nameof(serviceSource));
        if (!HaulFieldRules.IsToken(providerVersion, HaulFieldRules.MaxVersionLength))
        {
            throw new ArgumentException("The provider version must be a short token.", nameof(providerVersion));
        }

        ProviderVersion = providerVersion;
        _ledger = new HaulRequestLedger(ledgerCapacity);
    }

    public string ProviderVersion { get; }

    /// <summary>After <see cref="BeginShutdown"/> every op, hello included,
    /// answers Unavailable: the plugin is going away.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>Requests answered, for diagnostics.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Requests that ended in ProviderError.</summary>
    public int FaultCount { get; private set; }

    /// <summary>The type and message of the last caught exception, for one log
    /// line on the provider's side. Never sent across.</summary>
    public string LastFault { get; private set; } = string.Empty;

    public void BeginShutdown() => IsShuttingDown = true;

    /// <summary>The value a provider plugin publishes under
    /// <see cref="CapabilityMap.PropertyName"/>: one BCL delegate under
    /// <c>concernedcat.haul/1</c>, read-only.</summary>
    public IReadOnlyDictionary<string, object> CreateCapabilityMap()
    {
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint = Handle;
        var map = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major)] = endpoint,
        };

        return new ReadOnlyDictionary<string, object>(map);
    }

    /// <summary>Answers one request. Never throws.</summary>
    public IReadOnlyDictionary<string, string> Handle(IReadOnlyDictionary<string, string>? request)
    {
        RequestCount++;
        try
        {
            return HandleUnguarded(request);
        }
        catch (Exception exception)
        {
            FaultCount++;
            LastFault = exception.GetType().Name + ": " + exception.Message;
            return Fault(exception.GetType().Name);
        }
    }

    private IReadOnlyDictionary<string, string> HandleUnguarded(IReadOnlyDictionary<string, string>? request)
    {
        HaulRequestReading reading = HaulRequestReader.Read(request);
        if (!reading.IsValid)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, reading.Refusal, reading.Detail);
        }

        if (IsShuttingDown)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable, "the provider is shutting down");
        }

        IHaulService? service = _serviceSource();
        HaulRequestMessage message = reading.Message!;
        if (message.Op == HaulOp.Hello)
        {
            return Hello(service);
        }

        if (service == null)
        {
            return HaulRefusal.ToWire(
                HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable, "Gunnar's hauling runtime is not running");
        }

        Guid epoch = service.WorldLoadEpoch;
        _ledger.UseEpoch(epoch);
        if (epoch == Guid.Empty)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Unavailable, HaulWireReason.NoAuthority, "no world is loaded");
        }

        var based = (HaulEpochMessage)message;
        if (based.ProviderEpoch != epoch)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.EpochMismatch, "the provider's world was reloaded");
        }

        switch (message)
        {
            case DescribeLeaseMessage _:
                return DescribeLease(service, epoch);
            case GetHaulMessage getHaul:
                return GetHaul(service, getHaul);
            case HaulMutatingMessage mutation:
                return Mutate(service, mutation);
            default:
                return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownOp, "unhandled op");
        }
    }

    private IReadOnlyDictionary<string, string> Hello(IHaulService? service)
    {
        HaulReplyHeader accepted = HaulReplyHeader.Success(HaulReplyStatus.Accepted);
        if (service == null)
        {
            // Installed but Gunnar's runtime is not running (disabled, or not
            // started): a truthful handshake, with nothing on offer.
            return new HelloReply(
                accepted, HaulContract.Minor, ProviderVersion, Guid.Empty, WorkAuthorityVerdict.RuntimeDisabled,
                false, HaulWirePhase.Unassigned, null).ToWire();
        }

        HaulSnapshot snapshot = service.Snapshot;
        if (!HaulWireMapping.TryToWire(snapshot.Phase, out HaulWirePhase phase))
        {
            return Fault("the haul phase is unspecified");
        }

        if (!TryActiveLeaseId(service, out string? leaseId))
        {
            return Fault("the lease id cannot travel");
        }

        return new HelloReply(
            accepted, HaulContract.Minor, ProviderVersion, service.WorldLoadEpoch, service.Authority,
            service.WorkerAvailable, phase, leaseId).ToWire();
    }

    private static IReadOnlyDictionary<string, string> DescribeLease(IHaulService service, Guid epoch)
    {
        CartLease? lease = service.ActiveLease;
        if (lease == null || !lease.IsActive)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.NoLease, "no cart is assigned to Gunnar");
        }

        if (!lease.Cart.IsFromEpoch(epoch))
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.LeaseInvalidated, "the lease belongs to another world load");
        }

        if (!HaulFieldRules.IsToken(lease.LeaseId) || !HaulFieldRules.IsToken(lease.Cart.SessionId))
        {
            return Fault("the lease or cart key cannot travel");
        }

        HaulSnapshot snapshot = service.Snapshot;
        if (!HaulWireMapping.TryToWire(snapshot.Phase, out HaulWirePhase phase))
        {
            return Fault("the haul phase is unspecified");
        }

        return new DescribeLeaseReply(
            HaulReplyHeader.Success(HaulReplyStatus.Accepted), lease.LeaseId, lease.Cart.SessionId, snapshot.CartPosition,
            snapshot.CartStill, snapshot.CartUpright, snapshot.Attached, phase, snapshot.Revision).ToWire();
    }

    private static IReadOnlyDictionary<string, string> GetHaul(IHaulService service, GetHaulMessage message)
    {
        HaulSnapshot snapshot = service.Snapshot;
        if (!IsCurrentHaul(snapshot, message.HaulId))
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, "no such haul is active");
        }

        return HaulState(snapshot, HaulReplyStatus.Accepted);
    }

    private IReadOnlyDictionary<string, string> Mutate(IHaulService service, HaulMutatingMessage message)
    {
        string fingerprint = message.Fingerprint();
        switch (_ledger.Check(message.RequestId, fingerprint))
        {
            case HaulRequestCheck.DifferentPayload:
                return HaulRefusal.ToWire(
                    HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload,
                    "request id " + message.RequestId + " was already used with a different payload");

            case HaulRequestCheck.SamePayloadAccepted:
                return Success(service, message, HaulReplyStatus.AlreadySatisfied);

            case HaulRequestCheck.New:
                _ledger.Record(message.RequestId, fingerprint);
                break;
        }

        IReadOnlyDictionary<string, string>? refusal;
        HaulCommandResult result;
        switch (message)
        {
            case RequestHaulMessage leg:
                refusal = CheckRequestHaul(service, leg);
                if (refusal != null)
                {
                    return refusal;
                }

                result = service.RequestLeg(
                    new HaulLegRequest(
                        leg.HaulId, leg.OrderId, leg.LeaseId, leg.Purpose == HaulLegPurpose.ToDestination, leg.Target,
                        leg.ArrivalRadius),
                    leg.Revision);
                break;

            case AcknowledgeWaitMessage acknowledgement:
                refusal = CheckAcknowledgeWait(service, acknowledgement);
                if (refusal != null)
                {
                    return refusal;
                }

                result = service.AcknowledgeWait(
                    acknowledgement.HaulId, acknowledgement.ExpectedRevision ?? 0,
                    acknowledgement.Activity == HaulWaitActivity.Transferring);
                break;

            case CancelHaulMessage cancel:
                if (!IsCurrentHaul(service.Snapshot, cancel.HaulId))
                {
                    return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, "no such haul is active");
                }

                // Never refused for authority, availability or revision: a stop
                // is always something the provider should attempt.
                result = service.Cancel(cancel.HaulId, cancel.Disposition == HaulCancelDisposition.DetachAndPark);
                break;

            default:
                return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownOp, "unhandled op");
        }

        refusal = RefusalFor(result);
        if (refusal != null)
        {
            return refusal;
        }

        _ledger.MarkAccepted(message.RequestId);
        return Success(service, message, HaulReplyStatus.Accepted);
    }

    private static IReadOnlyDictionary<string, string>? CheckRequestHaul(IHaulService service, RequestHaulMessage leg)
    {
        WorkAuthorityVerdict authority = service.Authority;
        if (authority != WorkAuthorityVerdict.Granted)
        {
            return HaulRefusal.ToWire(
                HaulReplyStatus.Unavailable, HaulWireMapping.RefusalFor(authority), WorkAuthorityPolicy.Describe(authority));
        }

        if (!service.WorkerAvailable)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable, "Gunnar cannot work now");
        }

        CartLease? lease = service.ActiveLease;
        if (lease == null || !lease.IsActive)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.NoLease, "no cart is assigned to Gunnar");
        }

        if (!string.Equals(lease.LeaseId, leg.LeaseId, StringComparison.Ordinal))
        {
            return HaulRefusal.ToWire(
                HaulReplyStatus.Rejected, HaulWireReason.LeaseInvalidated, "that lease is no longer Gunnar's current lease");
        }

        HaulSnapshot snapshot = service.Snapshot;
        if (snapshot.HaulId.Length > 0)
        {
            bool sameHaul = string.Equals(snapshot.HaulId, leg.HaulId, StringComparison.Ordinal);
            if (!sameHaul || (snapshot.Phase != HaulPhase.Ready && snapshot.Phase != HaulPhase.Waiting))
            {
                return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, "Gunnar is busy with a haul");
            }
        }
        else if (snapshot.Phase != HaulPhase.Ready)
        {
            HaulWireReason attention = HaulWireMapping.ToWire(snapshot.Attention);
            return HaulRefusal.ToWire(
                HaulReplyStatus.Rejected, attention == HaulWireReason.Unspecified ? HaulWireReason.HaulBusy : attention,
                "Gunnar is not ready for a new haul");
        }

        if (leg.Revision != snapshot.Revision)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, "the haul revision moved on");
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? CheckAcknowledgeWait(
        IHaulService service, AcknowledgeWaitMessage acknowledgement)
    {
        HaulSnapshot snapshot = service.Snapshot;
        if (!IsCurrentHaul(snapshot, acknowledgement.HaulId))
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, "no such haul is active");
        }

        if (acknowledgement.Activity == HaulWaitActivity.Transferring)
        {
            // A transfer is about to begin: this is the moment the cart must be
            // provably held, so authority is re-asked here, not assumed.
            WorkAuthorityVerdict authority = service.Authority;
            if (authority != WorkAuthorityVerdict.Granted)
            {
                return HaulRefusal.ToWire(
                    HaulReplyStatus.Unavailable, HaulWireMapping.RefusalFor(authority), WorkAuthorityPolicy.Describe(authority));
            }

            if (snapshot.Phase != HaulPhase.Waiting || !snapshot.CartStill)
            {
                return HaulRefusal.ToWire(
                    HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, "the cart is not waiting still");
            }
        }
        else if (snapshot.Phase != HaulPhase.Unloading)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.HaulBusy, "no transfer hold is active");
        }

        if ((acknowledgement.ExpectedRevision ?? -1) != snapshot.Revision)
        {
            return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, "the haul revision moved on");
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? RefusalFor(HaulCommandResult result)
    {
        HaulWireReason reason = HaulWireMapping.ToWire(result.Reason);
        if (reason == HaulWireReason.Unspecified)
        {
            reason = HaulWireMapping.FromDetail(result.Detail);
        }

        switch (result.Outcome)
        {
            case HaulCommandOutcome.Accepted:
                return null;
            case HaulCommandOutcome.Stale:
                return HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, "the haul revision moved on");
            case HaulCommandOutcome.Rejected:
                return reason == HaulWireReason.Unspecified
                    ? Fault("the haul service refused without a reason")
                    : HaulRefusal.ToWire(HaulReplyStatus.Rejected, reason, null);
            case HaulCommandOutcome.Unavailable:
                return HaulRefusal.ToWire(
                    HaulReplyStatus.Unavailable,
                    reason == HaulWireReason.Unspecified ? HaulWireReason.WorkerUnavailable : reason,
                    null);
            default:
                return Fault("the haul service answered without an outcome");
        }
    }

    private static IReadOnlyDictionary<string, string> Success(
        IHaulService service, HaulMutatingMessage message, HaulReplyStatus status)
    {
        HaulSnapshot snapshot = service.Snapshot;
        if (!HaulWireMapping.TryToWire(snapshot.Phase, out HaulWirePhase phase))
        {
            return Fault("the haul phase is unspecified");
        }

        HaulReplyHeader header = HaulReplyHeader.Success(status);
        if (message is RequestHaulMessage leg)
        {
            return new RequestHaulReply(header, leg.HaulId, phase, snapshot.Revision).ToWire();
        }

        return new HaulPhaseReply(header, phase, snapshot.Revision).ToWire();
    }

    private static IReadOnlyDictionary<string, string> HaulState(HaulSnapshot snapshot, HaulReplyStatus status)
    {
        if (!HaulWireMapping.TryToWire(snapshot.Phase, out HaulWirePhase phase))
        {
            return Fault("the haul phase is unspecified");
        }

        return new GetHaulReply(
            HaulReplyHeader.Success(status), phase, HaulWireMapping.ToWire(snapshot.Attention), null, snapshot.Revision,
            snapshot.Arrived, snapshot.Attached, snapshot.CartPosition, snapshot.WorkerPosition, snapshot.CartStill).ToWire();
    }

    private static bool IsCurrentHaul(HaulSnapshot snapshot, string haulId) =>
        snapshot.HaulId.Length > 0 && string.Equals(snapshot.HaulId, haulId, StringComparison.Ordinal);

    private static bool TryActiveLeaseId(IHaulService service, out string? leaseId)
    {
        leaseId = null;
        CartLease? lease = service.ActiveLease;
        if (lease == null || !lease.IsActive)
        {
            return true;
        }

        if (!HaulFieldRules.IsToken(lease.LeaseId))
        {
            return false;
        }

        leaseId = lease.LeaseId;
        return true;
    }

    private static IReadOnlyDictionary<string, string> Fault(string detail) =>
        HaulRefusal.ToWire(HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable, detail);
}
