using System;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The three narrow Valheim 1.0.12 seams sailing Route Follow
/// requires (#243, contract in
/// <c>docs/mods/concerned-cartographer/SHIP_CONTROL_COMPATIBILITY.md</c>).
/// They form ONE ordered contract and are installed as a single
/// transaction — if any of the three fails to bind, every patch is removed
/// and the feature stays off for the session. An
/// <c>ShipControlls.ApplyControlls</c>-only fallback is forbidden, because
/// installed <c>Player.SetControls</c> dispatches doodad controls BEFORE its
/// own helm-exit check, so exit input could otherwise reach a synthetic
/// steering write in the same call.
///
/// 1. <see cref="BeforeSetControls"/> — read-only observation of the
///    complete raw action set, before vanilla's doodad dispatch. Cancels.
/// 2. <see cref="BeforeApplyControlls"/> — the only steering injection seam.
///    Replaces the rudder axis and nothing else.
/// 3. <see cref="BeforeStopDoodadControl"/> — lifecycle, while the exact
///    controller is still readable.
///
/// With no runtime callbacks attached every prefix is an exact vanilla
/// pass-through. None of them ever skips or replaces its original.</summary>
internal static class SailingRouteFollowAdapter
{
    /// <summary>Raw-input observation. <paramref name="exitInput"/> is
    /// vanilla's own helm-exit set (jump/attack/secondary/dodge).</summary>
    public delegate void RawControlsHandler(
        Player player,
        Vector3 moveDirection,
        bool exitInput,
        bool togglePressed);

    /// <summary>Steering injection. The handler returns true only when it
    /// authorised a bounded rudder-rate value in <paramref name="rudderInput"/>.</summary>
    public delegate bool SteeringHandler(
        ShipControlls controls,
        Vector3 rawMoveDirection,
        out float rudderInput);

    public static RawControlsHandler? RawControlsObserved;
    public static SteeringHandler? SteeringRequested;
    public static Action<Player, IDoodadController?>? DoodadControlStopping;

    private static Harmony? s_harmony;
    private static bool s_autoRunWasPressed;

    /// <summary>True when all three seams are bound for this session. The
    /// runtime refuses to start following unless this is true.</summary>
    public static bool Installed => s_harmony is not null;

    public static void Install(ManualLogSource log)
    {
        if (s_harmony is not null)
        {
            return;
        }

        Harmony? installing = null;
        try
        {
            Type[] setControlsSignature =
            {
                typeof(Vector3),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool),
            };
            var setControls = AccessTools.Method(
                typeof(Player), nameof(Player.SetControls), setControlsSignature);
            var applyControlls = AccessTools.Method(
                typeof(ShipControlls), nameof(ShipControlls.ApplyControlls),
                new[] { typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) });
            var stopDoodad = AccessTools.Method(
                typeof(Player), nameof(Player.StopDoodadControl), Type.EmptyTypes);
            if (setControls is null || applyControlls is null || stopDoodad is null)
            {
                log.LogWarning(
                    "Sailing Route Follow unavailable: Valheim ship-control signatures not recognised.");
                return;
            }

            installing = new Harmony(Plugin.PluginGuid + ".sailingroutefollow");
            // Priority.First so this observer sees the RAW autoRun value.
            // WalkingRouteFollowAdapter also prefixes Player.SetControls and
            // writes autoRun through a ref while it steers; at equal priority
            // Harmony orders by insertion and walking installs first, so
            // without this the sailing edge detector would latch onto the
            // other feature's synthetic input.
            installing.Patch(setControls,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter), nameof(BeforeSetControls))
                {
                    priority = Priority.First,
                });
            installing.Patch(applyControlls,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter), nameof(BeforeApplyControlls)));
            installing.Patch(stopDoodad,
                prefix: new HarmonyMethod(
                    typeof(SailingRouteFollowAdapter), nameof(BeforeStopDoodadControl)));
            s_harmony = installing;
        }
        catch (Exception exception)
        {
            // Partial installation fails CLOSED: remove whatever bound, drop
            // the callbacks, and leave vanilla helm behaviour untouched.
            try
            {
                installing?.UnpatchSelf();
            }
            catch
            {
                // Null callbacks below are already an exact pass-through.
            }

            RawControlsObserved = null;
            SteeringRequested = null;
            DoodadControlStopping = null;
            s_harmony = null;
            log.LogWarning(
                $"Sailing Route Follow unavailable: {SafeLogText.Brief(exception)}");
        }
    }

    public static void Uninstall()
    {
        RawControlsObserved = null;
        SteeringRequested = null;
        DoodadControlStopping = null;
        s_autoRunWasPressed = false;
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
            if (__instance != Player.m_localPlayer)
            {
                // Another character's controls must never move the local
                // player's edge state.
                return;
            }

            bool togglePressed = autoRun && !s_autoRunWasPressed;
            s_autoRunWasPressed = autoRun;
            RawControlsObserved?.Invoke(
                __instance,
                movedir,
                jump || attack || secondaryAttack || dodge,
                togglePressed);
        }
        catch
        {
            // A Route Follow failure must never disturb vanilla controls.
        }
    }

    private static void BeforeApplyControlls(
        ShipControlls __instance,
        ref Vector3 moveDir)
    {
        try
        {
            if (SteeringRequested is null)
            {
                return;
            }

            Vector3 raw = moveDir;
            if (SteeringRequested(__instance, raw, out float rudderInput))
            {
                // Rudder axis only. In particular z is preserved, so
                // vanilla's own dir.z > 0.5 / < -0.5 edges still decide
                // Forward()/Backward() and sail state stays entirely manual.
                moveDir = new Vector3(rudderInput, raw.y, raw.z);
            }
        }
        catch
        {
            // Leave the vanilla movement vector untouched on any failure.
        }
    }

    private static void BeforeStopDoodadControl(Player __instance)
    {
        try
        {
            if (__instance == Player.m_localPlayer)
            {
                DoodadControlStopping?.Invoke(
                    __instance, __instance.GetDoodadController());
            }
        }
        catch
        {
            // Never suppress or modify vanilla's own lifecycle stop.
        }
    }
}
