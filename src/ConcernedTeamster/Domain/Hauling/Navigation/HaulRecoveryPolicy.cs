using System;
using System.Collections.Generic;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

internal enum HaulRecoveryStep
{
    Unspecified = 0,

    /// <summary>Nothing to recover from; keep following the plan.</summary>
    Continue = 1,

    /// <summary>Hold still until <see cref="HaulRecoveryDecision.NotBefore"/>,
    /// walk the verified back-off goal, then plan the leg again.</summary>
    BackOff = 2,

    /// <summary>Hold still until <see cref="HaulRecoveryDecision.NotBefore"/>,
    /// then plan the leg again from where the cart is.</summary>
    Replan = 3,

    /// <summary>Stop and report <see cref="HaulRecoveryDecision.Reason"/>: no
    /// more manoeuvres on this leg.</summary>
    GiveUp = 4,
}

/// <summary>What to do after a pull stopped getting anywhere.</summary>
internal readonly struct HaulRecoveryDecision
{
    public HaulRecoveryDecision(
        HaulRecoveryStep step, float notBefore, SteeringGoal? backOffGoal, HaulAttentionReason reason, int attemptsUsed)
    {
        Step = step;
        NotBefore = notBefore;
        BackOffGoal = backOffGoal;
        Reason = reason;
        AttemptsUsed = attemptsUsed;
    }

    public HaulRecoveryStep Step { get; }

    /// <summary>The earliest time, on the caller's clock, for the manoeuvre.
    /// </summary>
    public float NotBefore { get; }

    /// <summary>For <see cref="HaulRecoveryStep.BackOff"/> only.</summary>
    public SteeringGoal? BackOffGoal { get; }

    /// <summary>For <see cref="HaulRecoveryStep.GiveUp"/> only; never
    /// Unspecified then.</summary>
    public HaulAttentionReason Reason { get; }

    public int AttemptsUsed { get; }
}

/// <summary>Bounded stuck recovery for one leg (CART-06): only verified-safe
/// local manoeuvres, never more force, never forever.
///
/// Each time the motion monitor says the pull is Stalled or Wedged, the place is
/// remembered in the failure cache - so no later plan drives back into it the
/// same way - and one attempt is spent. An attempt is at most one of:
/// - a short back-off straight behind the cart, only where the probe proved the
///   ground and space behind are clear, and never twice at the same place;
/// - planning the leg again from where the cart now is, which the failure cache
///   keeps off the stuck way.
/// Before each, Gunnar holds still for <see cref="HaulLimits.RecoveryBackoffSeconds"/>,
/// doubling each time up to <see cref="HaulLimits.RecoveryBackoffMaxSeconds"/>.
/// After <see cref="HaulLimits.MaxRecoveryAttempts"/> attempts the next stall
/// gives up with a reason. The ceiling is per leg: progress in between does not
/// refill it, so a haul cannot oscillate between a little progress and the same
/// snag. A refused re-plan also spends an attempt, except when only the query
/// budget was spent, which just waits for it.</summary>
internal sealed class HaulRecoveryPolicy
{
    private readonly HaulLimits _limits;
    private readonly BoundedRetry _retry;
    private readonly List<WorkPoint> _backedOffAt = new List<WorkPoint>();
    private HaulAttentionReason _gaveUpWith;

    public HaulRecoveryPolicy(HaulLimits limits)
    {
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        _retry = new BoundedRetry(
            _limits.MaxRecoveryAttempts + 1, _limits.RecoveryBackoffSeconds, _limits.RecoveryBackoffMaxSeconds);
    }

    /// <summary>Manoeuvres spent on this leg.</summary>
    public int AttemptsUsed => Math.Min(_retry.Failures, _limits.MaxRecoveryAttempts);

    public bool HasGivenUp => _gaveUpWith != HaulAttentionReason.Unspecified;

    /// <summary>The motion monitor judged the pull. Stalled and Wedged spend an
    /// attempt; anything else continues.</summary>
    public HaulRecoveryDecision OnMotion(
        HaulMotion motion,
        WorkPoint legTarget,
        WorkPoint pullerPosition,
        WorkPoint cartPosition,
        CartFootprint footprint,
        CartRoutePlanner planner,
        float now)
    {
        if (planner == null)
        {
            throw new ArgumentNullException(nameof(planner));
        }

        if (HasGivenUp)
        {
            return GiveUp(_gaveUpWith);
        }

        if (motion != HaulMotion.Stalled && motion != HaulMotion.Wedged)
        {
            return new HaulRecoveryDecision(HaulRecoveryStep.Continue, now, null, HaulAttentionReason.Unspecified, AttemptsUsed);
        }

        // The cart was going towards Gunnar; that is the way it must not be
        // planned through this place again for a while.
        if (planner.Failures != null && legTarget.IsFinite && cartPosition.IsFinite && pullerPosition.IsFinite)
        {
            planner.Failures.Remember(legTarget, cartPosition, pullerPosition, HaulAttentionReason.Wedged, now);
        }

        RetryDecision retry = _retry.RecordFailure(now);
        if (retry.GiveUp)
        {
            // A held cart holds its puller too, so Stalled and Wedged are the
            // same actionable problem for the player: the cart is stuck.
            return GiveUp(HaulAttentionReason.Wedged);
        }

        if (!BackedOffNear(cartPosition, footprint) &&
            planner.TryPlanBackOff(pullerPosition, cartPosition, footprint, now, out SteeringGoal backOff))
        {
            _backedOffAt.Add(cartPosition);
            return new HaulRecoveryDecision(
                HaulRecoveryStep.BackOff, retry.RetryAt, backOff, HaulAttentionReason.Unspecified, AttemptsUsed);
        }

        return new HaulRecoveryDecision(
            HaulRecoveryStep.Replan, retry.RetryAt, null, HaulAttentionReason.Unspecified, AttemptsUsed);
    }

    /// <summary>A re-plan made during recovery came back refused.
    /// <paramref name="queryBudgetAvailableAt"/> is when the planner's query
    /// budget allows the next query.</summary>
    public HaulRecoveryDecision OnReplanRefused(CartRouteVerdict verdict, float queryBudgetAvailableAt, float now)
    {
        if (HasGivenUp)
        {
            return GiveUp(_gaveUpWith);
        }

        if (verdict == CartRouteVerdict.Suitable || verdict == CartRouteVerdict.Unspecified)
        {
            return new HaulRecoveryDecision(HaulRecoveryStep.Continue, now, null, HaulAttentionReason.Unspecified, AttemptsUsed);
        }

        if (verdict == CartRouteVerdict.BudgetExhausted)
        {
            float when = CartRouteGeometry.IsFiniteValue(queryBudgetAvailableAt)
                ? Math.Max(now, queryBudgetAvailableAt)
                : now + _limits.RecoveryBackoffSeconds;
            return new HaulRecoveryDecision(HaulRecoveryStep.Replan, when, null, HaulAttentionReason.Unspecified, AttemptsUsed);
        }

        HaulAttentionReason reason = CartRouteFindings.TryGetAttention(verdict, out HaulAttentionReason mapped)
            ? mapped
            : HaulAttentionReason.NoRoute;
        RetryDecision retry = _retry.RecordFailure(now);
        if (retry.GiveUp)
        {
            return GiveUp(reason);
        }

        return new HaulRecoveryDecision(
            HaulRecoveryStep.Replan, retry.RetryAt, null, HaulAttentionReason.Unspecified, AttemptsUsed);
    }

    /// <summary>A new leg: a fresh ceiling and no remembered back-offs. The
    /// failure cache is not cleared - that is the point of it.</summary>
    public void Reset()
    {
        _retry.Reset();
        _backedOffAt.Clear();
        _gaveUpWith = HaulAttentionReason.Unspecified;
    }

    private bool BackedOffNear(WorkPoint cartPosition, CartFootprint footprint)
    {
        float samePlace = Math.Max(0.1f, (footprint.WidthMetres * 0.5f) + _limits.SideClearanceMetres);
        foreach (WorkPoint place in _backedOffAt)
        {
            if (place.HorizontalDistanceTo(cartPosition) <= samePlace)
            {
                return true;
            }
        }

        return false;
    }

    private HaulRecoveryDecision GiveUp(HaulAttentionReason reason)
    {
        _gaveUpWith = reason == HaulAttentionReason.Unspecified ? HaulAttentionReason.NoRoute : reason;
        return new HaulRecoveryDecision(
            HaulRecoveryStep.GiveUp, float.PositiveInfinity, null, _gaveUpWith, AttemptsUsed);
    }
}
