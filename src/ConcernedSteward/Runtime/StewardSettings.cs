using BepInEx.Configuration;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;

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
        ConfigEntry<string> questPickupItem,
        ConfigEntry<int> modelIndex,
        ConfigEntry<string> garment,
        ConfigEntry<string> garmentLegs,
        ConfigEntry<string> hairColour)
    {
        RuntimeEnabled = runtimeEnabled;
        TendFiresEnabled = tendFiresEnabled;
        DebugLogging = debugLogging;
        BaseCreature = baseCreature;
        QuestPickupItem = questPickupItem;
        ModelIndex = modelIndex;
        Garment = garment;
        GarmentLegs = garmentLegs;
        HairColour = hairColour;
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

    /// <summary>Which of the base creature's models she wears. One is the female
    /// model where a creature has two, which is vanilla's own convention; a base
    /// creature with only one model ignores this, because
    /// <c>VisEquipment.SetModel</c> refuses an index outside its own array.
    /// </summary>
    public ConfigEntry<int> ModelIndex { get; }

    /// <summary>A dress or robe to try before the leather.
    ///
    /// Empty by default, and that is an honest default rather than a missing
    /// one. #382 asks for a suitable vanilla dress or robe <i>if one exists</i>,
    /// and whether one does cannot be established from the installed assembly:
    /// armour prefab names are asset-bundle data. So the leather tunic and
    /// leather pants the issue names as the first-playable fallback are what
    /// ships, and this is the one line to change for anybody — the owner, or a
    /// player with a clothing mod — who knows a better name. A name this build
    /// does not have costs a log line and falls through to the leather.
    ///
    /// <b>No third-party asset is named here, shipped here, or depended on
    /// here.</b> #382 allows investigating one only as a future option and only
    /// with the owner's approval, and nothing in this repository may add, copy
    /// or redistribute somebody else's work. A player who has one already can
    /// type its name; that is the whole of the support offered.</summary>
    public ConfigEntry<string> Garment { get; }

    /// <summary>Legs to go with <see cref="Garment"/>. Leave empty for a
    /// garment that covers the legs by itself.</summary>
    public ConfigEntry<string> GarmentLegs { get; }

    /// <summary>Her hair colour, as <c>r,g,b</c> between zero and one. Light
    /// blonde by default. Anything that cannot be read leaves the base
    /// creature's own colouring alone rather than turning her black.</summary>
    public ConfigEntry<string> HairColour { get; }

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
            "marked. OFF by default, separately from the runtime switch: recruiting her and " +
            "letting her reach into your chest are different decisions. She walks the whole " +
            "way, takes only from the marked chest, and puts back whatever she does not burn.");

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
            "created and the reason is logged. Her final appearance is not settled yet.");

        ConfigEntry<string> questItem = config.Bind(
            "Steward",
            "QuestPickupItem",
            "Resin",
            "The item whose first pickup turns up the flint and steel that begins Sunniva's " +
            "introduction. Vanilla resin by default. Only the prefab name is read from here; " +
            "the item itself is looked up in the game, so a name this build does not have is " +
            "reported and the introduction simply never starts.");

        ConfigEntry<int> model = config.Bind(
            "Appearance",
            "ModelIndex",
            1,
            "Which of the base creature's models the Steward wears. 1 is the female model where " +
            "a creature has two. A base creature with only one model ignores this.");

        ConfigEntry<string> garment = config.Bind(
            "Appearance",
            "Garment",
            string.Empty,
            "A dress or robe for her to wear, by item prefab name, tried before the leather. " +
            "Empty by default: item prefab names are asset data and this mod will not guess one. " +
            "A name this game build does not have is reported and the leather is worn instead. " +
            "No clothing from another mod is included, copied or depended on here; naming one " +
            "you already have installed is the whole of the support offered.");

        ConfigEntry<string> garmentLegs = config.Bind(
            "Appearance",
            "GarmentLegs",
            string.Empty,
            "Legs to go with Garment, by item prefab name. Leave empty for a garment that " +
            "covers the legs by itself.");

        ConfigEntry<string> hairColour = config.Bind(
            "Appearance",
            "HairColour",
            StewardLooks.DefaultHairColour,
            "Her hair colour, as three numbers between 0 and 1 separated by commas. Light " +
            "blonde by default. Anything that cannot be read leaves the base creature's own " +
            "colouring alone.");

        return new StewardSettings(
            runtime, tend, debug, creature, questItem, model, garment, garmentLegs, hairColour);
    }
}
