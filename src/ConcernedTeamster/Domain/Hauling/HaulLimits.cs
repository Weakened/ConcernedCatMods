using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>Every numeric bound of hauling in one place (CART-05, CART-06), so
/// budgets, thresholds and ceilings are recorded, configurable and tested
/// rather than scattered. These are this mod's own limits for an NPC puller,
/// not vanilla values, and none of them is a claim about what is "safe" in
/// general. Contract revision C1 fixes the names and units; agent B owns the
/// default values and records the measurements behind them.</summary>
internal sealed class HaulLimits
{
    /// <summary>Provisional C1 defaults.</summary>
    public static HaulLimits Default => new HaulLimits();

    // Route planning (agent B)

    /// <summary>Rise over run a loaded route may not exceed.</summary>
    public float MaxGradeRatio { get; set; } = 0.12f;

    /// <summary>Free width beyond the cart's own width on each side.</summary>
    public float SideClearanceMetres { get; set; } = 0.5f;

    /// <summary>Navmesh path queries allowed per minute, across all hauls.
    /// </summary>
    public int PathQueriesPerMinute { get; set; } = 12;

    /// <summary>Clearance probes (swept casts) allowed per plan.</summary>
    public int ClearanceProbesPerPlan { get; set; } = 128;

    /// <summary>Spacing of clearance and grade samples along a route.</summary>
    public float SampleSpacingMetres { get; set; } = 1.5f;

    /// <summary>Longest route planned in one leg.</summary>
    public float MaxLegMetres { get; set; } = 64f;

    /// <summary>How often a running plan is rechecked against the world.
    /// </summary>
    public float PlanRefreshSeconds { get; set; } = 5f;

    // Approach and hitch (agent A)

    public float ApproachTimeoutSeconds { get; set; } = 60f;

    /// <summary>Fraction of the cart's own detach distance Gunnar must be
    /// within before attaching (DECISIONS.md D4).</summary>
    public float HitchReachFraction { get; set; } = 0.6f;

    public int MaxHitchAttempts { get; set; } = 3;

    /// <summary>How long a freshly owned cart's mass may take to catch up.
    /// </summary>
    public float MassSettleSeconds { get; set; } = 6f;

    /// <summary>Least upward component of the cart's up axis to attach.
    /// </summary>
    public float MinUprightDot { get; set; } = 0.5f;

    // Motion and recovery (agents A and B)

    /// <summary>Window over which progress is judged from BOTH bodies.</summary>
    public float StallWindowSeconds { get; set; } = 2.5f;

    /// <summary>Cart displacement below which, over the window, a commanded
    /// pull is not progress.</summary>
    public float StallCartDisplacementMetres { get; set; } = 0.75f;

    public int MaxRecoveryAttempts { get; set; } = 2;

    /// <summary>First wait between recoveries; doubles, capped.</summary>
    public float RecoveryBackoffSeconds { get; set; } = 5f;

    public float RecoveryBackoffMaxSeconds { get; set; } = 20f;

    /// <summary>Speed below which a stopped cart counts as still.</summary>
    public float StillSpeedMetresPerSecond { get; set; } = 0.15f;

    /// <summary>How long the cart must stay still before a transfer may start.
    /// </summary>
    public float StillForSeconds { get; set; } = 1f;

    // Cooperation (agent E)

    public float RendezvousTimeoutSeconds { get; set; } = 180f;

    /// <summary>How often the consumer may poll a haul.</summary>
    public float PollIntervalSeconds { get; set; } = 0.5f;

    /// <summary>Throws when a value is outside what the code was designed for.
    /// </summary>
    public HaulLimits Validate()
    {
        Require(MaxGradeRatio > 0f && MaxGradeRatio <= 1f, nameof(MaxGradeRatio));
        Require(SideClearanceMetres >= 0f && SideClearanceMetres <= 2f, nameof(SideClearanceMetres));
        Require(PathQueriesPerMinute >= 1 && PathQueriesPerMinute <= 120, nameof(PathQueriesPerMinute));
        Require(ClearanceProbesPerPlan >= 1 && ClearanceProbesPerPlan <= 1024, nameof(ClearanceProbesPerPlan));
        Require(SampleSpacingMetres >= 0.25f && SampleSpacingMetres <= 5f, nameof(SampleSpacingMetres));
        Require(MaxLegMetres >= 4f && MaxLegMetres <= 256f, nameof(MaxLegMetres));
        Require(PlanRefreshSeconds >= 0.5f && PlanRefreshSeconds <= 60f, nameof(PlanRefreshSeconds));
        Require(ApproachTimeoutSeconds >= 5f && ApproachTimeoutSeconds <= 600f, nameof(ApproachTimeoutSeconds));
        Require(HitchReachFraction > 0f && HitchReachFraction <= 0.9f, nameof(HitchReachFraction));
        Require(MaxHitchAttempts >= 1 && MaxHitchAttempts <= 10, nameof(MaxHitchAttempts));
        Require(MassSettleSeconds >= 0f && MassSettleSeconds <= 30f, nameof(MassSettleSeconds));
        Require(MinUprightDot >= 0.1f && MinUprightDot <= 1f, nameof(MinUprightDot));
        Require(StallWindowSeconds >= 1f && StallWindowSeconds <= 60f, nameof(StallWindowSeconds));
        Require(StallCartDisplacementMetres > 0f && StallCartDisplacementMetres <= 5f, nameof(StallCartDisplacementMetres));
        Require(MaxRecoveryAttempts >= 0 && MaxRecoveryAttempts <= 10, nameof(MaxRecoveryAttempts));
        Require(RecoveryBackoffSeconds > 0f && RecoveryBackoffMaxSeconds >= RecoveryBackoffSeconds, nameof(RecoveryBackoffSeconds));
        Require(StillSpeedMetresPerSecond > 0f && StillSpeedMetresPerSecond <= 1f, nameof(StillSpeedMetresPerSecond));
        Require(StillForSeconds >= 0f && StillForSeconds <= 10f, nameof(StillForSeconds));
        Require(RendezvousTimeoutSeconds >= 10f && RendezvousTimeoutSeconds <= 1800f, nameof(RendezvousTimeoutSeconds));
        Require(PollIntervalSeconds >= 0.1f && PollIntervalSeconds <= 10f, nameof(PollIntervalSeconds));
        return this;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, "Haul limit out of its designed range.");
        }
    }
}

/// <summary>Progress judged from both bodies, never from the walk animation
/// alone (CART-06).</summary>
internal enum HaulMotion
{
    Unspecified = 0,

    /// <summary>Not commanded to move.</summary>
    Idle = 1,

    /// <summary>Commanded, and the cart is moving along.</summary>
    Progressing = 2,

    /// <summary>Commanded, and neither body is getting anywhere.</summary>
    Stalled = 3,

    /// <summary>Commanded, Gunnar strains or moves but the cart does not
    /// follow: something holds the cart.</summary>
    Wedged = 4,
}

/// <summary>Agent B implements the judgement; agent A feeds samples and acts on
/// the answer.</summary>
internal interface IHaulMotionMonitor
{
    void Sample(float now, TheConcernedCat.Workers.WorkPoint puller, TheConcernedCat.Workers.WorkPoint cart, bool motorCommanded);

    HaulMotion Current { get; }

    void Reset();
}
