using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Draws routes on their own Jötunn overlay ("CC Routes", with its
/// own toggle). Styles (RC8-9): solid lines; dashed and dotted use a
/// GEOMETRIC cadence — a fixed on/off (or dot spacing) distance walked
/// along the polyline in overlay texels, with the phase carried across
/// vertices — never stored-point parity, so the pattern looks the same
/// regardless of how the points happen to be spaced and stays a real
/// dash/dot pattern at every zoom. Colors come from the route or its
/// status defaults. Full-texture redraws only — route edits are debounced
/// upstream.</summary>
internal sealed class RouteOverlayRenderer
{
    private const string OverlayName = "CC Routes";

    // Pattern cadence in overlay texels (one texel ≈ 11.6 m of world on
    // the default 2048 map): dashes 5 on / 4 off, dots every 4.
    private const float DashOnTexels = 5f;
    private const float DashOffTexels = 4f;
    private const float DotSpacingTexels = 4f;

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

                if (route.Points.Count == 1)
                {
                    DrawSegmentIntoBuffer(pixels, size, route.Points[0], route.Points[0], color);
                    continue;
                }

                switch (route.Style)
                {
                    case RouteStyle.Dashed:
                        DrawDashedPolyline(pixels, size, route.Points, color);
                        break;
                    case RouteStyle.Dotted:
                        DrawDottedPolyline(pixels, size, route.Points, color);
                        break;
                    default:
                        for (int index = 1; index < route.Points.Count; index++)
                        {
                            DrawSegmentIntoBuffer(pixels, size, route.Points[index - 1], route.Points[index], color);
                        }

                        break;
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

    /// <summary>Dash pattern walked by distance along the whole polyline;
    /// the phase carries across vertices so dashes flow through corners.</summary>
    private void DrawDashedPolyline(Color32[] pixels, int size, IReadOnlyList<RoadPoint> points, Color32 color)
    {
        const float cycle = DashOnTexels + DashOffTexels;
        float phase = 0f;
        Vector2 previous = Project(points[0], size);
        for (int index = 1; index < points.Count; index++)
        {
            Vector2 current = Project(points[index], size);
            float length = Vector2.Distance(previous, current);
            if (length <= 0.001f)
            {
                continue;
            }

            Vector2 direction = (current - previous) / length;
            float travelled = 0f;
            while (travelled < length)
            {
                float positionInCycle = phase % cycle;
                bool on = positionInCycle < DashOnTexels;
                float remainingInState = on ? DashOnTexels - positionInCycle : cycle - positionInCycle;
                float step = Mathf.Min(remainingInState, length - travelled);
                if (on && step > 0.05f)
                {
                    DrawLineIntoBuffer(
                        pixels, size,
                        previous + (direction * travelled),
                        previous + (direction * (travelled + step)),
                        color);
                }

                travelled += step;
                phase += step;
            }

            previous = current;
        }
    }

    /// <summary>Separated dots stamped at a fixed geometric spacing along
    /// the polyline, independent of stored point positions.</summary>
    private void DrawDottedPolyline(Color32[] pixels, int size, IReadOnlyList<RoadPoint> points, Color32 color)
    {
        float untilNextDot = 0f;
        Vector2 previous = Project(points[0], size);
        for (int index = 1; index < points.Count; index++)
        {
            Vector2 current = Project(points[index], size);
            float remaining = Vector2.Distance(previous, current);
            if (remaining <= 0.001f)
            {
                continue;
            }

            Vector2 direction = (current - previous) / remaining;
            Vector2 cursor = previous;
            while (untilNextDot <= remaining)
            {
                cursor += direction * untilNextDot;
                remaining -= untilNextDot;
                DrawLineIntoBuffer(pixels, size, cursor, cursor, color);
                untilNextDot = DotSpacingTexels;
            }

            untilNextDot -= remaining;
            previous = current;
        }
    }

    private static Vector2 Project(RoadPoint point, int size)
    {
        return MinimapManager.Instance.WorldToOverlayCoords(new Vector3(point.X, point.Y, point.Z), size);
    }

    private void DrawSegmentIntoBuffer(Color32[] pixels, int size, RoadPoint start, RoadPoint end, Color32 color)
    {
        DrawLineIntoBuffer(pixels, size, Project(start, size), Project(end, size), color);
    }

    private void DrawLineIntoBuffer(Color32[] pixels, int size, Vector2 a, Vector2 b, Color32 color)
    {
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
