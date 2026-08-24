using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

internal sealed class RoadOverlayRenderer
{
    private const string DirtOverlayName = "Concerned Cartographer - Dirt Paths";
    private const string PavedOverlayName = "Concerned Cartographer - Paved Roads";

    private static readonly Color32 DirtColor = new(138, 96, 58, 230);
    private static readonly Color32 PavedColor = new(180, 184, 188, 235);

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;

    public RoadOverlayRenderer(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    public void RedrawAll(RoadAtlas atlas)
    {
        try
        {
            var dirtOverlay = MinimapManager.Instance.GetMapOverlay(DirtOverlayName);
            var pavedOverlay = MinimapManager.Instance.GetMapOverlay(PavedOverlayName);
            int textureSize = dirtOverlay.TextureSize;

            Color32[] dirtPixels = new Color32[textureSize * textureSize];
            Color32[] pavedPixels = new Color32[textureSize * textureSize];

            foreach (RoadStroke stroke in atlas.Strokes)
            {
                if (stroke.Points.Count < 2)
                {
                    continue;
                }

                Color32[] target = stroke.Kind == RoadKind.Dirt ? dirtPixels : pavedPixels;
                for (int index = 1; index < stroke.Points.Count; index++)
                {
                    DrawIntoBuffer(
                        target,
                        textureSize,
                        stroke.Points[index - 1],
                        stroke.Points[index],
                        stroke.Kind == RoadKind.Dirt ? DirtColor : PavedColor);
                }
            }

            dirtOverlay.OverlayTex.SetPixels32(dirtPixels);
            pavedOverlay.OverlayTex.SetPixels32(pavedPixels);
            dirtOverlay.OverlayTex.Apply(false);
            pavedOverlay.OverlayTex.Apply(false);
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not rebuild road map overlays: {exception}");
        }
    }

    public void DrawSegment(RoadSegment segment)
    {
        try
        {
            string overlayName = segment.Kind == RoadKind.Dirt ? DirtOverlayName : PavedOverlayName;
            Color32 color = segment.Kind == RoadKind.Dirt ? DirtColor : PavedColor;
            var overlay = MinimapManager.Instance.GetMapOverlay(overlayName);
            int size = overlay.TextureSize;

            Vector2 start = MinimapManager.Instance.WorldToOverlayCoords(segment.Start, size);
            Vector2 end = MinimapManager.Instance.WorldToOverlayCoords(segment.End, size);
            DrawLine(
                (x, y) => overlay.OverlayTex.SetPixel(x, y, color),
                size,
                Mathf.RoundToInt(start.x),
                Mathf.RoundToInt(start.y),
                Mathf.RoundToInt(end.x),
                Mathf.RoundToInt(end.y),
                _settings.LineWidthPixels.Value);
            overlay.OverlayTex.Apply(false);
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not draw an incremental road segment: {exception.Message}");
        }
    }

    private void DrawIntoBuffer(
        Color32[] pixels,
        int size,
        Vector3 worldStart,
        Vector3 worldEnd,
        Color32 color)
    {
        Vector2 start = MinimapManager.Instance.WorldToOverlayCoords(worldStart, size);
        Vector2 end = MinimapManager.Instance.WorldToOverlayCoords(worldEnd, size);

        DrawLine(
            (x, y) => pixels[(y * size) + x] = color,
            size,
            Mathf.RoundToInt(start.x),
            Mathf.RoundToInt(start.y),
            Mathf.RoundToInt(end.x),
            Mathf.RoundToInt(end.y),
            _settings.LineWidthPixels.Value);
    }

    private static void DrawLine(
        Action<int, int> setPixel,
        int size,
        int x0,
        int y0,
        int x1,
        int y1,
        int lineWidth)
    {
        x0 = Mathf.Clamp(x0, 0, size - 1);
        y0 = Mathf.Clamp(y0, 0, size - 1);
        x1 = Mathf.Clamp(x1, 0, size - 1);
        y1 = Mathf.Clamp(y1, 0, size - 1);

        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        int radius = Math.Max(0, (lineWidth - 1) / 2);

        while (true)
        {
            Stamp(setPixel, size, x0, y0, radius);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int doubledError = 2 * error;
            if (doubledError >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubledError <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void Stamp(Action<int, int> setPixel, int size, int centerX, int centerY, int radius)
    {
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            if (y < 0 || y >= size)
            {
                continue;
            }

            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x >= 0 && x < size)
                {
                    setPixel(x, y);
                }
            }
        }
    }
}
