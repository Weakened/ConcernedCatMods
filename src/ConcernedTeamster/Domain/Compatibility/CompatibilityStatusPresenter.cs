using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>Composes the compatibility status surface text (CT-036) — the
/// log banner lines and the panel's rows are the exact same composition, so
/// "the status surface reflects exactly the applied policies" holds by
/// sharing one code path rather than two that could drift.</summary>
public static class CompatibilityStatusPresenter
{
    /// <summary>One line per actually-detected known mod. Not-found known
    /// mods and anything outside the registry produce nothing — silence is
    /// the default.</summary>
    public static IReadOnlyList<string> ComposeDetectedLines(IReadOnlyList<ModDetectionResult> results)
    {
        return results
            .Where(result => result.Found)
            .Select(result => TeamsterStrings.Format(
                "compat.line",
                result.Probe.DisplayName,
                VersionText(result.DetectedVersion),
                PolicyWord(result.Probe.Policy),
                result.Probe.Description))
            .ToList();
    }

    /// <summary>The empty-state line when nothing in the registry was
    /// detected (including when the registry itself is empty).</summary>
    public static string ComposeNoneDetectedLine() => TeamsterStrings.Get("compat.noneDetected");

    private static string VersionText(string? detectedVersion)
    {
        return string.IsNullOrEmpty(detectedVersion)
            ? TeamsterStrings.Get("compat.versionUnknown")
            : "v" + detectedVersion;
    }

    private static string PolicyWord(CompatibilityPolicy policy)
    {
        return policy switch
        {
            CompatibilityPolicy.Coexist => TeamsterStrings.Get("compat.policyCoexist"),
            CompatibilityPolicy.Adapt => TeamsterStrings.Get("compat.policyAdapt"),
            CompatibilityPolicy.Warn => TeamsterStrings.Get("compat.policyWarn"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(policy), policy, "Unhandled CompatibilityPolicy value — add a wording case above."),
        };
    }
}
