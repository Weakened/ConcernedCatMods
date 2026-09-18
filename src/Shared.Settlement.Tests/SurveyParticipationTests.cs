using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Presence;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Cooperation;

namespace Shared.Settlement.Tests;

/// <summary>#317, SPEC GATHER-03. The cross-assembly half of this is
/// <c>PresenceCapabilityExchangeTests</c>; these are the decisions either side
/// of it — how an answer becomes a survey party, and how often the question
/// gets asked.</summary>
public sealed class SurveyParticipationTests
{
    private const string Hulgi = "hulgi";

    private static IReadOnlyDictionary<string, string> Ok(
        bool known = true, bool present = true, bool visible = true, bool available = true) =>
        PresenceResponder.Describe(
            Hulgi, new PresenceFacts(known, present, visible, available), "1.2.2");

    private static CompanionPresence Presence(
        bool known = true, bool present = true, bool visible = true, bool available = true) =>
        CompanionPresence.From(Ok(known, present, visible, available));

    // ---- what an answer means ---------------------------------------------

    [Fact]
    public void HereAndFreeIsJoint()
    {
        SurveyParticipation decision = SurveyParticipation.Decide(Hulgi, Presence());

        Assert.True(decision.IsJoint);
        Assert.Equal(SoloReason.None, decision.Reason);
    }

    // The expected reason is a string because the enum is internal to the
    // shared source this project compiles, and xunit needs a public signature.
    [Theory]
    [InlineData(false, true, true, true, "NotKnown")]
    [InlineData(true, false, true, true, "NotHere")]
    [InlineData(true, true, false, true, "NotHere")]
    [InlineData(true, true, true, false, "Busy")]
    public void EveryWayOfNotBeingInItHasItsOwnReason(
        bool known, bool present, bool visible, bool available, string expected)
    {
        SurveyParticipation decision =
            SurveyParticipation.Decide(Hulgi, Presence(known, present, visible, available));

        Assert.False(decision.IsJoint);
        Assert.Equal(expected, decision.Reason.ToString());
    }

    [Fact]
    public void AnUnansweredPresenceIsSoloAndSaysSoWasNobodyToAsk()
    {
        SurveyParticipation decision =
            SurveyParticipation.Decide(Hulgi, CompanionPresence.Unknown("not installed"));

        Assert.False(decision.IsJoint);
        Assert.Equal(SoloReason.NoProvider, decision.Reason);
    }

    [Fact]
    public void AReplyThatIsNotOkIsNotAnAnswer()
    {
        IReadOnlyDictionary<string, string> refused = PresenceContract.Reply(
            PresenceContract.Statuses.Unavailable, "1.2.2", "no runtime");

        Assert.False(CompanionPresence.From(refused).Answered);
        Assert.False(SurveyParticipation.Decide(Hulgi, CompanionPresence.From(refused)).IsJoint);
    }

    [Fact]
    public void TheReasonsAreOrderedSoTheSentenceNamesTheFirstTrueThing()
    {
        // Nothing is true at all. "He is busy" would be a strange thing to tell
        // somebody who has never met him.
        SurveyParticipation decision =
            SurveyParticipation.Decide(Hulgi, Presence(known: false, present: false, visible: false, available: false));

        Assert.Equal(SoloReason.NotKnown, decision.Reason);
    }

    [Fact]
    public void EveryPartyAndReasonHasASentenceAndNoneOfThemSaysBug()
    {
        foreach (SoloReason reason in Enum.GetValues(typeof(SoloReason)))
        {
            CompanionPresence presence = reason switch
            {
                SoloReason.None => Presence(),
                SoloReason.NoProvider => CompanionPresence.Unknown("none"),
                SoloReason.NotKnown => Presence(known: false),
                SoloReason.NotHere => Presence(present: false),
                _ => Presence(available: false),
            };

            string sentence = SurveyParticipation.Decide(Hulgi, presence).Describe();
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("bug", sentence, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheDisplayNameIsTheOtherProductsAndThisLayerOnlyKnowsTheId()
    {
        SurveyParticipation decision = SurveyParticipation.Decide(Hulgi, Presence());

        Assert.Contains("Hulgi", decision.Describe());
        Assert.Contains("Hulgen", decision.Describe("Hulgen"));
        Assert.Equal(Hulgi, decision.CompanionId);
    }

    // ---- how often it is asked --------------------------------------------

    private sealed class Clock
    {
        public float Now { get; set; }
    }

    private sealed class CountingEndpoint
    {
        public int Calls { get; private set; }

        public bool Available { get; set; } = true;

        public PresenceDiscovery Discovery => PresenceDiscovery.Available("1.2.2", Call);

        private IReadOnlyDictionary<string, string> Call(IReadOnlyDictionary<string, string> request)
        {
            Calls++;
            return Ok(available: Available);
        }
    }

    [Fact]
    public void ItIsOnlyAskedWhileAnOrderIsActuallySurveying()
    {
        var endpoint = new CountingEndpoint();
        var clock = new Clock();
        var companions = new SurveyCompanions(() => endpoint.Discovery, "0.1.0", () => clock.Now);

        foreach (CollectionOrderState state in Enum.GetValues(typeof(CollectionOrderState)))
        {
            if (state == CollectionOrderState.Surveying)
            {
                continue;
            }

            clock.Now += 10f;
            Assert.False(companions.IsHelping(state));
        }

        Assert.Equal(0, endpoint.Calls);
    }

    [Fact]
    public void WithinTheIntervalTheLastAnswerStandsRatherThanBeingReAsked()
    {
        var endpoint = new CountingEndpoint();
        var clock = new Clock { Now = 100f };
        var companions = new SurveyCompanions(() => endpoint.Discovery, "0.1.0", () => clock.Now);

        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));
        clock.Now += 0.1f;
        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));
        clock.Now += 0.1f;
        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));

        Assert.Equal(1, endpoint.Calls);
    }

    [Fact]
    public void WalkingAwayShowsUpOnTheNextAsk()
    {
        var endpoint = new CountingEndpoint();
        var clock = new Clock { Now = 100f };
        var companions = new SurveyCompanions(() => endpoint.Discovery, "0.1.0", () => clock.Now);

        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));

        endpoint.Available = false;
        clock.Now += 1f;

        Assert.False(companions.IsHelping(CollectionOrderState.Surveying));
        Assert.Equal(SoloReason.Busy, companions.Participation.Reason);
        Assert.Equal(2, endpoint.Calls);
    }

    [Fact]
    public void LeavingTheSurveyStateForgetsTheAnswer()
    {
        var endpoint = new CountingEndpoint();
        var clock = new Clock { Now = 100f };
        var companions = new SurveyCompanions(() => endpoint.Discovery, "0.1.0", () => clock.Now);

        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));
        Assert.False(companions.IsHelping(CollectionOrderState.Collecting));

        Assert.False(companions.Participation.IsJoint);
    }

    [Fact]
    public void AWorldReloadDoesNotStartOutBelievingSomebodyIsStillStandingThere()
    {
        var endpoint = new CountingEndpoint();
        var clock = new Clock { Now = 100f };
        var companions = new SurveyCompanions(() => endpoint.Discovery, "0.1.0", () => clock.Now);

        Assert.True(companions.IsHelping(CollectionOrderState.Surveying));

        companions.Forget();

        Assert.False(companions.Participation.IsJoint);
        Assert.Equal(SoloReason.NoProvider, companions.Participation.Reason);
    }

    [Fact]
    public void ADiscoveryThatThrowsIsSoloRatherThanAnException()
    {
        var clock = new Clock { Now = 100f };
        var companions = new SurveyCompanions(
            () => throw new InvalidOperationException("no registry"), "0.1.0", () => clock.Now);

        Assert.False(companions.IsHelping(CollectionOrderState.Surveying));
        Assert.Equal(SoloReason.NoProvider, companions.Participation.Reason);
    }

    // ---- the wire format ---------------------------------------------------

    [Fact]
    public void APositionRoundTripsExactly()
    {
        IReadOnlyDictionary<string, string> reply = PresenceResponder.Describe(
            Hulgi, new PresenceFacts(true, true, true, true, true, -1234.5f, 31.25f, 0.125f), "1.2.2");

        CompanionPresence presence = CompanionPresence.From(reply);

        Assert.True(presence.HasPosition);
        Assert.Equal(-1234.5f, presence.Position.X);
        Assert.Equal(31.25f, presence.Position.Y);
        Assert.Equal(0.125f, presence.Position.Z);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1;2")]
    [InlineData("1;2;3;4")]
    [InlineData("one;two;three")]
    [InlineData("1;two;3")]
    public void APositionThatCannotBeReadIsNotAPositionNearTheOrigin(string raw)
    {
        Assert.False(PresenceContract.TryPoint(raw, out _, out _, out _));
    }

    [Fact]
    public void AMalformedRequestIsAnswered_NotThrown()
    {
        Assert.Equal(
            PresenceContract.Statuses.BadRequest,
            PresenceResponder.Answer(null, "1.2.2", true, _ => PresenceFacts.Nobody)[
                PresenceContract.Keys.Status]);

        Assert.Equal(
            PresenceContract.Statuses.BadRequest,
            PresenceResponder.Answer(
                new Dictionary<string, string> { [PresenceContract.Keys.Op] = string.Empty },
                "1.2.2", true, _ => PresenceFacts.Nobody)[PresenceContract.Keys.Status]);
    }

    [Fact]
    public void AnOpThisProviderDoesNotKnowIsUnsupportedAndNamesIt()
    {
        IReadOnlyDictionary<string, string> reply = PresenceResponder.Answer(
            PresenceContract.Request("fetchHim", Hulgi, "0.1.0"), "1.2.2", true, _ => PresenceFacts.Nobody);

        Assert.Equal(PresenceContract.Statuses.Unsupported, reply[PresenceContract.Keys.Status]);
        Assert.Equal("fetchHim", reply[PresenceContract.Keys.Reason]);
    }

    [Fact]
    public void AFutureMajorIsRefusedRatherThanAnsweredWithMajorOnesShape()
    {
        var request = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PresenceContract.Keys.Op] = PresenceContract.Ops.DescribeCompanion,
            [PresenceContract.Keys.ContractMajor] = "2",
            [PresenceContract.Keys.CompanionId] = Hulgi,
        };

        Assert.Equal(
            PresenceContract.Statuses.Unsupported,
            PresenceResponder.Answer(request, "1.2.2", true, _ => PresenceFacts.Nobody)[
                PresenceContract.Keys.Status]);
    }

    [Fact]
    public void HelloCarriesNoCompanionAndNeedsNone()
    {
        IReadOnlyDictionary<string, string> reply = PresenceResponder.Answer(
            PresenceContract.Request(PresenceContract.Ops.Hello, null, "0.1.0"),
            "1.2.2", true, _ => throw new InvalidOperationException("hello must not ask for facts"));

        Assert.Equal(PresenceContract.Statuses.Ok, reply[PresenceContract.Keys.Status]);
        Assert.Equal("1.2.2", reply[PresenceContract.Keys.ProviderVersion]);
    }

    [Fact]
    public void WithNoRuntimeTheFactsAreNeverEvenAskedFor()
    {
        IReadOnlyDictionary<string, string> reply = PresenceResponder.Answer(
            PresenceContract.Request(PresenceContract.Ops.DescribeCompanion, Hulgi, "0.1.0"),
            "1.2.2", ready: false, facts: _ => throw new InvalidOperationException("must not be asked"));

        Assert.Equal(PresenceContract.Statuses.Unavailable, reply[PresenceContract.Keys.Status]);
    }

    [Fact]
    public void TheContractKeyIsTheOneTheMapIsIndexedBy()
    {
        Assert.Equal(
            "concernedcat.presence/1",
            CapabilityMap.KeyFor(PresenceContract.Id, PresenceContract.Major));
    }
}
