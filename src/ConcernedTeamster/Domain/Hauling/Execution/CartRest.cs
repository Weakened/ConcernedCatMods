using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>"The cart is still": its speed has stayed at or below
/// <see cref="HaulLimits.StillSpeedMetresPerSecond"/> for
/// <see cref="HaulLimits.StillForSeconds"/> without a break. A transfer never
/// starts, and a cart is never let go, on a single slow sample.</summary>
internal sealed class CartStillnessTracker
{
    private readonly HaulLimits _limits;
    private float _stillSince = float.NaN;

    public CartStillnessTracker(HaulLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public void Observe(float now, float speedMetresPerSecond)
    {
        // A speed that could not be read is not stillness.
        if (!(speedMetresPerSecond <= _limits.StillSpeedMetresPerSecond) || !HaulExecutionLimits.IsFinite(now))
        {
            _stillSince = float.NaN;
            return;
        }

        if (float.IsNaN(_stillSince))
        {
            _stillSince = now;
        }
    }

    public bool IsStill(float now) =>
        !float.IsNaN(_stillSince) && now - _stillSince >= _limits.StillForSeconds;

    public void Reset() => _stillSince = float.NaN;
}

/// <summary>Whether a cart may be left standing where it is.</summary>
internal readonly struct ParkingDecision
{
    private ParkingDecision(bool safe, string detail)
    {
        Safe = safe;
        Detail = detail ?? string.Empty;
    }

    public bool Safe { get; }

    public string Detail { get; }

    public static ParkingDecision Allow() => new ParkingDecision(true, string.Empty);

    public static ParkingDecision Refuse(string detail) => new ParkingDecision(false, detail);
}

/// <summary>The safe-parking rule (CART-06): Gunnar lets go of a cart only on
/// ground that was actually measured, is out of water, is no steeper than
/// <see cref="HaulLimits.MaxParkingGradeRatio"/> (C2) along or across the
/// cart, with the cart upright and already still. Every unknown refuses: a
/// loaded cart released into a roll cannot be taken back.</summary>
internal static class ParkingJudge
{
    public static ParkingDecision Evaluate(
        ParkingGround ground,
        CartObservation cart,
        bool cartStill,
        HaulLimits limits,
        HaulExecutionLimits execution)
    {
        if (!cart.Resolved)
        {
            return ParkingDecision.Refuse("the cart does not resolve");
        }

        if (!ground.Measured)
        {
            return ParkingDecision.Refuse("the ground under the cart could not be measured");
        }

        if (ground.InWater)
        {
            return ParkingDecision.Refuse("the cart stands in water");
        }

        float along = Math.Abs(ground.GradeAlongRatio);
        float across = Math.Abs(ground.GradeAcrossRatio);
        if (!(along <= limits.MaxParkingGradeRatio) || !(across <= limits.MaxParkingGradeRatio))
        {
            return ParkingDecision.Refuse(FormattableString.Invariant(
                $"the ground slopes {along:0.###} along and {across:0.###} across, more than {limits.MaxParkingGradeRatio:0.###}"));
        }

        if (!(cart.UpDot >= limits.MinUprightDot))
        {
            return ParkingDecision.Refuse("the cart is not upright");
        }

        if (!cartStill)
        {
            return ParkingDecision.Refuse("the cart has not come to rest");
        }

        return ParkingDecision.Allow();
    }
}
