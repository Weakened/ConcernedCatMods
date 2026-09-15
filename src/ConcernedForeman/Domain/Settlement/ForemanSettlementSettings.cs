using BepInEx.Configuration;

namespace TheConcernedCat.ConcernedForeman.Domain.Settlement;

/// <summary>The settlement runtime's switches.
///
/// The runtime is <b>off by default and stays off until a person turns it on</b>.
/// That is not caution for its own sake: #273 draws a hard line between
/// Foreman's diagnostics half, which is read-only and client-safe, and the
/// settlement half, which owns shared world state. Installing the mod for its
/// diagnostics must never quietly enrol a player in the second thing.
///
/// Every value here is read once per decision rather than cached into the
/// worker, so switching the runtime off stops workers on the next tick instead
/// of at the next restart.</summary>
internal sealed class ForemanSettlementSettings
{
    private ForemanSettlementSettings(
        ConfigEntry<bool> settlementRuntimeEnabled,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<string> workerBaseCreature)
    {
        SettlementRuntimeEnabled = settlementRuntimeEnabled;
        DebugLogging = debugLogging;
        WorkerBaseCreature = workerBaseCreature;
    }

    /// <summary>The master switch for everything that can touch shared world
    /// state. False by default, and the worker treats false as a refusal with a
    /// stated reason rather than as a reason to idle silently.</summary>
    public ConfigEntry<bool> SettlementRuntimeEnabled { get; }

    public ConfigEntry<bool> DebugLogging { get; }

    /// <summary>The vanilla humanoid prefab a worker's body is cloned from.
    ///
    /// This is configurable because creature prefab names are <b>data</b>, in
    /// the game's asset bundles, not constants in the assembly — no amount of
    /// reading the installed DLL can prove one exists. So the name is resolved
    /// at runtime and fails closed: a missing or unsuitable prefab produces no
    /// worker and an explanation, never a guess.</summary>
    public ConfigEntry<string> WorkerBaseCreature { get; }

    public static ForemanSettlementSettings Bind(ConfigFile config)
    {
        ConfigEntry<bool> enabled = config.Bind(
            "Settlement",
            "SettlementRuntimeEnabled",
            false,
            "Opt in to the settlement runtime: workers, orders and construction. " +
            "OFF by default. The building diagnostics half of Concerned Foreman " +
            "is read-only and does not need this. Turning this on lets the mod " +
            "act on shared world state on a host you control; it never takes " +
            "ownership it was not granted, and it refuses rather than guessing " +
            "when it cannot establish that an action is allowed.");

        ConfigEntry<bool> debug = config.Bind(
            "Diagnostics",
            "DebugLogging",
            false,
            "Log every worker decision, including the per-tick pathfinding budget " +
            "and the exact reason for any deferral. Verbose; intended for bug reports.");

        ConfigEntry<string> baseCreature = config.Bind(
            "Settlement",
            "WorkerBaseCreature",
            "Dverger",
            "The vanilla humanoid prefab a settlement worker's body is cloned from. " +
            "Its AI is removed and replaced; only the body, animator and network " +
            "view are kept. If this prefab does not exist in your game build, or " +
            "is not a networked humanoid, no worker is created and the reason is " +
            "logged.");

        return new ForemanSettlementSettings(enabled, debug, baseCreature);
    }
}
