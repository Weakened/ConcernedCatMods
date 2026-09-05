using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>Decides Cartographer availability from a plugin lookup (CT-021).
/// Pure decision logic: the adapter supplies a lookup over BepInEx, tests
/// supply fakes, and every path — absent, version mismatch, probe failure,
/// available — ends in a report, never an exception. Check order is fixed:
/// absence first, then the version floor, then the member probe, so each
/// hidden state names its actual cause.</summary>
public static class CartographerGate
{
    public static CartographerCapabilityReport Evaluate(Func<CartographerLookup>? lookup)
    {
        if (lookup is null)
        {
            return CartographerCapabilityReport.ProbeFailed("unknown", "no plugin lookup supplied");
        }

        CartographerLookup? result;
        try
        {
            result = lookup();
        }
        catch (Exception exception)
        {
            return CartographerCapabilityReport.ProbeFailed(
                "unknown", $"plugin lookup threw {exception.GetType().Name}");
        }

        if (result is null)
        {
            return CartographerCapabilityReport.ProbeFailed("unknown", "plugin lookup returned nothing");
        }

        if (!result.Found)
        {
            return CartographerCapabilityReport.Absent();
        }

        string detectedVersion = result.Version?.ToString() ?? "unknown";
        if (result.Version is null || result.Version < CartographerContract.FloorVersion)
        {
            // A registry entry without a version is treated as a mismatch:
            // the floor cannot be proven, and proving it is the whole point.
            return CartographerCapabilityReport.VersionTooLow(detectedVersion);
        }

        if (result.Instance is null)
        {
            return CartographerCapabilityReport.ProbeFailed(
                detectedVersion, "plugin instance is not available");
        }

        IReadOnlyList<GameMemberRequirement> requirements;
        try
        {
            requirements = CartographerContract.BuildRequirements(result.Instance.GetType());
        }
        catch (Exception exception)
        {
            return CartographerCapabilityReport.ProbeFailed(
                detectedVersion, $"contract resolution threw {exception.GetType().Name}");
        }

        GameCapabilityReport probe = GameMemberProbe.Probe(requirements);
        if (!probe.Enabled)
        {
            return CartographerCapabilityReport.ProbeFailed(
                detectedVersion, "missing " + string.Join(", ", probe.MissingMembers));
        }

        return CartographerCapabilityReport.Available(detectedVersion, probe.VerifiedMembers.Count);
    }
}
