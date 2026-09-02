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
/// cadence from the live zoom.</summary>
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
        if (points is null || points.Count < 2 || dashOn <= 0f || cycle <= 0f || stamp is null)
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
            if (length <= 1e-6f)
            {
                continue;
            }

            float directionX = deltaX / length;
            float directionY = deltaY / length;
            float travelled = 0f;
            while (travelled < length && stamps < maxStamps)
            {
                float positionInCycle = phase % cycle;
                bool on = positionInCycle < dashOn;
                float remainingInState = on ? dashOn - positionInCycle : cycle - positionInCycle;
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

                travelled += step;
                phase += step;
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
        if (points is null || points.Count < 2 || spacing <= 0f || stamp is null)
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
            float remaining = (float)Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (remaining <= 1e-6f)
            {
                continue;
            }

            float directionX = deltaX / remaining;
            float directionY = deltaY / remaining;
            float cursorX = previousX;
            float cursorY = previousY;
            while (untilNextDot <= remaining && stamps < maxStamps)
            {
                cursorX += directionX * untilNextDot;
                cursorY += directionY * untilNextDot;
                remaining -= untilNextDot;
                stamp(cursorX, cursorY);
                stamps++;
                untilNextDot = spacing;
            }

            untilNextDot -= remaining;
        }

        return stamps;
    }
}
