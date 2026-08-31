using System;
using System.Text;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>`cc_roads align live` (DEF-v1.0-006): the end-to-end player-vs-
/// road-ink diagnostic. Gathers every live quantity the four error classes
/// need — stored geometry near the player, all three projections for the
/// player position, render resolution at the current zoom, and the live
/// player-marker anchor versus the canonical projection — prints them, and
/// hands the measurements to the pure <see cref="AlignmentVerdicts"/> for
/// the separated A/B/C/D verdicts. Read-only: never touches stored data,
/// pins, or overlays. Every quantity fails soft to n/a.</summary>
internal static class LiveAlignmentProbe
{
    public static string BuildReport(
        Vector3 playerPosition,
        bool standingOnRoad,
        RoadKind classifiedKind,
        RoadSurveyor.TraversalSample? latestSample,
        RoadObservation? lastAccepted,
        bool hasNearest,
        RoadPoint nearestPoint,
        float nearestDistanceMeters,
        RoadOverlayRenderer renderer)
    {
        var info = new StringBuilder();

        info.Append(Invariant($"player: ({playerPosition.x:0.00}, {playerPosition.y:0.00}, {playerPosition.z:0.00})"));
        info.Append('\n').Append(standingOnRoad
            ? Invariant($"terrain: {classifiedKind} road paint under the player")
            : "terrain: no road paint classified under the player");

        info.Append('\n').Append(latestSample is { } sample
            ? Invariant($"latest traversal sample: ({sample.Position.x:0.00}, {sample.Position.z:0.00}) classified={(sample.Classified ? sample.Kind.ToString() : "no")} accepted={(sample.Accepted ? "yes" : "no")}")
            : "latest traversal sample: none yet (surveyor has not sampled)");

        info.Append('\n').Append(lastAccepted is { } accepted
            ? Invariant($"latest accepted pipeline point: {accepted.Source}/{accepted.Kind} ({accepted.Position.X:0.00}, {accepted.Position.Z:0.00})")
            : "latest accepted pipeline point: none this session");

        info.Append('\n').Append(hasNearest
            ? Invariant($"nearest stored road point: ({nearestPoint.X:0.00}, {nearestPoint.Z:0.00}) at {nearestDistanceMeters:0.00} m")
            : "nearest stored road point: none within the search radius");

        // --- Projections of the player position ---------------------------
        bool hasNativePixel = MinimapReflection.TryWorldToPixel(playerPosition, out int pixelX, out int pixelY);
        info.Append('\n').Append(hasNativePixel
            ? Invariant($"native WorldToPixel: ({pixelX}, {pixelY})")
            : "native WorldToPixel: n/a");

        bool hasTextureSize = MinimapReflection.TryGetTextureSize(out int textureSize) && textureSize > 0;
        bool hasMapPoint = MinimapReflection.TryWorldToMapPoint(playerPosition, out float mapX, out float mapY);
        if (hasMapPoint && hasTextureSize)
        {
            info.Append('\n').Append(Invariant(
                $"native WorldToMapPoint: ({mapX:0.00000}, {mapY:0.00000}) = texel ({mapX * textureSize:0.00}, {mapY * textureSize:0.00})"));
        }
        else
        {
            info.Append('\n').Append("native WorldToMapPoint: n/a");
        }

        bool hasOverlay = renderer.TryGetOverlayProjection(playerPosition, out Vector2 overlayCoords, out int overlaySize);
        info.Append('\n').Append(hasOverlay
            ? Invariant($"CC overlay projection: ({overlayCoords.x:0.00}, {overlayCoords.y:0.00}) of {overlaySize}")
            : "CC overlay projection: n/a");

        // B measurement: worst divergence of the mod's projections from the
        // native pixel, in map-texture texels. Convention matches the
        // DEF-v1.0-002 diagnostic (pixel compared without half-texel bias).
        bool hasProjectionDelta = false;
        float projectionDeltaTexels = 0f;
        if (hasNativePixel && hasTextureSize)
        {
            if (hasMapPoint)
            {
                hasProjectionDelta = true;
                projectionDeltaTexels = Delta(mapX * textureSize, mapY * textureSize, pixelX, pixelY);
            }

            if (hasOverlay && overlaySize > 0)
            {
                float scale = (float)textureSize / overlaySize;
                hasProjectionDelta = true;
                projectionDeltaTexels = Mathf.Max(
                    projectionDeltaTexels,
                    Delta(overlayCoords.x * scale, overlayCoords.y * scale, pixelX, pixelY));
            }
        }

        // --- Render resolution and the live marker ------------------------
        string metersPerTexel = MinimapReflection.TryGetPixelSize(out float pixelMeters)
            ? Invariant($"{pixelMeters:0.###}")
            : "n/a";
        string zoomText = MinimapReflection.TryGetLargeZoom(out float zoom)
            ? Invariant($"{zoom:0.####}")
            : "n/a";

        float screenPixelsPerTexel = 0f;
        bool hasMarkerDelta = false;
        float markerDeltaPixels = 0f;
        string markerLine = "live marker: n/a (large map closed?)";

        try
        {
            Minimap? minimap = Minimap.instance;
            if (minimap != null && minimap.m_mapImageLarge != null &&
                minimap.m_largeRoot != null && minimap.m_largeRoot.activeInHierarchy)
            {
                var image = minimap.m_mapImageLarge;
                Rect uvRect = image.uvRect;
                Rect rect = image.rectTransform.rect;

                if (hasTextureSize && uvRect.width > 0f && rect.width > 0f &&
                    MapScreenMath.TryGetPixelsPerGuiUnit(image, out float pixelsPerGuiUnit))
                {
                    screenPixelsPerTexel = pixelsPerGuiUnit * rect.width / (uvRect.width * textureSize);
                }

                info.Append('\n').Append(Invariant(
                    $"large map uvRect: ({uvRect.xMin:0.00000}, {uvRect.yMin:0.00000}, {uvRect.width:0.00000}, {uvRect.height:0.00000}) rect {rect.width:0.#}x{rect.height:0.#}"));

                RectTransform? marker = minimap.m_largeMarker;
                if (hasMapPoint && marker != null && marker.gameObject.activeInHierarchy &&
                    uvRect.width > 0f && uvRect.height > 0f)
                {
                    (float expectedX, float expectedY) = RoadVectorMath.VanillaLocalGuiPosition(
                        mapX, mapY, uvRect.xMin, uvRect.yMin, uvRect.width, uvRect.height,
                        rect.width, rect.height);
                    if (MapScreenMath.TryLocalGuiToScreenPoint(image, new Vector2(expectedX, expectedY), out Vector2 expectedScreen) &&
                        MapScreenMath.TryTransformToScreenPoint(image, marker, out Vector2 actualScreen))
                    {
                        hasMarkerDelta = true;
                        markerDeltaPixels = (actualScreen - expectedScreen).magnitude;
                        markerLine = Invariant(
                            $"live marker: screen ({actualScreen.x:0.0}, {actualScreen.y:0.0}) vs expected ({expectedScreen.x:0.0}, {expectedScreen.y:0.0})");
                    }
                }
                else if (marker == null || !marker.gameObject.activeInHierarchy)
                {
                    markerLine = "live marker: n/a (marker hidden — aboard a ship?)";
                }
            }
        }
        catch (Exception exception)
        {
            markerLine = "live marker: n/a (" + exception.Message + ")";
        }

        string textureSizeText = hasTextureSize
            ? textureSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "n/a";
        info.Append('\n').Append(Invariant(
            $"mapTextureSize={textureSizeText} metersPerTexel={metersPerTexel} largeZoom={zoomText} screenPxPerTexel={screenPixelsPerTexel:0.0}"));
        info.Append('\n').Append(markerLine);

        bool vectorActive = renderer.VectorLayerActive;
        info.Append('\n').Append(vectorActive
            ? "vector road layer: ACTIVE (sub-texel large-map ink)"
            : "vector road layer: inactive");

        string verdicts = AlignmentVerdicts.Evaluate(
            standingOnRoad,
            hasNearest,
            nearestDistanceMeters,
            hasProjectionDelta,
            projectionDeltaTexels,
            screenPixelsPerTexel,
            vectorActive,
            hasMarkerDelta,
            markerDeltaPixels);

        return info.ToString() + "\n" + verdicts;
    }

    private static float Delta(float x, float y, int pixelX, int pixelY)
    {
        float dx = x - pixelX;
        float dy = y - pixelY;
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
