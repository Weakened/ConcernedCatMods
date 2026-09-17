using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Interop.Haul;

/// <summary>The part every reply shares (CONTRACTS.md §3.1): a status, and for
/// anything but Accepted or AlreadySatisfied a reason and an optional detail.
///
/// A reason name this contract minor does not know is kept as text and read as
/// <see cref="HaulWireReason.Unspecified"/>: shown as unknown, never guessed.
/// </summary>
internal readonly struct HaulReplyHeader
{
    private HaulReplyHeader(HaulReplyStatus status, HaulWireReason reason, string reasonName, string detail)
    {
        Status = status;
        Reason = reason;
        ReasonName = reasonName ?? string.Empty;
        Detail = detail ?? string.Empty;
    }

    public HaulReplyStatus Status { get; }

    public HaulWireReason Reason { get; }

    /// <summary>The reason exactly as it arrived; differs from
    /// <see cref="Reason"/>'s name only for a reason from a newer minor.</summary>
    public string ReasonName { get; }

    public string Detail { get; }

    public bool IsSuccess => Status == HaulReplyStatus.Accepted || Status == HaulReplyStatus.AlreadySatisfied;

    public static HaulReplyHeader Success(HaulReplyStatus status)
    {
        if (status != HaulReplyStatus.Accepted && status != HaulReplyStatus.AlreadySatisfied)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Success is Accepted or AlreadySatisfied.");
        }

        return new HaulReplyHeader(status, HaulWireReason.Unspecified, string.Empty, string.Empty);
    }

    /// <summary>Every refusal names a real reason: a reply that says "no"
    /// without saying why is a provider bug the consumer cannot act on.</summary>
    public static HaulReplyHeader Refusal(HaulReplyStatus status, HaulWireReason reason, string? detail)
    {
        if (status == HaulReplyStatus.Unspecified || status == HaulReplyStatus.Accepted ||
            status == HaulReplyStatus.AlreadySatisfied || !Enum.IsDefined(typeof(HaulReplyStatus), status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A refusal is not a success.");
        }

        if (reason == HaulWireReason.Unspecified || !Enum.IsDefined(typeof(HaulWireReason), reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "A refusal names its reason.");
        }

        return new HaulReplyHeader(status, reason, reason.ToString(), HaulFieldRules.TrimDetail(detail));
    }

    internal void WriteTo(WireMessage message)
    {
        message.SetEnum(HaulContract.Keys.Status, Status);
        if (!IsSuccess)
        {
            message.SetEnum(HaulContract.Keys.Reason, Reason);
            if (Detail.Length > 0)
            {
                message.Set(HaulContract.Keys.Detail, Detail);
            }
        }
    }

    /// <summary>False when the status is missing or unknown: such a reply is
    /// not an answer, and the consumer treats it as Unavailable.</summary>
    internal static bool TryRead(WireMessage message, out HaulReplyHeader header)
    {
        header = default;
        if (!message.TryGetEnum(HaulContract.Keys.Status, out HaulReplyStatus status) ||
            status == HaulReplyStatus.Unspecified)
        {
            return false;
        }

        string detail = message.TryGet(HaulContract.Keys.Detail, out string text)
            ? HaulFieldRules.TrimDetail(text)
            : string.Empty;

        if (status == HaulReplyStatus.Accepted || status == HaulReplyStatus.AlreadySatisfied)
        {
            header = new HaulReplyHeader(status, HaulWireReason.Unspecified, string.Empty, detail);
            return true;
        }

        message.TryGet(HaulContract.Keys.Reason, out string reasonName);
        message.TryGetEnum(HaulContract.Keys.Reason, out HaulWireReason reason);
        header = new HaulReplyHeader(status, reason, reasonName, detail);
        return true;
    }
}

/// <summary>Reply to <c>hello</c>.</summary>
internal sealed class HelloReply
{
    public HelloReply(
        HaulReplyHeader header,
        int contractMinor,
        string providerVersion,
        Guid providerEpoch,
        WorkAuthorityVerdict authority,
        bool workerAvailable,
        HaulWirePhase phase,
        string? leaseId)
    {
        Header = header;
        ContractMinor = contractMinor;
        ProviderVersion = providerVersion ?? string.Empty;
        ProviderEpoch = providerEpoch;
        Authority = authority;
        WorkerAvailable = workerAvailable;
        Phase = phase;
        LeaseId = leaseId ?? string.Empty;
    }

    public HaulReplyHeader Header { get; }

    public int ContractMinor { get; }

    public string ProviderVersion { get; }

    /// <summary>Empty while the provider has no world loaded.</summary>
    public Guid ProviderEpoch { get; }

    public WorkAuthorityVerdict Authority { get; }

    public bool WorkerAvailable { get; }

    public HaulWirePhase Phase { get; }

    /// <summary>Empty when no lease is active.</summary>
    public string LeaseId { get; }

    public bool HasLease => LeaseId.Length > 0;

    public static HelloReply Refused(HaulReplyHeader header) =>
        new HelloReply(header, 0, string.Empty, Guid.Empty, WorkAuthorityVerdict.Unspecified, false, HaulWirePhase.Unspecified, null);

    public IReadOnlyDictionary<string, string> ToWire()
    {
        WireMessage message = WireMessage.Create();
        Header.WriteTo(message);
        if (Header.IsSuccess)
        {
            message
                .SetInt(HaulContract.Keys.ContractMinor, ContractMinor)
                .Set(HaulContract.Keys.ProviderVersion, ProviderVersion)
                .Set(HaulContract.Keys.ProviderEpoch, HaulEpoch.Format(ProviderEpoch))
                .SetEnum(HaulContract.Keys.Authority, Authority)
                .SetBool(HaulContract.Keys.WorkerAvailable, WorkerAvailable)
                .SetEnum(HaulContract.Keys.Phase, Phase);
            if (HasLease)
            {
                message.Set(HaulContract.Keys.LeaseId, LeaseId);
            }
        }

        return message.ToWire();
    }

    public static bool TryRead(IReadOnlyDictionary<string, string>? wire, out HelloReply? reply)
    {
        reply = null;
        WireMessage message = WireMessage.From(wire);
        if (!HaulReplyHeader.TryRead(message, out HaulReplyHeader header))
        {
            return false;
        }

        if (!header.IsSuccess)
        {
            reply = Refused(header);
            return true;
        }

        string leaseId = string.Empty;
        if (!message.TryGetInt(HaulContract.Keys.ContractMinor, out int minor) || minor < 0 ||
            !message.TryGet(HaulContract.Keys.ProviderVersion, out string version) ||
            !message.TryGet(HaulContract.Keys.ProviderEpoch, out string epochText) ||
            !HaulEpoch.TryParse(epochText, out Guid epoch) ||
            !message.TryGetEnum(HaulContract.Keys.Authority, out WorkAuthorityVerdict authority) ||
            !message.TryGetBool(HaulContract.Keys.WorkerAvailable, out bool workerAvailable) ||
            !HaulReplyFields.TryReadPhase(message, out HaulWirePhase phase) ||
            (message.Has(HaulContract.Keys.LeaseId) &&
                (!message.TryGet(HaulContract.Keys.LeaseId, out leaseId) || !HaulFieldRules.IsToken(leaseId))))
        {
            return false;
        }

        reply = new HelloReply(header, minor, version, epoch, authority, workerAvailable, phase, leaseId);
        return true;
    }
}

/// <summary>Reply to <c>describeLease</c>.</summary>
internal sealed class DescribeLeaseReply
{
    public DescribeLeaseReply(
        HaulReplyHeader header,
        string? leaseId,
        string? cartSessionKey,
        WorkPoint? cartPosition,
        bool cartStill,
        bool cartUpright,
        bool attached,
        HaulWirePhase phase,
        int revision)
    {
        Header = header;
        LeaseId = leaseId ?? string.Empty;
        CartSessionKey = cartSessionKey ?? string.Empty;
        CartPosition = cartPosition;
        CartStill = cartStill;
        CartUpright = cartUpright;
        Attached = attached;
        Phase = phase;
        Revision = revision;
    }

    public HaulReplyHeader Header { get; }

    public string LeaseId { get; }

    /// <summary>The cart's in-session key in the provider epoch. The consumer
    /// resolves the cart's container from it in its own process, within that
    /// epoch only.</summary>
    public string CartSessionKey { get; }

    /// <summary>Null when the provider cannot see the cart right now.</summary>
    public WorkPoint? CartPosition { get; }

    public bool CartStill { get; }

    public bool CartUpright { get; }

    public bool Attached { get; }

    public HaulWirePhase Phase { get; }

    public int Revision { get; }

    public static DescribeLeaseReply Refused(HaulReplyHeader header) =>
        new DescribeLeaseReply(header, null, null, null, false, false, false, HaulWirePhase.Unspecified, 0);

    public IReadOnlyDictionary<string, string> ToWire()
    {
        WireMessage message = WireMessage.Create();
        Header.WriteTo(message);
        if (Header.IsSuccess)
        {
            message
                .Set(HaulContract.Keys.LeaseId, LeaseId)
                .Set(HaulContract.Keys.CartSessionKey, CartSessionKey)
                .SetBool(HaulContract.Keys.CartStill, CartStill)
                .SetBool(HaulContract.Keys.CartUpright, CartUpright)
                .SetBool(HaulContract.Keys.Attached, Attached)
                .SetEnum(HaulContract.Keys.Phase, Phase)
                .SetInt(HaulContract.Keys.Revision, Revision);
            HaulReplyFields.WritePoint(message, HaulContract.Keys.CartPosition, CartPosition);
        }

        return message.ToWire();
    }

    public static bool TryRead(IReadOnlyDictionary<string, string>? wire, out DescribeLeaseReply? reply)
    {
        reply = null;
        WireMessage message = WireMessage.From(wire);
        if (!HaulReplyHeader.TryRead(message, out HaulReplyHeader header))
        {
            return false;
        }

        if (!header.IsSuccess)
        {
            reply = Refused(header);
            return true;
        }

        if (!message.TryGet(HaulContract.Keys.LeaseId, out string leaseId) || !HaulFieldRules.IsToken(leaseId) ||
            !message.TryGet(HaulContract.Keys.CartSessionKey, out string cartKey) || !HaulFieldRules.IsToken(cartKey) ||
            !message.TryGetBool(HaulContract.Keys.CartStill, out bool still) ||
            !message.TryGetBool(HaulContract.Keys.CartUpright, out bool upright) ||
            !message.TryGetBool(HaulContract.Keys.Attached, out bool attached) ||
            !HaulReplyFields.TryReadPhase(message, out HaulWirePhase phase) ||
            !HaulReplyFields.TryReadRevision(message, out int revision) ||
            !HaulReplyFields.TryReadOptionalPoint(message, HaulContract.Keys.CartPosition, out WorkPoint? position))
        {
            return false;
        }

        reply = new DescribeLeaseReply(header, leaseId, cartKey, position, still, upright, attached, phase, revision);
        return true;
    }
}

/// <summary>Reply to <c>requestHaul</c>.</summary>
internal sealed class RequestHaulReply
{
    public RequestHaulReply(HaulReplyHeader header, string? haulId, HaulWirePhase phase, int revision)
    {
        Header = header;
        HaulId = haulId ?? string.Empty;
        Phase = phase;
        Revision = revision;
    }

    public HaulReplyHeader Header { get; }

    public string HaulId { get; }

    public HaulWirePhase Phase { get; }

    public int Revision { get; }

    public static RequestHaulReply Refused(HaulReplyHeader header) =>
        new RequestHaulReply(header, null, HaulWirePhase.Unspecified, 0);

    public IReadOnlyDictionary<string, string> ToWire()
    {
        WireMessage message = WireMessage.Create();
        Header.WriteTo(message);
        if (Header.IsSuccess)
        {
            message
                .Set(HaulContract.Keys.HaulId, HaulId)
                .SetEnum(HaulContract.Keys.Phase, Phase)
                .SetInt(HaulContract.Keys.Revision, Revision);
        }

        return message.ToWire();
    }

    public static bool TryRead(IReadOnlyDictionary<string, string>? wire, out RequestHaulReply? reply)
    {
        reply = null;
        WireMessage message = WireMessage.From(wire);
        if (!HaulReplyHeader.TryRead(message, out HaulReplyHeader header))
        {
            return false;
        }

        if (!header.IsSuccess)
        {
            reply = Refused(header);
            return true;
        }

        if (!message.TryGet(HaulContract.Keys.HaulId, out string haulId) || !HaulFieldRules.IsSlug(haulId) ||
            !HaulReplyFields.TryReadPhase(message, out HaulWirePhase phase) ||
            !HaulReplyFields.TryReadRevision(message, out int revision))
        {
            return false;
        }

        reply = new RequestHaulReply(header, haulId, phase, revision);
        return true;
    }
}

/// <summary>Reply to <c>getHaul</c>. On success <c>reason</c> carries the
/// haul's attention reason, present only while there is one.</summary>
internal sealed class GetHaulReply
{
    public GetHaulReply(
        HaulReplyHeader header,
        HaulWirePhase phase,
        HaulWireReason attention,
        string? attentionName,
        int revision,
        bool arrived,
        bool attached,
        WorkPoint? cartPosition,
        WorkPoint? workerPosition,
        bool cartStill)
    {
        Header = header;
        Phase = phase;
        Attention = attention;
        AttentionName = attentionName ?? (attention == HaulWireReason.Unspecified ? string.Empty : attention.ToString());
        Revision = revision;
        Arrived = arrived;
        Attached = attached;
        CartPosition = cartPosition;
        WorkerPosition = workerPosition;
        CartStill = cartStill;
    }

    public HaulReplyHeader Header { get; }

    public HaulWirePhase Phase { get; }

    /// <summary>Unspecified when there is no attention reason, or when the
    /// provider sent one this minor does not know (see
    /// <see cref="AttentionName"/>).</summary>
    public HaulWireReason Attention { get; }

    public string AttentionName { get; }

    public bool HasAttention => AttentionName.Length > 0;

    public int Revision { get; }

    /// <summary>The leg's final goal is reached, the phase is Waiting and the
    /// cart is still.</summary>
    public bool Arrived { get; }

    public bool Attached { get; }

    public WorkPoint? CartPosition { get; }

    public WorkPoint? WorkerPosition { get; }

    public bool CartStill { get; }

    public static GetHaulReply Refused(HaulReplyHeader header) =>
        new GetHaulReply(header, HaulWirePhase.Unspecified, HaulWireReason.Unspecified, null, 0, false, false, null, null, false);

    public IReadOnlyDictionary<string, string> ToWire()
    {
        WireMessage message = WireMessage.Create();
        Header.WriteTo(message);
        if (Header.IsSuccess)
        {
            message
                .SetEnum(HaulContract.Keys.Phase, Phase)
                .SetInt(HaulContract.Keys.Revision, Revision)
                .SetBool(HaulContract.Keys.Arrived, Arrived)
                .SetBool(HaulContract.Keys.Attached, Attached)
                .SetBool(HaulContract.Keys.CartStill, CartStill);
            if (Attention != HaulWireReason.Unspecified)
            {
                message.SetEnum(HaulContract.Keys.Reason, Attention);
            }

            HaulReplyFields.WritePoint(message, HaulContract.Keys.CartPosition, CartPosition);
            HaulReplyFields.WritePoint(message, HaulContract.Keys.WorkerPosition, WorkerPosition);
        }

        return message.ToWire();
    }

    public static bool TryRead(IReadOnlyDictionary<string, string>? wire, out GetHaulReply? reply)
    {
        reply = null;
        WireMessage message = WireMessage.From(wire);
        if (!HaulReplyHeader.TryRead(message, out HaulReplyHeader header))
        {
            return false;
        }

        if (!header.IsSuccess)
        {
            reply = Refused(header);
            return true;
        }

        if (!HaulReplyFields.TryReadPhase(message, out HaulWirePhase phase) ||
            !HaulReplyFields.TryReadRevision(message, out int revision) ||
            !message.TryGetBool(HaulContract.Keys.Arrived, out bool arrived) ||
            !message.TryGetBool(HaulContract.Keys.Attached, out bool attached) ||
            !message.TryGetBool(HaulContract.Keys.CartStill, out bool still) ||
            !HaulReplyFields.TryReadOptionalPoint(message, HaulContract.Keys.CartPosition, out WorkPoint? cart) ||
            !HaulReplyFields.TryReadOptionalPoint(message, HaulContract.Keys.WorkerPosition, out WorkPoint? worker))
        {
            return false;
        }

        string attentionName = string.Empty;
        HaulWireReason attention = HaulWireReason.Unspecified;
        if (message.TryGet(HaulContract.Keys.Reason, out string reasonText) && reasonText.Length > 0)
        {
            attentionName = reasonText;
            message.TryGetEnum(HaulContract.Keys.Reason, out attention);
        }

        reply = new GetHaulReply(header, phase, attention, attentionName, revision, arrived, attached, cart, worker, still);
        return true;
    }
}

/// <summary>Reply to <c>acknowledgeWait</c> and <c>cancelHaul</c>: where the
/// haul is now.</summary>
internal sealed class HaulPhaseReply
{
    public HaulPhaseReply(HaulReplyHeader header, HaulWirePhase phase, int revision)
    {
        Header = header;
        Phase = phase;
        Revision = revision;
    }

    public HaulReplyHeader Header { get; }

    public HaulWirePhase Phase { get; }

    public int Revision { get; }

    public static HaulPhaseReply Refused(HaulReplyHeader header) =>
        new HaulPhaseReply(header, HaulWirePhase.Unspecified, 0);

    public IReadOnlyDictionary<string, string> ToWire()
    {
        WireMessage message = WireMessage.Create();
        Header.WriteTo(message);
        if (Header.IsSuccess)
        {
            message
                .SetEnum(HaulContract.Keys.Phase, Phase)
                .SetInt(HaulContract.Keys.Revision, Revision);
        }

        return message.ToWire();
    }

    public static bool TryRead(IReadOnlyDictionary<string, string>? wire, out HaulPhaseReply? reply)
    {
        reply = null;
        WireMessage message = WireMessage.From(wire);
        if (!HaulReplyHeader.TryRead(message, out HaulReplyHeader header))
        {
            return false;
        }

        if (!header.IsSuccess)
        {
            reply = Refused(header);
            return true;
        }

        if (!HaulReplyFields.TryReadPhase(message, out HaulWirePhase phase) ||
            !HaulReplyFields.TryReadRevision(message, out int revision))
        {
            return false;
        }

        reply = new HaulPhaseReply(header, phase, revision);
        return true;
    }
}

/// <summary>A refusal carries only the header, whatever the op, so the
/// provider can answer a request it could not even parse.</summary>
internal static class HaulRefusal
{
    public static IReadOnlyDictionary<string, string> ToWire(HaulReplyStatus status, HaulWireReason reason, string? detail)
    {
        WireMessage message = WireMessage.Create();
        HaulReplyHeader.Refusal(status, reason, detail).WriteTo(message);
        return message.ToWire();
    }
}

internal static class HaulReplyFields
{
    public static bool TryReadPhase(WireMessage message, out HaulWirePhase phase) =>
        message.TryGetEnum(HaulContract.Keys.Phase, out phase) && phase != HaulWirePhase.Unspecified;

    public static bool TryReadRevision(WireMessage message, out int revision) =>
        message.TryGetInt(HaulContract.Keys.Revision, out revision) && revision >= 0;

    public static void WritePoint(WireMessage message, string key, WorkPoint? point)
    {
        if (point.HasValue && point.Value.IsFinite)
        {
            message.Set(key, point.Value.Format());
        }
    }

    /// <summary>Absent is fine (unknown position); present and unparseable is
    /// a malformed reply.</summary>
    public static bool TryReadOptionalPoint(WireMessage message, string key, out WorkPoint? point)
    {
        point = null;
        if (!message.Has(key))
        {
            return true;
        }

        if (!message.TryGet(key, out string text) || !WorkPoint.TryParse(text, out WorkPoint parsed))
        {
            return false;
        }

        point = parsed;
        return true;
    }
}
