using System.Collections.Generic;
using System.Linq;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>The precedence policy (CT-037): decides whether Teamster's
/// LoadModel-derived advice (warnings, stuck diagnostics, recovery
/// guidance, route bottlenecks) is still trustworthy. Generic over
/// <see cref="CompatibilityAffectedAspect"/>, never a specific mod's GUID or
/// name — a future registry entry tagged
/// <see cref="CompatibilityAffectedAspect.CartMassOrPhysics"/> gates every
/// consumer of this method automatically, with no feature code to touch.</summary>
public static class CompatibilityAdvisoryGate
{
    /// <summary>False when any actually-detected known mod affects cart mass
    /// or physics — every calibrated verdict assumes vanilla physics and
    /// must not be presented as truth once that assumption breaks.</summary>
    public static bool CartMassAdviceReliable(IReadOnlyList<ModDetectionResult> results)
    {
        return !results.Any(result =>
            result.Found && result.Probe.AffectedAspect == CompatibilityAffectedAspect.CartMassOrPhysics);
    }
}
