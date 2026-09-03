namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC13 polish 2: the marker palette's mouse-wheel scroll speed,
/// pinned as pure numbers so the regression suite can hold the owner's
/// "roughly 2–3× RC12" target. The palette list is a stock ScrollRect
/// whose wheel step is its scrollSensitivity; RC12 shipped whatever the
/// UI library configured, which the owner judged too slow. The tuning is
/// multiplicative — the shipped step is the RC12 step times
/// <see cref="WheelFactor"/> — with a floor of three list rows per wheel
/// notch (the standard desktop lines-per-notch feel) in case the base
/// sensitivity is ever degenerate. Bounds and map-zoom isolation are
/// unchanged: the ScrollRect still clamps its own content, and the RC11
/// wheel guard keeps the map zoom at net zero under CC UI.</summary>
internal static class PaletteScrollTuning
{
    /// <summary>Multiplier over the RC12 wheel step. Held in the owner's
    /// requested 2–3× window by <c>Rc13PolishTests</c>.</summary>
    public const float WheelFactor = 3f;

    /// <summary>Minimum wheel step in list pixels: three palette rows per
    /// notch (row pitch 26 px = 24 px row + 2 px gap).</summary>
    public const float MinimumStepPixels = 78f;

    /// <summary>The sensitivity to ship for a given RC12 base
    /// sensitivity.</summary>
    public static float Scaled(float rc12Sensitivity)
    {
        float scaled = rc12Sensitivity > 0f ? rc12Sensitivity * WheelFactor : MinimumStepPixels;
        return scaled >= MinimumStepPixels ? scaled : MinimumStepPixels;
    }
}
