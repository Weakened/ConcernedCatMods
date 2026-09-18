using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Workers;

namespace Interop.Tests.Consumer;

/// <summary>The consumer product as the test sees it: Foreman's discovery gate
/// and haul client, fed only what the plugin registry would give the adapter
/// (installed or not, a version, the value of the provider's capability
/// property). Every answer is summarised into strings by the consumer's own
/// reply types, so what the test asserts is what Foreman would read.</summary>
public sealed class ConsumerHarness
{
    private readonly FixedSource _source;
    private readonly HaulClient _client;

    private ConsumerHarness(HaulDiscovery discovery)
    {
        _source = new FixedSource(discovery);
        _client = new HaulClient(_source, CooperationLimits.Default, "0.2.0");
    }

    public static ConsumerHarness Discover(bool installed, Version? version, object? capabilityProperty, string? readFailure = null)
    {
        HaulDiscovery discovery = HaulProviderGate.Evaluate(() => installed
            ? HaulProviderLookup.Detected(version, capabilityProperty, readFailure)
            : HaulProviderLookup.NotFound());
        return new ConsumerHarness(discovery);
    }

    public string DiscoveryStatus => _source.Discovery.Status.ToString();

    public string DiscoveryDetail => _source.Discovery.Detail;

    public string DiscoveryLogLine => _source.Discovery.LogLine;

    public string ProviderEpoch => HaulEpoch.Format(_client.ProviderEpoch);

    public int EpochGeneration => _client.EpochGeneration;

    public bool IsProviderLost => _client.IsProviderLost;

    public int ConsecutiveNoAnswers => _client.ConsecutiveNoAnswers;

    public int EndpointCalls => _client.EndpointCalls;

    public float PollIntervalSeconds => CooperationLimits.Default.PollIntervalSeconds;

    public float RendezvousTimeoutSeconds => CooperationLimits.Default.RendezvousTimeoutSeconds;

    /// <summary>Proof that this assembly compiled its own copy of the contract.
    /// </summary>
    public string ContractTypeIdentity => typeof(HaulReplyStatus).AssemblyQualifiedName ?? string.Empty;

    public IReadOnlyDictionary<string, string> Hello(float now)
    {
        HaulCall<HelloReply> call = _client.Hello(now);
        Dictionary<string, string> summary = Summarise(call);
        if (call.Succeeded)
        {
            HelloReply reply = call.Reply!;
            summary["contractMinor"] = reply.ContractMinor.ToString(CultureInfo.InvariantCulture);
            summary["providerVersion"] = reply.ProviderVersion;
            summary["providerEpoch"] = HaulEpoch.Format(reply.ProviderEpoch);
            summary["authority"] = reply.Authority.ToString();
            summary["workerAvailable"] = Bool(reply.WorkerAvailable);
            summary["phase"] = reply.Phase.ToString();
            summary["leaseId"] = reply.LeaseId;
        }

        return summary;
    }

    public IReadOnlyDictionary<string, string> DescribeLease(float now)
    {
        HaulCall<DescribeLeaseReply> call = _client.DescribeLease(now);
        Dictionary<string, string> summary = Summarise(call);
        if (call.Succeeded)
        {
            DescribeLeaseReply reply = call.Reply!;
            summary["leaseId"] = reply.LeaseId;
            summary["cartSessionKey"] = reply.CartSessionKey;
            summary["cartPosition"] = reply.CartPosition?.Format() ?? string.Empty;
            summary["cartStill"] = Bool(reply.CartStill);
            summary["cartUpright"] = Bool(reply.CartUpright);
            summary["attached"] = Bool(reply.Attached);
            summary["phase"] = reply.Phase.ToString();
            summary["revision"] = reply.Revision.ToString(CultureInfo.InvariantCulture);
        }

        return summary;
    }

    public IReadOnlyDictionary<string, string> RequestHaul(
        string requestId, int expectedRevision, string orderId, string haulId, string leaseId, bool toDestination,
        float x, float y, float z, float arrivalRadius, float now)
    {
        var message = new RequestHaulMessage(
            _client.ProviderEpoch == Guid.Empty ? new Guid(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) : _client.ProviderEpoch,
            requestId, expectedRevision, orderId, haulId, leaseId,
            toDestination ? HaulLegPurpose.ToDestination : HaulLegPurpose.ToRendezvous, new WorkPoint(x, y, z), arrivalRadius);
        HaulCall<RequestHaulReply> call = _client.RequestHaul(message, now);
        Dictionary<string, string> summary = Summarise(call);
        if (call.Succeeded)
        {
            RequestHaulReply reply = call.Reply!;
            summary["haulId"] = reply.HaulId;
            summary["phase"] = reply.Phase.ToString();
            summary["revision"] = reply.Revision.ToString(CultureInfo.InvariantCulture);
        }

        return summary;
    }

    public IReadOnlyDictionary<string, string> GetHaul(string haulId, float now)
    {
        HaulCall<GetHaulReply> call = _client.GetHaul(haulId, now);
        Dictionary<string, string> summary = Summarise(call);
        if (call.Succeeded)
        {
            GetHaulReply reply = call.Reply!;
            summary["phase"] = reply.Phase.ToString();
            summary["attention"] = reply.AttentionName;
            summary["revision"] = reply.Revision.ToString(CultureInfo.InvariantCulture);
            summary["arrived"] = Bool(reply.Arrived);
            summary["attached"] = Bool(reply.Attached);
            summary["cartStill"] = Bool(reply.CartStill);
            summary["cartPosition"] = reply.CartPosition?.Format() ?? string.Empty;
            summary["workerPosition"] = reply.WorkerPosition?.Format() ?? string.Empty;
        }

        return summary;
    }

    public IReadOnlyDictionary<string, string> AcknowledgeWait(
        string requestId, int expectedRevision, string haulId, bool transferring, float now)
    {
        var message = new AcknowledgeWaitMessage(
            EpochOrPlaceholder(), requestId, expectedRevision, haulId,
            transferring ? HaulWaitActivity.Transferring : HaulWaitActivity.Done);
        return SummarisePhase(_client.AcknowledgeWait(message, now));
    }

    public IReadOnlyDictionary<string, string> CancelHaul(string requestId, string haulId, bool detachAndPark, float now)
    {
        var message = new CancelHaulMessage(
            EpochOrPlaceholder(), requestId, haulId,
            detachAndPark ? HaulCancelDisposition.DetachAndPark : HaulCancelDisposition.StopAndWait);
        return SummarisePhase(_client.CancelHaul(message, now));
    }

    private Guid EpochOrPlaceholder() =>
        _client.ProviderEpoch == Guid.Empty ? new Guid(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) : _client.ProviderEpoch;

    private static IReadOnlyDictionary<string, string> SummarisePhase(HaulCall<HaulPhaseReply> call)
    {
        Dictionary<string, string> summary = Summarise(call);
        if (call.Succeeded)
        {
            summary["phase"] = call.Reply!.Phase.ToString();
            summary["revision"] = call.Reply.Revision.ToString(CultureInfo.InvariantCulture);
        }

        return summary;
    }

    private static Dictionary<string, string> Summarise<TReply>(HaulCall<TReply> call)
        where TReply : class
    {
        string status = string.Empty;
        switch (call.Reply)
        {
            case HelloReply hello:
                status = hello.Header.Status.ToString();
                break;
            case DescribeLeaseReply lease:
                status = lease.Header.Status.ToString();
                break;
            case RequestHaulReply leg:
                status = leg.Header.Status.ToString();
                break;
            case GetHaulReply haul:
                status = haul.Header.Status.ToString();
                break;
            case HaulPhaseReply phase:
                status = phase.Header.Status.ToString();
                break;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outcome"] = call.Outcome.ToString(),
            ["status"] = status,
            ["reason"] = call.ReasonName,
            ["detail"] = call.Detail,
            ["fromCache"] = Bool(call.FromCache),
        };
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed class FixedSource : IHaulEndpointSource
    {
        public FixedSource(HaulDiscovery discovery)
        {
            Discovery = discovery;
        }

        public HaulDiscovery Discovery { get; }
    }
}
