using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Interop.Haul;

/// <summary>One request of <c>concernedcat.haul/1</c> (CONTRACTS.md §3).
///
/// Built by the consumer and parsed by the provider from the same source, so
/// the two halves cannot disagree about a key, a format or a bound. A request
/// object is valid by construction: its constructor refuses what the provider
/// would refuse, and <see cref="HaulRequestReader"/> produces only objects its
/// constructors accept.</summary>
internal abstract class HaulRequestMessage
{
    protected HaulRequestMessage(HaulOp op)
    {
        if (op == HaulOp.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(op), "A request needs an op.");
        }

        Op = op;
    }

    public HaulOp Op { get; }

    public IReadOnlyDictionary<string, string> ToWire() => Write().ToWire();

    /// <summary>The canonical text of every field this request is made of, for
    /// request-id idempotence. Built from the parsed values and not from the
    /// arriving strings, so <c>1.50</c> and <c>1.5</c> are the same payload,
    /// and a field this contract minor does not know never makes a retry look
    /// like a different request.</summary>
    public string Fingerprint()
    {
        IReadOnlyDictionary<string, string> fields = Write().ToWire();
        var keys = new List<string>(fields.Keys);
        keys.Sort(StringComparer.Ordinal);

        var text = new StringBuilder();
        foreach (string key in keys)
        {
            string value = fields[key];
            text.Append(key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(key)
                .Append('=')
                .Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value)
                .Append(';');
        }

        return text.ToString();
    }

    protected abstract void WriteFields(WireMessage message);

    private WireMessage Write()
    {
        WireMessage message = WireMessage.Create()
            .Set(HaulContract.Keys.Op, HaulOps.WireName(Op))
            .SetInt(HaulContract.Keys.ContractMajor, HaulContract.Major);
        WriteFields(message);
        return message;
    }

    protected static string RequireSlug(string? value, string name)
    {
        if (!HaulFieldRules.IsSlug(value))
        {
            throw new ArgumentException(name + " must be a slug (a-z, 0-9, single dashes, at most 48).", name);
        }

        return value!;
    }

    protected static string RequireToken(string? value, string name, int maxLength)
    {
        if (!HaulFieldRules.IsToken(value, maxLength))
        {
            throw new ArgumentException(name + " must be 1-" + maxLength + " characters without spaces.", name);
        }

        return value!;
    }
}

/// <summary><c>hello</c>: no side effects. The consumer learns the provider's
/// version, epoch, authority and whether Gunnar and a lease exist.</summary>
internal sealed class HelloMessage : HaulRequestMessage
{
    public HelloMessage(string consumerVersion)
        : base(HaulOp.Hello)
    {
        ConsumerVersion = RequireToken(consumerVersion, nameof(consumerVersion), HaulFieldRules.MaxVersionLength);
    }

    public string ConsumerVersion { get; }

    protected override void WriteFields(WireMessage message) =>
        message.Set(HaulContract.Keys.ConsumerVersion, ConsumerVersion);
}

/// <summary>Every request after <c>hello</c> names the provider epoch it is
/// based on.</summary>
internal abstract class HaulEpochMessage : HaulRequestMessage
{
    protected HaulEpochMessage(HaulOp op, Guid providerEpoch)
        : base(op)
    {
        if (providerEpoch == Guid.Empty)
        {
            throw new ArgumentException("A request is based on a real provider epoch.", nameof(providerEpoch));
        }

        ProviderEpoch = providerEpoch;
    }

    public Guid ProviderEpoch { get; }

    protected override void WriteFields(WireMessage message)
    {
        message.Set(HaulContract.Keys.ProviderEpoch, HaulEpoch.Format(ProviderEpoch));
        WriteOpFields(message);
    }

    protected abstract void WriteOpFields(WireMessage message);
}

/// <summary><c>describeLease</c>: the active lease, its cart and the phase.
/// </summary>
internal sealed class DescribeLeaseMessage : HaulEpochMessage
{
    public DescribeLeaseMessage(Guid providerEpoch)
        : base(HaulOp.DescribeLease, providerEpoch)
    {
    }

    protected override void WriteOpFields(WireMessage message)
    {
    }
}

/// <summary><c>getHaul</c>: poll one haul.</summary>
internal sealed class GetHaulMessage : HaulEpochMessage
{
    public GetHaulMessage(Guid providerEpoch, string haulId)
        : base(HaulOp.GetHaul, providerEpoch)
    {
        HaulId = RequireSlug(haulId, nameof(haulId));
    }

    public string HaulId { get; }

    protected override void WriteOpFields(WireMessage message) => message.Set(HaulContract.Keys.HaulId, HaulId);
}

/// <summary>A request that changes something on the provider: it carries the
/// idempotence key and the revision it was decided against.</summary>
internal abstract class HaulMutatingMessage : HaulEpochMessage
{
    protected HaulMutatingMessage(HaulOp op, Guid providerEpoch, string requestId, int? expectedRevision)
        : base(op, providerEpoch)
    {
        if (!HaulOps.IsMutating(op))
        {
            throw new ArgumentOutOfRangeException(nameof(op), "Only mutating ops carry a request id.");
        }

        RequestId = RequireSlug(requestId, nameof(requestId));
        if (expectedRevision.HasValue && expectedRevision.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision), "Revisions start at zero.");
        }

        ExpectedRevision = expectedRevision;
    }

    /// <summary>A settlement slug, unique per attempt. A retry of the same
    /// attempt repeats it with the same payload.</summary>
    public string RequestId { get; }

    /// <summary>The haul revision the consumer last read (0 before any haul).
    /// Always present on <c>requestHaul</c> and <c>acknowledgeWait</c>.</summary>
    public int? ExpectedRevision { get; }

    protected override void WriteOpFields(WireMessage message)
    {
        message.Set(HaulContract.Keys.RequestId, RequestId);
        if (ExpectedRevision.HasValue)
        {
            message.SetInt(HaulContract.Keys.ExpectedRevision, ExpectedRevision.Value);
        }

        WriteMutationFields(message);
    }

    protected abstract void WriteMutationFields(WireMessage message);
}

/// <summary><c>requestHaul</c>: start a leg that brings the leased cart to a
/// point, either a rendezvous for loading or the delivery container.</summary>
internal sealed class RequestHaulMessage : HaulMutatingMessage
{
    public RequestHaulMessage(
        Guid providerEpoch,
        string requestId,
        int expectedRevision,
        string orderId,
        string haulId,
        string leaseId,
        HaulLegPurpose purpose,
        WorkPoint target,
        float arrivalRadius)
        : base(HaulOp.RequestHaul, providerEpoch, requestId, expectedRevision)
    {
        OrderId = RequireSlug(orderId, nameof(orderId));
        HaulId = RequireSlug(haulId, nameof(haulId));
        LeaseId = RequireToken(leaseId, nameof(leaseId), HaulFieldRules.MaxTokenLength);
        if (purpose == HaulLegPurpose.Unspecified || !Enum.IsDefined(typeof(HaulLegPurpose), purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), "A leg needs a purpose.");
        }

        if (!target.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(target), "A leg needs a finite target.");
        }

        if (!HaulFieldRules.IsFinite(arrivalRadius) || !(arrivalRadius > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(arrivalRadius), "A leg needs a positive arrival radius.");
        }

        Purpose = purpose;
        Target = target;
        ArrivalRadius = arrivalRadius;
    }

    public string OrderId { get; }

    public string HaulId { get; }

    public string LeaseId { get; }

    public HaulLegPurpose Purpose { get; }

    public WorkPoint Target { get; }

    public float ArrivalRadius { get; }

    public int Revision => ExpectedRevision ?? 0;

    protected override void WriteMutationFields(WireMessage message)
    {
        message
            .Set(HaulContract.Keys.OrderId, OrderId)
            .Set(HaulContract.Keys.HaulId, HaulId)
            .Set(HaulContract.Keys.LeaseId, LeaseId)
            .SetEnum(HaulContract.Keys.Purpose, Purpose)
            .Set(HaulContract.Keys.Target, Target.Format())
            .Set(HaulContract.Keys.ArrivalRadius, ArrivalRadius.ToString("R", CultureInfo.InvariantCulture));
    }
}

/// <summary><c>acknowledgeWait</c>: the consumer is about to transfer at the
/// waiting cart (hold it still), or has finished (it may move again).</summary>
internal sealed class AcknowledgeWaitMessage : HaulMutatingMessage
{
    public AcknowledgeWaitMessage(
        Guid providerEpoch, string requestId, int expectedRevision, string haulId, HaulWaitActivity activity)
        : base(HaulOp.AcknowledgeWait, providerEpoch, requestId, expectedRevision)
    {
        HaulId = RequireSlug(haulId, nameof(haulId));
        if (activity == HaulWaitActivity.Unspecified || !Enum.IsDefined(typeof(HaulWaitActivity), activity))
        {
            throw new ArgumentOutOfRangeException(nameof(activity), "An acknowledgement needs an activity.");
        }

        Activity = activity;
    }

    public string HaulId { get; }

    public HaulWaitActivity Activity { get; }

    protected override void WriteMutationFields(WireMessage message)
    {
        message
            .Set(HaulContract.Keys.HaulId, HaulId)
            .SetEnum(HaulContract.Keys.Activity, Activity);
    }
}

/// <summary><c>cancelHaul</c>: stop safely, and optionally detach and park.
///
/// The expected revision is optional here and never makes a cancel stale:
/// CONTRACTS.md §3.2 lists no revision for this op and the haul service's
/// cancel takes none, because a stop must not be refused for being based on a
/// view one revision old. A consumer may still send it for its own records.
/// </summary>
internal sealed class CancelHaulMessage : HaulMutatingMessage
{
    public CancelHaulMessage(
        Guid providerEpoch, string requestId, string haulId, HaulCancelDisposition disposition, int? expectedRevision = null)
        : base(HaulOp.CancelHaul, providerEpoch, requestId, expectedRevision)
    {
        HaulId = RequireSlug(haulId, nameof(haulId));
        if (disposition == HaulCancelDisposition.Unspecified ||
            !Enum.IsDefined(typeof(HaulCancelDisposition), disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition), "A cancel needs a disposition.");
        }

        Disposition = disposition;
    }

    public string HaulId { get; }

    public HaulCancelDisposition Disposition { get; }

    protected override void WriteMutationFields(WireMessage message)
    {
        message
            .Set(HaulContract.Keys.HaulId, HaulId)
            .SetEnum(HaulContract.Keys.Disposition, Disposition);
    }
}

/// <summary>What the provider made of an arriving request: a valid message,
/// or the protocol refusal to answer with. Never throws.</summary>
internal sealed class HaulRequestReading
{
    private HaulRequestReading(HaulOp op, HaulRequestMessage? message, HaulWireReason refusal, string detail)
    {
        Op = op;
        Message = message;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>The op, when it could be read; Unspecified otherwise.</summary>
    public HaulOp Op { get; }

    public HaulRequestMessage? Message { get; }

    public bool IsValid => Message != null;

    /// <summary><c>UnknownOp</c> or <c>MalformedRequest</c> when invalid.</summary>
    public HaulWireReason Refusal { get; }

    public string Detail { get; }

    public static HaulRequestReading Valid(HaulRequestMessage message) =>
        new HaulRequestReading(message.Op, message, HaulWireReason.Unspecified, string.Empty);

    public static HaulRequestReading Invalid(HaulOp op, HaulWireReason refusal, string detail) =>
        new HaulRequestReading(op, null, refusal, HaulFieldRules.TrimDetail(detail));
}

/// <summary>Parses and validates an arriving request (provider side). Every
/// field is checked against the same rules the request constructors enforce,
/// so a request that parses is one the consumer could have built.</summary>
internal static class HaulRequestReader
{
    public static HaulRequestReading Read(IReadOnlyDictionary<string, string>? wire)
    {
        WireMessage message = WireMessage.From(wire);
        if (!message.TryGet(HaulContract.Keys.Op, out string opName) || opName.Length == 0)
        {
            return HaulRequestReading.Invalid(HaulOp.Unspecified, HaulWireReason.MalformedRequest, "missing op");
        }

        if (!HaulOps.TryParse(opName, out HaulOp op))
        {
            return HaulRequestReading.Invalid(HaulOp.Unspecified, HaulWireReason.UnknownOp, "unknown op '" + opName + "'");
        }

        if (!message.TryGetInt(HaulContract.Keys.ContractMajor, out int major) || major != HaulContract.Major)
        {
            return Malformed(op, HaulContract.Keys.ContractMajor + " must be " + HaulContract.Major);
        }

        if (op == HaulOp.Hello)
        {
            return message.TryGet(HaulContract.Keys.ConsumerVersion, out string version) &&
                HaulFieldRules.IsToken(version, HaulFieldRules.MaxVersionLength)
                ? HaulRequestReading.Valid(new HelloMessage(version))
                : Malformed(op, HaulContract.Keys.ConsumerVersion);
        }

        if (!message.TryGet(HaulContract.Keys.ProviderEpoch, out string epochText) ||
            !HaulEpoch.TryParse(epochText, out Guid epoch) || epoch == Guid.Empty)
        {
            return Malformed(op, HaulContract.Keys.ProviderEpoch);
        }

        switch (op)
        {
            case HaulOp.DescribeLease:
                return HaulRequestReading.Valid(new DescribeLeaseMessage(epoch));
            case HaulOp.GetHaul:
                return TryReadSlug(message, HaulContract.Keys.HaulId, out string getHaulId)
                    ? HaulRequestReading.Valid(new GetHaulMessage(epoch, getHaulId))
                    : Malformed(op, HaulContract.Keys.HaulId);
        }

        if (!TryReadSlug(message, HaulContract.Keys.RequestId, out string requestId))
        {
            return Malformed(op, HaulContract.Keys.RequestId);
        }

        int? expectedRevision = null;
        if (message.Has(HaulContract.Keys.ExpectedRevision))
        {
            if (!message.TryGetInt(HaulContract.Keys.ExpectedRevision, out int revision) || revision < 0)
            {
                return Malformed(op, HaulContract.Keys.ExpectedRevision);
            }

            expectedRevision = revision;
        }

        if (!TryReadSlug(message, HaulContract.Keys.HaulId, out string haulId))
        {
            return Malformed(op, HaulContract.Keys.HaulId);
        }

        switch (op)
        {
            case HaulOp.RequestHaul:
                return ReadRequestHaul(message, epoch, requestId, expectedRevision, haulId);

            case HaulOp.AcknowledgeWait:
                if (!expectedRevision.HasValue)
                {
                    return Malformed(op, HaulContract.Keys.ExpectedRevision);
                }

                return message.TryGetEnum(HaulContract.Keys.Activity, out HaulWaitActivity activity) &&
                    activity != HaulWaitActivity.Unspecified
                    ? HaulRequestReading.Valid(
                        new AcknowledgeWaitMessage(epoch, requestId, expectedRevision.Value, haulId, activity))
                    : Malformed(op, HaulContract.Keys.Activity);

            case HaulOp.CancelHaul:
                return message.TryGetEnum(HaulContract.Keys.Disposition, out HaulCancelDisposition disposition) &&
                    disposition != HaulCancelDisposition.Unspecified
                    ? HaulRequestReading.Valid(
                        new CancelHaulMessage(epoch, requestId, haulId, disposition, expectedRevision))
                    : Malformed(op, HaulContract.Keys.Disposition);

            default:
                return HaulRequestReading.Invalid(op, HaulWireReason.UnknownOp, "unhandled op");
        }
    }

    private static HaulRequestReading ReadRequestHaul(
        WireMessage message, Guid epoch, string requestId, int? expectedRevision, string haulId)
    {
        const HaulOp op = HaulOp.RequestHaul;
        if (!expectedRevision.HasValue)
        {
            return Malformed(op, HaulContract.Keys.ExpectedRevision);
        }

        if (!TryReadSlug(message, HaulContract.Keys.OrderId, out string orderId))
        {
            return Malformed(op, HaulContract.Keys.OrderId);
        }

        if (!message.TryGet(HaulContract.Keys.LeaseId, out string leaseId) || !HaulFieldRules.IsToken(leaseId))
        {
            return Malformed(op, HaulContract.Keys.LeaseId);
        }

        if (!message.TryGetEnum(HaulContract.Keys.Purpose, out HaulLegPurpose purpose) ||
            purpose == HaulLegPurpose.Unspecified)
        {
            return Malformed(op, HaulContract.Keys.Purpose);
        }

        if (!message.TryGet(HaulContract.Keys.Target, out string targetText) ||
            !WorkPoint.TryParse(targetText, out WorkPoint target))
        {
            return Malformed(op, HaulContract.Keys.Target);
        }

        if (!message.TryGet(HaulContract.Keys.ArrivalRadius, out string radiusText) ||
            !float.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
            !HaulFieldRules.IsFinite(radius) || !(radius > 0f))
        {
            return Malformed(op, HaulContract.Keys.ArrivalRadius);
        }

        return HaulRequestReading.Valid(new RequestHaulMessage(
            epoch, requestId, expectedRevision.Value, orderId, haulId, leaseId, purpose, target, radius));
    }

    private static bool TryReadSlug(WireMessage message, string key, out string value) =>
        message.TryGet(key, out value) && HaulFieldRules.IsSlug(value);

    private static HaulRequestReading Malformed(HaulOp op, string field) =>
        HaulRequestReading.Invalid(op, HaulWireReason.MalformedRequest, "missing or invalid " + field);
}
