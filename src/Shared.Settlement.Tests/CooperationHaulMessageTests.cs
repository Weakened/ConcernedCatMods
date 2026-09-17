using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>#317: the request and reply builders of <c>concernedcat.haul/1</c>
/// that both products compile (CONTRACTS.md §3.1-3.2). What one side writes the
/// other side reads, with the same bounds, and a malformed or future message is
/// refused or shown as unknown rather than guessed.</summary>
public sealed class CooperationHaulMessageTests
{
    private static readonly Guid Epoch = new Guid("cccccccc-0000-0000-0000-000000000001");
    private static readonly WorkPoint Target = new WorkPoint(-36.8f, 84.125f, 23.9f);

    [Fact]
    public void OpsTravelAsTheirExactContractNames()
    {
        foreach (HaulOp op in new[] { HaulOp.Hello, HaulOp.DescribeLease, HaulOp.RequestHaul, HaulOp.GetHaul, HaulOp.AcknowledgeWait, HaulOp.CancelHaul })
        {
            Assert.True(HaulOps.TryParse(HaulOps.WireName(op), out HaulOp back));
            Assert.Equal(op, back);
        }

        Assert.False(HaulOps.TryParse("RequestHaul", out _));
        Assert.False(HaulOps.TryParse("requesthaul", out _));
        Assert.False(HaulOps.TryParse(null, out _));
        Assert.True(HaulOps.IsMutating(HaulOp.CancelHaul));
        Assert.False(HaulOps.IsMutating(HaulOp.GetHaul));
        Assert.Throws<ArgumentOutOfRangeException>(() => HaulOps.WireName(HaulOp.Unspecified));
    }

    [Fact]
    public void EpochsTravelAsThirtyTwoHexDigits()
    {
        string text = HaulEpoch.Format(Epoch);
        Assert.Equal(32, text.Length);
        Assert.True(HaulEpoch.TryParse(text, out Guid back));
        Assert.Equal(Epoch, back);
        Assert.False(HaulEpoch.TryParse(Epoch.ToString("D"), out _));
        Assert.False(HaulEpoch.TryParse("", out _));
        Assert.False(HaulEpoch.TryParse(null, out _));
    }

    [Fact]
    public void EveryRequestRoundTripsThroughTheWireUnchanged()
    {
        HaulRequestMessage[] requests =
        {
            new HelloMessage("0.2.0"),
            new DescribeLeaseMessage(Epoch),
            new GetHaulMessage(Epoch, "h-order-1-abcd1234"),
            new RequestHaulMessage(Epoch, "h-order-1-abcd1234-q1", 7, "order-1", "h-order-1-abcd1234", "lease:5", HaulLegPurpose.ToDestination, Target, 2.75f),
            new AcknowledgeWaitMessage(Epoch, "h-order-1-abcd1234-t2", 8, "h-order-1-abcd1234", HaulWaitActivity.Transferring),
            new CancelHaulMessage(Epoch, "h-order-1-abcd1234-c3", "h-order-1-abcd1234", HaulCancelDisposition.DetachAndPark, 9),
            new CancelHaulMessage(Epoch, "h-order-1-abcd1234-c4", "h-order-1-abcd1234", HaulCancelDisposition.StopAndWait),
        };

        foreach (HaulRequestMessage request in requests)
        {
            IReadOnlyDictionary<string, string> wire = request.ToWire();
            Assert.Equal(HaulOps.WireName(request.Op), wire[HaulContract.Keys.Op]);
            Assert.Equal("1", wire[HaulContract.Keys.ContractMajor]);

            HaulRequestReading reading = HaulRequestReader.Read(wire);
            Assert.True(reading.IsValid, request.Op + ": " + reading.Detail);
            Assert.Equal(request.Op, reading.Op);
            Assert.Equal(request.GetType(), reading.Message!.GetType());
            Assert.Equal(request.Fingerprint(), reading.Message.Fingerprint());
        }

        var leg = (RequestHaulMessage)HaulRequestReader.Read(requests[3].ToWire()).Message!;
        Assert.Equal(Target, leg.Target);
        Assert.Equal(2.75f, leg.ArrivalRadius);
        Assert.Equal(HaulLegPurpose.ToDestination, leg.Purpose);
        Assert.Equal(7, leg.ExpectedRevision);

        var cancel = (CancelHaulMessage)HaulRequestReader.Read(requests[6].ToWire()).Message!;
        Assert.Null(cancel.ExpectedRevision);
        Assert.False(requests[6].ToWire().ContainsKey(HaulContract.Keys.ExpectedRevision));
    }

    [Fact]
    public void FingerprintsIgnoreFormattingAndUnknownFieldsButNotContent()
    {
        var request = new RequestHaulMessage(Epoch, "h-1-q1", 3, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, 1.5f);
        var wire = new Dictionary<string, string>(request.ToWire())
        {
            [HaulContract.Keys.ArrivalRadius] = "1.50",
            [HaulContract.Keys.ExpectedRevision] = "03",
            ["futureField"] = "whatever",
        };

        HaulRequestReading reading = HaulRequestReader.Read(wire);
        Assert.True(reading.IsValid, reading.Detail);
        Assert.Equal(request.Fingerprint(), reading.Message!.Fingerprint());

        var elsewhere = new RequestHaulMessage(Epoch, "h-1-q1", 3, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, new WorkPoint(-36.8f, 84.125f, 24f), 1.5f);
        var laterRevision = new RequestHaulMessage(Epoch, "h-1-q1", 4, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, 1.5f);
        Assert.NotEqual(request.Fingerprint(), elsewhere.Fingerprint());
        Assert.NotEqual(request.Fingerprint(), laterRevision.Fingerprint());

        // Length prefixes keep "ab"+"c" and "a"+"bc" apart.
        Assert.NotEqual(
            new GetHaulMessage(Epoch, "ab-c").Fingerprint(),
            new GetHaulMessage(Epoch, "a-bc").Fingerprint());
    }

    [Fact]
    public void RequestConstructorsRefuseWhatTheProviderWouldRefuse()
    {
        Assert.Throws<ArgumentException>(() => new HelloMessage(""));
        Assert.Throws<ArgumentException>(() => new HelloMessage("a version with spaces"));
        Assert.Throws<ArgumentException>(() => new DescribeLeaseMessage(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new GetHaulMessage(Epoch, "Haul_1"));
        Assert.Throws<ArgumentException>(() => new RequestHaulMessage(Epoch, "", 0, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestHaulMessage(Epoch, "h-1-q1", -1, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, 1f));
        Assert.Throws<ArgumentException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", "lease 1", HaulLegPurpose.ToRendezvous, Target, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", "lease-1", HaulLegPurpose.Unspecified, Target, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, new WorkPoint(float.NaN, 0f, 0f), 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", "lease-1", HaulLegPurpose.ToRendezvous, Target, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AcknowledgeWaitMessage(Epoch, "h-1-t1", 0, "h-1", HaulWaitActivity.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CancelHaulMessage(Epoch, "h-1-c1", "h-1", (HaulCancelDisposition)9));
        Assert.Throws<ArgumentException>(() => new RequestHaulMessage(Epoch, "h-1-q1", 0, "order-1", "h-1", new string('x', 65), HaulLegPurpose.ToRendezvous, Target, 1f));
        Assert.Throws<ArgumentException>(() => new GetHaulMessage(Epoch, new string('a', 49)));
    }

    [Fact]
    public void RepliesRoundTripAndEveryRefusalNamesItsReason()
    {
        HaulReplyHeader accepted = HaulReplyHeader.Success(HaulReplyStatus.Accepted);

        var hello = new HelloReply(accepted, 0, "1.0.5", Epoch, WorkAuthorityVerdict.Granted, true, HaulWirePhase.Ready, "lease-1");
        Assert.True(HelloReply.TryRead(hello.ToWire(), out HelloReply? helloBack));
        Assert.Equal("1.0.5", helloBack!.ProviderVersion);
        Assert.Equal(Epoch, helloBack.ProviderEpoch);
        Assert.Equal(WorkAuthorityVerdict.Granted, helloBack.Authority);
        Assert.True(helloBack.WorkerAvailable);
        Assert.Equal(HaulWirePhase.Ready, helloBack.Phase);
        Assert.Equal("lease-1", helloBack.LeaseId);

        var lease = new DescribeLeaseReply(accepted, "lease-1", "5:42", Target, true, true, false, HaulWirePhase.Ready, 4);
        Assert.True(DescribeLeaseReply.TryRead(lease.ToWire(), out DescribeLeaseReply? leaseBack));
        Assert.Equal("5:42", leaseBack!.CartSessionKey);
        Assert.Equal(Target, leaseBack.CartPosition);
        Assert.Equal(4, leaseBack.Revision);

        var leg = new RequestHaulReply(HaulReplyHeader.Success(HaulReplyStatus.AlreadySatisfied), "h-1", HaulWirePhase.Pulling, 9);
        Assert.True(RequestHaulReply.TryRead(leg.ToWire(), out RequestHaulReply? legBack));
        Assert.Equal(HaulReplyStatus.AlreadySatisfied, legBack!.Header.Status);
        Assert.Equal("h-1", legBack.HaulId);

        var haul = new GetHaulReply(accepted, HaulWirePhase.NeedsAttention, HaulWireReason.Wedged, null, 11, false, true, Target, null, false);
        Assert.True(GetHaulReply.TryRead(haul.ToWire(), out GetHaulReply? haulBack));
        Assert.Equal(HaulWireReason.Wedged, haulBack!.Attention);
        Assert.Equal("Wedged", haulBack.AttentionName);
        Assert.Null(haulBack.WorkerPosition);

        var quiet = new GetHaulReply(accepted, HaulWirePhase.Waiting, HaulWireReason.Unspecified, null, 12, true, true, Target, Target, true);
        Assert.False(quiet.ToWire().ContainsKey(HaulContract.Keys.Reason));

        var phase = new HaulPhaseReply(accepted, HaulWirePhase.Unloading, 13);
        Assert.True(HaulPhaseReply.TryRead(phase.ToWire(), out HaulPhaseReply? phaseBack));
        Assert.Equal(HaulWirePhase.Unloading, phaseBack!.Phase);

        IReadOnlyDictionary<string, string> refusal = HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.RevisionMismatch, "moved on");
        Assert.True(HaulPhaseReply.TryRead(refusal, out HaulPhaseReply? refused));
        Assert.False(refused!.Header.IsSuccess);
        Assert.Equal(HaulWireReason.RevisionMismatch, refused.Header.Reason);
        Assert.Equal("moved on", refused.Header.Detail);

        Assert.Throws<ArgumentOutOfRangeException>(() => HaulReplyHeader.Refusal(HaulReplyStatus.Rejected, HaulWireReason.Unspecified, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => HaulReplyHeader.Refusal(HaulReplyStatus.Accepted, HaulWireReason.NoLease, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => HaulReplyHeader.Success(HaulReplyStatus.Stale));
    }

    [Fact]
    public void AReasonFromANewerMinorIsShownAsUnknownNeverGuessed()
    {
        var wire = new Dictionary<string, string>
        {
            [HaulContract.Keys.Status] = "Rejected",
            [HaulContract.Keys.Reason] = "CartOnFire",
        };

        Assert.True(GetHaulReply.TryRead(wire, out GetHaulReply? reply));
        Assert.Equal(HaulWireReason.Unspecified, reply!.Header.Reason);
        Assert.Equal("CartOnFire", reply.Header.ReasonName);

        var attention = new Dictionary<string, string>(
            new GetHaulReply(HaulReplyHeader.Success(HaulReplyStatus.Accepted), HaulWirePhase.NeedsAttention, HaulWireReason.Unspecified, null, 3, false, false, null, null, false).ToWire())
        {
            [HaulContract.Keys.Reason] = "CartOnFire",
        };
        Assert.True(GetHaulReply.TryRead(attention, out reply));
        Assert.Equal(HaulWireReason.Unspecified, reply!.Attention);
        Assert.True(reply.HasAttention);
        Assert.Equal("CartOnFire", reply.AttentionName);
    }

    [Fact]
    public void AReplyWithoutAKnownStatusOrMissingFieldsIsNotAnAnswer()
    {
        Assert.False(HelloReply.TryRead(null, out _));
        Assert.False(HelloReply.TryRead(new Dictionary<string, string>(), out _));
        Assert.False(HelloReply.TryRead(new Dictionary<string, string> { ["status"] = "Maybe" }, out _));
        Assert.False(HelloReply.TryRead(new Dictionary<string, string> { ["status"] = "Unspecified" }, out _));
        Assert.False(HelloReply.TryRead(new Dictionary<string, string> { ["status"] = "Accepted" }, out _));

        var phase = new Dictionary<string, string>(new HaulPhaseReply(HaulReplyHeader.Success(HaulReplyStatus.Accepted), HaulWirePhase.Waiting, 2).ToWire());
        phase[HaulContract.Keys.Phase] = "Teleporting";
        Assert.False(HaulPhaseReply.TryRead(phase, out _));
        phase[HaulContract.Keys.Phase] = "Unspecified";
        Assert.False(HaulPhaseReply.TryRead(phase, out _));

        var lease = new Dictionary<string, string>(new DescribeLeaseReply(HaulReplyHeader.Success(HaulReplyStatus.Accepted), "lease-1", "5:42", Target, true, true, true, HaulWirePhase.Waiting, 2).ToWire());
        lease[HaulContract.Keys.CartPosition] = "1;2";
        Assert.False(DescribeLeaseReply.TryRead(lease, out _));
        lease.Remove(HaulContract.Keys.CartPosition);
        Assert.True(DescribeLeaseReply.TryRead(lease, out _));
        lease[HaulContract.Keys.Revision] = "-4";
        Assert.False(DescribeLeaseReply.TryRead(lease, out _));
    }

    [Fact]
    public void DetailsAreSingleLineAndBounded()
    {
        string detail = HaulFieldRules.TrimDetail("line one\nline two\r" + new string('x', 400));
        Assert.DoesNotContain("\n", detail);
        Assert.DoesNotContain("\r", detail);
        Assert.Equal(HaulFieldRules.MaxDetailLength, detail.Length);
        Assert.Equal(string.Empty, HaulFieldRules.TrimDetail(null));
        Assert.True(HaulFieldRules.IsToken("5:42"));
        Assert.False(HaulFieldRules.IsToken("5 42"));
        Assert.False(HaulFieldRules.IsToken("tab\there"));
    }
}
