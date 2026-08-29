using System;
using BepInEx.Logging;
using HarmonyLib;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Consumes vanilla large-map click actions ONLY while an
/// explicitly UI-entered route mode is active (#101), so a drawn stroke
/// does not simultaneously cross off / create / name vanilla pins under
/// the cursor. Drag panning is suppressed separately per frame by the
/// runtime. The gate is a pair of skippable prefixes on the public
/// Minimap click handlers; vanilla behavior returns the instant
/// <see cref="ConsumeClicks"/> is false, and the patch is removed on
/// dispose. Right-click delete and middle-click ping are untouched.</summary>
internal static class MapInputGate
{
    public static volatile bool ConsumeClicks;

    private static Harmony? s_harmony;

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
}
