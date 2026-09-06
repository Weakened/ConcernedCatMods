using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>Evaluates every known-mod probe against a caller-supplied lookup
/// (CT-036). Pure and total: probes not in the registry are never queried at
/// all, which is what makes "unknown mods produce no warnings" true by
/// construction rather than by a filter that could be gotten wrong.</summary>
public static class CompatibilityRegistry
{
    /// <summary>A lookup failure for one probe (for example a malformed
    /// third-party plugin metadata entry) fails closed to "not found" for
    /// that probe only — mirroring <c>CartographerGate.Evaluate</c>'s
    /// fail-closed shape, but per-probe rather than all-or-nothing, since
    /// one bad entry must not blind every other registered probe for the
    /// rest of the session.</summary>
    public static IReadOnlyList<ModDetectionResult> Evaluate(
        IReadOnlyList<KnownModProbe> registry, Func<string, (bool Found, string? Version)> lookup)
    {
        var results = new List<ModDetectionResult>(registry.Count);
        foreach (KnownModProbe probe in registry)
        {
            bool found = false;
            string? version = null;
            try
            {
                (found, version) = lookup(probe.Guid);
            }
            catch
            {
                // Fail closed for this probe only; the rest of the
                // registry still gets a fair evaluation.
            }

            results.Add(new ModDetectionResult(probe, found, version));
        }

        return results;
    }
}
