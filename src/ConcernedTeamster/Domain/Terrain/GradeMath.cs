using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Terrain;

/// <summary>Deterministic grade math (CT-004). Pure functions only: the
/// adapter supplies ground heights sampled ahead of and behind the cart along
/// its heading, and everything here is provable against synthetic fixtures.
/// Smoothing spec: exponential moving average with <see cref="SmoothingAlpha"/>,
/// then direction classification with hysteresis (enter a slope state at
/// ±<see cref="DirectionEnterThresholdPercent"/>, leave it below
/// ±<see cref="DirectionExitThresholdPercent"/>) so sample noise cannot make
/// the display oscillate.</summary>
public static class GradeMath
{
    /// <summary>EMA weight of the newest instant grade. 0.35 settles a step
    /// change in ~5 samples (2.5 s at the default interval) while absorbing
    /// single-sample terrain noise.</summary>
    public const float SmoothingAlpha = 0.35f;

    /// <summary>Smoothed grade magnitude that enters Climbing/Descending.</summary>
    public const float DirectionEnterThresholdPercent = 4f;

    /// <summary>Smoothed grade magnitude below which the state returns to
    /// Level. The gap to the enter threshold is the hysteresis band.</summary>
    public const float DirectionExitThresholdPercent = 2f;

    /// <summary>Grade percent (rise over horizontal run) from two ground
    /// heights sampled at horizontal points ahead of and behind the cart.
    /// Positive means uphill toward the heading. Non-finite inputs or a
    /// non-positive run yield NaN — the adapter reports grade unavailable.</summary>
    public static float ComputeInstantGradePercent(float heightAhead, float heightBehind, float runMeters)
    {
        if (float.IsNaN(heightAhead) || float.IsInfinity(heightAhead) ||
            float.IsNaN(heightBehind) || float.IsInfinity(heightBehind) ||
            float.IsNaN(runMeters) || float.IsInfinity(runMeters) || runMeters <= 0f)
        {
            return float.NaN;
        }

        return (heightAhead - heightBehind) / runMeters * 100f;
    }

    /// <summary>EMA step. A NaN previous value (first sample, or the entry
    /// was evicted) restarts from the instant value; a NaN instant keeps the
    /// previous value unchanged.</summary>
    public static float Smooth(float previousSmoothedPercent, float instantGradePercent)
    {
        if (float.IsNaN(instantGradePercent))
        {
            return previousSmoothedPercent;
        }

        if (float.IsNaN(previousSmoothedPercent))
        {
            return instantGradePercent;
        }

        return previousSmoothedPercent + SmoothingAlpha * (instantGradePercent - previousSmoothedPercent);
    }

    /// <summary>Hysteresis classification: state changes only when the
    /// smoothed grade crosses the enter threshold (or falls back through the
    /// exit threshold), so values wandering inside the band keep the previous
    /// answer instead of flickering.</summary>
    public static GradeDirection ClassifyDirection(float smoothedGradePercent, GradeDirection previousDirection)
    {
        if (float.IsNaN(smoothedGradePercent))
        {
            return previousDirection;
        }

        float magnitude = Math.Abs(smoothedGradePercent);
        bool uphill = smoothedGradePercent > 0f;

        switch (previousDirection)
        {
            case GradeDirection.Climbing:
                if (smoothedGradePercent >= DirectionExitThresholdPercent)
                {
                    return GradeDirection.Climbing;
                }

                break;

            case GradeDirection.Descending:
                if (smoothedGradePercent <= -DirectionExitThresholdPercent)
                {
                    return GradeDirection.Descending;
                }

                break;
        }

        if (magnitude >= DirectionEnterThresholdPercent)
        {
            return uphill ? GradeDirection.Climbing : GradeDirection.Descending;
        }

        return GradeDirection.Level;
    }
}
