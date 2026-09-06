using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>Evaluates every known-mod probe against a caller-supplied lookup
/// (CT-036). Pure and total: probes not in the registry are never queried at
/// all, which is what makes "unknown mods produce no warnings" true by
/// construction rather than by a filter that could be gotten wrong.</summary>
public static class CompatibilityRegistry
{
    public static IReadOnlyList<ModDetectionResult> Evaluate(
        IReadOnlyList<KnownModProbe> registry, Func<string, (bool Found, string? Version)> lookup)
    {
        var results = new List<ModDetectionResult>(registry.Count);
        foreach (KnownModProbe probe in registry)
        {
            (bool found, string? version) = lookup(probe.Guid);
            results.Add(new ModDetectionResult(probe, found, version));
        }

        return results;
    }
}
