using System;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Skippable Harmony prefixes that let Concerned Cartographer own
/// gameplay input while the armed Quick Pin interaction is active
/// (RC14 fix 3), mirroring the <see cref="MapInputGate"/> pattern: a
/// runtime-supplied guard, pass-through in one branch when it is off, and
/// Uninstall on dispose.
///
/// Two narrow chokes (game version 0.220.x, publicized assembly):
/// - <c>Humanoid.StartAttack(Character, bool)</c> — the single entry every
///   player attack goes through (Player declares no override). Skipping it
///   for the LOCAL player only, while the guard holds, stops the Quick Pin
///   capture click from swinging a weapon without touching camera,
///   movement, or other characters' attacks.
/// - <c>Menu.Update()</c> — where vanilla polls Escape to open the pause
///   menu. Skipped only while the guard holds AND the menu is not already
///   visible, so a visible menu always keeps processing its own input.
///
/// Fail-soft: if either target is missing after a game update, the gate
/// logs one warning and stays uninstalled — Quick Pin then behaves as in
/// RC13 (functional, without input ownership) instead of breaking.</summary>
internal static class PlayerInputGate
{
    /// <summary>Supplied by the runtime: true while the local player's
    /// attack input is owned by the armed Quick Pin interaction.</summary>
    public static Func<bool>? SuppressAttack;

    /// <summary>Supplied by the runtime: true while Escape is owned by the
    /// armed Quick Pin interaction (cancel must not also open the pause
    /// menu on the same press).</summary>
    public static Func<bool>? SuppressMenu;

    private static Harmony? _harmony;
    private static bool _installed;

    public static void Install(ManualLogSource log)
    {
        if (_installed)
        {
            return;
        }

        try
        {
            var startAttack = AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack));
            var menuUpdate = AccessTools.Method(typeof(Menu), "Update");
            if (startAttack is null || menuUpdate is null)
            {
                log.LogWarning(
                    "Quick Pin input ownership unavailable (vanilla input members not found); armed Quick Pin will not suppress attack/menu input.");
                return;
            }

            _harmony = new Harmony(Plugin.PluginGuid + ".playerinput");
            _harmony.Patch(
                startAttack,
                prefix: new HarmonyMethod(typeof(PlayerInputGate), nameof(BeforeStartAttack)));
            _harmony.Patch(
                menuUpdate,
                prefix: new HarmonyMethod(typeof(PlayerInputGate), nameof(BeforeMenuUpdate)));
            _installed = true;
        }
        catch (Exception exception)
        {
            log.LogWarning($"Quick Pin input ownership unavailable: {SafeLogText.Brief(exception)}");
            _harmony = null;
        }
    }

    public static void Uninstall()
    {
        try
        {
            _harmony?.UnpatchSelf();
        }
        catch
        {
            // Teardown is best effort.
        }

        _harmony = null;
        _installed = false;
    }

    private static bool BeforeStartAttack(Humanoid __instance, ref bool __result)
    {
        try
        {
            if (SuppressAttack?.Invoke() == true &&
                __instance is Player player &&
                player == Player.m_localPlayer)
            {
                __result = false;
                return false;
            }
        }
        catch
        {
            // Never disturb vanilla combat on a guard failure.
        }

        return true;
    }

    private static bool BeforeMenuUpdate()
    {
        try
        {
            if (SuppressMenu?.Invoke() == true && !Menu.IsVisible())
            {
                return false;
            }
        }
        catch
        {
            // Never disturb the vanilla menu on a guard failure.
        }

        return true;
    }
}
