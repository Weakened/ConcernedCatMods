using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Narrow Valheim 1.0.7 control choke for local walking Route Follow.
/// When no runtime callback is present, the prefix is exact vanilla pass-through.</summary>
internal static class WalkingRouteFollowAdapter
{
    public delegate void ControlsHandler(
        Player player, Vector3 moveDirection, ref bool autoRunPressed);

    public static ControlsHandler? ControlsApplying;
    public static Action<Player>? ManualLookApplying;

    private static Harmony? s_harmony;

    public static void Install(ManualLogSource log)
    {
        if (s_harmony is not null)
        {
            return;
        }

        Harmony? installingHarmony = null;
        try
        {
            Type[] signature =
            {
                typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool),
            };
            var target = AccessTools.Method(typeof(Player), nameof(Player.SetControls), signature);
            var lookTarget = AccessTools.Method(
                typeof(Player), nameof(Player.SetMouseLook),
                new[] { typeof(Vector2) });
            if (target is null || lookTarget is null)
            {
                log.LogWarning("Walking Route Follow unavailable: Valheim control signatures not recognised.");
                return;
            }

            installingHarmony = new Harmony(Plugin.PluginGuid + ".walkingroutefollow");
            installingHarmony.Patch(target,
                prefix: new HarmonyMethod(
                    typeof(WalkingRouteFollowAdapter),
                    nameof(BeforeSetControls)));
            installingHarmony.Patch(lookTarget,
                prefix: new HarmonyMethod(
                    typeof(WalkingRouteFollowAdapter),
                    nameof(BeforeSetMouseLook)));
            s_harmony = installingHarmony;
        }
        catch (Exception exception)
        {
            // Installation is transactional: if the second patch fails,
            // remove the first before advertising this adapter as absent.
            try
            {
                installingHarmony?.UnpatchSelf();
            }
            catch
            {
                // Null callbacks below remain an exact vanilla pass-through.
            }

            ControlsApplying = null;
            ManualLookApplying = null;
            s_harmony = null;
            log.LogWarning(
                $"Walking Route Follow unavailable: {SafeLogText.Brief(exception)}");
        }
    }

    public static void Uninstall()
    {
        ControlsApplying = null;
        ManualLookApplying = null;
        try
        {
            s_harmony?.UnpatchSelf();
        }
        catch
        {
            // Teardown is best effort; the null callback is pass-through.
        }

        s_harmony = null;
    }

    private static void BeforeSetMouseLook(
        Player __instance,
        Vector2 mouseLook)
    {
        try
        {
            if (__instance == Player.m_localPlayer &&
                mouseLook.sqrMagnitude > 0.0001f)
            {
                ManualLookApplying?.Invoke(__instance);
            }
        }
        catch
        {
            // Manual look must keep its vanilla behavior on any failure.
        }
    }

    private static void BeforeSetControls(
        Player __instance,
        Vector3 movedir,
        ref bool autoRun)
    {
        try
        {
            if (__instance == Player.m_localPlayer)
            {
                ControlsApplying?.Invoke(__instance, movedir, ref autoRun);
            }
        }
        catch
        {
            // A Route Follow failure must never disturb vanilla controls.
        }
    }
}
