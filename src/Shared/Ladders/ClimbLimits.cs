using System;

namespace TheConcernedCat.Ladders;

/// <summary>Every number the climb obeys, in one place, with the reasons.
/// Defaults are tuned for a player's body on Valheim's wooden ladder and are
/// validated, because a climb driven by a nonsense number is a climb that
/// throws a character off a tower.</summary>
internal sealed class ClimbLimits
{
    /// <summary>How far in front of the rungs a character may stand and still
    /// start climbing. Generous on purpose: the brief asks for no pixel-perfect
    /// positioning.</summary>
    public float MountReachMetres { get; set; } = 1.1f;

    /// <summary>How far off the ladder's centre a character may start, beyond
    /// half the ladder's width.</summary>
    public float MountSideMarginMetres { get; set; } = 0.45f;

    /// <summary>How far above the top or below the foot a character may be and
    /// still mount: stepping on from a platform edge, or from a step at the
    /// bottom.</summary>
    public float MountHeightMarginMetres { get; set; } = 1.2f;

    /// <summary>How much the character must be turned towards the ladder to
    /// mount by walking into it: 1 is dead on, 0 is sideways. Walking past a
    /// ladder must not grab it.</summary>
    public float MountFacingAgreement { get; set; } = 0.35f;

    /// <summary>Metres per second of climb. Vanilla walking is about 3 m/s on
    /// the flat; climbing is deliberately slower than walking and faster than a
    /// staircase detour.</summary>
    public float ClimbSpeedMetresPerSecond { get; set; } = 1.8f;

    /// <summary>How fast the body settles onto the ladder's line and facing
    /// when mounting. A whole second would feel like a magnet; an instant would
    /// be the teleport this feature exists to replace.</summary>
    public float AlignSeconds { get; set; } = 0.25f;

    /// <summary>How far out from the rungs the body hangs while climbing.</summary>
    public float BodyOffsetMetres { get; set; } = 0.35f;

    /// <summary>How far above the ladder's head the feet finish when stepping
    /// off at the top, so the character lands on the floor and not in it.</summary>
    public float TopStepUpMetres { get; set; } = 0.4f;

    /// <summary>How far in from the edge the character is placed when stepping
    /// off at the top.</summary>
    public float TopStepInMetres { get; set; } = 0.6f;

    /// <summary>The climb ends on its own if the body ends up further from the
    /// ladder than this: something pushed, threw or teleported the character.</summary>
    public float LostContactMetres { get; set; } = 1.6f;

    /// <summary>Stamina per second of climbing. Zero by default: vanilla asks
    /// none for walking up its own stairs, and a ladder must not be the
    /// expensive way up.</summary>
    public float StaminaPerSecond { get; set; } = 0f;

    /// <summary>Speed multiplier from the player's settings.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    public float EffectiveClimbSpeed => Math.Max(0.1f, ClimbSpeedMetresPerSecond * SpeedMultiplier);

    public ClimbLimits Validate()
    {
        Require(MountReachMetres > 0f && MountReachMetres <= 3f, nameof(MountReachMetres));
        Require(MountSideMarginMetres >= 0f && MountSideMarginMetres <= 2f, nameof(MountSideMarginMetres));
        Require(MountHeightMarginMetres >= 0f && MountHeightMarginMetres <= 3f, nameof(MountHeightMarginMetres));
        Require(MountFacingAgreement >= -1f && MountFacingAgreement <= 1f, nameof(MountFacingAgreement));
        Require(ClimbSpeedMetresPerSecond > 0f && ClimbSpeedMetresPerSecond <= 6f, nameof(ClimbSpeedMetresPerSecond));
        Require(AlignSeconds >= 0f && AlignSeconds <= 1f, nameof(AlignSeconds));
        Require(BodyOffsetMetres > 0f && BodyOffsetMetres <= 1f, nameof(BodyOffsetMetres));
        Require(TopStepUpMetres >= 0f && TopStepUpMetres <= 2f, nameof(TopStepUpMetres));
        Require(TopStepInMetres > 0f && TopStepInMetres <= 2f, nameof(TopStepInMetres));
        Require(LostContactMetres > BodyOffsetMetres, nameof(LostContactMetres));
        Require(StaminaPerSecond >= 0f && StaminaPerSecond <= 10f, nameof(StaminaPerSecond));
        Require(SpeedMultiplier >= 0.25f && SpeedMultiplier <= 3f, nameof(SpeedMultiplier));
        return this;
    }

    public static ClimbLimits Default => new ClimbLimits();

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, "A climb limit is outside its designed range.");
        }
    }
}
