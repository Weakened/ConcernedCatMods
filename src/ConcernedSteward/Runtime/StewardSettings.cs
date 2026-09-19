using BepInEx.Configuration;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>The Steward's switches.
///
/// <b>Everything is off until a person turns it on, and there are two
/// switches, not one.</b> The outer one decides whether this mod may exist in a
/// player's world at all; the inner one decides whether the Steward moves a
/// player's materials. They are separate because they are different
/// commitments: somebody may want to meet him, mark their settlement and look
/// at what he would do without yet wanting him reaching into their chest.
///
/// Every value is read <b>once per decision</b> rather than cached into the
/// worker, so switching one off stops him on the next tick instead of at the
/// next restart. That matters more here than it looks: the tick after a player
/// changes their mind is the one that would otherwise withdraw the wood.
/// </summary>
internal sealed class StewardSettings
{
    private StewardSettings(
        ConfigEntry<bool> runtimeEnabled,
        ConfigEntry<bool> tendFiresEnabled,
        ConfigEntry<bool> debugLogging,
        ConfigEntry<string> baseCreature,
        ConfigEntry<string> questPickupItem)
    {
        RuntimeEnabled = runtimeEnabled;
        TendFiresEnabled = tendFiresEnabled;
        DebugLogging = debugLogging;
        BaseCreature = baseCreature;
        QuestPickupItem = questPickupItem;
    }

    /// <summary>The master switch. False by default. With it false the Steward
    /// cannot be recruited, nothing can be marked, and no world state is
    /// touched at all.</summary>
    public ConfigEntry<bool> RuntimeEnabled { get; }

    /// <summary>Whether he actually tends fires. <b>False by default even with
    /// the runtime on</b>, and #340 says so explicitly: the active automation
    /// stays off until the live handoff is ready. A recruited Steward with this
    /// off stands in the settlement and does nothing, which is exactly what it
    /// should look like.</summary>
    public ConfigEntry<bool> TendFiresEnabled { get; }

    public ConfigEntry<bool> DebugLogging { get; }

    /// <summary>The vanilla humanoid prefab his body is cloned from.
    ///
    /// Configurable because creature prefab names are <b>data</b>, in the
    /// game's asset bundles, not constants in the assembly — no amount of
    /// reading the installed DLL can prove one exists. So the name is resolved
    /// at runtime and fails closed: a missing or unsuitable prefab produces no
    /// Steward and an explanation, never a guess.
    ///
    /// The final appearance is not settled. This is the placeholder that lets
    /// the rest of the work be proved in a real game.</summary>
    public ConfigEntry<string> BaseCreature { get; }

    /// <summary>The vanilla item whose pickup turns up Sunniva's flint and
    /// steel.
    ///
    /// Configurable for the same reason <see cref="BaseCreature"/> is: an item
    /// prefab name is asset-bundle data and cannot be proved from the installed
    /// assembly, so a wrong one has to be a config edit and a logged line rather
    /// than a guess that compiles. The item's real name — the string vanilla
    /// itself compares against — is read off this prefab and is never written
    /// down here.</summary>
    public ConfigEntry<string> QuestPickupItem { get; }

    public static StewardSettings Bind(ConfigFile config)
    {
        ConfigEntry<bool> runtime = config.Bind(
            "Steward",
            "StewardRuntimeEnabled",
            false,
            "Opt in to the Steward: a settlement worker with a real body, a real inventory " +
            "and one job. OFF by default. With this off, this mod does nothing at all — it " +
            "marks nothing, spawns nothing and touches no world state. Turning it on lets it " +
            "act on a host you control; it never takes ownership it was not granted, and it " +
            "refuses rather than guessing when it cannot establish that an action is allowed.");

        ConfigEntry<bool> tend = config.Bind(
            "Steward",
            "TendFiresEnabled",
            false,
            "Let the Steward keep the settlement's fires burning with wood from the chest you " +
            "marked. OFF by default, separately from the runtime switch: recruiting him and " +
            "letting him reach into your chest are different decisions. He walks the whole " +
            "way, takes only from the marked chest, and puts back whatever he does not burn.");

        ConfigEntry<bool> debug = config.Bind(
            "Diagnostics",
            "DebugLogging",
            false,
            "Log every Steward decision, including each measured withdrawal and each unit of " +
            "fuel. Verbose; intended for bug reports.");

        ConfigEntry<string> creature = config.Bind(
            "Steward",
            "BaseCreature",
            "Dverger",
            "The vanilla humanoid prefab the Steward's body is cloned from. Its AI is removed " +
            "and replaced; only the body, animator and network view are kept. If this prefab " +
            "does not exist in your game build, or is not a networked humanoid, no Steward is " +
            "created and the reason is logged. His final appearance is not settled yet.");

        ConfigEntry<string> questItem = config.Bind(
            "Steward",
            "QuestPickupItem",
            "Resin",
            "The item whose first pickup turns up the flint and steel that begins Sunniva's " +
            "introduction. Vanilla resin by default. Only the prefab name is read from here; " +
            "the item itself is looked up in the game, so a name this build does not have is " +
            "reported and the introduction simply never starts.");

        return new StewardSettings(runtime, tend, debug, creature, questItem);
    }
}
