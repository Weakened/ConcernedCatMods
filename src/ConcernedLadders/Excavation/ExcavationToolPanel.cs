using System;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The Iron Shovel's build panel (#587, owner playtest 2026-11-11: "Can we add tool description on the bottom
/// similar to hoe and other ones?"). The panel at the bottom of the screen is the game's own build info panel,
/// <c>Hud.m_buildHud</c>, which <c>Hud.UpdateBuild</c> shows only while the player is in place mode and fills from the
/// selected piece (read from the installed game). The shovel is not a place-mode tool, so after that update, while the
/// shovel's outline is up (<see cref="Info"/>), the panel is shown again with the shovel's icon, the chosen size's name and
/// its one line, and its requirement and snapping rows hidden. The next update hides it again as soon as the outline is
/// gone. Display only: nothing in the world or the player is touched. Not observed in game.</summary>
internal sealed class ExcavationToolPanel : IDisposable
{
    private static AccessTools.FieldRef<Hud, Image>? _snappingIcon;
    private static bool _faultLogged;
    private static Action<string>? _log;
    private Harmony? _harmony;

    /// <summary>What to show, or null to leave the panel to the game. Set by the plugin from the preview.</summary>
    internal static Func<(string Name, string Description, Sprite? Icon)?>? Info { get; set; }

    internal bool Install(string harmonyId, Action<string> log)
    {
        try
        {
            MethodInfo? update = AccessTools.Method(typeof(Hud), "UpdateBuild", new[] { typeof(Player), typeof(bool) });
            if (update == null)
            {
                log("Iron Shovel build panel unavailable: the installed game's HUD did not match.");
                return false;
            }

            FieldInfo? snapping = AccessTools.Field(typeof(Hud), "m_snappingIcon");
            _snappingIcon = snapping != null && snapping.FieldType == typeof(Image) ? AccessTools.FieldRefAccess<Hud, Image>("m_snappingIcon") : null;
            _log = log;
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(update, postfix: new HarmonyMethod(typeof(ExcavationToolPanel), nameof(AfterUpdateBuild)));
            return true;
        }
        catch (Exception exception)
        {
            log("Iron Shovel build panel unavailable (" + SafeFailure.Brief(exception) + ").");
            Dispose();
            return false;
        }
    }

    private static void AfterUpdateBuild(Hud __instance, Player player)
    {
        try
        {
            if (__instance == null || player == null || player.InPlaceMode()) return;
            (string Name, string Description, Sprite? Icon)? info = Info?.Invoke();
            if (info == null || __instance.m_buildHud == null) return;
            __instance.m_buildHud.SetActive(true);
            if (__instance.m_buildSelection != null) __instance.m_buildSelection.text = info.Value.Name;
            if (__instance.m_pieceDescription != null) __instance.m_pieceDescription.text = info.Value.Description;
            if (__instance.m_buildIcon != null)
            {
                __instance.m_buildIcon.sprite = info.Value.Icon;
                __instance.m_buildIcon.enabled = info.Value.Icon != null;
            }

            Image? snapping = _snappingIcon != null ? _snappingIcon(__instance) : null;
            if (snapping != null) snapping.enabled = false;
            foreach (GameObject requirement in __instance.m_requirementItems)
            {
                if (requirement != null) requirement.SetActive(false);
            }
        }
        catch (Exception exception)
        {
            if (_faultLogged) return;
            _faultLogged = true;
            _log?.Invoke("Iron Shovel build panel failed (" + SafeFailure.Brief(exception) + "); the shovel itself is unaffected.");
        }
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Teardown only. */ }
        _harmony = null;
        _snappingIcon = null;
        _log = null;
        Info = null;
    }
}
