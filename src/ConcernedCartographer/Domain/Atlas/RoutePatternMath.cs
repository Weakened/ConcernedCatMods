using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The ONE geometric dash/dot cadence walker behind both route
/// presentations (RC10 feedback 5/6): the large-map vector layer and the
/// texture overlay both stamp through here, so the pattern math cannot
/// drift apart. Distances are walked along the whole polyline with the
/// phase carried across vertices — the pattern depends only on geometry,
/// never on how the stored points happen to be spaced. Units are the
/// caller's (screen-derived baked units on the vector path, texels on the
/// texture path); zoom stability comes from the caller deriving the
/// cadence from the live zoom.
///
/// RC12 blocker 3: both walkers are structurally terminating. Dots are
/// stamped from an INTEGER per-segment count (never a float countdown
/// that can stall below float precision on huge coordinates), the dash
/// walk carries its phase modulo one cycle and aborts on any non-advancing
/// step, and segments with non-finite geometry are skipped. Combined with
/// real maxStamps budgets from every caller, no route data — however long
/// or corrupt — can spin these loops long enough to stall a frame.</summary>
internal static class RoutePatternMath
{
    public delegate void StampSegment(float startX, float startY, float endX, float endY);

    public delegate void StampDot(float x, float y);

    /// <summary>Walks a dash pattern (dashOn drawn, dashOff skipped) along
    /// the polyline. Returns the number of stamped dash segments; stops at
    /// maxStamps.</summary>
    public static int WalkDashes(
        IReadOnlyList<(float X, float Y)> points,
        float dashOn,
        float dashOff,
        int maxStamps,
        StampSegment stamp)
    {
        float cycle = dashOn + dashOff;
        if (points is null || points.Count < 2 || stamp is null ||
            !IsFinitePositive(dashOn) || !IsFinitePositive(cycle))
        {
            return 0;
        }

        int stamps = 0;
        float phase = 0f;
        for (int index = 1; index < points.Count; index++)
        {
            (float previousX, float previousY) = points[index - 1];
            (float currentX, float currentY) = points[index];
            float deltaX = currentX - previousX;
            float deltaY = currentY - previousY;
            float length = (float)Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (!IsFinitePositive(length) || length <= 1e-6f)
            {
                // Zero-length or non-finite segment: no distance to pattern.
                continue;
            }

            float directionX = deltaX / length;
            float directionY = deltaY / length;
            float travelled = 0f;
            while (travelled < length && stamps < maxStamps)
            {
                bool on = phase < dashOn;
                float remainingInState = on ? dashOn - phase : cycle - phase;
                float step = Math.Min(remainingInState, length - travelled);
                if (on && step > 1e-4f)
                {
                    stamp(
                        previousX + (directionX * travelled),
                        previousY + (directionY * travelled),
                        previousX + (directionX * (travelled + step)),
                        previousY + (directionY * (travelled + step)));
                    stamps++;
                }

                float advanced = travelled + step;
                if (!(advanced > travelled))
                {
                    // Float precision exhausted (astronomical coordinates):
                    // the walk can no longer advance; abandon this segment
                    // rather than spinning forever.
                    break;
                }

                travelled = advanced;
                phase = (phase + step) % cycle;
            }

            if (stamps >= maxStamps)
            {
                break;
            }
        }

        return stamps;
    }

    /// <summary>Stamps dots at a fixed geometric spacing along the
    /// polyline, starting at the first point. Returns the dot count;
    /// stops at maxStamps.</summary>
    public static int WalkDots(
        IReadOnlyList<(float X, float Y)> points,
        float spacing,
        int maxStamps,
        StampDot stamp)
    {
        if (points is null || points.Count < 2 || stamp is null || !IsFinitePositive(spacing))
        {
            return 0;
        }

        int stamps = 0;
        float untilNextDot = 0f;
        for (int index = 1; index < points.Count && stamps < maxStamps; index++)
        {
            (float previousX, float previousY) = points[index - 1];
            (float currentX, float currentY) = points[index];
            float deltaX = currentX - previousX;
            float deltaY = currentY - previousY;
            float length = (float)Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (!IsFinitePositive(length) || length <= 1e-6f)
            {
                continue;
            }

            if (untilNextDot > length)
            {
                untilNextDot -= length;
                continue;
            }

            // Integer dot count for this segment, capped by the remaining
            // budget: each dot's position is computed from the segment
            // START, so precision never degrades along the walk and the
            // loop bound is a plain int.
            float directionX = deltaX / length;
            float directionY = deltaY / length;
            double dotsThatFit = Math.Floor((length - untilNextDot) / (double)spacing) + 1d;
            int count = (int)Math.Min(dotsThatFit, maxStamps - stamps);
            for (int dot = 0; dot < count; dot++)
            {
                float along = untilNextDot + (spacing * dot);
                stamp(previousX + (directionX * along), previousY + (directionY * along));
                stamps++;
            }

            untilNextDot = untilNextDot + (spacing * count) - length;
        }

        return stamps;
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }
}
