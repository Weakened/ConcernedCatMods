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
/// own toggle) — since RC10 this texture is the MINIMAP + fallback
/// presentation, suppressed on the large map while the shared vector layer
/// (RoadVectorLayer) draws routes in screen-space width/cadence there
/// (feedback 5: one route presentation at a time, exactly like roads).
/// Styles (RC8-9): solid lines; dashed and dotted use a GEOMETRIC cadence
/// — a fixed on/off (or dot spacing) distance walked along the polyline
/// in overlay texels, with the phase carried across vertices — never
/// stored-point parity, so the pattern looks the same regardless of how
/// the points happen to be spaced. Colors come from <see cref="RouteInk"/>
/// (shared with the vector layer). Full-texture redraws only — route
/// edits are debounced upstream. The Jötunn checkbox is hooked as the
/// route layer's real user switch (feedback 7).</summary>
internal sealed class RouteOverlayRenderer
{
    private const string OverlayName = "CC Routes";

    // Pattern cadence in overlay texels (one texel ≈ 11.6 m of world on
    // the default 2048 map): dashes 5 on / 4 off, dots every 3 (RC10
    // feedback 6 tightened dots from 4 for a readable continuous cadence).
    private const float DashOnTexels = 5f;
    private const float DashOffTexels = 4f;
    private const float DotSpacingTexels = 3f;

    /// <summary>RC12 blocker 3: a REAL per-route stamp budget for the
    /// texture pattern walk (int.MaxValue before, which let corrupt or
    /// absurdly long geometry spin the walker for seconds). A full 2048²
    /// texture diagonal is ~2 900 texels ≈ 1 000 dots; 24 000 covers any
    /// legitimate route many times over and keeps the worst case bounded
    /// to a few milliseconds.</summary>
    private const int MaxPatternStampsPerRoute = 24_000;

    private readonly CartographerSettings _settings;
    private readonly RateLimitedLog _rateLimited;
    private readonly OverlayUserToggleHook _toggleHook = new();
    private MinimapManager.MapOverlay? _overlay;
    private bool _userEnabled = true;
    private bool _lastSuppressed;

    public RouteOverlayRenderer(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _rateLimited = new RateLimitedLog(log, 5f);
    }

    /// <summary>Raised when the player flips the CC Routes checkbox in
    /// Jötunn's overlay panel, so the runtime can mirror it into the
    /// vector route presentation.</summary>
    public event Action<bool>? UserToggled;

    /// <summary>RC14 fix 4: drops the cached overlay handle at a fresh
    /// Minimap so the next use re-resolves (mirrors
    /// RoadOverlayRenderer.ResetMapSession).</summary>
    public void ResetMapSession()
    {
        _overlay = null;
    }

    /// <summary>Per-frame visibility drive: hooks the Jötunn checkbox as
    /// the user switch and applies the RC8-1 one-presentation rule (the
    /// texture hides on the large map while the vector layer draws
    /// routes, and returns for the minimap/fallback).</summary>
    public void TickVisibility(bool vectorRoutesActive)
    {
        _lastSuppressed = vectorRoutesActive;
        if (TryGetOverlay(out MinimapManager.MapOverlay? overlay))
        {
            _toggleHook.Maintain(overlay!, _userEnabled, HandleUserToggle);
        }

        ApplyVisibility();
    }

    private void HandleUserToggle(bool enabled)
    {
        SetEnabled(enabled);
        UserToggled?.Invoke(enabled);
    }

    private void ApplyVisibility()
    {
        // RC11 feedback 1: unconditional write, no applied-state cache —
        // Jötunn's own listener writes Enabled first on checkbox clicks,
        // so a cache diverges exactly when it matters (see
        // RoadOverlayRenderer.SetTextureOverlayEnabled). The setter no-ops
        // on unchanged values.
        bool effective = OverlayVisibilityRule.EffectiveTexture(_userEnabled, _lastSuppressed);
        if (TryGetOverlay(out MinimapManager.MapOverlay? overlay))
        {
            try
            {
                overlay!.Enabled = effective;
            }
            catch (Exception exception)
            {
                _rateLimited.Warning("route-toggle", $"Could not toggle the route overlay: {exception.Message}");
            }
        }

        _toggleHook.SyncCheckbox(OverlayVisibilityRule.CheckboxShows(_userEnabled));
    }

    private bool TryGetOverlay(out MinimapManager.MapOverlay? overlay)
    {
        // RC14 fix 4: presence is not liveness — Jötunn destroys the
        // overlay texture on Minimap teardown, so a handle cached in the
        // previous game session must re-resolve (same rule as roads).
        overlay = _overlay;
        if (!OverlayHandleRule.MustReresolve(
                overlay is not null,
                overlay is not null && overlay.OverlayTex != null))
        {
            return true;
        }

        try
        {
            overlay = MinimapManager.Instance.GetMapOverlay(OverlayName);
            _overlay = overlay;
            return overlay is not null;
        }
        catch (Exception exception)
        {
            _rateLimited.Warning("route-overlay-get", $"Could not resolve the route overlay: {exception.Message}");
            return false;
        }
    }

    public void RedrawAll(RouteStore routes)
    {
        try
        {
            if (!TryGetOverlay(out MinimapManager.MapOverlay? overlay))
            {
                return;
            }

            // RC15 item 8 (mirrors RoadOverlayRenderer.RedrawAll): capture
            // the live texture at resolve and re-check it before the write,
            // so a map teardown mid-redraw fails soft instead of throwing.
            Texture2D? texture = overlay!.OverlayTex;
            if (texture == null)
            {
                _overlay = null;
                _rateLimited.Warning(
                    "route-redraw-teardown",
                    "Route overlay lifecycle: overlay texture torn down at resolve during a full redraw; " +
                    "handle reset, redraw deferred to the next valid map session.");
                return;
            }

            int size = overlay.TextureSize;

            // RC12 blocker 3: reuse one pixel buffer across redraws. A
            // 2048² texture is a 16 MB Color32[]; allocating it per redraw
            // made repeated style changes stutter through GC spikes.
            Color32[] pixels;
            if (_pixelBuffer is not null && _pixelBufferSize == size)
            {
                pixels = _pixelBuffer;
                Array.Clear(pixels, 0, pixels.Length);
            }
            else
            {
                pixels = new Color32[size * size];
                _pixelBuffer = pixels;
                _pixelBufferSize = size;
            }

            foreach (AtlasRoute route in routes.Living)
            {
                if (route.Archived || route.Points.Count == 0)
                {
                    continue;
                }

                Color32 color = RouteInk.Resolve(route, _settings.HighContrast.Value);

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

            // RC15 item 8: liveness must still hold at write time
            // (OverlayHandleRule.MayWrite) — the route walk above is a
            // teardown-sized window too.
            if (!OverlayHandleRule.MayWrite(textureAliveAtResolve: true, textureAliveAtWrite: texture != null))
            {
                _overlay = null;
                _rateLimited.Warning(
                    "route-redraw-teardown",
                    "Route overlay lifecycle: overlay texture torn down between resolve and write during a " +
                    "full redraw; handle reset, redraw deferred to the next valid map session.");
                return;
            }

            texture!.SetPixels32(pixels);
            texture.Apply(false);
        }
        catch (Exception exception)
        {
            _rateLimited.Warning("route-redraw", $"Could not redraw routes: {exception.Message}");
        }
    }

    /// <summary>The route layer's USER switch (checkbox/console): the
    /// texture follows through the effective-visibility rule.</summary>
    public void SetEnabled(bool enabled)
    {
        _userEnabled = enabled;
        ApplyVisibility();
    }

    // Projection buffer reused per route (full redraws only).
    private readonly List<(float X, float Y)> _projected = new();

    // Reused full-texture pixel buffer (RC12 blocker 3).
    private Color32[]? _pixelBuffer;
    private int _pixelBufferSize;

    private void ProjectPolyline(IReadOnlyList<RoadPoint> points, int size)
    {
        _projected.Clear();
        foreach (RoadPoint point in points)
        {
            Vector2 projected = Project(point, size);
            _projected.Add((projected.x, projected.y));
        }
    }

    /// <summary>Dash pattern via the shared <see cref="RoutePatternMath"/>
    /// walker (RC10): identical geometry to the vector presentation.</summary>
    private void DrawDashedPolyline(Color32[] pixels, int size, IReadOnlyList<RoadPoint> points, Color32 color)
    {
        ProjectPolyline(points, size);
        RoutePatternMath.WalkDashes(
            _projected, DashOnTexels, DashOffTexels, MaxPatternStampsPerRoute,
            (fromX, fromY, toX, toY) => DrawLineIntoBuffer(
                pixels, size, new Vector2(fromX, fromY), new Vector2(toX, toY), color));
    }

    /// <summary>Dots via the shared <see cref="RoutePatternMath"/> walker:
    /// fixed geometric spacing, independent of stored point positions.</summary>
    private void DrawDottedPolyline(Color32[] pixels, int size, IReadOnlyList<RoadPoint> points, Color32 color)
    {
        ProjectPolyline(points, size);
        RoutePatternMath.WalkDots(
            _projected, DotSpacingTexels, MaxPatternStampsPerRoute,
            (x, y) => DrawLineIntoBuffer(pixels, size, new Vector2(x, y), new Vector2(x, y), color));
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
}
