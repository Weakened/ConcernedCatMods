using System.Collections.Generic;
using Interop.Tests.Consumer;
using Interop.Tests.Provider;

namespace Interop.Tests;

/// <summary>#317, CONTRACTS.md §9 and SPEC Gate A: the provider half of
/// <c>concernedcat.haul/1</c> (as Teamster compiles it) and the consumer half
/// (as Foreman compiles it), built into two separate assemblies, exchanging
/// every op through nothing but the BCL capability map - plus the failure
/// matrix: absent, version too low, major mismatch, a provider that throws,
/// a stale epoch, a stale revision, and a request id reused with another
/// payload.</summary>
public sealed class HaulCapabilityExchangeTests
{
    private const string Order = "collect-1";
    private const string Haul = "h-collect-1-0f1e2d3c";

    [Fact]
    public void TheTwoHalvesAreSeparatelyCompiledCopiesOfOneContract()
    {
        var provider = new ProviderHarness();
        ConsumerHarness consumer = Connect(provider);

        string providerType = provider.ContractTypeIdentity;
        string consumerType = consumer.ContractTypeIdentity;
        Assert.NotEqual(providerType, consumerType);
        Assert.StartsWith("TheConcernedCat.Interop.Haul.HaulReplyStatus, TheConcernedCat.Interop.Tests.Provider,", providerType);
        Assert.StartsWith("TheConcernedCat.Interop.Haul.HaulReplyStatus, TheConcernedCat.Interop.Tests.Consumer,", consumerType);

        // Only a mscorlib delegate crossed.
        object value = Assert.Single(provider.Capabilities).Value;
        Assert.Equal(
            typeof(Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>),
            value.GetType());
    }

    [Fact]
    public void BothSidesAgreeOnThePollIntervalAndTheRendezvousTimeout()
    {
        var provider = new ProviderHarness();
        ConsumerHarness consumer = Connect(provider);

        Assert.Equal(provider.PollIntervalSeconds, consumer.PollIntervalSeconds);
        Assert.Equal(provider.RendezvousTimeoutSeconds, consumer.RendezvousTimeoutSeconds);
    }

    [Fact]
    public void EveryOpIsExchangedThroughTheCapabilityMap()
    {
        var provider = new ProviderHarness();
        Assert.Equal("Assigned", provider.AssignCart("lease-7", "12:345"));
        ConsumerHarness consumer = Connect(provider);
        Assert.Equal("Available", consumer.DiscoveryStatus);
        Assert.Contains("AVAILABLE", consumer.DiscoveryLogLine);

        IReadOnlyDictionary<string, string> hello = consumer.Hello(0f);
        AssertOutcome(hello, "Succeeded", "Accepted");
        Assert.Equal("0", hello["contractMinor"]);
        Assert.Equal("1.0.5", hello["providerVersion"]);
        Assert.Equal(provider.EpochText, hello["providerEpoch"]);
        Assert.Equal("Granted", hello["authority"]);
        Assert.Equal("true", hello["workerAvailable"]);
        Assert.Equal("Ready", hello["phase"]);
        Assert.Equal("lease-7", hello["leaseId"]);

        IReadOnlyDictionary<string, string> lease = consumer.DescribeLease(1f);
        AssertOutcome(lease, "Succeeded", "Accepted");
        Assert.Equal("12:345", lease["cartSessionKey"]);
        Assert.Equal("true", lease["cartUpright"]);
        Assert.Equal(provider.Revision.ToString(), lease["revision"]);

        IReadOnlyDictionary<string, string> leg = consumer.RequestHaul(
            Haul + "-q1", provider.Revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 2f);
        AssertOutcome(leg, "Succeeded", "Accepted");
        Assert.Equal(Haul, leg["haulId"]);
        Assert.Equal("Approaching", leg["phase"]);
        Assert.Equal(1, provider.LegCalls);
        Assert.Equal("20;30;20", provider.LastLegTarget);
        Assert.False(provider.LastLegToDestination);
        Assert.Equal(Order, provider.LastLegOrderId);

        provider.CompleteLeg(20f, 30f, 20f);
        IReadOnlyDictionary<string, string> haul = consumer.GetHaul(Haul, 3f);
        AssertOutcome(haul, "Succeeded", "Accepted");
        Assert.Equal("Waiting", haul["phase"]);
        Assert.Equal("true", haul["arrived"]);
        Assert.Equal("true", haul["attached"]);
        Assert.Equal("true", haul["cartStill"]);
        Assert.Equal("20;30;20", haul["cartPosition"]);
        Assert.Equal(string.Empty, haul["attention"]);

        IReadOnlyDictionary<string, string> hold = consumer.AcknowledgeWait(Haul + "-t2", provider.Revision, Haul, true, 4f);
        AssertOutcome(hold, "Succeeded", "Accepted");
        Assert.Equal("Unloading", hold["phase"]);

        IReadOnlyDictionary<string, string> done = consumer.AcknowledgeWait(Haul + "-d3", provider.Revision, Haul, false, 5f);
        AssertOutcome(done, "Succeeded", "Accepted");
        Assert.Equal("Waiting", done["phase"]);

        IReadOnlyDictionary<string, string> onward = consumer.RequestHaul(
            Haul + "-q4", provider.Revision, Order, Haul, "lease-7", true, 40f, 30f, 40f, 3f, 6f);
        AssertOutcome(onward, "Succeeded", "Accepted");
        Assert.Equal("Pulling", onward["phase"]);
        Assert.True(provider.LastLegToDestination);

        IReadOnlyDictionary<string, string> stop = consumer.CancelHaul(Haul + "-c5", Haul, false, 7f);
        AssertOutcome(stop, "Succeeded", "Accepted");
        Assert.Equal("Waiting", stop["phase"]);
        Assert.True(provider.Attached);

        IReadOnlyDictionary<string, string> park = consumer.CancelHaul(Haul + "-c6", Haul, true, 8f);
        AssertOutcome(park, "Succeeded", "Accepted");
        Assert.Equal("Ready", park["phase"]);
        Assert.False(provider.Attached);

        Assert.Equal(2, provider.AcknowledgeCalls);
        Assert.Equal(2, provider.CancelCalls);
        Assert.Equal(0, provider.FaultCount);
        Assert.Equal(0, consumer.ConsecutiveNoAnswers);
    }

    [Fact]
    public void AnAbsentProviderIsDiscoveredAsAbsentAndNothingIsCalled()
    {
        ConsumerHarness consumer = ConsumerHarness.Discover(installed: false, version: null, capabilityProperty: null);

        Assert.Equal("Absent", consumer.DiscoveryStatus);
        Assert.Contains("not installed", consumer.DiscoveryLogLine);
        AssertOutcome(consumer.Hello(0f), "NoAnswer", string.Empty);
        AssertOutcome(consumer.GetHaul(Haul, 1f), "Stale", string.Empty);
        Assert.Equal(0, consumer.EndpointCalls);
    }

    [Fact]
    public void AnOldTeamsterOrOneWithoutTheCapabilityFailsSafe()
    {
        var provider = new ProviderHarness();

        ConsumerHarness tooOld = ConsumerHarness.Discover(true, new Version(1, 0, 4), provider.Capabilities);
        Assert.Equal("VersionTooLow", tooOld.DiscoveryStatus);
        AssertOutcome(tooOld.Hello(0f), "NoAnswer", string.Empty);

        ConsumerHarness noProperty = ConsumerHarness.Discover(true, new Version(1, 0, 5), null);
        Assert.Equal("ProbeFailed", noProperty.DiscoveryStatus);

        ConsumerHarness unreadable = ConsumerHarness.Discover(true, new Version(1, 0, 5), null, "getter threw TargetInvocationException");
        Assert.Equal("ProbeFailed", unreadable.DiscoveryStatus);
        Assert.Contains("TargetInvocationException", unreadable.DiscoveryLogLine);

        Assert.Equal(0, tooOld.EndpointCalls + noProperty.EndpointCalls + unreadable.EndpointCalls);
        Assert.Equal(0, provider.LegCalls);
    }

    [Fact]
    public void AProviderPublishingAnotherMajorIsAMajorMismatch()
    {
        var provider = new ProviderHarness();
        var onlyMajorTwo = new Dictionary<string, object>
        {
            ["concernedcat.haul/2"] = provider.Capabilities["concernedcat.haul/1"],
        };

        ConsumerHarness consumer = ConsumerHarness.Discover(true, new Version(2, 0, 0), onlyMajorTwo);

        Assert.Equal("MajorMismatch", consumer.DiscoveryStatus);
        Assert.Equal("concernedcat.haul/2", consumer.DiscoveryDetail);
        AssertOutcome(consumer.Hello(0f), "NoAnswer", string.Empty);
        Assert.Equal(0, consumer.EndpointCalls);
    }

    [Fact]
    public void AProviderExceptionNeverCrossesAndCountsTowardLosingTheProvider()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");

        // Caught inside the provider: ProviderError on the wire.
        provider.ThrowOnNextCommand();
        IReadOnlyDictionary<string, string> leg = consumer.RequestHaul(
            Haul + "-q1", provider.Revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 1f);
        AssertOutcome(leg, "NoAnswer", "ProviderError");
        Assert.Equal(1, provider.FaultCount);
        Assert.Equal(1, consumer.ConsecutiveNoAnswers);

        // The same attempt, retried after the backoff: applied once, now.
        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", provider.Revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 5f),
            "Succeeded", "Accepted");
        Assert.Equal(2, provider.LegCalls);

        // Not caught at all: a delegate that throws is still only NoAnswer.
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> broken =
            _ => throw new InvalidOperationException("a provider without its guard");
        ConsumerHarness brokenConsumer = ConsumerHarness.Discover(
            true, new Version(1, 0, 5), new Dictionary<string, object> { ["concernedcat.haul/1"] = broken });
        Assert.Equal("Available", brokenConsumer.DiscoveryStatus);
        AssertOutcome(brokenConsumer.Hello(0f), "NoAnswer", string.Empty);
        AssertOutcome(brokenConsumer.Hello(1f), "NoAnswer", string.Empty);
        AssertOutcome(brokenConsumer.Hello(3f), "NoAnswer", string.Empty);
        Assert.True(brokenConsumer.IsProviderLost);
    }

    [Fact]
    public void AReloadOnTheProviderMakesTheOldEpochStaleUntilTheConsumerSaysHelloAgain()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");
        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", provider.Revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 1f),
            "Succeeded", "Accepted");
        string oldEpoch = consumer.ProviderEpoch;

        provider.ReloadWorld();

        IReadOnlyDictionary<string, string> stale = consumer.GetHaul(Haul, 2f);
        AssertOutcome(stale, "Stale", "Stale");
        Assert.Equal("EpochMismatch", stale["reason"]);
        Assert.Equal(1, consumer.EpochGeneration);

        // No epoch now: mutations are not even sent.
        int calls = consumer.EndpointCalls;
        AssertOutcome(consumer.CancelHaul(Haul + "-c2", Haul, true, 3f), "Stale", string.Empty);
        Assert.Equal(calls, consumer.EndpointCalls);

        IReadOnlyDictionary<string, string> hello = consumer.Hello(4f);
        AssertOutcome(hello, "Succeeded", "Accepted");
        Assert.NotEqual(oldEpoch, hello["providerEpoch"]);
        Assert.Equal("Unassigned", hello["phase"]);
        Assert.Equal(string.Empty, hello["leaseId"]);
        AssertOutcome(consumer.DescribeLease(5f), "Refused", "Rejected");
        Assert.Equal(1, provider.LegCalls);
    }

    [Fact]
    public void AnOldRevisionIsStaleAndTheConsumerMustReadAgain()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");
        int revision = provider.Revision;
        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 1f),
            "Succeeded", "Accepted");
        provider.CompleteLeg(20f, 30f, 20f);

        IReadOnlyDictionary<string, string> stale = consumer.AcknowledgeWait(Haul + "-t2", revision, Haul, true, 2f);
        AssertOutcome(stale, "Stale", "Stale");
        Assert.Equal("RevisionMismatch", stale["reason"]);
        Assert.Equal(0, provider.AcknowledgeCalls);
        Assert.Equal("Waiting", provider.Phase);

        IReadOnlyDictionary<string, string> haul = consumer.GetHaul(Haul, 3f);
        AssertOutcome(
            consumer.AcknowledgeWait(Haul + "-t3", int.Parse(haul["revision"]), Haul, true, 4f),
            "Succeeded", "Accepted");
        Assert.Equal("Unloading", provider.Phase);
    }

    [Fact]
    public void ARequestIdReusedWithADifferentPayloadIsRefusedAndAnExactRetryIsAlreadySatisfied()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");
        int revision = provider.Revision;

        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 1f),
            "Succeeded", "Accepted");
        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 2f),
            "Succeeded", "AlreadySatisfied");

        IReadOnlyDictionary<string, string> different = consumer.RequestHaul(
            Haul + "-q1", revision, Order, Haul, "lease-7", false, 25f, 30f, 20f, 3f, 3f);
        AssertOutcome(different, "Refused", "Rejected");
        Assert.Equal("DuplicateRequestDifferentPayload", different["reason"]);
        Assert.Equal(1, provider.LegCalls);
        Assert.Equal("20;30;20", provider.LastLegTarget);
    }

    [Fact]
    public void ProviderEndedControlReachesTheConsumerAsNeedsAttentionWithItsReason()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");
        AssertOutcome(
            consumer.RequestHaul(Haul + "-q1", provider.Revision, Order, Haul, "lease-7", false, 20f, 30f, 20f, 3f, 1f),
            "Succeeded", "Accepted");
        provider.CompleteLeg(20f, 30f, 20f);

        provider.EndControl("CartDestroyed");
        IReadOnlyDictionary<string, string> haul = consumer.GetHaul(Haul, 2f);
        AssertOutcome(haul, "Succeeded", "Accepted");
        Assert.Equal("NeedsAttention", haul["phase"]);
        Assert.Equal("CartDestroyed", haul["attention"]);
        Assert.Equal("false", haul["attached"]);

        provider.SetAuthority("OtherPeersConnected");
        IReadOnlyDictionary<string, string> refused = consumer.AcknowledgeWait(Haul + "-t2", provider.Revision, Haul, true, 3f);
        AssertOutcome(refused, "Unavailable", "Unavailable");
        Assert.Equal("OtherPeersConnected", refused["reason"]);
    }

    [Fact]
    public void AStoppedRuntimeOrAShuttingDownProviderIsUnavailableNotAnError()
    {
        var provider = new ProviderHarness();
        provider.AssignCart("lease-7", "12:345");
        ConsumerHarness consumer = Connect(provider);
        AssertOutcome(consumer.Hello(0f), "Succeeded", "Accepted");

        provider.StopRuntime();
        IReadOnlyDictionary<string, string> lease = consumer.DescribeLease(1f);
        AssertOutcome(lease, "Unavailable", "Unavailable");
        Assert.Equal("WorkerUnavailable", lease["reason"]);

        IReadOnlyDictionary<string, string> hello = consumer.Hello(2f);
        AssertOutcome(hello, "Succeeded", "Accepted");
        Assert.Equal("RuntimeDisabled", hello["authority"]);
        Assert.Equal("false", hello["workerAvailable"]);

        provider.BeginShutdown();
        AssertOutcome(consumer.Hello(3f), "Unavailable", "Unavailable");
        Assert.False(consumer.IsProviderLost);
        Assert.Equal(0, consumer.ConsecutiveNoAnswers);
    }

    private static ConsumerHarness Connect(ProviderHarness provider) =>
        ConsumerHarness.Discover(true, new Version(1, 0, 5), provider.Capabilities);

    private static void AssertOutcome(IReadOnlyDictionary<string, string> summary, string outcome, string status)
    {
        Assert.True(
            outcome == summary["outcome"] && status == summary["status"],
            "expected " + outcome + "/" + status + " but was " + summary["outcome"] + "/" + summary["status"] +
            " (" + summary["reason"] + ": " + summary["detail"] + ")");
    }
}
