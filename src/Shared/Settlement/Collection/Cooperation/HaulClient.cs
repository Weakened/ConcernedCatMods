using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Where the consumer gets the haul endpoint from: the adapter's
/// once-per-world-session discovery in the game, a fake in tests.</summary>
internal interface IHaulEndpointSource
{
    HaulDiscovery Discovery { get; }
}

/// <summary>How one call to the haul provider ended, from the consumer's side.
/// </summary>
internal enum HaulCallOutcome
{
    Unspecified = 0,

    /// <summary>Accepted, or AlreadySatisfied (a retry of something that
    /// already happened).</summary>
    Succeeded = 1,

    /// <summary>Rejected: the provider refused and nothing changed.</summary>
    Refused = 2,

    /// <summary>Based on an old epoch or revision: re-read and decide again.
    /// </summary>
    Stale = 3,

    /// <summary>The provider answered that it cannot serve now (no authority,
    /// no worker, no world). A clear "not now", not a transport failure.
    /// </summary>
    Unavailable = 4,

    /// <summary>No trustworthy answer: no endpoint, a thrown or missing reply,
    /// a malformed reply, ProviderError, or backing off after those. Nothing is
    /// assumed to have changed. Counts toward losing the provider.</summary>
    NoAnswer = 5,
}

/// <summary>One call's outcome and, when there was an answer, the reply.
/// </summary>
internal sealed class HaulCall<TReply>
    where TReply : class
{
    public HaulCall(
        HaulCallOutcome outcome, TReply? reply, HaulWireReason reason, string reasonName, string detail, bool fromCache)
    {
        Outcome = outcome;
        Reply = reply;
        Reason = reason;
        ReasonName = reasonName ?? string.Empty;
        Detail = detail ?? string.Empty;
        FromCache = fromCache;
    }

    public HaulCallOutcome Outcome { get; }

    public TReply? Reply { get; }

    public HaulWireReason Reason { get; }

    public string ReasonName { get; }

    public string Detail { get; }

    /// <summary>A poll inside the poll interval, answered from the last reply
    /// instead of calling the provider again.</summary>
    public bool FromCache { get; }

    public bool Succeeded => Outcome == HaulCallOutcome.Succeeded && Reply != null;

    public HaulCall<TReply> AsCached() => new HaulCall<TReply>(Outcome, Reply, Reason, ReasonName, Detail, true);
}

/// <summary>The consumer's half of <c>concernedcat.haul/1</c> (CONTRACTS.md §3,
/// D7), game-free: builds nothing itself beyond the call, and owns the three
/// rules a consumer must keep.
///
/// <b>Epochs.</b> The provider epoch comes from <c>hello</c> and is attached to
/// every later request. A Stale/EpochMismatch answer forgets it and bumps
/// <see cref="EpochGeneration"/>; a hello that reveals a different epoch bumps
/// it too. A haul started under one generation is gone in the next - the
/// provider's world was reloaded - and the loop treats that as losing the cart.
///
/// <b>Polling.</b> <c>getHaul</c> and <c>describeLease</c> reach the provider at
/// most once per <see cref="CooperationLimits.PollIntervalSeconds"/>; faster
/// polls get the last answer back, marked <see cref="HaulCall{T}.FromCache"/>.
/// Any accepted mutation clears those caches, so a loop never acts on a phase
/// from before its own acknowledgement.
///
/// <b>Failures.</b> Every call goes through <see cref="CapabilityMap.TryCall"/>,
/// so nothing thrown across the boundary reaches the game. A call with no
/// trustworthy answer is recorded in a bounded retry with doubling backoff;
/// while backing off, calls answer NoAnswer without reaching the provider, and
/// once the ceiling is reached <see cref="IsProviderLost"/> stays true until the
/// loop has reconciled and calls <see cref="ResetFailures"/>.</summary>
internal sealed class HaulClient
{
    private readonly IHaulEndpointSource _source;
    private readonly CooperationLimits _limits;
    private readonly string _consumerVersion;
    private readonly BoundedRetry _failures;

    private HaulCall<GetHaulReply>? _lastGetHaul;
    private string _lastGetHaulId = string.Empty;
    private float _lastGetHaulAt;
    private HaulCall<DescribeLeaseReply>? _lastLease;
    private float _lastLeaseAt;

    public HaulClient(IHaulEndpointSource source, CooperationLimits limits, string consumerVersion)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        if (!HaulFieldRules.IsToken(consumerVersion, HaulFieldRules.MaxVersionLength))
        {
            throw new ArgumentException("The consumer version must be a short token.", nameof(consumerVersion));
        }

        _consumerVersion = consumerVersion;
        _failures = new BoundedRetry(limits.MaxConsecutiveNoAnswers, limits.NoAnswerBackoffSeconds, limits.NoAnswerBackoffMaxSeconds);
    }

    public CapabilityStatus DiscoveryStatus => _source.Discovery.Status;

    /// <summary>Empty until a hello succeeded with a loaded world, and again
    /// after the epoch was found stale.</summary>
    public Guid ProviderEpoch { get; private set; }

    /// <summary>Increments whenever the known epoch is lost or replaced.
    /// </summary>
    public int EpochGeneration { get; private set; }

    public HelloReply? LastHello { get; private set; }

    public bool IsProviderLost => _failures.IsExhausted;

    public int ConsecutiveNoAnswers => _failures.Failures;

    /// <summary>Calls that actually reached the endpoint, for tests and
    /// diagnostics.</summary>
    public int EndpointCalls { get; private set; }

    public void ResetFailures() => _failures.Reset();

    public void ForgetEpoch()
    {
        if (ProviderEpoch != Guid.Empty)
        {
            ProviderEpoch = Guid.Empty;
            EpochGeneration++;
        }

        ClearCaches();
    }

    public HaulCall<HelloReply> Hello(float now)
    {
        HaulCall<HelloReply> call = Call<HelloReply>(new HelloMessage(_consumerVersion), now, HelloReply.TryRead);
        if (call.Succeeded)
        {
            HelloReply hello = call.Reply!;
            LastHello = hello;
            if (hello.ProviderEpoch != ProviderEpoch)
            {
                if (ProviderEpoch != Guid.Empty || hello.ProviderEpoch == Guid.Empty)
                {
                    EpochGeneration++;
                }

                ProviderEpoch = hello.ProviderEpoch;
                ClearCaches();
            }
        }

        return call;
    }

    public HaulCall<DescribeLeaseReply> DescribeLease(float now)
    {
        if (_lastLease != null && now - _lastLeaseAt < _limits.PollIntervalSeconds && now >= _lastLeaseAt)
        {
            return _lastLease.AsCached();
        }

        if (ProviderEpoch == Guid.Empty)
        {
            return NoEpoch<DescribeLeaseReply>();
        }

        HaulCall<DescribeLeaseReply> call = Call<DescribeLeaseReply>(
            new DescribeLeaseMessage(ProviderEpoch), now, DescribeLeaseReply.TryRead);
        if (call.Outcome != HaulCallOutcome.NoAnswer)
        {
            _lastLease = call;
            _lastLeaseAt = now;
        }

        return call;
    }

    public HaulCall<GetHaulReply> GetHaul(string haulId, float now)
    {
        if (_lastGetHaul != null && string.Equals(_lastGetHaulId, haulId, StringComparison.Ordinal) &&
            now - _lastGetHaulAt < _limits.PollIntervalSeconds && now >= _lastGetHaulAt)
        {
            return _lastGetHaul.AsCached();
        }

        if (ProviderEpoch == Guid.Empty)
        {
            return NoEpoch<GetHaulReply>();
        }

        HaulCall<GetHaulReply> call = Call<GetHaulReply>(new GetHaulMessage(ProviderEpoch, haulId), now, GetHaulReply.TryRead);
        if (call.Outcome != HaulCallOutcome.NoAnswer)
        {
            _lastGetHaul = call;
            _lastGetHaulId = haulId;
            _lastGetHaulAt = now;
        }

        return call;
    }

    public HaulCall<RequestHaulReply> RequestHaul(RequestHaulMessage message, float now) =>
        Mutate<RequestHaulReply>(message, now, RequestHaulReply.TryRead);

    public HaulCall<HaulPhaseReply> AcknowledgeWait(AcknowledgeWaitMessage message, float now) =>
        Mutate<HaulPhaseReply>(message, now, HaulPhaseReply.TryRead);

    public HaulCall<HaulPhaseReply> CancelHaul(CancelHaulMessage message, float now) =>
        Mutate<HaulPhaseReply>(message, now, HaulPhaseReply.TryRead);

    private HaulCall<TReply> Mutate<TReply>(HaulMutatingMessage message, float now, ReplyReader<TReply> read)
        where TReply : class
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (ProviderEpoch == Guid.Empty)
        {
            return NoEpoch<TReply>();
        }

        if (message.ProviderEpoch != ProviderEpoch)
        {
            // Decided under an epoch this client no longer holds: the provider
            // would say the same, so it is not asked.
            return new HaulCall<TReply>(
                HaulCallOutcome.Stale, null, HaulWireReason.EpochMismatch, HaulWireReason.EpochMismatch.ToString(),
                "the request was built for a previous provider epoch", false);
        }

        HaulCall<TReply> call = Call(message, now, read);
        if (call.Succeeded)
        {
            ClearCaches();
        }

        return call;
    }

    private delegate bool ReplyReader<TReply>(IReadOnlyDictionary<string, string>? wire, out TReply? reply)
        where TReply : class;

    private HaulCall<TReply> Call<TReply>(HaulRequestMessage message, float now, ReplyReader<TReply> read)
        where TReply : class
    {
        HaulDiscovery discovery = _source.Discovery;
        if (discovery == null || !discovery.IsAvailable)
        {
            return new HaulCall<TReply>(
                HaulCallOutcome.NoAnswer, null, HaulWireReason.Unspecified, string.Empty,
                "the haul provider is not available (" + (discovery?.Status ?? CapabilityStatus.Unspecified) + ")", false);
        }

        if (_failures.IsWaiting(now))
        {
            return new HaulCall<TReply>(
                HaulCallOutcome.NoAnswer, null, HaulWireReason.Unspecified, string.Empty, "backing off after failed calls", false);
        }

        EndpointCalls++;
        IReadOnlyDictionary<string, string>? wire = CapabilityMap.TryCall(discovery.Endpoint, message.ToWire());
        if (wire == null || !read(wire, out TReply? reply) || reply == null)
        {
            _failures.RecordFailure(now);
            return new HaulCall<TReply>(
                HaulCallOutcome.NoAnswer, null, HaulWireReason.Unspecified, string.Empty,
                wire == null ? "the provider threw or returned nothing" : "the provider's reply was malformed", false);
        }

        HaulReplyHeader header = HeaderOf(reply);
        switch (header.Status)
        {
            case HaulReplyStatus.Accepted:
            case HaulReplyStatus.AlreadySatisfied:
                _failures.Reset();
                return new HaulCall<TReply>(HaulCallOutcome.Succeeded, reply, HaulWireReason.Unspecified, string.Empty, header.Detail, false);

            case HaulReplyStatus.Rejected:
                _failures.Reset();
                return new HaulCall<TReply>(HaulCallOutcome.Refused, reply, header.Reason, header.ReasonName, header.Detail, false);

            case HaulReplyStatus.Stale:
                _failures.Reset();
                if (header.Reason == HaulWireReason.EpochMismatch)
                {
                    ForgetEpoch();
                }

                return new HaulCall<TReply>(HaulCallOutcome.Stale, reply, header.Reason, header.ReasonName, header.Detail, false);

            case HaulReplyStatus.Unavailable:
                _failures.Reset();
                return new HaulCall<TReply>(HaulCallOutcome.Unavailable, reply, header.Reason, header.ReasonName, header.Detail, false);

            default:
                // ProviderError: the provider caught its own exception. Nothing
                // is assumed to have changed; it counts like no answer at all.
                _failures.RecordFailure(now);
                return new HaulCall<TReply>(HaulCallOutcome.NoAnswer, reply, header.Reason, header.ReasonName, header.Detail, false);
        }
    }

    private static HaulReplyHeader HeaderOf(object reply)
    {
        switch (reply)
        {
            case HelloReply hello:
                return hello.Header;
            case DescribeLeaseReply lease:
                return lease.Header;
            case RequestHaulReply leg:
                return leg.Header;
            case GetHaulReply haul:
                return haul.Header;
            case HaulPhaseReply phase:
                return phase.Header;
            default:
                throw new ArgumentException("Not a haul reply.", nameof(reply));
        }
    }

    private static HaulCall<TReply> NoEpoch<TReply>()
        where TReply : class =>
        new HaulCall<TReply>(
            HaulCallOutcome.Stale, null, HaulWireReason.EpochMismatch, HaulWireReason.EpochMismatch.ToString(),
            "no provider epoch: say hello first", false);

    private void ClearCaches()
    {
        _lastGetHaul = null;
        _lastGetHaulId = string.Empty;
        _lastLease = null;
    }
}
