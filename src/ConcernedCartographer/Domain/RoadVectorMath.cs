namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure affine math for the high-precision large-map road layer
/// (DEF-v1.0-006). Vanilla positions large-map content with
/// <c>local = ((m − uvMin) / uvSize) · rectSize</c> over the map image's
/// uvRect (per-axis — the large-map uvRect is aspect-corrected, so x and
/// y scales differ). Road vertices are baked ONCE in zoom-independent
/// "map space" (<c>(m − 0.5) · refSize</c>) and a container transform
/// reproduces the vanilla formula exactly for any pan/zoom, so vertices
/// keep sub-texel precision and pan/zoom is a transform update, not a
/// mesh rebuild.</summary>
internal static class RoadVectorMath
{
    /// <summary>Zoom-independent reference size for baked coordinates.</summary>
    public const float ReferenceSize = 2048f;

    /// <summary>Bakes a normalized map point (uv) into map-space vertex
    /// coordinates centered on the map middle.</summary>
    public static (float X, float Y) Bake(float mapX, float mapY)
    {
        return ((mapX - 0.5f) * ReferenceSize, (mapY - 0.5f) * ReferenceSize);
    }

    /// <summary>The container scale and bottom-left-relative position that
    /// make <c>position + scale · baked</c> equal vanilla's
    /// <c>((m − uvMin)/uvSize) · rectSize</c> for every map point.</summary>
    public static (float ScaleX, float ScaleY, float PositionX, float PositionY) ContentTransform(
        float uvMinX, float uvMinY, float uvWidth, float uvHeight, float rectWidth, float rectHeight)
    {
        float scaleX = rectWidth / (ReferenceSize * uvWidth);
        float scaleY = rectHeight / (ReferenceSize * uvHeight);
        float positionX = (0.5f - uvMinX) / uvWidth * rectWidth;
        float positionY = (0.5f - uvMinY) / uvHeight * rectHeight;
        return (scaleX, scaleY, positionX, positionY);
    }

    /// <summary>Half line width in baked units for a target on-screen
    /// width, at the current zoom (uvWidth) and rect width.</summary>
    public static float BakedHalfWidth(float screenPixels, float uvWidth, float rectWidth)
    {
        if (rectWidth <= 0f)
        {
            return screenPixels / 2f;
        }

        return screenPixels * ReferenceSize * uvWidth / rectWidth / 2f;
    }

    /// <summary>Vanilla's own MapPointToLocalGuiPos, replicated for
    /// verification: local GUI position (from the map rect's bottom-left)
    /// of a normalized map point under the given uv window.</summary>
    public static (float X, float Y) VanillaLocalGuiPosition(
        float mapX, float mapY, float uvMinX, float uvMinY, float uvWidth, float uvHeight,
        float rectWidth, float rectHeight)
    {
        return ((mapX - uvMinX) / uvWidth * rectWidth, (mapY - uvMinY) / uvHeight * rectHeight);
    }
}
