using System;
using System.Collections.Generic;
using TheConcernedCat.Interop;
using TheConcernedCat.Interop.Haul;

namespace TheConcernedCat.ConcernedSteward.Domain.Interop;

/// <summary>What the plugin registry said about a bulk-restock provider,
/// gathered by the adapter's single property read. Only BCL values cross.
/// </summary>
internal sealed class RestockProviderLookup
{
    private RestockProviderLookup(bool found, System.Version? version, object? capabilities, string readFailure)
    {
        Found = found;
        ProviderVersion = version;
        Capabilities = capabilities;
        ReadFailure = readFailure;
    }

    public bool Found { get; }

    /// <summary>Null when the registry carried none. <b>A version that cannot
    /// be proven is below the floor</b>, never a guess.</summary>
    public System.Version? ProviderVersion { get; }

    /// <summary>The value of the provider's capability-map property, or null.
    /// </summary>
    public object? Capabilities { get; }

    public string ReadFailure { get; }

    public static RestockProviderLookup NotFound() =>
        new RestockProviderLookup(false, null, null, string.Empty);

    public static RestockProviderLookup Detected(
        System.Version? version, object? capabilities, string? readFailure = null) =>
        new RestockProviderLookup(true, version, capabilities, readFailure ?? string.Empty);
}

/// <summary>What one probe found.</summary>
internal sealed class RestockDiscovery
{
    internal RestockDiscovery(CapabilityStatus status, string detail)
    {
        Status = status;
        Detail = detail ?? string.Empty;
    }

    public CapabilityStatus Status { get; }

    public string Detail { get; }

    public bool IsAvailable => Status == CapabilityStatus.Available;

    internal static RestockDiscovery NotProbed { get; } =
        new RestockDiscovery(CapabilityStatus.Unspecified, "not looked for yet");

    /// <summary>One line for the log, once per world session.</summary>
    public string LogLine => "Bulk restocking: " + Detail + ".";

    public override string ToString() => LogLine;
}

/// <summary>Whether somebody else could bring wood to the Steward's depot.
///
/// <b>This is a seam and a probe, and #340 builds nothing behind it.</b> A
/// future Steward could ask a haul provider to restock a depot that has run
/// dry; today he notices whether one is there and says so. The probe exists now
/// rather than later for one reason: the shape of "what happens when the other
/// mod is absent, or speaks a different version" is a property of the whole
/// design, and it is much cheaper to fix while there is nothing behind it.
///
/// <b>The answer never gates him.</b> Absent, too old, mismatched, broken — the
/// Steward tends fires from the marked chest exactly the same way in every one
/// of them. Nothing in <c>UpkeepLoop</c> consults this, and that is the point:
/// installing this mod must not change what any other mod does, and not
/// installing another mod must not change what this one does.
///
/// <b>No compile-time reference to any sibling product.</b> The provider is
/// found by a GUID string and one public property name; only BCL types cross
/// the boundary. <c>HaulContract</c> is shared source in this product's own
/// assembly, not a reference to Teamster.</summary>
internal static class RestockProviderGate
{
    /// <summary>The oldest provider whose <c>concernedcat.haul/1</c> endpoint
    /// this build will speak to.
    ///
    /// <b><c>System.Version</c>, spelled out.</b> Valheim declares a global
    /// <c>Version</c> class of its own, so the unqualified name binds to the
    /// game's inside this assembly and the code does not compile. It is a
    /// three-second fix and an easy one to reintroduce, which is why it is
    /// written down here.
    ///
    /// Matching the floor the Foreman consumer already settled on. A version
    /// below it, or one the registry could not state, is refused rather than
    /// tried — a handshake with an older contract is exactly the kind of thing
    /// that appears to work until it moves something.</summary>
    public static readonly System.Version FloorVersion = new System.Version(1, 0, 5);

    public static RestockDiscovery Evaluate(Func<RestockProviderLookup> lookup)
    {
        RestockProviderLookup found;
        try
        {
            found = lookup() ?? RestockProviderLookup.NotFound();
        }
        catch (Exception exception)
        {
            return new RestockDiscovery(
                CapabilityStatus.ProbeFailed,
                "looking for a hauler failed (" + exception.GetType().Name + "), so the Steward " +
                "carries his own wood");
        }

        if (!found.Found)
        {
            return new RestockDiscovery(
                CapabilityStatus.Absent,
                "no hauler is installed, so the Steward carries his own wood");
        }

        if (found.ProviderVersion == null || found.ProviderVersion < FloorVersion)
        {
            return new RestockDiscovery(
                CapabilityStatus.VersionTooLow,
                "a hauler is installed but it is " +
                (found.ProviderVersion == null ? "of an unknown version" : found.ProviderVersion.ToString()) +
                ", older than the " + FloorVersion + " this build speaks to, so the Steward " +
                "carries his own wood");
        }

        if (found.Capabilities is not IReadOnlyDictionary<string, object> map)
        {
            return new RestockDiscovery(
                CapabilityStatus.ProbeFailed,
                "a hauler is installed but publishes nothing this build understands" +
                (found.ReadFailure.Length == 0 ? string.Empty : " (" + found.ReadFailure + ")") +
                ", so the Steward carries his own wood");
        }

        if (!CapabilityMap.TryGetEndpoint(map, HaulContract.Id, HaulContract.Major, out _))
        {
            return new RestockDiscovery(
                CapabilityStatus.MajorMismatch,
                "a hauler is installed but speaks no " +
                CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major) +
                ", so the Steward carries his own wood");
        }

        return new RestockDiscovery(
            CapabilityStatus.Available,
            "a hauler is available for " + CapabilityMap.KeyFor(HaulContract.Id, HaulContract.Major) +
            ". The Steward still carries his own wood in this build; bulk restocking is not " +
            "built yet");
    }
}
