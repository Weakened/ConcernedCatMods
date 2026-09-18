using System;

namespace TheConcernedCat.Ladders;

/// <summary>The climb's switches and the numbers the Valheim side needs, in one
/// game-free object.
///
/// The settings file belongs to another workstream (LADDERS.md §10: CF-LAD-004
/// owns settings, the lead owns the settings file), so this is deliberately a
/// plain object with the spec's defaults rather than anything bound to BepInEx.
/// Whoever binds `Ladders/Enabled`, `AutoMount`, `ClimbSpeed`, `StaminaCost` and
/// `UseTeleport` writes into an instance of this; until then the defaults are
/// exactly the table in LADDERS.md §8, so the default experience needs no
/// configuration.
///
/// Values are read once per decision rather than copied into the climb, so
/// switching ladders off ends a climb in progress on the next frame instead of
/// at the next restart.</summary>
internal sealed class ClimbOptions
{
    /// <summary>The master switch. False restores vanilla exactly: no patch
    /// takes effect, no survey runs, and `Ladder.Interact` teleports again.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Walking into a ladder climbs it. Off means Use only.</summary>
    public bool AutoMount { get; set; } = true;

    /// <summary>Multiplier on the tuned climb speed.</summary>
    public float ClimbSpeedMultiplier { get; set; } = 1f;

    /// <summary>Stamina per second of climbing. Zero by default: vanilla charges
    /// nothing to walk up its own stairs.</summary>
    public float StaminaPerSecond { get; set; } = 0f;

    /// <summary>How long after any ending before a ladder can be grabbed again.
    /// Short enough not to be felt, long enough that letting go is letting
    /// go.</summary>
    public float ReMountCooldownSeconds { get; set; } = 0.35f;

    /// <summary>How far around the character the survey looks for ladders. A
    /// little more than a body length past the most generous mount reach, so a
    /// ladder is known about before it can be mounted.</summary>
    public float SurveyRadiusMetres { get; set; } = 4f;

    /// <summary>How often the survey looks. Four times a second is far below
    /// what a player can notice walking into a ladder, and it is the whole idle
    /// cost of the feature.</summary>
    public float SurveyIntervalSeconds { get; set; } = 0.25f;

    /// <summary>How close to the top the climber has to be before the landing
    /// probe runs. The probe costs a raycast and a capsule test, and its answer
    /// is only used at the top.</summary>
    public float TopProbeWithinMetres { get; set; } = 0.75f;

    /// <summary>How far above the head of the ladder a floor may be and still
    /// count as the landing.</summary>
    public float TopFloorAboveMetres { get; set; } = 1f;

    /// <summary>How far below the head of the ladder a floor may be and still
    /// count as the landing. Generous downwards, because a ladder that pokes
    /// above its platform is the normal way people build them.</summary>
    public float TopFloorBelowMetres { get; set; } = 1.25f;

    /// <summary>The fastest the climb will move a body to correct a drift. It
    /// exists so that a body which was pushed, or which snagged on geometry,
    /// is pulled back at a speed that reads as climbing rather than as the
    /// teleport this feature replaces. Past
    /// <see cref="ClimbLimits.LostContactMetres"/> the climb ends instead.</summary>
    public float MaxCorrectionSpeed { get; set; } = 4f;

    /// <summary>The climb's own tuned numbers, with the player's settings
    /// applied. Clamped rather than validated-and-thrown: a silly number in a
    /// config file must not stop a player from climbing, and the domain's own
    /// ranges are the truth about what is sensible.</summary>
    public ClimbLimits ToLimits()
    {
        var limits = new ClimbLimits
        {
            SpeedMultiplier = Clamp(ClimbSpeedMultiplier, 0.25f, 3f, 1f),
            StaminaPerSecond = Clamp(StaminaPerSecond, 0f, 10f, 0f),
        };
        return limits.Validate();
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
