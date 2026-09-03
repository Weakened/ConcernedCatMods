using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC14 final-smoke fix 2: the Atlas drawer's dragged position is
/// a durable preference, not per-session state. This pure rule owns the
/// three testable decisions: (a) the stored "x,y" string round-trips
/// through invariant culture and anything malformed or non-finite reads as
/// "nothing stored" (default dock semantics preserved); (b) a restored
/// offset is clamped so the WHOLE panel sits inside the canvas — an old
/// coordinate saved at another resolution or UI scale can never strand the
/// panel off-screen; (c) when the scaled panel is larger than the canvas
/// on an axis (legal at UiScale 1.6 on small reference rects) the panel
/// centers on that axis instead of oscillating between impossible
/// bounds. Offsets are measured from the canvas right-center anchor
/// (anchor 1, 0.5), matching the drawer's build-time anchoring.</summary>
internal static class PanelPositionRule
{
    /// <summary>Parses a stored "x,y" offset. False (and zeroed outputs)
    /// for null/empty/malformed/non-finite input, which callers treat as
    /// "use the default dock".</summary>
    public static bool TryParse(string? stored, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        string[] parts = stored!.Split(',');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY))
        {
            return false;
        }

        if (!IsFinite(parsedX) || !IsFinite(parsedY))
        {
            return false;
        }

        x = parsedX;
        y = parsedY;
        return true;
    }

    /// <summary>Serializes an offset for storage; the inverse of
    /// <see cref="TryParse"/> for every finite input.</summary>
    public static string Serialize(float x, float y)
    {
        return x.ToString("R", CultureInfo.InvariantCulture) + "," +
            y.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Clamps a right-center-anchored panel-center offset so the
    /// whole scaled panel stays inside the canvas rect. Axes where the
    /// panel exceeds the canvas center instead.</summary>
    public static (float X, float Y) Clamp(
        float x,
        float y,
        float panelWidth,
        float panelHeight,
        float uiScale,
        float canvasWidth,
        float canvasHeight)
    {
        float halfWidth = panelWidth * uiScale / 2f;
        float halfHeight = panelHeight * uiScale / 2f;

        // Anchor (1, 0.5): x offsets are negative into the canvas, y
        // offsets are centered. Panel center in canvas space is
        // (canvasWidth + x, canvasHeight / 2 + y).
        float clampedX = ClampAxis(x, halfWidth - canvasWidth, -halfWidth);
        float clampedY = ClampAxis(y, halfHeight - (canvasHeight / 2f), (canvasHeight / 2f) - halfHeight);
        return (clampedX, clampedY);
    }

    private static float ClampAxis(float value, float min, float max)
    {
        if (min > max)
        {
            // Panel larger than the canvas on this axis: center it.
            return (min + max) / 2f;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
