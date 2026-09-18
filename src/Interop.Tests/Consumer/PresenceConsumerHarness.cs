using System;
using TheConcernedCat.Interop.Presence;
using TheConcernedCat.Settlement.Collection.Cooperation;

namespace Interop.Tests.Consumer;

/// <summary>The presence consumer as the test sees it: Foreman's real
/// discovery gate, presence client and survey decision, fed only what the
/// plugin registry would give the adapter. Every answer is summarised into
/// strings and booleans, so what the test asserts is what Foreman would
/// read.</summary>
public sealed class PresenceConsumerHarness
{
    private readonly PresenceDiscovery _discovery;
    private readonly CompanionPresenceClient _client;

    private PresenceConsumerHarness(PresenceDiscovery discovery)
    {
        _discovery = discovery;
        _client = new CompanionPresenceClient(() => discovery, "0.1.0");
    }

    public static PresenceConsumerHarness Discover(
        bool installed, Version? version, object? capabilityProperty, string? readFailure = null)
    {
        PresenceDiscovery discovery = PresenceProviderGate.Evaluate(() => installed
            ? PresenceProviderLookup.Detected(version, capabilityProperty, readFailure)
            : PresenceProviderLookup.NotFound());
        return new PresenceConsumerHarness(discovery);
    }

    public string DiscoveryStatus => _discovery.Status.ToString();

    public string DiscoveryDetail => _discovery.Detail;

    public string DiscoveryLogLine => _discovery.LogLine;

    public bool IsAvailable => _discovery.IsAvailable;

    /// <summary>The floor, so the test states the version boundary in the
    /// consumer's own terms rather than repeating a literal.</summary>
    public static string FloorVersion => PresenceProviderGate.FloorVersion.ToString();

    public static string HulgiId => PresenceContract.Companions.Hulgi;

    public PresenceAnswer Ask(string companionId)
    {
        CompanionPresence presence = _client.Describe(companionId);
        SurveyParticipation participation = SurveyParticipation.Decide(companionId, presence);
        return new PresenceAnswer(
            presence.Answered,
            presence.Known,
            presence.Present,
            presence.Visible,
            presence.Available,
            presence.HasPosition,
            presence.Position.X,
            presence.Position.Y,
            presence.Position.Z,
            participation.IsJoint,
            participation.Reason.ToString(),
            participation.Describe());
    }
}

/// <summary>What the consumer concluded, in BCL types only.</summary>
public sealed class PresenceAnswer
{
    internal PresenceAnswer(
        bool answered, bool known, bool present, bool visible, bool available,
        bool hasPosition, float x, float y, float z,
        bool joint, string soloReason, string sentence)
    {
        Answered = answered;
        Known = known;
        Present = present;
        Visible = visible;
        Available = available;
        HasPosition = hasPosition;
        X = x;
        Y = y;
        Z = z;
        Joint = joint;
        SoloReason = soloReason;
        Sentence = sentence;
    }

    public bool Answered { get; }

    public bool Known { get; }

    public bool Present { get; }

    public bool Visible { get; }

    public bool Available { get; }

    public bool HasPosition { get; }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public bool Joint { get; }

    public string SoloReason { get; }

    public string Sentence { get; }
}
