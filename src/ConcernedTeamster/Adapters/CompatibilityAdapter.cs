using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>The only code that asks BepInEx about other installed mods for
/// compatibility awareness (CT-036) — mirrors <see cref="CartographerCapability"/>'s
/// probe shape, generalized to a whole registry instead of one specific mod.
/// Detects each <see cref="Domain.Compatibility.CompatibilityKnownMods.Registry"/>
/// entry by GUID via <c>Chainloader.PluginInfos</c>; policies and
/// descriptions are pure data, so adding a mod never touches this adapter
/// or any feature code.</summary>
public static class CompatibilityAdapter
{
    private static IReadOnlyList<ModDetectionResult>? _results;

    /// <summary>The probe outcome, or null before <see cref="EnsureProbed"/>
    /// ran.</summary>
    public static IReadOnlyList<ModDetectionResult>? Results => _results;

    /// <summary>CT-037: whether cart-mass-calibrated advice (warnings, stuck
    /// diagnosis, recovery guidance, route bottlenecks) can be trusted right
    /// now. Before the first probe (<see cref="Results"/> is null) this is
    /// true — fail-open until proven otherwise, so nothing gates on a probe
    /// that simply has not run yet.</summary>
    public static bool CartMassAdviceReliable =>
        CompatibilityAdvisoryGate.CartMassAdviceReliable(_results ?? System.Array.Empty<ModDetectionResult>());

    /// <summary>Runs the probe once per session and logs one line per
    /// actually-detected known mod (or one "none detected" line otherwise).
    /// Called from the plugin's first Update tick — BepInEx fills
    /// PluginInfos in load order, so probing from Awake could misread a
    /// not-yet-loaded mod as absent, exactly as CT-021 found for
    /// Cartographer.</summary>
    public static IReadOnlyList<ModDetectionResult> EnsureProbed(ManualLogSource logger)
    {
        if (_results is not null)
        {
            return _results;
        }

        _results = CompatibilityRegistry.Evaluate(CompatibilityKnownMods.Registry, Lookup);

        IReadOnlyList<string> lines = CompatibilityStatusPresenter.ComposeDetectedLines(_results);
        if (lines.Count == 0)
        {
            logger.LogInfo(CompatibilityStatusPresenter.ComposeNoneDetectedLine());
        }
        else
        {
            foreach (string line in lines)
            {
                logger.LogInfo(line);
            }
        }

        return _results;
    }

    private static (bool Found, string? Version) Lookup(string guid)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || info is null)
        {
            return (false, null);
        }

        return (true, info.Metadata?.Version?.ToString());
    }
}
