using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Screen-space measurements over the large-map image, shared by
/// the vector road layer (line-width targeting) and the live alignment
/// diagnostic (marker/texel deltas in true screen pixels). Everything
/// fails soft with false.</summary>
internal static class MapScreenMath
{
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>The camera RectTransformUtility needs for the canvas the
    /// graphic lives on (null for a screen-space-overlay canvas).</summary>
    public static Camera? CanvasCamera(Graphic graphic)
    {
        Canvas? canvas = graphic.canvas;
        if (canvas == null)
        {
            return null;
        }

        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    /// <summary>Screen pixels per GUI unit of the image rect (uniform under
    /// Valheim's GuiScaler; measured along the bottom edge).</summary>
    public static bool TryGetPixelsPerGuiUnit(RawImage image, out float pixelsPerGuiUnit)
    {
        pixelsPerGuiUnit = 1f;
        if (image == null)
        {
            return false;
        }

        RectTransform rectTransform = image.rectTransform;
        float width = rectTransform.rect.width;
        if (width <= 0f)
        {
            return false;
        }

        Camera? camera = CanvasCamera(image);
        rectTransform.GetWorldCorners(Corners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
        Vector2 bottomRight = RectTransformUtility.WorldToScreenPoint(camera, Corners[3]);
        float screenWidth = (bottomRight - bottomLeft).magnitude;
        if (screenWidth <= 0f)
        {
            return false;
        }

        pixelsPerGuiUnit = screenWidth / width;
        return true;
    }

    /// <summary>Screen point of a bottom-left-relative local GUI position on
    /// the image rect (the space RoadVectorMath works in).</summary>
    public static bool TryLocalGuiToScreenPoint(RawImage image, Vector2 bottomLeftRelative, out Vector2 screenPoint)
    {
        screenPoint = default;
        if (image == null)
        {
            return false;
        }

        RectTransform rectTransform = image.rectTransform;
        Vector2 local = rectTransform.rect.min + bottomLeftRelative;
        Vector3 world = rectTransform.TransformPoint(local);
        screenPoint = RectTransformUtility.WorldToScreenPoint(CanvasCamera(image), world);
        return true;
    }

    /// <summary>Screen point of an arbitrary transform's pivot (e.g. the
    /// live player marker).</summary>
    public static bool TryTransformToScreenPoint(RawImage referenceImage, Transform target, out Vector2 screenPoint)
    {
        screenPoint = default;
        if (referenceImage == null || target == null)
        {
            return false;
        }

        screenPoint = RectTransformUtility.WorldToScreenPoint(CanvasCamera(referenceImage), target.position);
        return true;
    }
}
