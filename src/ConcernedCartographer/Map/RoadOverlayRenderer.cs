using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

internal sealed class RoadOverlayRenderer
{
    // Jotunn's overlay toggle panel truncates long names; both layers must stay
    // distinguishable after truncation, so the mod prefix is a short "CC".
    private const string DirtOverlayName = "CC Dirt Paths";
    private const string PavedOverlayName = "CC Paved Roads";

    // Dark, fully opaque ink (cloud-layer contrast); the high-contrast
    // palette pushes dirt near black and paved near white for accessibility.
    private Color32 DirtColor => _settings.HighContrast.Value
        ? new Color32(26, 15, 6, 255)
        : new Color32(94, 62, 34, 255);

    private Color32 PavedColor => _settings.HighContrast.Value
        ? new Color32(245, 245, 250, 255)
        : new Color32(88, 90, 96, 255);

    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;

    public RoadOverlayRenderer(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _rateLimited = new RateLimitedLog(log, 5f);
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
                if (stroke.Points.Count == 0 || stroke.Hidden)
                {
                    continue;
                }

                Color32[] target = stroke.Kind == RoadKind.Dirt ? dirtPixels : pavedPixels;
                Color32 color = stroke.Kind == RoadKind.Dirt ? DirtColor : PavedColor;

                if (stroke.Points.Count == 1)
                {
                    // Construction dabs and chunk-recovery hits can be lone
                    // points; a zero-length line stamps a dot.
                    DrawIntoBuffer(target, textureSize, stroke.Points[0], stroke.Points[0], color);
                    continue;
                }

                for (int index = 1; index < stroke.Points.Count; index++)
                {
                    DrawIntoBuffer(
                        target,
                        textureSize,
                        stroke.Points[index - 1],
                        stroke.Points[index],
                        color);
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

    public void DrawCalibrationMarkers()
    {
        try
        {
            var overlay = MinimapManager.Instance.GetMapOverlay(DirtOverlayName);
            int size = overlay.TextureSize;
            StampCross(overlay.OverlayTex, size, new Vector3(0f, 0f, 0f), new Color32(255, 0, 255, 255), "origin (0,0)");
            StampCross(overlay.OverlayTex, size, new Vector3(128f, 0f, 0f), new Color32(255, 255, 0, 255), "+128m east");
            StampCross(overlay.OverlayTex, size, new Vector3(0f, 0f, 128f), new Color32(0, 255, 255, 255), "+128m north");
            overlay.OverlayTex.Apply(false);
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not draw calibration markers: {exception.Message}");
        }
    }

    private void StampCross(Texture2D texture, int size, Vector3 world, Color32 color, string label)
    {
        // The exact projection used by DrawSegment/DrawIntoBuffer.
        Vector2 coords = MinimapManager.Instance.WorldToOverlayCoords(world, size);
        int centerX = Mathf.RoundToInt(coords.x);
        int centerY = Mathf.RoundToInt(coords.y);
        StampCrossAt(texture, size, centerX, centerY, color);

        _log.LogInfo($"Calibration marker {label}: world ({world.x:0.#}, {world.z:0.#}) -> overlay pixel ({centerX}, {centerY}) of {size}.");
    }

    private static void StampCrossAt(Texture2D texture, int size, int centerX, int centerY, Color32 color)
    {
        const int armTexels = 4;

        for (int offset = -armTexels; offset <= armTexels; offset++)
        {
            int x = centerX + offset;
            if (x >= 0 && x < size && centerY >= 0 && centerY < size)
            {
                texture.SetPixel(x, centerY, color);
            }

            int y = centerY + offset;
            if (centerX >= 0 && centerX < size && y >= 0 && y < size)
            {
                texture.SetPixel(centerX, y, color);
            }
        }
    }

    private readonly List<Minimap.PinData> _alignmentPins = new();

    /// <summary>Deterministic alignment diagnostic backing `cc_roads align`
    /// (DEF-v1.0-002): at each probe position it places a session-only
    /// native map pin (never saved to the character) and stamps an overlay
    /// cross through the exact projection path road rendering uses. PASS =
    /// pin center and cross coincide within one map texel. Every quantity
    /// needed to localize a mismatch is logged. Crosses live in the dirt
    /// overlay texture, so any full redraw clears them; the pins persist
    /// until `cc_roads align clear` or logout.</summary>
    public string RunAlignmentProbe(Vector3 playerPosition, RoadAtlas atlas)
    {
        try
        {
            ClearAlignmentPins();
            var overlay = MinimapManager.Instance.GetMapOverlay(DirtOverlayName);
            int size = overlay.TextureSize;

            var probes = new List<(string Label, Vector3 World)>
            {
                ("player", playerPosition),
                ("origin", new Vector3(0f, 0f, 0f)),
                ("east+128", new Vector3(128f, 0f, 0f)),
                ("north+128", new Vector3(0f, 0f, 128f)),
            };
            if (TryGetLatestDirtPoint(atlas, out Vector3 dirtPoint))
            {
                probes.Add(("latest-dirt-point", dirtPoint));
            }

            var report = new StringBuilder();
            foreach ((string label, Vector3 world) in probes)
            {
                report.Append('\n').Append(ProbeOne(overlay.OverlayTex, size, label, world));
            }

            overlay.OverlayTex.Apply(false);
            return $"Alignment probe: {probes.Count} position(s), each marked with a native dot pin \"CC align ...\" " +
                "plus a magenta overlay cross drawn through the road-rendering projection. PASS = every pin center " +
                "sits on its cross within one map texel (~12 m of world). Full numbers are in the BepInEx log." +
                report +
                "\n'cc_roads align clear' removes the pins; any road redraw clears the crosses.";
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Alignment probe failed: {exception}");
            return "Alignment probe failed: " + exception.Message;
        }
    }

    /// <summary>Removes the probe's native pins (the overlay crosses clear
    /// with the next full redraw).</summary>
    public void ClearAlignmentProbe()
    {
        ClearAlignmentPins();
    }

    private string ProbeOne(Texture2D texture, int size, string label, Vector3 world)
    {
        try
        {
            Minimap.PinData pin = Minimap.instance.AddPin(
                world, Minimap.PinType.Icon3, $"CC align {label}", save: false, isChecked: false);
            if (pin is not null)
            {
                _alignmentPins.Add(pin);
            }
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Alignment probe '{label}': could not add the native pin: {exception.Message}");
        }

        // The exact projection used by DrawSegment/DrawIntoBuffer.
        Vector2 coords = MinimapManager.Instance.WorldToOverlayCoords(world, size);
        int overlayX = Mathf.RoundToInt(coords.x);
        int overlayY = Mathf.RoundToInt(coords.y);
        StampCrossAt(texture, size, overlayX, overlayY, new Color32(255, 0, 255, 255));

        string nativePixel = MinimapReflection.TryWorldToPixel(world, out int pixelX, out int pixelY)
            ? FormattableString.Invariant($"({pixelX}, {pixelY})")
            : "n/a";
        string nativeUv = MinimapReflection.TryWorldToMapPoint(world, out float mapX, out float mapY)
            ? FormattableString.Invariant($"({mapX:0.#####}, {mapY:0.#####}) -> uv*overlaySize ({mapX * size:0.#}, {mapY * size:0.#})")
            : "n/a";
        string mapTexture = MinimapReflection.TryGetTextureSize(out int textureSize)
            ? textureSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "n/a";
        string metersPerTexel = MinimapReflection.TryGetPixelSize(out float pixelMeters)
            ? FormattableString.Invariant($"{pixelMeters:0.###}")
            : "n/a";
        string zoom = MinimapReflection.TryGetLargeZoom(out float largeZoom)
            ? FormattableString.Invariant($"{largeZoom:0.####}")
            : "n/a";

        string worldPart = FormattableString.Invariant($"world=({world.x:0.##}, {world.z:0.##})");
        string line =
            $"Alignment probe '{label}': {worldPart} overlaySize={size} " +
            $"overlayPixel=({overlayX}, {overlayY}) nativeWorldToPixel={nativePixel} nativeMapUv={nativeUv} " +
            $"mapTextureSize={mapTexture} metersPerTexel={metersPerTexel} largeZoom={zoom}";
        _log.LogInfo(line);
        return line;
    }

    private static bool TryGetLatestDirtPoint(RoadAtlas atlas, out Vector3 world)
    {
        for (int index = atlas.Strokes.Count - 1; index >= 0; index--)
        {
            RoadStroke stroke = atlas.Strokes[index];
            if (stroke.Kind == RoadKind.Dirt && !stroke.Hidden && stroke.Points.Count > 0)
            {
                RoadPoint point = stroke.Points[stroke.Points.Count - 1];
                world = new Vector3(point.X, point.Y, point.Z);
                return true;
            }
        }

        world = default;
        return false;
    }

    private void ClearAlignmentPins()
    {
        foreach (Minimap.PinData pin in _alignmentPins)
        {
            try
            {
                Minimap.instance?.RemovePin(pin);
            }
            catch
            {
                // A vanished pin (map teardown) needs no cleanup.
            }
        }

        _alignmentPins.Clear();
    }

    public void DrawSegment(RoadSegment segment)
    {
        try
        {
            string overlayName = segment.Kind == RoadKind.Dirt ? DirtOverlayName : PavedOverlayName;
            Color32 color = segment.Kind == RoadKind.Dirt ? DirtColor : PavedColor;
            var overlay = MinimapManager.Instance.GetMapOverlay(overlayName);
            int size = overlay.TextureSize;

            Vector2 start = MinimapManager.Instance.WorldToOverlayCoords(ToVector3(segment.Start), size);
            Vector2 end = MinimapManager.Instance.WorldToOverlayCoords(ToVector3(segment.End), size);
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
            // Every new sample retries this path, so keep the warning rate-limited.
            _rateLimited.Warning("draw-segment", $"Could not draw an incremental road segment: {exception.Message}");
        }
    }

    /// <summary>Shows or hides one road layer (the same switch as Jötunn's
    /// overlay toggle panel).</summary>
    public void SetOverlayEnabled(RoadKind kind, bool enabled)
    {
        try
        {
            string overlayName = kind == RoadKind.Dirt ? DirtOverlayName : PavedOverlayName;
            MinimapManager.Instance.GetMapOverlay(overlayName).Enabled = enabled;
        }
        catch (Exception exception)
        {
            _rateLimited.Warning("overlay-toggle", $"Could not toggle the {kind} overlay: {exception.Message}");
        }
    }

    /// <summary>Draws a single observation point as a dot, for sources whose
    /// first (possibly only) point never produces a segment.</summary>
    public void DrawPoint(RoadKind kind, RoadPoint point)
    {
        DrawSegment(new RoadSegment(kind, point, point));
    }

    private static Vector3 ToVector3(RoadPoint point)
    {
        return new Vector3(point.X, point.Y, point.Z);
    }

    private void DrawIntoBuffer(
        Color32[] pixels,
        int size,
        RoadPoint worldStart,
        RoadPoint worldEnd,
        Color32 color)
    {
        Vector2 start = MinimapManager.Instance.WorldToOverlayCoords(ToVector3(worldStart), size);
        Vector2 end = MinimapManager.Instance.WorldToOverlayCoords(ToVector3(worldEnd), size);

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

        while (true)
        {
            Stamp(setPixel, size, x0, y0, lineWidth);
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

    private static void Stamp(Action<int, int> setPixel, int size, int centerX, int centerY, int lineWidth)
    {
        // Integer division made even widths collapse (2 -> 1 texel); cover the
        // exact configured width with a box balanced around the center texel.
        int low = Math.Max(1, lineWidth) / 2;
        int high = Math.Max(1, lineWidth) - 1 - low;

        for (int y = centerY - low; y <= centerY + high; y++)
        {
            if (y < 0 || y >= size)
            {
                continue;
            }

            for (int x = centerX - low; x <= centerX + high; x++)
            {
                if (x >= 0 && x < size)
                {
                    setPixel(x, y);
                }
            }
        }
    }
}
