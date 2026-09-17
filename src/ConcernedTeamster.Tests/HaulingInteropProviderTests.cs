using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#317: Teamster's provider of <c>concernedcat.haul/1</c>
/// (CONTRACTS.md §3) against a haul service that enforces nothing on its
/// behalf - every op, request-id idempotence, epoch and revision staleness,
/// the protocol's legality rules, and the promise that nothing thrown inside
/// crosses the boundary.</summary>
public class HaulingInteropProviderTests
{
    private static readonly Guid Epoch = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid NextEpoch = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly WorkPoint Rendezvous = new WorkPoint(20f, 30f, 20f);

    // --- hello and reads --------------------------------------------------------------------

    [Fact]
    public void HelloIsATruthfulHandshakeWithNoSideEffects()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        int revision = service.Revision;

        WireMessage reply = Send(provider, new HelloMessage("0.2.0").ToWire());

        AssertStatus(reply, HaulReplyStatus.Accepted);
        Assert.True(reply.TryGetInt(HaulContract.Keys.ContractMinor, out int minor));
        Assert.Equal(HaulContract.Minor, minor);
        Assert.True(reply.TryGet(HaulContract.Keys.ProviderVersion, out string version));
        Assert.Equal("1.0.5", version);
        Assert.True(reply.TryGet(HaulContract.Keys.ProviderEpoch, out string epoch));
        Assert.Equal(Epoch.ToString("N"), epoch);
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Authority, out WorkAuthorityVerdict authority));
        Assert.Equal(WorkAuthorityVerdict.Granted, authority);
        Assert.True(reply.TryGetBool(HaulContract.Keys.WorkerAvailable, out bool available));
        Assert.True(available);
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Phase, out HaulWirePhase phase));
        Assert.Equal(HaulWirePhase.Ready, phase);
        Assert.True(reply.TryGet(HaulContract.Keys.LeaseId, out string leaseId));
        Assert.Equal("lease-1", leaseId);

        Assert.Equal(revision, service.Revision);
        Assert.Equal(0, service.LegCalls + service.AcknowledgeCalls + service.CancelCalls);
    }

    [Fact]
    public void HelloWithoutALeaseOmitsTheLeaseAndReportsTheRealAuthority()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create(withLease: false);
        service.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        service.WorkerAvailable = false;

        WireMessage reply = Send(provider, new HelloMessage("0.2.0").ToWire());

        AssertStatus(reply, HaulReplyStatus.Accepted);
        Assert.False(reply.Has(HaulContract.Keys.LeaseId));
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Authority, out WorkAuthorityVerdict authority));
        Assert.Equal(WorkAuthorityVerdict.OtherPeersConnected, authority);
        Assert.True(reply.TryGetBool(HaulContract.Keys.WorkerAvailable, out bool available));
        Assert.False(available);
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Phase, out HaulWirePhase phase));
        Assert.Equal(HaulWirePhase.Unassigned, phase);
    }

    [Fact]
    public void WithoutAHaulRuntimeHelloOffersNothingAndEverythingElseIsUnavailable()
    {
        var provider = new HaulProvider(() => null, "1.0.5");

        WireMessage hello = Send(provider, new HelloMessage("0.2.0").ToWire());
        AssertStatus(hello, HaulReplyStatus.Accepted);
        Assert.True(hello.TryGet(HaulContract.Keys.ProviderEpoch, out string epoch));
        Assert.Equal(Guid.Empty.ToString("N"), epoch);
        Assert.True(hello.TryGetEnum(HaulContract.Keys.Authority, out WorkAuthorityVerdict authority));
        Assert.Equal(WorkAuthorityVerdict.RuntimeDisabled, authority);
        Assert.True(hello.TryGetBool(HaulContract.Keys.WorkerAvailable, out bool available));
        Assert.False(available);

        AssertRefusal(Send(provider, new DescribeLeaseMessage(Epoch).ToWire()), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
        AssertRefusal(Send(provider, RequestHaul("h-r1", 0)), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
    }

    [Fact]
    public void EveryOpIsUnavailableOnceShuttingDown()
    {
        (HaulProvider provider, _) = Create();
        provider.BeginShutdown();

        AssertRefusal(Send(provider, new HelloMessage("0.2.0").ToWire()), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
        AssertRefusal(Send(provider, new GetHaulMessage(Epoch, "h-order-1").ToWire()), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
        AssertRefusal(Send(provider, Cancel("h-c1", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
    }

    [Fact]
    public void DescribeLeaseReportsTheLeasedCartOrNoLease()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();

        WireMessage lease = Send(provider, new DescribeLeaseMessage(Epoch).ToWire());
        AssertStatus(lease, HaulReplyStatus.Accepted);
        Assert.True(DescribeLeaseReply.TryRead(lease.ToWire(), out DescribeLeaseReply? parsed));
        Assert.Equal("lease-1", parsed!.LeaseId);
        Assert.Equal("5:42", parsed.CartSessionKey);
        Assert.Equal(new WorkPoint(10f, 30f, 10f), parsed.CartPosition);
        Assert.True(parsed.CartStill);
        Assert.True(parsed.CartUpright);
        Assert.False(parsed.Attached);
        Assert.Equal(HaulWirePhase.Ready, parsed.Phase);
        Assert.Equal(service.Revision, parsed.Revision);

        service.CartPosition = null;
        Assert.True(DescribeLeaseReply.TryRead(Send(provider, new DescribeLeaseMessage(Epoch).ToWire()).ToWire(), out parsed));
        Assert.Null(parsed!.CartPosition);

        service.ReleaseLease();
        AssertRefusal(Send(provider, new DescribeLeaseMessage(Epoch).ToWire()), HaulReplyStatus.Rejected, HaulWireReason.NoLease);
    }

    [Fact]
    public void GetHaulReportsPhaseAttentionAndPositionsForTheCurrentHaulOnly()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertRefusal(Send(provider, new GetHaulMessage(Epoch, "h-order-1").ToWire()), HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul);

        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);

        Assert.True(GetHaulReply.TryRead(Send(provider, new GetHaulMessage(Epoch, "h-order-1").ToWire()).ToWire(), out GetHaulReply? haul));
        Assert.Equal(HaulWirePhase.Waiting, haul!.Phase);
        Assert.True(haul.Arrived);
        Assert.True(haul.Attached);
        Assert.True(haul.CartStill);
        Assert.False(haul.HasAttention);
        Assert.Equal(Rendezvous, haul.CartPosition);
        Assert.NotNull(haul.WorkerPosition);
        Assert.Equal(service.Revision, haul.Revision);

        AssertRefusal(Send(provider, new GetHaulMessage(Epoch, "h-other").ToWire()), HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul);

        service.EndControl(HaulAttentionReason.PlayerTookOver);
        Assert.True(GetHaulReply.TryRead(Send(provider, new GetHaulMessage(Epoch, "h-order-1").ToWire()).ToWire(), out haul));
        Assert.Equal(HaulWirePhase.NeedsAttention, haul!.Phase);
        Assert.Equal(HaulWireReason.PlayerTookOver, haul.Attention);
        Assert.False(haul.Attached);
        Assert.False(haul.Arrived);
    }

    // --- requestHaul ------------------------------------------------------------------------

    [Fact]
    public void RequestHaulStartsALegAndAnswersTheNewRevision()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        int before = service.Revision;

        WireMessage reply = Send(provider, RequestHaul("h-r1", before, purpose: HaulLegPurpose.ToDestination));

        Assert.True(RequestHaulReply.TryRead(reply.ToWire(), out RequestHaulReply? parsed));
        Assert.Equal(HaulReplyStatus.Accepted, parsed!.Header.Status);
        Assert.Equal("h-order-1", parsed.HaulId);
        Assert.Equal(HaulWirePhase.Approaching, parsed.Phase);
        Assert.Equal(service.Revision, parsed.Revision);
        Assert.True(parsed.Revision > before);

        Assert.Equal(1, service.LegCalls);
        Assert.Equal("h-order-1", service.LastLeg!.HaulId);
        Assert.Equal("order-1", service.LastLeg.OrderId);
        Assert.Equal("lease-1", service.LastLeg.LeaseId);
        Assert.True(service.LastLeg.ToDestination);
        Assert.Equal(Rendezvous, service.LastLeg.Target);
        Assert.Equal(3f, service.LastLeg.ArrivalRadiusMetres);
    }

    [Fact]
    public void ARetryOfAnAcceptedRequestIsAlreadySatisfiedAndNeverAppliedTwice()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        IReadOnlyDictionary<string, string> request = RequestHaul("h-r1", service.Revision);

        AssertStatus(Send(provider, request), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);

        // The reply was lost; the consumer repeats the same attempt after the
        // revision has moved on. Idempotence is decided before staleness.
        WireMessage retry = Send(provider, request);
        AssertStatus(retry, HaulReplyStatus.AlreadySatisfied);
        Assert.True(RequestHaulReply.TryRead(retry.ToWire(), out RequestHaulReply? parsed));
        Assert.Equal(HaulWirePhase.Waiting, parsed!.Phase);
        Assert.Equal(service.Revision, parsed.Revision);
        Assert.Equal(1, service.LegCalls);
    }

    [Fact]
    public void TheSameRequestIdWithADifferentPayloadIsRejectedForEveryMutatingOp()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);

        var differentTarget = new RequestHaulMessage(
            Epoch, "h-r1", 0, "order-1", "h-order-1", "lease-1", HaulLegPurpose.ToRendezvous, new WorkPoint(21f, 30f, 20f), 3f);
        AssertRefusal(Send(provider, differentTarget.ToWire()), HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload);

        AssertStatus(Send(provider, Acknowledge("h-a1", service.Revision, HaulWaitActivity.Transferring)), HaulReplyStatus.Accepted);
        AssertRefusal(
            Send(provider, Acknowledge("h-a1", service.Revision, HaulWaitActivity.Done)),
            HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload);

        AssertStatus(Send(provider, Cancel("h-c1", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Accepted);
        AssertRefusal(
            Send(provider, Cancel("h-c1", HaulCancelDisposition.DetachAndPark)),
            HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload);

        // A request id used by one op cannot be reused by another either.
        AssertRefusal(Send(provider, Cancel("h-r1", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Rejected, HaulWireReason.DuplicateRequestDifferentPayload);
        Assert.Equal(1, service.LegCalls);
        Assert.Equal(1, service.AcknowledgeCalls);
        Assert.Equal(1, service.CancelCalls);
    }

    [Fact]
    public void ARequestThatWasNotAcceptedIsEvaluatedAgainOnRetry()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        service.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        IReadOnlyDictionary<string, string> request = RequestHaul("h-r1", service.Revision);

        AssertRefusal(Send(provider, request), HaulReplyStatus.Unavailable, HaulWireReason.OtherPeersConnected);
        Assert.Equal(0, service.LegCalls);

        service.Authority = WorkAuthorityVerdict.Granted;
        AssertStatus(Send(provider, request), HaulReplyStatus.Accepted);
        Assert.Equal(1, service.LegCalls);
    }

    [Fact]
    public void ARequestNeedsAuthorityAWorkerAndTheCurrentLease()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();

        service.Authority = WorkAuthorityVerdict.RuntimeDisabled;
        AssertRefusal(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Unavailable, HaulWireReason.NoAuthority);
        service.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        AssertRefusal(Send(provider, RequestHaul("h-r2", service.Revision)), HaulReplyStatus.Unavailable, HaulWireReason.OtherPeersConnected);
        service.Authority = WorkAuthorityVerdict.Granted;

        service.WorkerAvailable = false;
        AssertRefusal(Send(provider, RequestHaul("h-r3", service.Revision)), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);
        service.WorkerAvailable = true;

        AssertRefusal(
            Send(provider, RequestHaul("h-r4", service.Revision, leaseId: "lease-0")),
            HaulReplyStatus.Rejected, HaulWireReason.LeaseInvalidated);

        service.ReleaseLease();
        AssertRefusal(Send(provider, RequestHaul("h-r5", service.Revision)), HaulReplyStatus.Rejected, HaulWireReason.NoLease);
        Assert.Equal(0, service.LegCalls);
    }

    [Fact]
    public void OneHaulPerGunnarAndANewLegOnlyFromReadyOrWaiting()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);

        AssertRefusal(
            Send(provider, RequestHaul("h-r2", service.Revision, haulId: "h-other")),
            HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);
        AssertRefusal(Send(provider, RequestHaul("h-r3", service.Revision)), HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);

        service.CompleteLeg(Rendezvous);
        AssertStatus(
            Send(provider, RequestHaul("h-r4", service.Revision, purpose: HaulLegPurpose.ToDestination)),
            HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Pulling, service.Phase);
        Assert.Equal(2, service.LegCalls);
    }

    [Fact]
    public void GunnarNeedingAttentionRefusesANewHaulWithHisReason()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        service.EndControl(HaulAttentionReason.BrakeEngaged);

        AssertRefusal(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Rejected, HaulWireReason.BrakeEngaged);
        Assert.Equal(0, service.LegCalls);
    }

    [Fact]
    public void AnOldProviderEpochIsStaleForEveryOpButHello()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.ReloadWorld(NextEpoch);

        AssertRefusal(Send(provider, new DescribeLeaseMessage(Epoch).ToWire()), HaulReplyStatus.Stale, HaulWireReason.EpochMismatch);
        AssertRefusal(Send(provider, new GetHaulMessage(Epoch, "h-order-1").ToWire()), HaulReplyStatus.Stale, HaulWireReason.EpochMismatch);
        AssertRefusal(Send(provider, RequestHaul("h-r1", 0)), HaulReplyStatus.Stale, HaulWireReason.EpochMismatch);
        AssertRefusal(Send(provider, Acknowledge("h-a1", 0, HaulWaitActivity.Done)), HaulReplyStatus.Stale, HaulWireReason.EpochMismatch);
        AssertRefusal(Send(provider, Cancel("h-c1", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Stale, HaulWireReason.EpochMismatch);

        WireMessage hello = Send(provider, new HelloMessage("0.2.0").ToWire());
        Assert.True(hello.TryGet(HaulContract.Keys.ProviderEpoch, out string epoch));
        Assert.Equal(NextEpoch.ToString("N"), epoch);
        Assert.Equal(1, service.LegCalls);
    }

    [Fact]
    public void AWorldReloadForgetsEveryRequestId()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);

        service.ReloadWorld(NextEpoch);
        service.AssignCart("lease-2", "7:1");
        var sameIdNewWorld = new RequestHaulMessage(
            NextEpoch, "h-r1", service.Revision, "order-1", "h-order-2", "lease-2", HaulLegPurpose.ToRendezvous, Rendezvous, 3f);

        AssertStatus(Send(provider, sameIdNewWorld.ToWire()), HaulReplyStatus.Accepted);
        Assert.Equal(2, service.LegCalls);
    }

    [Fact]
    public void AnOldRevisionIsStaleForALegAndAnAcknowledgementButNeverForACancel()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        int old = service.Revision;
        AssertStatus(Send(provider, RequestHaul("h-r1", old)), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);

        AssertRefusal(Send(provider, RequestHaul("h-r2", old, purpose: HaulLegPurpose.ToDestination)), HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch);
        AssertRefusal(Send(provider, Acknowledge("h-a1", old, HaulWaitActivity.Transferring)), HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch);
        Assert.Equal(HaulPhase.Waiting, service.Phase);

        var staleCancel = new CancelHaulMessage(Epoch, "h-c1", "h-order-1", HaulCancelDisposition.DetachAndPark, old);
        AssertStatus(Send(provider, staleCancel.ToWire()), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Ready, service.Phase);
        Assert.Equal(1, service.LegCalls);
        Assert.Equal(0, service.AcknowledgeCalls);
    }

    // --- acknowledgeWait and cancelHaul -------------------------------------------------------

    [Fact]
    public void TransferringIsAcceptedOnlyAtAStillWaitingCartAndDoneOnlyWhileUnloading()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);

        AssertRefusal(Send(provider, Acknowledge("h-a1", service.Revision, HaulWaitActivity.Transferring)), HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);
        service.CompleteLeg(Rendezvous);

        AssertRefusal(Send(provider, Acknowledge("h-a2", service.Revision, HaulWaitActivity.Done)), HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);

        service.CartStill = false;
        AssertRefusal(Send(provider, Acknowledge("h-a3", service.Revision, HaulWaitActivity.Transferring)), HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);
        service.CartStill = true;

        service.Authority = WorkAuthorityVerdict.OtherPeersConnected;
        AssertRefusal(Send(provider, Acknowledge("h-a4", service.Revision, HaulWaitActivity.Transferring)), HaulReplyStatus.Unavailable, HaulWireReason.OtherPeersConnected);
        service.Authority = WorkAuthorityVerdict.Granted;
        Assert.Equal(0, service.AcknowledgeCalls);

        WireMessage hold = Send(provider, Acknowledge("h-a5", service.Revision, HaulWaitActivity.Transferring));
        Assert.True(HaulPhaseReply.TryRead(hold.ToWire(), out HaulPhaseReply? held));
        Assert.Equal(HaulReplyStatus.Accepted, held!.Header.Status);
        Assert.Equal(HaulWirePhase.Unloading, held.Phase);
        Assert.Equal(service.Revision, held.Revision);

        // While the hold is on, nothing may move the cart.
        AssertRefusal(
            Send(provider, RequestHaul("h-r2", service.Revision, purpose: HaulLegPurpose.ToDestination)),
            HaulReplyStatus.Rejected, HaulWireReason.HaulBusy);

        AssertStatus(Send(provider, Acknowledge("h-a6", service.Revision, HaulWaitActivity.Done)), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Waiting, service.Phase);
        Assert.Equal(2, service.AcknowledgeCalls);
    }

    [Fact]
    public void ACancelWhileUnloadingIsAcceptedAndCompletesOnlyAfterDone()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);
        AssertStatus(Send(provider, Acknowledge("h-a1", service.Revision, HaulWaitActivity.Transferring)), HaulReplyStatus.Accepted);

        WireMessage cancel = Send(provider, Cancel("h-c1", HaulCancelDisposition.DetachAndPark));
        Assert.True(HaulPhaseReply.TryRead(cancel.ToWire(), out HaulPhaseReply? cancelled));
        Assert.Equal(HaulWirePhase.Unloading, cancelled!.Phase);

        AssertStatus(Send(provider, Acknowledge("h-a2", service.Revision, HaulWaitActivity.Done)), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Ready, service.Phase);
        Assert.False(service.Attached);
        Assert.Equal(string.Empty, service.HaulId);
    }

    [Fact]
    public void CancelDispositionsStopAndWaitOrDetachAndPark()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.CompleteLeg(Rendezvous);
        AssertStatus(Send(provider, RequestHaul("h-r2", service.Revision, purpose: HaulLegPurpose.ToDestination)), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Pulling, service.Phase);

        AssertStatus(Send(provider, Cancel("h-c1", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Waiting, service.Phase);
        Assert.True(service.Attached);

        AssertStatus(Send(provider, Cancel("h-c2", HaulCancelDisposition.DetachAndPark)), HaulReplyStatus.Accepted);
        Assert.Equal(HaulPhase.Ready, service.Phase);
        Assert.False(service.Attached);

        AssertRefusal(Send(provider, Cancel("h-c3", HaulCancelDisposition.StopAndWait)), HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul);
        Assert.Equal(2, service.CancelCalls);
    }

    [Fact]
    public void ACancelIsAttemptedEvenWithoutAuthorityOrAWorker()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        AssertStatus(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Accepted);
        service.Authority = WorkAuthorityVerdict.NotHost;
        service.WorkerAvailable = false;

        AssertStatus(Send(provider, Cancel("h-c1", HaulCancelDisposition.DetachAndPark)), HaulReplyStatus.Accepted);
        Assert.Equal(1, service.CancelCalls);
    }

    // --- failures -------------------------------------------------------------------------------

    [Fact]
    public void MalformedAndUnknownRequestsAreRejectedNeverThrown()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        string epoch = Epoch.ToString("N");

        AssertRefusal(Send(provider, null), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        AssertRefusal(Send(provider, Wire(("contractMajor", "1"))), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        AssertRefusal(Send(provider, Wire(("op", "teleportCart"), ("contractMajor", "1"))), HaulReplyStatus.Rejected, HaulWireReason.UnknownOp);
        AssertRefusal(Send(provider, Wire(("op", "Hello"), ("contractMajor", "1"))), HaulReplyStatus.Rejected, HaulWireReason.UnknownOp);
        AssertRefusal(Send(provider, Wire(("op", "hello"), ("contractMajor", "2"), ("consumerVersion", "1"))), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        AssertRefusal(Send(provider, Wire(("op", "hello"), ("contractMajor", "1"))), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        AssertRefusal(Send(provider, Wire(("op", "describeLease"), ("contractMajor", "1"), ("providerEpoch", "not-a-guid"))), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        AssertRefusal(Send(provider, Wire(("op", "getHaul"), ("contractMajor", "1"), ("providerEpoch", epoch), ("haulId", "Bad Id"))), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);

        var good = new Dictionary<string, string>(RequestHaul("h-r1", service.Revision));
        foreach ((string key, string bad) in new[]
        {
            ("requestId", ""), ("requestId", "UPPER"), ("expectedRevision", "-1"), ("expectedRevision", "one"),
            ("orderId", "order--1"), ("haulId", ""), ("leaseId", "lease 1"), ("purpose", "Unspecified"),
            ("purpose", "1"), ("target", "1;NaN;3"), ("target", "1;2"), ("arrivalRadius", "0"),
            ("arrivalRadius", "Infinity"), ("providerEpoch", Guid.Empty.ToString("N")),
        })
        {
            var mutated = new Dictionary<string, string>(good) { [key] = bad };
            AssertRefusal(Send(provider, mutated), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);
        }

        var missingRevision = new Dictionary<string, string>(good);
        missingRevision.Remove("expectedRevision");
        AssertRefusal(Send(provider, missingRevision), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);

        var acknowledgement = new Dictionary<string, string>(Acknowledge("h-a1", 0, HaulWaitActivity.Done));
        acknowledgement.Remove("expectedRevision");
        AssertRefusal(Send(provider, acknowledgement), HaulReplyStatus.Rejected, HaulWireReason.MalformedRequest);

        Assert.Equal(0, service.LegCalls + service.AcknowledgeCalls + service.CancelCalls);
        Assert.Equal(0, provider.FaultCount);
    }

    [Fact]
    public void AnExceptionInsideTheServiceBecomesProviderErrorAndNothingIsRemembered()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();
        IReadOnlyDictionary<string, string> request = RequestHaul("h-r1", service.Revision);
        service.ThrowOnNextCommand = new InvalidOperationException("boom");

        AssertRefusal(Send(provider, request), HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable);
        Assert.Equal(1, provider.FaultCount);
        Assert.Contains("boom", provider.LastFault);

        // The same attempt again: not "already satisfied", because nothing was.
        AssertStatus(Send(provider, request), HaulReplyStatus.Accepted);

        service.ThrowOnSnapshot = new NullReferenceException();
        AssertRefusal(Send(provider, new HelloMessage("0.2.0").ToWire()), HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable);
        Assert.Equal(2, provider.FaultCount);

        var throwingSource = new HaulProvider(() => throw new InvalidOperationException("no runtime"), "1.0.5");
        AssertRefusal(Send(throwingSource, new HelloMessage("0.2.0").ToWire()), HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable);
    }

    [Fact]
    public void ServiceAnswersAreMappedAndARefusalWithoutAReasonIsAProviderFault()
    {
        (HaulProvider provider, HaulingInteropFakeService service) = Create();

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Rejected, HaulAttentionReason.TooNarrow, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r1", service.Revision)), HaulReplyStatus.Rejected, HaulWireReason.TooNarrow);

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Rejected, HaulAttentionReason.Unspecified, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r2", service.Revision)), HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable);

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Unavailable, HaulAttentionReason.Unspecified, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r3", service.Revision)), HaulReplyStatus.Unavailable, HaulWireReason.WorkerUnavailable);

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Unavailable, HaulAttentionReason.AuthorityLost, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r4", service.Revision)), HaulReplyStatus.Unavailable, HaulWireReason.NoAuthority);

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Stale, HaulAttentionReason.Unspecified, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r5", service.Revision)), HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch);

        service.ForcedResult = new HaulCommandResult(HaulCommandOutcome.Unspecified, HaulAttentionReason.Unspecified, service.Revision);
        AssertRefusal(Send(provider, RequestHaul("h-r6", service.Revision)), HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable);
    }

    // --- mapping and publication --------------------------------------------------------------

    [Fact]
    public void EveryPhaseMapsToItsWireNameAndUnspecifiedDoesNotTravel()
    {
        foreach (HaulPhase phase in HaulPhases.All())
        {
            Assert.True(HaulWireMapping.TryToWire(phase, out HaulWirePhase wire), phase.ToString());
            Assert.Equal(phase.ToString(), wire.ToString());
        }

        Assert.False(HaulWireMapping.TryToWire(HaulPhase.Unspecified, out _));
    }

    [Fact]
    public void EveryAttentionReasonHasAWireReason()
    {
        foreach (HaulAttentionReason reason in Enum.GetValues<HaulAttentionReason>())
        {
            HaulWireReason wire = HaulWireMapping.ToWire(reason);
            if (reason == HaulAttentionReason.Unspecified)
            {
                Assert.Equal(HaulWireReason.Unspecified, wire);
                continue;
            }

            Assert.NotEqual(HaulWireReason.Unspecified, wire);
            string expected = reason == HaulAttentionReason.AuthorityLost ? "NoAuthority" : reason.ToString();
            Assert.Equal(expected, wire.ToString());
        }
    }

    [Fact]
    public void TheRequestLedgerIsBoundedOldestFirstAndForgetsOnANewEpoch()
    {
        var ledger = new HaulRequestLedger(capacity: 2);
        ledger.UseEpoch(Epoch);
        ledger.Record("a", "1");
        ledger.Record("b", "2");
        ledger.MarkAccepted("b");
        ledger.Record("b", "other");

        Assert.Equal(HaulRequestCheck.SamePayloadNotAccepted, ledger.Check("a", "1"));
        Assert.Equal(HaulRequestCheck.SamePayloadAccepted, ledger.Check("b", "2"));
        Assert.Equal(HaulRequestCheck.DifferentPayload, ledger.Check("b", "other"));

        ledger.Record("c", "3");
        Assert.Equal(2, ledger.Count);
        Assert.Equal(HaulRequestCheck.New, ledger.Check("a", "1"));

        ledger.UseEpoch(NextEpoch);
        Assert.Equal(0, ledger.Count);
        Assert.Equal(HaulRequestCheck.New, ledger.Check("b", "2"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HaulRequestLedger(0));
    }

    [Fact]
    public void TheCapabilityMapPublishesOneReadOnlyBclDelegateUnderMajorOne()
    {
        (HaulProvider provider, _) = Create();
        IReadOnlyDictionary<string, object> map = provider.CreateCapabilityMap();

        Assert.Single(map);
        Assert.True(CapabilityMap.TryGetEndpoint(map, HaulContract.Id, 1, out var endpoint));
        Assert.False(CapabilityMap.TryGetEndpoint(map, HaulContract.Id, 2, out _));
        Assert.IsNotType<Dictionary<string, object>>(map);

        IReadOnlyDictionary<string, string>? reply = CapabilityMap.TryCall(endpoint, new HelloMessage("0.2.0").ToWire());
        Assert.NotNull(reply);
        AssertStatus(WireMessage.From(reply), HaulReplyStatus.Accepted);
        Assert.Throws<ArgumentException>(() => new HaulProvider(() => null, "has space"));
    }

    // --- helpers --------------------------------------------------------------------------------

    private static (HaulProvider Provider, HaulingInteropFakeService Service) Create(bool withLease = true)
    {
        var service = new HaulingInteropFakeService(Epoch);
        if (withLease)
        {
            Assert.Equal(LeaseOutcome.Assigned, service.AssignCart("lease-1", "5:42"));
        }

        return (new HaulProvider(() => service, "1.0.5"), service);
    }

    private static IReadOnlyDictionary<string, string> RequestHaul(
        string requestId, int revision, string haulId = "h-order-1", string leaseId = "lease-1",
        HaulLegPurpose purpose = HaulLegPurpose.ToRendezvous) =>
        new RequestHaulMessage(Epoch, requestId, revision, "order-1", haulId, leaseId, purpose, Rendezvous, 3f).ToWire();

    private static IReadOnlyDictionary<string, string> Acknowledge(string requestId, int revision, HaulWaitActivity activity) =>
        new AcknowledgeWaitMessage(Epoch, requestId, revision, "h-order-1", activity).ToWire();

    private static IReadOnlyDictionary<string, string> Cancel(string requestId, HaulCancelDisposition disposition) =>
        new CancelHaulMessage(Epoch, requestId, "h-order-1", disposition).ToWire();

    private static IReadOnlyDictionary<string, string> Wire(params (string Key, string Value)[] fields)
    {
        var wire = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in fields)
        {
            wire[key] = value;
        }

        return wire;
    }

    private static WireMessage Send(HaulProvider provider, IReadOnlyDictionary<string, string>? request)
    {
        IReadOnlyDictionary<string, string> reply = provider.Handle(request);
        Assert.NotNull(reply);
        return WireMessage.From(reply);
    }

    private static void AssertStatus(WireMessage reply, HaulReplyStatus expected)
    {
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Status, out HaulReplyStatus status), "status missing");
        string reason = reply.TryGet(HaulContract.Keys.Reason, out string text) ? text : "-";
        Assert.True(expected == status, "expected " + expected + " but was " + status + " (" + reason + ")");
    }

    private static void AssertRefusal(WireMessage reply, HaulReplyStatus status, HaulWireReason reason)
    {
        AssertStatus(reply, status);
        Assert.True(reply.TryGetEnum(HaulContract.Keys.Reason, out HaulWireReason actual), "reason missing");
        Assert.Equal(reason, actual);
    }
}
