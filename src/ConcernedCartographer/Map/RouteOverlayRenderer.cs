using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Draws routes on their own Jötunn overlay ("CC Routes", with its
/// own toggle). Styles: solid lines, dashed (alternate segments), dotted
/// (points only). Colors come from the route or its status defaults.
/// Full-texture redraws only — route edits are debounced upstream.</summary>
internal sealed class RouteOverlayRenderer
{
    private const string OverlayName = "CC Routes";

    private Color32 PlannedColor => _settings.HighContrast.Value
        ? new Color32(0, 220, 255, 255)
        : new Color32(210, 210, 235, 255);

    private Color32 ActiveColor => _settings.HighContrast.Value
        ? new Color32(255, 160, 0, 255)
        : new Color32(255, 200, 80, 255);

    private Color32 DoneColor => _settings.HighContrast.Value
        ? new Color32(200, 200, 200, 255)
        : new Color32(135, 135, 135, 255);

    private readonly CartographerSettings _settings;
    private readonly RateLimitedLog _rateLimited;

    public RouteOverlayRenderer(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _rateLimited = new RateLimitedLog(log, 5f);
    }

    public void RedrawAll(RouteStore routes)
    {
        try
        {
            var overlay = MinimapManager.Instance.GetMapOverlay(OverlayName);
            int size = overlay.TextureSize;
            Color32[] pixels = new Color32[size * size];

            foreach (AtlasRoute route in routes.Living)
            {
                if (route.Archived || route.Points.Count == 0)
                {
                    continue;
                }

                Color32 color = route.ColorArgb is int argb
                    ? FromArgb(argb)
                    : route.Status == RouteStatus.Active ? ActiveColor
                    : route.Status == RouteStatus.Done ? DoneColor
                    : PlannedColor;

                if (route.Points.Count == 1 || route.Style == RouteStyle.Dotted)
                {
                    foreach (RoadPoint point in route.Points)
                    {
                        DrawSegmentIntoBuffer(pixels, size, point, point, color);
                    }

                    continue;
                }

                for (int index = 1; index < route.Points.Count; index++)
                {
                    if (route.Style == RouteStyle.Dashed && index % 2 == 0)
                    {
                        continue;
                    }

                    DrawSegmentIntoBuffer(pixels, size, route.Points[index - 1], route.Points[index], color);
                }
            }

            overlay.OverlayTex.SetPixels32(pixels);
            overlay.OverlayTex.Apply(false);
        }
        catch (Exception exception)
        {
            _rateLimited.Warning("route-redraw", $"Could not redraw routes: {exception.Message}");
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            MinimapManager.Instance.GetMapOverlay(OverlayName).Enabled = enabled;
        }
        catch (Exception exception)
        {
            _rateLimited.Warning("route-toggle", $"Could not toggle the route overlay: {exception.Message}");
        }
    }

    private void DrawSegmentIntoBuffer(Color32[] pixels, int size, RoadPoint start, RoadPoint end, Color32 color)
    {
        Vector2 a = MinimapManager.Instance.WorldToOverlayCoords(new Vector3(start.X, start.Y, start.Z), size);
        Vector2 b = MinimapManager.Instance.WorldToOverlayCoords(new Vector3(end.X, end.Y, end.Z), size);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(a.x), 0, size - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(a.y), 0, size - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(b.x), 0, size - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(b.y), 0, size - 1);

        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        int width = Math.Max(1, _settings.LineWidthPixels.Value);
        int low = width / 2;
        int high = width - 1 - low;

        while (true)
        {
            for (int py = y0 - low; py <= y0 + high; py++)
            {
                if (py < 0 || py >= size)
                {
                    continue;
                }

                for (int px = x0 - low; px <= x0 + high; px++)
                {
                    if (px >= 0 && px < size)
                    {
                        pixels[(py * size) + px] = color;
                    }
                }
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static Color32 FromArgb(int argb)
    {
        return new Color32(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
    }
}
