using BepInEx.Configuration;
using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>The `Ladders` configuration section
/// (`docs/mods/concerned-foreman/LADDERS.md` §8), bound the way Foreman binds
/// everything else (<c>ForemanSettlementSettings</c>): six
/// <see cref="ConfigEntry{T}"/> objects, read at the moment they are used, and
/// nothing cached into the climb.
///
/// There is deliberately nothing here but binding. Every rule about the values
/// is in <see cref="LadderSettingValues"/>, which has no BepInEx in it and is
/// tested; this class exists so that the rules have somewhere to be read from.
///
/// <b>Zero configuration is the default experience.</b> Every default is the
/// spec's, so a player who never opens the config file climbs ladders.</summary>
internal sealed class LadderSettings
{
    private LadderSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> autoMount,
        ConfigEntry<float> climbSpeed,
        ConfigEntry<float> staminaCost,
        ConfigEntry<bool> useTeleport,
        ConfigEntry<bool> npcClimbing)
    {
        Enabled = enabled;
        AutoMount = autoMount;
        ClimbSpeed = climbSpeed;
        StaminaCost = staminaCost;
        UseTeleport = useTeleport;
        NpcClimbing = npcClimbing;
    }

    /// <summary>The master switch. Off restores vanilla exactly: with it off at
    /// load nothing is patched at all, and switched off later every patch falls
    /// straight through and a climb in progress ends on the next frame.</summary>
    public ConfigEntry<bool> Enabled { get; }

    /// <summary>Walking into a ladder climbs it.</summary>
    public ConfigEntry<bool> AutoMount { get; }

    /// <summary>Multiplier on the tuned climb speed.</summary>
    public ConfigEntry<float> ClimbSpeed { get; }

    /// <summary>Stamina per second of climbing.</summary>
    public ConfigEntry<float> StaminaCost { get; }

    /// <summary>Give the Use key back to vanilla's teleport. Read live by the
    /// interaction patch, so it takes effect without a restart.</summary>
    public ConfigEntry<bool> UseTeleport { get; }

    /// <summary>Let a settlement worker climb. Bound now so the section is the
    /// one the spec describes; <b>nothing reads it yet</b> — the worker seam is
    /// CF-LAD-005, and when it lands it obeys the worker rules already in force
    /// (opted in, host, no other peers).</summary>
    public ConfigEntry<bool> NpcClimbing { get; }

    /// <summary>The six settings as values, already brought into range.</summary>
    public LadderSettingValues Values => new LadderSettingValues(
        Enabled.Value,
        AutoMount.Value,
        ClimbSpeed.Value,
        StaminaCost.Value,
        UseTeleport.Value,
        NpcClimbing.Value).Sanitised();

    public static LadderSettings Bind(ConfigFile config)
    {
        LadderSettingValues defaults = LadderSettingValues.Defaults;

        ConfigEntry<bool> enabled = config.Bind(
            "Ladders",
            "Enabled",
            defaults.Enabled,
            "Climb the game's own ladders instead of being teleported up them. " +
            "Turning this off restores vanilla exactly: with it off when the game " +
            "starts, Concerned Foreman patches nothing at all and every ladder " +
            "behaves as it does without the mod installed.");

        ConfigEntry<bool> autoMount = config.Bind(
            "Ladders",
            "AutoMount",
            defaults.AutoMount,
            "Walking into a ladder starts the climb. Turn this off if you would " +
            "rather press Use every time; walking past a ladder never grabs it " +
            "either way.");

        ConfigEntry<float> climbSpeed = config.Bind(
            "Ladders",
            "ClimbSpeed",
            defaults.ClimbSpeed,
            new ConfigDescription(
                "Multiplier on the climbing speed. 1 is the tuned speed, which is " +
                "slower than walking and faster than going round by the stairs.",
                new AcceptableValueRange<float>(
                    LadderSettingValues.MinimumClimbSpeed, LadderSettingValues.MaximumClimbSpeed)));

        ConfigEntry<float> staminaCost = config.Bind(
            "Ladders",
            "StaminaCost",
            defaults.StaminaCost,
            new ConfigDescription(
                "Stamina used per second of climbing. Zero by default, because " +
                "Valheim charges nothing to walk up its own staircases and a " +
                "ladder must not be the expensive way up.",
                new AcceptableValueRange<float>(
                    LadderSettingValues.MinimumStaminaCost, LadderSettingValues.MaximumStaminaCost)));

        ConfigEntry<bool> useTeleport = config.Bind(
            "Ladders",
            "UseTeleport",
            defaults.UseTeleport,
            "Give the Use key back to vanilla's teleport. Some players use it " +
            "deliberately. With this on, pressing Use on a ladder does exactly " +
            "what it does without the mod; walking into one still climbs it " +
            "unless AutoMount is off as well.");

        ConfigEntry<bool> npcClimbing = config.Bind(
            "Ladders",
            "NpcClimbing",
            defaults.NpcClimbing,
            "Let a settlement worker climb a ladder. Off until NPC traversal has " +
            "passed its own test; nothing in this build reads it.");

        return new LadderSettings(enabled, autoMount, climbSpeed, staminaCost, useTeleport, npcClimbing);
    }

    /// <summary>Copies the live values onto the climb's options. Called every
    /// frame: the values are read where they are used, never cached into a
    /// climb, so a change takes effect on the next frame.</summary>
    public void ApplyTo(ClimbOptions options) => Values.ApplyTo(options);
}
