using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Presence;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>What the plugin registry said about the presence provider. Only BCL
/// values: the adapter has already done the one property read.</summary>
internal sealed class PresenceProviderLookup
{
    private PresenceProviderLookup(bool found, System.Version? version, object? capabilities, string readFailure)
    {
        Found = found;
        Version = version;
        Capabilities = capabilities;
        ReadFailure = readFailure;
    }

    public bool Found { get; }

    /// <summary>Null when the registry carried none. A version that cannot be
    /// proven is below the floor, never a guess.</summary>
    public System.Version? Version { get; }

    public object? Capabilities { get; }

    public string ReadFailure { get; }

    public static PresenceProviderLookup NotFound() => new(false, null, null, string.Empty);

    public static PresenceProviderLookup Detected(
        System.Version? version, object? capabilities, string? readFailure = null) =>
        new(true, version, capabilities, readFailure ?? string.Empty);
}

/// <summary>The outcome of discovering the presence provider for one world
/// session. Only <see cref="CapabilityStatus.Available"/> carries an
/// endpoint.</summary>
internal sealed class PresenceDiscovery
{
    private PresenceDiscovery(
        CapabilityStatus status,
        string providerVersion,
        string detail,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint)
    {
        Status = status;
        ProviderVersion = providerVersion;
        Detail = detail;
        Endpoint = endpoint;
    }

    public CapabilityStatus Status { get; }

    public string ProviderVersion { get; }

    public string Detail { get; }

    public Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? Endpoint { get; }

    public bool IsAvailable => Status == CapabilityStatus.Available && Endpoint != null;

    public static PresenceDiscovery NotProbed { get; } =
        new(CapabilityStatus.Unspecified, "unknown", "not probed yet", null);

    /// <summary>The single line logged per world session.
    ///
    /// Every absent case says the same thing about consequences, because it is
    /// true and because it is the thing a player wants to know: surveying still
    /// works, it is just Thorstein on his own.</summary>
    public string LogLine
    {
        get
        {
            const string Capability = PresenceContract.Id + "/1";
            switch (Status)
            {
                case CapabilityStatus.Available:
                    return "Presence capability " + Capability + " AVAILABLE: " +
                        PresenceProviderGate.ProviderName + " " + ProviderVersion +
                        ". Hulgi can join a survey when he is actually here and free.";
                case CapabilityStatus.Absent:
                    return "Presence capability " + Capability + " absent: " +
                        PresenceProviderGate.ProviderName + " is not installed. Surveys are Thorstein's alone.";
                case CapabilityStatus.VersionTooLow:
                    return "Presence capability " + Capability + " hidden: " +
                        PresenceProviderGate.ProviderName + " " + ProviderVersion + " is older than " +
                        PresenceProviderGate.FloorVersion +
                        ". Update it to survey with Hulgi; surveys keep working without him.";
                case CapabilityStatus.MajorMismatch:
                    return "Presence capability " + Capability + " hidden: " +
                        PresenceProviderGate.ProviderName + " " + ProviderVersion +
                        " offers a different contract (" + Detail +
                        "). One of the two mods needs an update; surveys keep working without him.";
                case CapabilityStatus.ProbeFailed:
                    return "Presence capability " + Capability + " hidden: " +
                        PresenceProviderGate.ProviderName + " " + ProviderVersion +
                        " was found but its capability did not verify (" + Detail +
                        "). Surveys keep working without him.";
                default:
                    return "Presence capability " + Capability + " not probed yet.";
            }
        }
    }

    public static PresenceDiscovery Available(
        string providerVersion,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint) =>
        new(CapabilityStatus.Available, providerVersion, string.Empty,
            endpoint ?? throw new ArgumentNullException(nameof(endpoint)));

    public static PresenceDiscovery Hidden(CapabilityStatus status, string providerVersion, string detail)
    {
        if (status == CapabilityStatus.Available || status == CapabilityStatus.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A hidden discovery names why.");
        }

        return new PresenceDiscovery(status, providerVersion ?? "unknown", detail ?? string.Empty, null);
    }
}

/// <summary>Decides whether the presence provider can be used. Pure: the
/// adapter supplies a lookup over the plugin registry, tests supply fakes, and
/// every path ends in a discovery rather than an exception. The order is fixed
/// — absence, version floor, the property, the contract major — so each hidden
/// state names its actual cause rather than the first check that noticed.
/// </summary>
internal static class PresenceProviderGate
{
    public const string ProviderName = "Concerned Cartographer";

    /// <summary>The oldest Cartographer that can publish
    /// <c>concernedcat.presence/1</c>. The property probe, not this number, is
    /// the real gate; the floor exists only so "too old" gets its own
    /// actionable message instead of looking like a broken install.</summary>
    public static readonly System.Version FloorVersion = new System.Version(1, 2, 2);

    public static PresenceDiscovery Evaluate(Func<PresenceProviderLookup>? lookup)
    {
        if (lookup == null)
        {
            return PresenceDiscovery.Hidden(CapabilityStatus.ProbeFailed, "unknown", "no plugin lookup supplied");
        }

        PresenceProviderLookup? result;
        try
        {
            result = lookup();
        }
        catch (Exception exception)
        {
            return PresenceDiscovery.Hidden(
                CapabilityStatus.ProbeFailed, "unknown", "plugin lookup threw " + exception.GetType().Name);
        }

        if (result == null)
        {
            return PresenceDiscovery.Hidden(CapabilityStatus.ProbeFailed, "unknown", "plugin lookup returned nothing");
        }

        if (!result.Found)
        {
            return PresenceDiscovery.Hidden(CapabilityStatus.Absent, "unknown", "not installed");
        }

        string version = result.Version?.ToString() ?? "unknown";
        if (result.Version == null || result.Version < FloorVersion)
        {
            return PresenceDiscovery.Hidden(CapabilityStatus.VersionTooLow, version, "below " + FloorVersion);
        }

        if (result.ReadFailure.Length > 0)
        {
            return PresenceDiscovery.Hidden(CapabilityStatus.ProbeFailed, version, result.ReadFailure);
        }

        if (result.Capabilities is not IReadOnlyDictionary<string, object> map)
        {
            return PresenceDiscovery.Hidden(
                CapabilityStatus.ProbeFailed, version,
                CapabilityMap.PropertyName + " is missing or is not a read-only string-to-object map");
        }

        if (CapabilityMap.TryGetEndpoint(map, PresenceContract.Id, PresenceContract.Major, out var endpoint) &&
            endpoint != null)
        {
            return PresenceDiscovery.Available(version, endpoint);
        }

        return PresenceDiscovery.Hidden(
            CapabilityStatus.MajorMismatch, version,
            "no " + PresenceContract.Id + "/" + PresenceContract.Major + " endpoint in the map");
    }
}

/// <summary>What one companion's own product said about them, right now.
///
/// <see cref="Available"/> is the only field credit may depend on, and it is
/// the provider's answer rather than a conclusion drawn here: a consumer that
/// derived availability from the other three would be deciding for itself that
/// somebody is free.</summary>
internal readonly struct CompanionPresence
{
    private CompanionPresence(
        bool answered, bool known, bool present, bool visible, bool available,
        SitePoint position, bool hasPosition, string detail)
    {
        Answered = answered;
        Known = known;
        Present = present;
        Visible = visible;
        Available = available;
        Position = position;
        HasPosition = hasPosition;
        Detail = detail;
    }

    /// <summary>The provider answered at all. False covers every way of not
    /// knowing — not installed, no endpoint, an exception, a malformed reply —
    /// because they have the same consequence.</summary>
    public bool Answered { get; }

    public bool Known { get; }

    public bool Present { get; }

    public bool Visible { get; }

    /// <summary>Here, shown, and not in the middle of something.</summary>
    public bool Available { get; }

    public SitePoint Position { get; }

    public bool HasPosition { get; }

    public string Detail { get; }

    /// <summary>Nobody answered. Every flag false, which is the safe direction:
    /// an unanswered presence is an absent companion, so nothing is credited.
    /// </summary>
    public static CompanionPresence Unknown(string detail) =>
        new(false, false, false, false, false, default, false, detail ?? string.Empty);

    public static CompanionPresence From(IReadOnlyDictionary<string, string>? reply)
    {
        if (reply == null)
        {
            return Unknown("no reply");
        }

        string? status = PresenceContract.Read(reply, PresenceContract.Keys.Status);
        if (!string.Equals(status, PresenceContract.Statuses.Ok, StringComparison.Ordinal))
        {
            return Unknown(status ?? "no status");
        }

        bool hasPosition = PresenceContract.TryPoint(
            PresenceContract.Read(reply, PresenceContract.Keys.Position), out float x, out float y, out float z);

        return new CompanionPresence(
            answered: true,
            known: PresenceContract.ReadBool(reply, PresenceContract.Keys.Known),
            present: PresenceContract.ReadBool(reply, PresenceContract.Keys.Present),
            visible: PresenceContract.ReadBool(reply, PresenceContract.Keys.Visible),
            available: PresenceContract.ReadBool(reply, PresenceContract.Keys.Available),
            position: new SitePoint(x, y, z),
            hasPosition: hasPosition,
            detail: string.Empty);
    }
}

/// <summary>Asks the presence provider about one companion, and never throws.
///
/// There is no caching beyond the caller's own polling interval: presence is
/// exactly the thing that changes while nobody is looking, and a remembered
/// "he was here a minute ago" is how somebody gets credited for a survey they
/// walked away from.</summary>
internal sealed class CompanionPresenceClient
{
    private readonly Func<PresenceDiscovery> _discovery;
    private readonly string _consumerVersion;

    internal CompanionPresenceClient(Func<PresenceDiscovery> discovery, string consumerVersion)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _consumerVersion = string.IsNullOrEmpty(consumerVersion) ? "unknown" : consumerVersion;
    }

    public CompanionPresence Describe(string companionId)
    {
        if (string.IsNullOrEmpty(companionId))
        {
            return CompanionPresence.Unknown("no companion id");
        }

        PresenceDiscovery discovery;
        try
        {
            discovery = _discovery() ?? PresenceDiscovery.NotProbed;
        }
        catch (Exception exception)
        {
            return CompanionPresence.Unknown("discovery threw " + exception.GetType().Name);
        }

        if (!discovery.IsAvailable)
        {
            return CompanionPresence.Unknown(discovery.Detail.Length > 0 ? discovery.Detail : "no provider");
        }

        return CompanionPresence.From(CapabilityMap.TryCall(
            discovery.Endpoint,
            PresenceContract.Request(
                PresenceContract.Ops.DescribeCompanion, companionId, _consumerVersion)));
    }
}
