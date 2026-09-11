using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Atomic Valheim 1.0.12 helm-control boundary for Sailing Route
/// Follow. Missing callbacks or any bind failure are exact vanilla pass-through.</summary>
internal static class SailingRouteFollowAdapter
{
    public delegate void RawControlsHandler(
        Player player,
        ShipControlls? controls,
        Vector3 moveDirection,
        bool attack,
        bool secondaryAttack,
        bool jump,
        bool autoRun,
        bool dodge);

    public delegate void HelmControlsHandler(
        Player player,
        ShipControlls controls,
        Ship ship,
        ref Vector3 moveDirection);

    public delegate void LifecycleHandler(
        Player player,
        ShipControlls controls);

    public static RawControlsHandler? RawControlsApplying;
    public static HelmControlsHandler? HelmControlsApplying;
    public static LifecycleHandler? DoodadStopping;

    private static Harmony? s_harmony;

    public static bool Available => s_harmony is not null;

    public static void Install(ManualLogSource log)
    {
        if (s_harmony is not null)
        {
            return;
        }

        Harmony? installingHarmony = null;
        try
        {
            Type[] playerSignature =
            {
                typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool),
            };
            Type[] helmSignature =
            {
                typeof(Vector3), typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool),
            };
            var playerTarget = AccessTools.Method(
                typeof(Player), nameof(Player.SetControls), playerSignature);
            var helmTarget = AccessTools.Method(
                typeof(ShipControlls), nameof(ShipControlls.ApplyControlls),
                helmSignature);
            var stopTarget = AccessTools.Method(
                typeof(Player), nameof(Player.StopDoodadControl),
                Type.EmptyTypes);
            var controlledShipTarget = AccessTools.Method(
                typeof(Player), nameof(Player.GetControlledShip),
                Type.EmptyTypes);
            var doodadTarget = AccessTools.Method(
                typeof(Player), nameof(Player.GetDoodadController),
                Type.EmptyTypes);
            var playerIdTarget = AccessTools.Method(
                typeof(Player), nameof(Player.GetPlayerID),
                Type.EmptyTypes);
            var validUserTarget = AccessTools.Method(
                typeof(ShipControlls), nameof(ShipControlls.HaveValidUser),
                Type.EmptyTypes);
            var userTarget = AccessTools.Method(
                typeof(ShipControlls), nameof(ShipControlls.GetUser),
                Type.EmptyTypes);
            var rudderTarget = AccessTools.Method(
                typeof(Ship), nameof(Ship.GetRudderValue),
                Type.EmptyTypes);
            bool contractValid =
                playerTarget is not null &&
                helmTarget is not null &&
                stopTarget is not null &&
                controlledShipTarget?.ReturnType == typeof(Ship) &&
                doodadTarget?.ReturnType == typeof(IDoodadController) &&
                playerIdTarget?.ReturnType == typeof(long) &&
                validUserTarget?.ReturnType == typeof(bool) &&
                userTarget?.ReturnType == typeof(long) &&
                rudderTarget?.ReturnType == typeof(float);
            if (!contractValid)
            {
                log.LogWarning(
                    "Sailing Route Follow unavailable: Valheim helm-control contract not recognised.");
                return;
            }

            installingHarmony = new Harmony(
                Plugin.PluginGuid + ".sailingroutefollow");
            installingHarmony.Patch(
                playerTarget,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter),
                    nameof(BeforeSetControls)));
            installingHarmony.Patch(
                helmTarget,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter),
                    nameof(BeforeApplyControls)));
            installingHarmony.Patch(
                stopTarget,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter),
                    nameof(BeforeStopDoodadControl)));
            s_harmony = installingHarmony;
        }
        catch (Exception exception)
        {
            try
            {
                installingHarmony?.UnpatchSelf();
            }
            catch
            {
                // Null callbacks below retain vanilla pass-through.
            }

            ClearCallbacks();
            s_harmony = null;
            log.LogWarning(
                $"Sailing Route Follow unavailable: {SafeLogText.Brief(exception)}");
        }
    }

    public static void Uninstall()
    {
        ClearCallbacks();
        try
        {
            s_harmony?.UnpatchSelf();
        }
        catch
        {
            // Teardown is best effort; null callbacks are pass-through.
        }

        s_harmony = null;
    }

    private static void BeforeSetControls(
        Player __instance,
        Vector3 movedir,
        bool attack,
        bool secondaryAttack,
        bool jump,
        bool autoRun,
        bool dodge)
    {
        try
        {
            if (__instance == Player.m_localPlayer)
            {
                RawControlsApplying?.Invoke(
                    __instance,
                    __instance.GetDoodadController() as ShipControlls,
                    movedir,
                    attack,
                    secondaryAttack,
                    jump,
                    autoRun,
                    dodge);
            }
        }
        catch
        {
            // Observation must never modify or suppress vanilla controls.
        }
    }

    private static void BeforeApplyControls(
        ShipControlls __instance,
        ref Vector3 moveDir)
    {
        Vector3 original = moveDir;
        try
        {
            Player? player = Player.m_localPlayer;
            Ship? ship = player?.GetControlledShip();
            if (player != null && ship != null)
            {
                HelmControlsApplying?.Invoke(
                    player, __instance, ship, ref moveDir);
                if (!IsFinite(moveDir))
                {
                    moveDir = original;
                }
            }
        }
        catch
        {
            // A callback may have mutated by-ref input before failing.
            // Restore the complete original vector for exact pass-through.
            moveDir = original;
        }
    }

    private static bool IsFinite(in Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static void BeforeStopDoodadControl(Player __instance)
    {
        try
        {
            if (__instance == Player.m_localPlayer &&
                __instance.GetDoodadController() is ShipControlls controls)
            {
                DoodadStopping?.Invoke(__instance, controls);
            }
        }
        catch
        {
            // Lifecycle observation never suppresses vanilla teardown.
        }
    }

    private static void ClearCallbacks()
    {
        RawControlsApplying = null;
        HelmControlsApplying = null;
        DoodadStopping = null;
    }
}
