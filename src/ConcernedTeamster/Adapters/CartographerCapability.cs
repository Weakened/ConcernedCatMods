using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>The only code that asks BepInEx about Concerned Cartographer
/// (CT-021). Detects the plugin by GUID and version through
/// Chainloader.PluginInfos, hands the outcome to the pure domain gate, logs
/// exactly one INFO line per session, and exposes read-only route access for
/// later leaves. There is no compile-time reference to Cartographer anywhere
/// — the contract is resolved reflectively per
/// docs/mods/concerned-teamster/CARTOGRAPHER_CONTRACT.md, and
/// tools/validate_repo.py audits both products for accidental coupling.</summary>
public static class CartographerCapability
{
    private static CartographerCapabilityReport? _report;
    private static object? _pluginInstance;

    /// <summary>The probe outcome, or null before <see cref="EnsureProbed"/>
    /// ran. Callers treat null as "hidden" — only an explicit Available
    /// report can surface integration features.</summary>
    public static CartographerCapabilityReport? Report => _report;

    public static bool IsAvailable => _report is { IsAvailable: true };

    /// <summary>Runs the probe once per session and logs its single INFO
    /// line. Called from the plugin's first Update tick — after every
    /// plugin's Awake has run — because BepInEx fills PluginInfos in load
    /// order and probing from Awake could misread a not-yet-loaded
    /// Cartographer as absent. Idempotent; plugins cannot hot-load, so the
    /// first answer stands for the session.</summary>
    public static CartographerCapabilityReport EnsureProbed(ManualLogSource logger)
    {
        if (_report is not null)
        {
            return _report;
        }

        _report = CartographerGate.Evaluate(Lookup);
        logger.LogInfo(_report.LogLine);
        return _report;
    }

    /// <summary>Copies the current world's living routes. False (empty list)
    /// unless the capability is Available and the chain is readable right
    /// now. The store is re-resolved from the plugin instance on every call
    /// because Cartographer replaces it on world enter — never cached.</summary>
    public static bool TryReadRoutes(out IReadOnlyList<CartographerRouteSnapshot> routes)
    {
        if (!IsAvailable)
        {
            routes = Array.Empty<CartographerRouteSnapshot>();
            return false;
        }

        return CartographerRouteReader.TryReadRoutes(_pluginInstance, out routes);
    }

    /// <summary>Reads the route table's monotonic change stamp so callers
    /// can poll cheaply and re-copy only on change. False unless Available
    /// and readable.</summary>
    public static bool TryReadRouteChangeStamp(out long changeStamp)
    {
        if (!IsAvailable)
        {
            changeStamp = 0;
            return false;
        }

        return CartographerRouteReader.TryReadChangeStamp(_pluginInstance, out changeStamp);
    }

    private static CartographerLookup Lookup()
    {
        if (!Chainloader.PluginInfos.TryGetValue(CartographerContract.PluginGuid, out PluginInfo info) ||
            info is null)
        {
            return CartographerLookup.NotFound();
        }

        System.Version? version = info.Metadata?.Version;
        _pluginInstance = info.Instance;
        return CartographerLookup.Detected(version, info.Instance);
    }
}
