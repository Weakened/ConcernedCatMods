using System;
using Interop.Tests.Consumer;
using Interop.Tests.Provider;

namespace Interop.Tests;

/// <summary>#317, SPEC GATHER-03: Cartographer publishes whether a companion is
/// here and free, and Foreman decides from that whether a survey is joint.
///
/// Both halves are compiled into their own assemblies, so every contract type
/// in this exchange is two different CLR types and only the BCL map crosses —
/// exactly as it is between the two shipped DLLs. A test that shared one
/// assembly would prove the logic and nothing about the boundary, and the
/// boundary is the part that breaks in the field.</summary>
public sealed class PresenceCapabilityExchangeTests
{
    private static readonly Version Current = new(1, 2, 2);

    private static PresenceConsumerHarness Against(PresenceProviderHarness provider, Version? version = null) =>
        PresenceConsumerHarness.Discover(installed: true, version ?? Current, provider.Capabilities);

    [Fact]
    public void HeIsHereAndFreeSoTheSurveyIsJoint()
    {
        var provider = new PresenceProviderHarness();

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.True(answer.Answered);
        Assert.True(answer.Joint);
        Assert.Equal("None", answer.SoloReason);
        Assert.Contains("together with Hulgi", answer.Sentence);
    }

    [Fact]
    public void HisPositionSurvivesTheCrossing()
    {
        var provider = new PresenceProviderHarness { X = 12.5f, Y = 30.25f, Z = -4f };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.True(answer.HasPosition);
        Assert.Equal(12.5f, answer.X);
        Assert.Equal(30.25f, answer.Y);
        Assert.Equal(-4f, answer.Z);
    }

    [Fact]
    public void NoBodyMeansNoPositionRatherThanTheOrigin()
    {
        var provider = new PresenceProviderHarness { Present = false, HasPosition = false };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.False(answer.HasPosition);
        Assert.False(answer.Joint);
    }

    // ---- no false credit: the four ways he is not in the survey -------------

    [Fact]
    public void ANotInstalledCartographerIsSoloWithoutAsking()
    {
        var consumer = PresenceConsumerHarness.Discover(installed: false, null, null);

        PresenceAnswer answer = consumer.Ask(PresenceConsumerHarness.HulgiId);

        Assert.False(answer.Answered);
        Assert.False(answer.Joint);
        Assert.Equal("NoProvider", answer.SoloReason);
        Assert.Contains("Thorstein alone", answer.Sentence);
        Assert.Contains("not installed", consumer.DiscoveryLogLine);
    }

    [Fact]
    public void APlayerWhoHasNotMetHimSurveysAlone()
    {
        var provider = new PresenceProviderHarness { Known = false, Present = false, Available = false };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.True(answer.Answered);
        Assert.False(answer.Joint);
        Assert.Equal("NotKnown", answer.SoloReason);
    }

    [Fact]
    public void AHiddenCompanionIsKnownButNotHere()
    {
        var provider = new PresenceProviderHarness { Visible = false, Available = false };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.True(answer.Known);
        Assert.False(answer.Joint);
        Assert.Equal("NotHere", answer.SoloReason);
        Assert.Contains("is not here", answer.Sentence);
    }

    [Fact]
    public void ABusyCompanionIsNotCredited()
    {
        var provider = new PresenceProviderHarness { Available = false };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.True(answer.Present);
        Assert.True(answer.Visible);
        Assert.False(answer.Joint);
        Assert.Equal("Busy", answer.SoloReason);
        Assert.Contains("is busy", answer.Sentence);
    }

    [Fact]
    public void AvailabilityIsTheProvidersAnswerAndNotRederivedHere()
    {
        // Here, shown, and the provider still says no. A consumer that
        // concluded availability from present && visible would call this joint.
        var provider = new PresenceProviderHarness { Present = true, Visible = true, Available = false };

        Assert.False(Against(provider).Ask(PresenceConsumerHarness.HulgiId).Joint);
    }

    // ---- the provider is there but cannot answer ---------------------------

    [Fact]
    public void NoRuntimeIsUnavailableAndThereforeSolo()
    {
        var provider = new PresenceProviderHarness { Ready = false };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.False(answer.Answered);
        Assert.False(answer.Joint);
        Assert.Equal("NoProvider", answer.SoloReason);
    }

    [Fact]
    public void AProviderThatThrowsIsSoloAndNotAnException()
    {
        var provider = new PresenceProviderHarness { Throw = true };

        PresenceAnswer answer = Against(provider).Ask(PresenceConsumerHarness.HulgiId);

        Assert.False(answer.Answered);
        Assert.False(answer.Joint);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void SomebodyElsesCompanionIsAnsweredNotRefused()
    {
        var provider = new PresenceProviderHarness();

        PresenceAnswer answer = Against(provider).Ask("somebody-else");

        Assert.True(answer.Answered);
        Assert.False(answer.Known);
        Assert.False(answer.Joint);
    }

    [Fact]
    public void AskingAboutNobodyNeverReachesTheProvider()
    {
        var provider = new PresenceProviderHarness();

        PresenceAnswer answer = Against(provider).Ask(string.Empty);

        Assert.False(answer.Answered);
        Assert.Equal(0, provider.Calls);
    }

    // ---- discovery ---------------------------------------------------------

    [Fact]
    public void ACartographerBelowTheFloorIsHiddenWithAnActionableLine()
    {
        var provider = new PresenceProviderHarness();

        var consumer = PresenceConsumerHarness.Discover(installed: true, new Version(1, 2, 1), provider.Capabilities);

        Assert.Equal("VersionTooLow", consumer.DiscoveryStatus);
        Assert.False(consumer.IsAvailable);
        Assert.Contains(PresenceConsumerHarness.FloorVersion, consumer.DiscoveryLogLine);
        Assert.Contains("surveys keep working", consumer.DiscoveryLogLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AVersionThatCannotBeProvenIsBelowTheFloorNotAGuess()
    {
        var provider = new PresenceProviderHarness();

        var consumer = PresenceConsumerHarness.Discover(installed: true, null, provider.Capabilities);

        Assert.Equal("VersionTooLow", consumer.DiscoveryStatus);
    }

    [Fact]
    public void ACapabilityPropertyThatCouldNotBeReadIsHiddenWithItsReason()
    {
        var consumer = PresenceConsumerHarness.Discover(
            installed: true, Current, null, "reading ConcernedCatCapabilities threw NullReferenceException");

        Assert.Equal("ProbeFailed", consumer.DiscoveryStatus);
        Assert.Contains("NullReferenceException", consumer.DiscoveryDetail);
    }

    [Fact]
    public void APropertyThatIsNotAMapIsProbeFailed()
    {
        var consumer = PresenceConsumerHarness.Discover(installed: true, Current, "not a map");

        Assert.Equal("ProbeFailed", consumer.DiscoveryStatus);
    }

    [Fact]
    public void AMapWithoutThisContractIsAMajorMismatch()
    {
        var consumer = PresenceConsumerHarness.Discover(
            installed: true, Current, PresenceProviderHarness.MapWithoutPresence());

        Assert.Equal("MajorMismatch", consumer.DiscoveryStatus);
        Assert.Contains("concernedcat.presence/1", consumer.DiscoveryDetail);
    }

    [Fact]
    public void AnEntryThatIsNotCallableIsAMajorMismatchRatherThanACrash()
    {
        var consumer = PresenceConsumerHarness.Discover(
            installed: true, Current, PresenceProviderHarness.MapWithWrongShape());

        Assert.Equal("MajorMismatch", consumer.DiscoveryStatus);
    }

    [Fact]
    public void AProbeThatThrowsIsHiddenRatherThanPropagated()
    {
        var consumer = PresenceConsumerHarness.Discover(installed: true, Current, new ThrowingMap());

        Assert.Equal("ProbeFailed", consumer.DiscoveryStatus);
    }

    private sealed class ThrowingMap
    {
        public override string ToString() => throw new InvalidOperationException();
    }
}
