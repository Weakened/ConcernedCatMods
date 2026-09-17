using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>#317: the consumer's discovery gate and protocol client
/// (DECISIONS.md D7, CONTRACTS.md §3): absence and incompatibility fail safe
/// with one log line, the provider epoch is tracked, polls respect the poll
/// interval, and failures back off to an honest "provider lost".</summary>
public sealed class CooperationHaulClientTests
{
    private static readonly Guid Epoch = new Guid("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid NextEpoch = new Guid("dddddddd-0000-0000-0000-000000000002");

    // --- discovery ---------------------------------------------------------------------------

    [Fact]
    public void DiscoveryNamesTheActualCauseOfEveryHiddenStateInOneLine()
    {
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint = request => request;
        var good = new Dictionary<string, object> { ["concernedcat.haul/1"] = endpoint };

        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(null));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => throw new InvalidOperationException()));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => null!));
        AssertDiscovery(CapabilityStatus.Absent, HaulProviderGate.Evaluate(HaulProviderLookup.NotFound));
        AssertDiscovery(CapabilityStatus.VersionTooLow, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(null, good)));
        AssertDiscovery(CapabilityStatus.VersionTooLow, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 0, 4), good)));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 0, 5), good, "property getter threw")));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 0, 5), null)));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 0, 5), new Dictionary<string, string>())));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 1, 0), new Dictionary<string, object> { ["concernedcat.haul/1"] = "not a delegate" })));
        AssertDiscovery(CapabilityStatus.ProbeFailed, HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 1, 0), new Dictionary<string, object> { ["concernedcat.other/1"] = endpoint })));

        HaulDiscovery mismatch = HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(
            new Version(2, 0, 0), new Dictionary<string, object> { ["concernedcat.haul/3"] = endpoint, ["concernedcat.haul/2"] = endpoint }));
        AssertDiscovery(CapabilityStatus.MajorMismatch, mismatch);
        Assert.Equal("concernedcat.haul/2, concernedcat.haul/3", mismatch.Detail);

        HaulDiscovery available = HaulProviderGate.Evaluate(() => HaulProviderLookup.Detected(new Version(1, 0, 5), good));
        AssertDiscovery(CapabilityStatus.Available, available);
        Assert.True(available.IsAvailable);
        Assert.Same(endpoint, available.Endpoint);
        Assert.Equal("1.0.5", available.ProviderVersion);

        Assert.False(HaulDiscovery.NotProbed.IsAvailable);
        Assert.Throws<ArgumentOutOfRangeException>(() => HaulDiscovery.Hidden(CapabilityStatus.Available, "1", ""));
    }

    // --- epochs --------------------------------------------------------------------------------

    [Fact]
    public void HelloRecordsTheEpochAndEveryLaterRequestCarriesIt()
    {
        CooperationTestEndpoint endpoint = CooperationTestEndpoint.Healthy(Epoch);
        HaulClient client = Client(endpoint);

        HaulCall<DescribeLeaseReply> beforeHello = client.DescribeLease(0f);
        Assert.Equal(HaulCallOutcome.Stale, beforeHello.Outcome);
        Assert.Equal(0, client.EndpointCalls);

        HaulCall<HelloReply> hello = client.Hello(0f);
        Assert.True(hello.Succeeded);
        Assert.Equal(Epoch, client.ProviderEpoch);
        Assert.Equal("2.0.0", endpoint.Requests[0][HaulContract.Keys.ConsumerVersion]);

        Assert.True(client.DescribeLease(1f).Succeeded);
        Assert.Equal(Epoch.ToString("N"), endpoint.Requests[1][HaulContract.Keys.ProviderEpoch]);
        Assert.True(client.GetHaul("h-1", 1f).Succeeded);
        Assert.Equal(Epoch.ToString("N"), endpoint.Requests[2][HaulContract.Keys.ProviderEpoch]);
        Assert.Equal(0, client.EpochGeneration);
    }

    [Fact]
    public void AStaleEpochIsForgottenAndANewEpochStartsANewGeneration()
    {
        CooperationTestEndpoint endpoint = CooperationTestEndpoint.Healthy(Epoch);
        HaulClient client = Client(endpoint);
        Assert.True(client.Hello(0f).Succeeded);

        endpoint.Respond = _ => HaulRefusal.ToWire(HaulReplyStatus.Stale, HaulWireReason.EpochMismatch, "reloaded");
        HaulCall<GetHaulReply> stale = client.GetHaul("h-1", 1f);
        Assert.Equal(HaulCallOutcome.Stale, stale.Outcome);
        Assert.Equal(HaulWireReason.EpochMismatch, stale.Reason);
        Assert.Equal(Guid.Empty, client.ProviderEpoch);
        Assert.Equal(1, client.EpochGeneration);

        var message = new AcknowledgeWaitMessage(Epoch, "h-1-t1", 3, "h-1", HaulWaitActivity.Done);
        int calls = client.EndpointCalls;
        Assert.Equal(HaulCallOutcome.Stale, client.AcknowledgeWait(message, 2f).Outcome);
        Assert.Equal(calls, client.EndpointCalls);

        endpoint.Respond = CooperationTestEndpoint.HealthyResponder(NextEpoch);
        Assert.True(client.Hello(3f).Succeeded);
        Assert.Equal(NextEpoch, client.ProviderEpoch);
        Assert.Equal(1, client.EpochGeneration);

        // A mutation decided under the old epoch is not sent under the new one.
        Assert.Equal(HaulCallOutcome.Stale, client.AcknowledgeWait(message, 4f).Outcome);
        Assert.Equal(calls + 1, client.EndpointCalls);

        // A hello that reveals yet another epoch is a reload the client did not
        // hear about through a stale answer.
        endpoint.Respond = CooperationTestEndpoint.HealthyResponder(Epoch);
        Assert.True(client.Hello(5f).Succeeded);
        Assert.Equal(2, client.EpochGeneration);
    }

    // --- polling ------------------------------------------------------------------------------

    [Fact]
    public void PollsNeverReachTheProviderFasterThanThePollInterval()
    {
        CooperationTestEndpoint endpoint = CooperationTestEndpoint.Healthy(Epoch);
        HaulClient client = Client(endpoint);
        Assert.True(client.Hello(0f).Succeeded);
        int afterHello = client.EndpointCalls;

        Assert.False(client.GetHaul("h-1", 10f).FromCache);
        HaulCall<GetHaulReply> cached = client.GetHaul("h-1", 10.2f);
        Assert.True(cached.FromCache);
        Assert.True(cached.Succeeded);
        Assert.True(client.GetHaul("h-1", 10.49f).FromCache);
        Assert.Equal(afterHello + 1, client.EndpointCalls);

        Assert.False(client.GetHaul("h-2", 10.3f).FromCache);
        Assert.False(client.GetHaul("h-2", 10.8f).FromCache);
        Assert.Equal(afterHello + 3, client.EndpointCalls);

        Assert.False(client.DescribeLease(11f).FromCache);
        Assert.True(client.DescribeLease(11.1f).FromCache);

        // An accepted mutation invalidates what was cached before it.
        var acknowledgement = new AcknowledgeWaitMessage(Epoch, "h-2-t1", 3, "h-2", HaulWaitActivity.Transferring);
        Assert.True(client.AcknowledgeWait(acknowledgement, 11.2f).Succeeded);
        Assert.False(client.GetHaul("h-2", 11.25f).FromCache);
        Assert.False(client.DescribeLease(11.25f).FromCache);
    }

    // --- failures -------------------------------------------------------------------------------

    [Fact]
    public void NoAnswerBacksOffAndLosesTheProviderAtTheCeiling()
    {
        CooperationTestEndpoint endpoint = CooperationTestEndpoint.Healthy(Epoch);
        HaulClient client = Client(endpoint);
        Assert.True(client.Hello(0f).Succeeded);

        endpoint.Respond = _ => throw new InvalidOperationException("provider bug");
        Assert.Equal(HaulCallOutcome.NoAnswer, client.GetHaul("h-1", 1f).Outcome);
        Assert.Equal(1, client.ConsecutiveNoAnswers);
        int calls = client.EndpointCalls;

        // Backing off: not even asked.
        Assert.Equal(HaulCallOutcome.NoAnswer, client.GetHaul("h-1", 1.6f).Outcome);
        Assert.Equal(calls, client.EndpointCalls);

        Assert.Equal(HaulCallOutcome.NoAnswer, client.GetHaul("h-1", 2f).Outcome);
        Assert.Equal(HaulCallOutcome.NoAnswer, client.GetHaul("h-1", 4f).Outcome);
        Assert.True(client.IsProviderLost);
        Assert.Equal(calls + 2, client.EndpointCalls);

        endpoint.Respond = CooperationTestEndpoint.HealthyResponder(Epoch);
        client.ResetFailures();
        Assert.False(client.IsProviderLost);
        Assert.True(client.GetHaul("h-1", 5f).Succeeded);
    }

    [Fact]
    public void ProviderErrorsAndMalformedRepliesCountAsNoAnswerButARefusalDoesNot()
    {
        CooperationTestEndpoint endpoint = CooperationTestEndpoint.Healthy(Epoch);
        HaulClient client = Client(endpoint);
        Assert.True(client.Hello(0f).Succeeded);

        endpoint.Respond = _ => HaulRefusal.ToWire(HaulReplyStatus.ProviderError, HaulWireReason.WorkerUnavailable, "NullReferenceException");
        HaulCall<GetHaulReply> error = client.GetHaul("h-1", 1f);
        Assert.Equal(HaulCallOutcome.NoAnswer, error.Outcome);
        Assert.Equal("NullReferenceException", error.Detail);
        Assert.Equal(1, client.ConsecutiveNoAnswers);

        endpoint.Respond = _ => new Dictionary<string, string> { ["status"] = "Accepted" };
        Assert.Equal(HaulCallOutcome.NoAnswer, client.GetHaul("h-1", 3f).Outcome);
        Assert.Equal(2, client.ConsecutiveNoAnswers);

        endpoint.Respond = _ => HaulRefusal.ToWire(HaulReplyStatus.Unavailable, HaulWireReason.OtherPeersConnected, null);
        HaulCall<GetHaulReply> unavailable = client.GetHaul("h-1", 7f);
        Assert.Equal(HaulCallOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(HaulWireReason.OtherPeersConnected, unavailable.Reason);
        Assert.Equal(0, client.ConsecutiveNoAnswers);

        endpoint.Respond = _ => HaulRefusal.ToWire(HaulReplyStatus.Rejected, HaulWireReason.UnknownHaul, null);
        Assert.Equal(HaulCallOutcome.Refused, client.GetHaul("h-1", 8f).Outcome);
        Assert.False(client.IsProviderLost);
    }

    [Fact]
    public void AnAbsentOrIncompatibleProviderAnswersNoAnswerWithoutThrowing()
    {
        var endpoint = new CooperationTestEndpoint
        {
            Discovery = HaulDiscovery.Hidden(CapabilityStatus.Absent, "unknown", "not installed"),
        };
        HaulClient client = Client(endpoint);

        HaulCall<HelloReply> hello = client.Hello(0f);
        Assert.Equal(HaulCallOutcome.NoAnswer, hello.Outcome);
        Assert.Contains("Absent", hello.Detail);
        Assert.Equal(0, client.EndpointCalls);
        Assert.Equal(CapabilityStatus.Absent, client.DiscoveryStatus);

        endpoint.Discovery = HaulDiscovery.Hidden(CapabilityStatus.MajorMismatch, "9.0.0", "concernedcat.haul/2");
        Assert.Equal(HaulCallOutcome.NoAnswer, client.Hello(1f).Outcome);
        Assert.Equal(0, client.EndpointCalls);
    }

    // --- request ids ---------------------------------------------------------------------------

    [Fact]
    public void RequestIdsAreBoundedSlugsUniquePerRunAndAttempt()
    {
        var shortOrder = new OrderId("collect-1");
        var longOrder = new OrderId("collect-stone-and-wood-for-the-long-hall-0001");
        var nonce = new Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

        var ids = new HaulRequestIds(shortOrder, nonce);
        Assert.Equal("h-collect-1-0f1e2d3c", ids.HaulId);
        Assert.Equal("h-collect-1-0f1e2d3c-q1", ids.Next('q'));
        Assert.Equal("h-collect-1-0f1e2d3c-t2", ids.NextTransfer().Value);
        Assert.Equal(2, ids.Issued);

        var longIds = new HaulRequestIds(longOrder, nonce);
        Assert.True(WorkSlug.IsValid(longIds.HaulId));
        Assert.True(longIds.HaulId.Length <= 31);
        string id = longIds.Next('c');
        for (int index = 0; index < 100000; index++)
        {
            id = longIds.Next('z');
        }

        Assert.True(WorkSlug.IsValid(id), id);
        Assert.True(id.Length <= WorkSlug.MaxLength, id);

        var otherRun = new HaulRequestIds(shortOrder, new Guid("1f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"));
        Assert.NotEqual(ids.HaulId, otherRun.HaulId);
        Assert.Throws<ArgumentOutOfRangeException>(() => ids.Next('Q'));
        Assert.Throws<ArgumentException>(() => new HaulRequestIds(shortOrder, Guid.Empty));
    }

    [Fact]
    public void LimitsRefuseValuesOutsideTheirDesign()
    {
        CooperationLimits.Default.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooperationLimits { PollIntervalSeconds = 0.01f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooperationLimits { RendezvousTimeoutSeconds = 5f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooperationLimits { CartStandOffMetres = 3f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CooperationLimits { MaxTransferRefusals = 0 }.Validate());
    }

    private static HaulClient Client(CooperationTestEndpoint endpoint) =>
        new HaulClient(endpoint, CooperationLimits.Default, "2.0.0");

    private static void AssertDiscovery(CapabilityStatus expected, HaulDiscovery discovery)
    {
        Assert.Equal(expected, discovery.Status);
        Assert.Equal(expected == CapabilityStatus.Available, discovery.Endpoint != null);
        string line = discovery.LogLine;
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.DoesNotContain("\n", line);
        Assert.Contains("concernedcat.haul/1", line);
    }
}
