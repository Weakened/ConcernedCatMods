using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedForeman.Domain.Ladders;

/// <summary>The `Ladders` configuration section as six plain values
/// (`docs/mods/concerned-foreman/LADDERS.md` §8).
///
/// The BepInEx side of the section is a handful of <c>Config.Bind</c> calls and
/// nothing else; every rule about what the numbers mean, what happens to a
/// silly one, and how they reach the climb lives here, where it can be tested
/// without the game.
///
/// <b>The defaults are the spec's table.</b> A player who never opens the
/// config file gets climbable ladders, auto-mount on, the tuned speed, no
/// stamina cost, no teleport, and no NPC climbing — that is the whole point of
/// the row of defaults, and a test holds it to them.
///
/// Four of the six map onto <see cref="ClimbOptions"/>, which the climb reads
/// live. The other two have no home there and must not be given one:
/// <see cref="ClimbOptions"/> is the frozen game-free domain, and
/// <c>UseTeleport</c> belongs to the interaction patch while
/// <c>NpcClimbing</c> belongs to the worker seam (CF-LAD-005).</summary>
internal readonly struct LadderSettingValues
{
    /// <summary>The slowest climb the domain will accept
    /// (<see cref="ClimbLimits.SpeedMultiplier"/>). Slower than this and a
    /// player is stuck on a ladder rather than climbing it.</summary>
    internal const float MinimumClimbSpeed = 0.25f;

    /// <summary>The fastest. Past this the servo that holds the body on the
    /// ladder is moving faster than the physics step can follow.</summary>
    internal const float MaximumClimbSpeed = 3f;

    internal const float MinimumStaminaCost = 0f;

    /// <summary>Ten stamina a second empties a new character in under three
    /// seconds. It is the ceiling the domain validates, and nobody should want
    /// it; it exists so the range is stated rather than assumed.</summary>
    internal const float MaximumStaminaCost = 10f;

    internal LadderSettingValues(
        bool enabled,
        bool autoMount,
        float climbSpeed,
        float staminaCost,
        bool useTeleport,
        bool npcClimbing)
    {
        Enabled = enabled;
        AutoMount = autoMount;
        ClimbSpeed = climbSpeed;
        StaminaCost = staminaCost;
        UseTeleport = useTeleport;
        NpcClimbing = npcClimbing;
    }

    /// <summary>Climb ladders instead of teleporting up them. False restores
    /// vanilla.</summary>
    internal bool Enabled { get; }

    /// <summary>Walking into a ladder climbs it. False means Use only.</summary>
    internal bool AutoMount { get; }

    /// <summary>Multiplier on the tuned climb speed.</summary>
    internal float ClimbSpeed { get; }

    /// <summary>Stamina per second of climbing. Zero by default, because
    /// vanilla charges nothing to walk up its own stairs.</summary>
    internal float StaminaCost { get; }

    /// <summary>Give Use back to vanilla's teleport.</summary>
    internal bool UseTeleport { get; }

    /// <summary>Let a worker climb, once the NPC seam has passed its own gate.
    /// Off, and nothing reads it yet.</summary>
    internal bool NpcClimbing { get; }

    /// <summary>Exactly the table in LADDERS.md §8.</summary>
    internal static LadderSettingValues Defaults => new LadderSettingValues(
        enabled: true,
        autoMount: true,
        climbSpeed: 1f,
        staminaCost: 0f,
        useTeleport: false,
        npcClimbing: false);

    /// <summary>The same settings with anything nonsensical brought back into
    /// range.
    ///
    /// A configuration file is a text file a person edits, and it can contain
    /// <c>NaN</c>, a negative speed, or a number with too many zeroes. None of
    /// those may stop a player climbing, and none of them may reach the domain:
    /// <see cref="ClimbLimits.Validate"/> throws on an out-of-range number, and
    /// a throw at the moment somebody walks into a ladder is the worst possible
    /// place for one. So this clamps, exactly as
    /// <see cref="ClimbOptions.ToLimits"/> does, and the two agree.</summary>
    internal LadderSettingValues Sanitised() => new LadderSettingValues(
        Enabled,
        AutoMount,
        Clamp(ClimbSpeed, MinimumClimbSpeed, MaximumClimbSpeed, Defaults.ClimbSpeed),
        Clamp(StaminaCost, MinimumStaminaCost, MaximumStaminaCost, Defaults.StaminaCost),
        UseTeleport,
        NpcClimbing);

    /// <summary>Copies the four settings the climb itself reads onto the live
    /// options object. Called every frame from the plugin's own Update, so that
    /// a setting changed in a config manager takes effect on the next frame
    /// instead of at the next restart — which is also how switching ladders off
    /// ends a climb already in progress.
    ///
    /// It allocates nothing and it is six field writes.</summary>
    internal void ApplyTo(ClimbOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        LadderSettingValues safe = Sanitised();
        options.Enabled = safe.Enabled;
        options.AutoMount = safe.AutoMount;
        options.ClimbSpeedMultiplier = safe.ClimbSpeed;
        options.StaminaPerSecond = safe.StaminaCost;
    }

    private static float Clamp(float value, float low, float high, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Min(Math.Max(value, low), high);
    }
}
