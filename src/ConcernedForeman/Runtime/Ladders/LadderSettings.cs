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
/// <b>Off unless somebody opts in (#436).</b> Player ladder climbing is owned by
/// Concerned Ladders, which refuses to run while this motor is installed, so a
/// player who never opens the config file gets nothing patched by this section.
/// The switch is bound as <c>Ladders/PlayerClimbing</c>, not the old
/// <c>Ladders/Enabled</c>, so a <c>true</c> an older development build wrote as
/// its default is never read (<see cref="LadderSettingValues.PlayerClimbingKey"/>).
/// The worker-traversal seam (#334) does not depend on this switch.
///
/// <c>Ladders/Enabled</c> is still bound, as a <b>veto only</b>: false turns
/// the climb off whatever <c>PlayerClimbing</c> says, and true (its default,
/// and what old builds wrote) grants nothing. It stays because Concerned
/// Ladders 1.0.0, which is published and cannot change, tells a player who
/// opted in to "disable Foreman's Ladders/Enabled and restart"; that
/// instruction has to work (<see cref="LadderSettingValues.Effective"/>).</summary>
internal sealed class LadderSettings
{
    private LadderSettings(
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> autoMount,
        ConfigEntry<float> climbSpeed,
        ConfigEntry<float> staminaCost,
        ConfigEntry<bool> useTeleport,
        ConfigEntry<bool> npcClimbing,
        ConfigEntry<bool> legacyEnabled)
    {
        Enabled = enabled;
        LegacyEnabled = legacyEnabled;
        AutoMount = autoMount;
        ClimbSpeed = climbSpeed;
        StaminaCost = staminaCost;
        UseTeleport = useTeleport;
        NpcClimbing = npcClimbing;
    }

    /// <summary>The master switch, <c>Ladders/PlayerClimbing</c>, off by
    /// default. Off restores vanilla exactly: with it off at load nothing is
    /// patched at all, and switched off later every patch falls straight through
    /// and a climb in progress ends on the next frame.</summary>
    public ConfigEntry<bool> Enabled { get; }

    /// <summary><c>Ladders/Enabled</c>: a veto, never a grant. See the class
    /// remarks.</summary>
    public ConfigEntry<bool> LegacyEnabled { get; }

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
        LadderSettingValues.Effective(Enabled.Value, LegacyEnabled.Value),
        AutoMount.Value,
        ClimbSpeed.Value,
        StaminaCost.Value,
        UseTeleport.Value,
        NpcClimbing.Value).Sanitised();

    public static LadderSettings Bind(ConfigFile config)
    {
        LadderSettingValues defaults = LadderSettingValues.Defaults;

        ConfigEntry<bool> enabled = config.Bind(
            LadderSettingValues.Section,
            LadderSettingValues.PlayerClimbingKey,
            defaults.Enabled,
            "Off by default. Player ladder climbing now belongs to the separate " +
            "Concerned Ladders mod, and Concerned Ladders refuses to run while " +
            "this is on. Turn it on only if you want Foreman's older climb on the " +
            "game's own ladders instead. Off restores vanilla exactly: with it off " +
            "when the game starts, Concerned Foreman patches nothing at all and every " +
            "ladder behaves as it does without the mod installed. (An older " +
            "development build used the key Enabled; a true there no longer turns this on, and a false " +
            "there still turns it off.)");

        ConfigEntry<bool> autoMount = config.Bind(
            LadderSettingValues.Section,
            "AutoMount",
            defaults.AutoMount,
            "Walking into a ladder starts the climb. Turn this off if you would " +
            "rather press Use every time; walking past a ladder never grabs it " +
            "either way.");

        ConfigEntry<float> climbSpeed = config.Bind(
            LadderSettingValues.Section,
            "ClimbSpeed",
            defaults.ClimbSpeed,
            new ConfigDescription(
                "Multiplier on the climbing speed. 1 is the tuned speed, which is " +
                "slower than walking and faster than going round by the stairs.",
                new AcceptableValueRange<float>(
                    LadderSettingValues.MinimumClimbSpeed, LadderSettingValues.MaximumClimbSpeed)));

        ConfigEntry<float> staminaCost = config.Bind(
            LadderSettingValues.Section,
            "StaminaCost",
            defaults.StaminaCost,
            new ConfigDescription(
                "Stamina used per second of climbing. Zero by default, because " +
                "Valheim charges nothing to walk up its own staircases and a " +
                "ladder must not be the expensive way up.",
                new AcceptableValueRange<float>(
                    LadderSettingValues.MinimumStaminaCost, LadderSettingValues.MaximumStaminaCost)));

        ConfigEntry<bool> useTeleport = config.Bind(
            LadderSettingValues.Section,
            "UseTeleport",
            defaults.UseTeleport,
            "Give the Use key back to vanilla's teleport. Some players use it " +
            "deliberately. With this on, pressing Use on a ladder does exactly " +
            "what it does without the mod; walking into one still climbs it " +
            "unless AutoMount is off as well.");

        ConfigEntry<bool> npcClimbing = config.Bind(
            LadderSettingValues.Section,
            "NpcClimbing",
            defaults.NpcClimbing,
            "Let a settlement worker climb a ladder. Off until NPC traversal has " +
            "passed its own test; nothing in this build reads it.");

        ConfigEntry<bool> legacyEnabled = config.Bind(
            LadderSettingValues.Section,
            LadderSettingValues.RetiredEnabledKey,
            true,
            "Kept only so an older instruction still works. Setting this to false turns Foreman's ladder " +
            "climbing off whatever PlayerClimbing says; true does nothing on its own. Concerned Ladders " +
            "asks you to set this to false if you turned PlayerClimbing on.");

        return new LadderSettings(
            enabled, autoMount, climbSpeed, staminaCost, useTeleport, npcClimbing, legacyEnabled);
    }

    /// <summary>Copies the live values onto the climb's options. Called every
    /// frame: the values are read where they are used, never cached into a
    /// climb, so a change takes effect on the next frame.</summary>
    public void ApplyTo(ClimbOptions options) => Values.ApplyTo(options);
}
