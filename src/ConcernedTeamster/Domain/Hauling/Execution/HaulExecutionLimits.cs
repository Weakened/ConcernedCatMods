using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>The executor's own thresholds: the numbers the mechanics need that
/// contract C1's <see cref="HaulLimits"/> does not name. They are this mod's
/// limits for an NPC puller, chosen conservatively and documented in
/// <c>docs/mods/concerned-teamster/GUNNAR_HAULING.md</c>; none is a vanilla value
/// and none is a claim about what is safe in general. Each is measured or
/// confirmed at the live Gate B, and a value the lead wants shared with other
/// agents belongs in <see cref="HaulLimits"/> through a contract revision.
/// </summary>
internal sealed class HaulExecutionLimits
{
    /// <summary>The cart's up axis below which vanilla's own <c>CanAttach</c>
    /// lets go (<c>transform.up.y &lt; 0.1</c>, verified in the 1.0.12
    /// decompile). A joint that vanished below it is a tipped cart.</summary>
    public const float VanillaTipUpDot = 0.1f;

    public static HaulExecutionLimits Default => new HaulExecutionLimits();

    /// <summary>How far Gunnar's facing may be from the cart's heading (centre
    /// to handle) when he hitches. He pulls forward, so he stands at the handle
    /// facing away from the cart.</summary>
    public float AlignHeadingToleranceDegrees { get; set; } = 45f;

    /// <summary>How long the body's own pathfinder may report no path before an
    /// approach attempt counts as failed.</summary>
    public float NoPathGraceSeconds { get; set; } = 5f;

    /// <summary>How long a stop may take before a cart that will not come to
    /// rest is reported rather than waited for.</summary>
    public float StoppingTimeoutSeconds { get; set; } = 15f;

    /// <summary>How long a released cart is watched: a cart that starts to
    /// roll inside this window was not parked safely, and is reported.
    /// </summary>
    public float DetachSettleSeconds { get; set; } = 2f;

    /// <summary>Speed above which a released cart counts as rolling away. Above
    /// <see cref="HaulLimits.StillSpeedMetresPerSecond"/> on purpose: releasing
    /// the joint and vanilla's own 1 N wake-up push jiggle a standing cart.
    /// </summary>
    public float RollAwaySpeedMetresPerSecond { get; set; } = 0.5f;

    /// <summary>The joint load, as a fraction of the cart's own break force, at
    /// which Gunnar stops pulling instead of letting the hitch snap: a pull
    /// that strains that hard is not progress, and escalating is forbidden.
    /// </summary>
    public float JointStrainRatio { get; set; } = 0.8f;

    /// <summary>How far from a cart the player may be when selecting it.
    /// </summary>
    public float SelectionReachMetres { get; set; } = 8f;

    /// <summary>How long a selection waits for its confirmation before it has
    /// to be made again.</summary>
    public float SelectionConfirmSeconds { get; set; } = 30f;

    /// <summary>Relative tolerance when comparing masses (cart mass currency,
    /// puller calibration).</summary>
    public float MassToleranceRatio { get; set; } = 0.01f;

    /// <summary>Absolute floor of the mass tolerance, for float sums of small
    /// bodies.</summary>
    public float MassToleranceKg { get; set; } = 0.05f;

    /// <summary>How far the puller's scale may be from one. The joint's
    /// connected anchor is in the puller's local space, so a scaled body would
    /// hold the cart somewhere other than where the hitch was measured.
    /// </summary>
    public float ScaleTolerance { get; set; } = 0.02f;

    /// <summary>How long Gunnar's worker tick may be silent before his body is
    /// treated as not working (not owned, faulted or gone).</summary>
    public float WorkerTickStaleSeconds { get; set; } = 1.5f;

    /// <summary>Arrival radius of a standalone leg given without one.</summary>
    public float DefaultArrivalRadiusMetres { get; set; } = 2f;

    /// <summary>Throws when a value is outside what the code was designed for.
    /// </summary>
    public HaulExecutionLimits Validate()
    {
        Require(AlignHeadingToleranceDegrees >= 5f && AlignHeadingToleranceDegrees <= 90f, nameof(AlignHeadingToleranceDegrees));
        Require(NoPathGraceSeconds >= 1f && NoPathGraceSeconds <= 30f, nameof(NoPathGraceSeconds));
        Require(StoppingTimeoutSeconds >= 2f && StoppingTimeoutSeconds <= 120f, nameof(StoppingTimeoutSeconds));
        Require(DetachSettleSeconds >= 0.5f && DetachSettleSeconds <= 10f, nameof(DetachSettleSeconds));
        Require(RollAwaySpeedMetresPerSecond > 0f && RollAwaySpeedMetresPerSecond <= 3f, nameof(RollAwaySpeedMetresPerSecond));
        Require(JointStrainRatio > 0.1f && JointStrainRatio < 1f, nameof(JointStrainRatio));
        Require(SelectionReachMetres >= 2f && SelectionReachMetres <= 30f, nameof(SelectionReachMetres));
        Require(SelectionConfirmSeconds >= 5f && SelectionConfirmSeconds <= 600f, nameof(SelectionConfirmSeconds));
        Require(MassToleranceRatio > 0f && MassToleranceRatio <= 0.1f, nameof(MassToleranceRatio));
        Require(MassToleranceKg > 0f && MassToleranceKg <= 1f, nameof(MassToleranceKg));
        Require(ScaleTolerance > 0f && ScaleTolerance <= 0.2f, nameof(ScaleTolerance));
        Require(WorkerTickStaleSeconds >= 0.25f && WorkerTickStaleSeconds <= 10f, nameof(WorkerTickStaleSeconds));
        Require(DefaultArrivalRadiusMetres >= 0.5f && DefaultArrivalRadiusMetres <= 10f, nameof(DefaultArrivalRadiusMetres));
        return this;
    }

    /// <summary>True when two masses agree within the relative tolerance or the
    /// absolute floor, whichever is larger. Any non-finite value disagrees.
    /// </summary>
    public bool MassesAgree(float measured, float expected)
    {
        if (!IsFinite(measured) || !IsFinite(expected))
        {
            return false;
        }

        float tolerance = Math.Max(MassToleranceKg, Math.Abs(expected) * MassToleranceRatio);
        return Math.Abs(measured - expected) <= tolerance;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, "Haul execution limit out of its designed range.");
        }
    }
}
