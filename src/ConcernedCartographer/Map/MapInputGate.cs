using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Consumes vanilla large-map click actions ONLY while an
/// explicitly UI-entered route mode is active (#101), so a drawn stroke
/// does not simultaneously cross off / create / name vanilla pins under
/// the cursor. Drag panning is suppressed separately per frame by the
/// runtime. The gate is a pair of skippable prefixes on the public
/// Minimap click handlers; vanilla behavior returns the instant
/// <see cref="ConsumeClicks"/> is false, and the patch is removed on
/// dispose. Right-click delete and middle-click ping are untouched.
///
/// RC11 blocker 7: the gate also owns the WHEEL guard. Vanilla reads the
/// zoom wheel globally inside <c>Minimap.UpdateMap</c> with no
/// pointer-over-UI awareness, so scrolling a CC list also zoomed the map
/// underneath. While <see cref="WheelGuard"/> reports the pointer over CC
/// UI (or a CC text field focused), a prefix snapshots both zoom levels
/// and a postfix restores them — the wheel event still reaches the UI's
/// ScrollRect through the event system, but the map zoom nets to zero.
/// Fails soft: without the zoom field accessors the patch is not
/// installed and behavior is exactly RC10.</summary>
internal static class MapInputGate
{
    public static volatile bool ConsumeClicks;

    /// <summary>Supplied by the runtime: true when the map wheel must not
    /// zoom (pointer over CC UI / CC text field focused).</summary>
    public static Func<bool>? WheelGuard;

    private static Harmony? s_harmony;

    private static readonly AccessTools.FieldRef<Minimap, float>? LargeZoomField = BuildZoomRef("m_largeZoom");
    private static readonly AccessTools.FieldRef<Minimap, float>? SmallZoomField = BuildZoomRef("m_smallZoom");

    [ThreadStatic] private static bool t_restoreZoom;
    [ThreadStatic] private static float t_savedLargeZoom;
    [ThreadStatic] private static float t_savedSmallZoom;
    [ThreadStatic] private static Rect t_savedLargeUv;
    [ThreadStatic] private static Rect t_savedSmallUv;

    public static void Install(ManualLogSource log)
    {
        if (s_harmony is not null)
        {
            return;
        }

        try
        {
            var harmony = new Harmony(Plugin.PluginGuid + ".mapinput");
            harmony.Patch(
                AccessTools.Method(typeof(Minimap), nameof(Minimap.OnMapLeftClick)),
                prefix: new HarmonyMethod(typeof(MapInputGate), nameof(BeforeMapClick)));
            harmony.Patch(
                AccessTools.Method(typeof(Minimap), nameof(Minimap.OnMapDblClick)),
                prefix: new HarmonyMethod(typeof(MapInputGate), nameof(BeforeMapClick)));

            if (LargeZoomField is not null && SmallZoomField is not null)
            {
                harmony.Patch(
                    AccessTools.Method(typeof(Minimap), "UpdateMap"),
                    prefix: new HarmonyMethod(typeof(MapInputGate), nameof(BeforeUpdateMap)),
                    postfix: new HarmonyMethod(typeof(MapInputGate), nameof(AfterUpdateMap)));
            }
            else
            {
                log.LogWarning("Map wheel guard unavailable (zoom fields not found); scrolling CC lists may zoom the map.");
            }

            s_harmony = harmony;
        }
        catch (Exception exception)
        {
            log.LogWarning($"Map input gate unavailable (route drawing may also toggle pins under the cursor): {exception.Message}");
        }
    }

    public static void Uninstall()
    {
        ConsumeClicks = false;
        WheelGuard = null;
        try
        {
            s_harmony?.UnpatchSelf();
        }
        catch
        {
            // Teardown race; the prefixes are inert with ConsumeClicks false.
        }

        s_harmony = null;
    }

    private static bool BeforeMapClick()
    {
        return !ConsumeClicks;
    }

    private static void BeforeUpdateMap(Minimap __instance)
    {
        t_restoreZoom = false;
        try
        {
            if (WheelGuard?.Invoke() != true)
            {
                return;
            }

            t_savedLargeZoom = LargeZoomField!(__instance);
            t_savedSmallZoom = SmallZoomField!(__instance);
            if (__instance.m_mapImageLarge != null)
            {
                t_savedLargeUv = __instance.m_mapImageLarge.uvRect;
            }

            if (__instance.m_mapImageSmall != null)
            {
                t_savedSmallUv = __instance.m_mapImageSmall.uvRect;
            }

            t_restoreZoom = true;
        }
        catch
        {
            t_restoreZoom = false;
        }
    }

    private static void AfterUpdateMap(Minimap __instance)
    {
        if (!t_restoreZoom)
        {
            return;
        }

        t_restoreZoom = false;
        try
        {
            // Restore the uv windows too: UpdateMap already re-centered
            // with the wheeled zoom, and leaving that for even one frame
            // reads as jitter while scrolling a CC list.
            LargeZoomField!(__instance) = t_savedLargeZoom;
            SmallZoomField!(__instance) = t_savedSmallZoom;
            if (__instance.m_mapImageLarge != null)
            {
                __instance.m_mapImageLarge.uvRect = t_savedLargeUv;
            }

            if (__instance.m_mapImageSmall != null)
            {
                __instance.m_mapImageSmall.uvRect = t_savedSmallUv;
            }
        }
        catch
        {
            // Zoom restore is cosmetic; never disturb the map update.
        }
    }

    private static AccessTools.FieldRef<Minimap, float>? BuildZoomRef(string fieldName)
    {
        try
        {
            return AccessTools.FieldRefAccess<Minimap, float>(fieldName);
        }
        catch
        {
            return null;
        }
    }
}
