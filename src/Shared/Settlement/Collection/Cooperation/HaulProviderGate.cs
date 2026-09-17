using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>What the plugin registry said about the haul provider, gathered by
/// the consumer's adapter at its first update of a world session. Only BCL
/// values: the adapter has already done the one property read.</summary>
internal sealed class HaulProviderLookup
{
    private HaulProviderLookup(bool found, System.Version? version, object? capabilities, string readFailure)
    {
        Found = found;
        Version = version;
        Capabilities = capabilities;
        ReadFailure = readFailure;
    }

    public bool Found { get; }

    /// <summary>Null when the registry carried none: a version that cannot be
    /// proven is below the floor, never a guess.</summary>
    public System.Version? Version { get; }

    /// <summary>The value of the provider's
    /// <see cref="CapabilityMap.PropertyName"/> property, or null.</summary>
    public object? Capabilities { get; }

    /// <summary>Why the property could not be read, when it could not.</summary>
    public string ReadFailure { get; }

    public static HaulProviderLookup NotFound() => new HaulProviderLookup(false, null, null, string.Empty);

    public static HaulProviderLookup Detected(System.Version? version, object? capabilities, string? readFailure = null) =>
        new HaulProviderLookup(true, version, capabilities, readFailure ?? string.Empty);
}

/// <summary>The outcome of discovering the haul provider for one world
/// session. Only <see cref="CapabilityStatus.Available"/> carries an endpoint.
/// </summary>
internal sealed class HaulDiscovery
{
    private HaulDiscovery(
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

    /// <summary>Not probed yet: nothing is assumed, nothing is offered.</summary>
    public static HaulDiscovery NotProbed { get; } =
        new HaulDiscovery(CapabilityStatus.Unspecified, "unknown", "not probed yet", null);

    /// <summary>The single line logged per world session.</summary>
    public string LogLine
    {
        get
        {
            string capability = HaulContract.Id + "/" + HaulContract.Major;
            switch (Status)
            {
                case CapabilityStatus.Available:
                    return "Haul capability " + capability + " AVAILABLE: " + HaulProviderGate.ProviderName + " " +
                        ProviderVersion + ". Cooperative orders can use Gunnar and an assigned cart.";
                case CapabilityStatus.Absent:
                    return "Haul capability " + capability + " absent: " + HaulProviderGate.ProviderName +
                        " is not installed. Collection orders run solo.";
                case CapabilityStatus.VersionTooLow:
                    return "Haul capability " + capability + " hidden: " + HaulProviderGate.ProviderName + " " +
                        ProviderVersion + " is older than " + HaulProviderGate.FloorVersion +
                        ". Update it to collect with Gunnar; solo orders keep working.";
                case CapabilityStatus.MajorMismatch:
                    return "Haul capability " + capability + " hidden: " + HaulProviderGate.ProviderName + " " +
                        ProviderVersion + " offers a different contract (" + Detail +
                        "). One of the two mods needs an update; solo orders keep working.";
                case CapabilityStatus.ProbeFailed:
                    return "Haul capability " + capability + " hidden: " + HaulProviderGate.ProviderName + " " +
                        ProviderVersion + " was found but its capability did not verify (" + Detail +
                        "). Solo orders keep working.";
                default:
                    return "Haul capability " + capability + " not probed yet.";
            }
        }
    }

    public static HaulDiscovery Available(
        string providerVersion,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> endpoint) =>
        new HaulDiscovery(
            CapabilityStatus.Available, providerVersion, string.Empty,
            endpoint ?? throw new ArgumentNullException(nameof(endpoint)));

    public static HaulDiscovery Hidden(CapabilityStatus status, string providerVersion, string detail)
    {
        if (status == CapabilityStatus.Available || status == CapabilityStatus.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A hidden discovery names why.");
        }

        return new HaulDiscovery(status, providerVersion ?? "unknown", detail ?? string.Empty, null);
    }
}

/// <summary>Decides whether the haul provider can be used (DECISIONS.md D7).
/// Pure: the adapter supplies a lookup over the plugin registry, tests supply
/// fakes, and every path ends in a discovery, never an exception. Order is
/// fixed - absence, version floor, the property, the contract major - so each
/// hidden state names its actual cause.</summary>
internal static class HaulProviderGate
{
    public const string ProviderName = "Concerned Teamster";

    /// <summary>The oldest Teamster that can publish
    /// <c>concernedcat.haul/1</c>: the first release after 1.0.4, which has no
    /// capability property at all. The property probe, not this number, is
    /// the forward-compatibility gate; the floor only turns "too old" into its
    /// own actionable message.</summary>
    public static readonly System.Version FloorVersion = new System.Version(1, 0, 5);

    public static HaulDiscovery Evaluate(Func<HaulProviderLookup>? lookup)
    {
        if (lookup == null)
        {
            return HaulDiscovery.Hidden(CapabilityStatus.ProbeFailed, "unknown", "no plugin lookup supplied");
        }

        HaulProviderLookup? result;
        try
        {
            result = lookup();
        }
        catch (Exception exception)
        {
            return HaulDiscovery.Hidden(
                CapabilityStatus.ProbeFailed, "unknown", "plugin lookup threw " + exception.GetType().Name);
        }

        if (result == null)
        {
            return HaulDiscovery.Hidden(CapabilityStatus.ProbeFailed, "unknown", "plugin lookup returned nothing");
        }

        if (!result.Found)
        {
            return HaulDiscovery.Hidden(CapabilityStatus.Absent, "unknown", "not installed");
        }

        string version = result.Version?.ToString() ?? "unknown";
        if (result.Version == null || result.Version < FloorVersion)
        {
            return HaulDiscovery.Hidden(CapabilityStatus.VersionTooLow, version, "below " + FloorVersion);
        }

        if (result.ReadFailure.Length > 0)
        {
            return HaulDiscovery.Hidden(CapabilityStatus.ProbeFailed, version, result.ReadFailure);
        }

        if (!(result.Capabilities is IReadOnlyDictionary<string, object> map))
        {
            return HaulDiscovery.Hidden(
                CapabilityStatus.ProbeFailed, version,
                CapabilityMap.PropertyName + " is missing or is not a read-only string-to-object map");
        }

        if (CapabilityMap.TryGetEndpoint(map, HaulContract.Id, HaulContract.Major, out var endpoint) && endpoint != null)
        {
            return HaulDiscovery.Available(version, endpoint);
        }

        string key = CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major);
        if (map.ContainsKey(key))
        {
            return HaulDiscovery.Hidden(CapabilityStatus.ProbeFailed, version, key + " has an unexpected shape");
        }

        var otherMajors = new List<string>();
        foreach (string published in map.Keys)
        {
            if (published != null && published.StartsWith(HaulContract.Id + "/", StringComparison.Ordinal))
            {
                otherMajors.Add(published);
            }
        }

        if (otherMajors.Count > 0)
        {
            otherMajors.Sort(StringComparer.Ordinal);
            return HaulDiscovery.Hidden(CapabilityStatus.MajorMismatch, version, string.Join(", ", otherMajors.ToArray()));
        }

        return HaulDiscovery.Hidden(CapabilityStatus.ProbeFailed, version, key + " is not published");
    }
}
