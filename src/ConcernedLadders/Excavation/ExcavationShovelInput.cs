using System;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The Iron Shovel's deliberate actions (#487 slices 3 and 4; the dig session since #551): while the shovel is
/// the local player's equipped weapon, one press of the game's own Attack binding (keyboard or controller) asks
/// <see cref="ExcavationRuntime.Primary"/> to dig what the preview shows; one press of Secondary Attack asks
/// <see cref="ExcavationRuntime.Secondary"/> to arm and then confirm an undo of the last dig. Both in the
/// same step ask for nothing. A press while any menu, the inventory, the map, chat, the console or the build menu is open is ignored
/// here too, on top of the game withholding it.
///
/// Read from the decompiled installed game (1.0.21): <c>PlayerController.FixedUpdate</c> turns the Attack and
/// Secondary Attack buttons into <c>Character.m_attack</c> / <c>m_secondaryAttack</c>, true for exactly one input step
/// per press (the button's down edge), and sets every control false while chat, the console, a text input, a menu or
/// the barber has the input; it also withholds attack while the inventory, the map, a store or the build menu is open,
/// and inside the radial menu. <c>Player.PlayerAttackInput</c> then reads them in the player's FixedUpdate, only for the
/// living local owner. So a prefix there sees one edge per press, and none from any UI, without this mod reading a key.
///
/// The prefix only observes: it returns nothing, so the game's own attack input always runs after it. That is safe
/// because the shovel has <b>no vanilla attack</b> (the item registration clears both attack animations, so
/// <c>HavePrimaryAttack</c>/<c>HaveSecondaryAttack</c> are false and <c>Humanoid.StartAttack</c> returns before it
/// charges, swings, hits or spawns anything). Holding the button, or the game's 0.5 s attack queue, therefore never
/// digs twice. Any mismatch in the installed game's members leaves the shovel unable to dig, and says so in the log.
///
/// <b>Not observed in game.</b></summary>
internal sealed class ExcavationShovelInput : IDisposable
{
    private static AccessTools.FieldRef<Character, bool>? _attack;
    private static AccessTools.FieldRef<Character, bool>? _secondaryAttack;
    private static ExcavationRuntime? _runtime;
    private static Action<string>? _log;
    private static bool _faultLogged;
    private static AccessTools.FieldRef<GameCamera, float>? _cameraDistance;

    /// <summary>True while the wheel sets the dig's depth (#551): the camera's distance is then held, so the same turn of the
    /// wheel does not also zoom. Set by the plugin from the preview.</summary>
    internal static Func<bool>? HoldCamera { get; set; }
    private readonly Action<string> _startupLog;
    private Harmony? _harmony;

    internal ExcavationShovelInput(Action<string> log) => _startupLog = log;

    internal bool Install(string harmonyId, ExcavationRuntime runtime)
    {
        try
        {
            MethodInfo? input = AccessTools.Method(typeof(Player), "PlayerAttackInput", new[] { typeof(float) });
            if (input == null || input.ReturnType != typeof(void) || !IsBool("m_attack") || !IsBool("m_secondaryAttack"))
            {
                _startupLog("Iron Shovel input unavailable: the installed game's attack input did not match. The shovel cannot dig.");
                return false;
            }
            _attack = AccessTools.FieldRefAccess<Character, bool>("m_attack");
            _secondaryAttack = AccessTools.FieldRefAccess<Character, bool>("m_secondaryAttack");
            _runtime = runtime;
            _log = _startupLog;
            _faultLogged = false;
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(input, prefix: new HarmonyMethod(typeof(ExcavationShovelInput), nameof(BeforeAttackInput)));
            InstallCameraHold();
            return true;
        }
        catch (Exception exception)
        {
            _startupLog("Iron Shovel input unavailable (" + SafeFailure.Brief(exception) + "). The shovel cannot dig.");
            Dispose();
            return false;
        }
    }

    /// <summary>The shovel borrows the wheel to choose a size (#551; #574). GameCamera.UpdateCamera subtracts the wheel from its private
    /// <c>m_distance</c> and clamps it (read from the installed game, 1.0.21); holding that one field across the update while
    /// <see cref="HoldCamera"/> says so leaves every other camera behaviour alone. If the game does not match, the size still
    /// changes and the camera also zooms; the log says so.</summary>
    private void InstallCameraHold()
    {
        try
        {
            MethodInfo? update = AccessTools.Method(typeof(GameCamera), "UpdateCamera", new[] { typeof(float) });
            FieldInfo? distance = AccessTools.Field(typeof(GameCamera), "m_distance");
            if (update == null || distance == null || distance.FieldType != typeof(float) || distance.IsStatic)
            {
                _startupLog("Iron Shovel camera hold unavailable: the installed game's camera did not match. Setting the depth also zooms.");
                return;
            }

            _cameraDistance = AccessTools.FieldRefAccess<GameCamera, float>("m_distance");
            _harmony!.Patch(update, prefix: new HarmonyMethod(typeof(ExcavationShovelInput), nameof(BeforeCameraUpdate)),
                postfix: new HarmonyMethod(typeof(ExcavationShovelInput), nameof(AfterCameraUpdate)));
        }
        catch (Exception exception)
        {
            _cameraDistance = null;
            _startupLog("Iron Shovel camera hold unavailable (" + SafeFailure.Brief(exception) + "). Setting the depth also zooms.");
        }
    }

    private static void BeforeCameraUpdate(GameCamera __instance, out float __state)
    {
        __state = float.NaN;
        try
        {
            AccessTools.FieldRef<GameCamera, float>? distance = _cameraDistance;
            if (distance != null && HoldCamera?.Invoke() == true) __state = distance(__instance);
        }
        catch { __state = float.NaN; }
    }

    private static void AfterCameraUpdate(GameCamera __instance, float __state)
    {
        try
        {
            AccessTools.FieldRef<GameCamera, float>? distance = _cameraDistance;
            if (distance != null && !float.IsNaN(__state)) distance(__instance) = __state;
        }
        catch { /* The camera keeps whatever it chose. */ }
    }

    private static bool IsBool(string name)
    {
        FieldInfo? field = AccessTools.Field(typeof(Character), name);
        return field != null && field.FieldType == typeof(bool) && !field.IsStatic;
    }

    /// <summary>Observes the attack edges of the local player's step. Never skips the game's own input, and never
    /// throws into it.</summary>
    private static void BeforeAttackInput(Player __instance)
    {
        ExcavationRuntime? runtime = _runtime;
        AccessTools.FieldRef<Character, bool>? attack = _attack;
        AccessTools.FieldRef<Character, bool>? secondary = _secondaryAttack;
        if (runtime == null || attack == null || secondary == null) return;
        try
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer) || __instance.InPlaceMode()) return;
            ItemDrop.ItemData? weapon = __instance.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared == null || !IronShovel.IsShovelName(weapon.m_shared.m_name)) return;
            ExcavationPress press = ExcavationPreviewRules.PressFor(attack(__instance), secondary(__instance));
            if (press == ExcavationPress.None || ExcavationPreview.UiOpen()) return;
            string line = press == ExcavationPress.Primary ? runtime.Primary(__instance, weapon) : runtime.Secondary(__instance, weapon);
            Show(line);
        }
        catch (Exception exception)
        {
            if (_faultLogged) return;
            _faultLogged = true;
            try { _log?.Invoke("Iron Shovel input fault (" + SafeFailure.Brief(exception) + "); reported once."); } catch { /* Log only. */ }
        }
    }

    /// <summary>Shows a result to the player the way the ladders do (the HUD's top-left message, one short line), Unity
    /// null-safe. An empty result shows nothing: the preview's own line already says what happens next.</summary>
    internal static void Show(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null) hud.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
        catch { /* Feedback only. */ }
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Teardown only. */ }
        _harmony = null;
        _runtime = null;
        _attack = null;
        _secondaryAttack = null;
        _cameraDistance = null;
        HoldCamera = null;
        _log = null;
    }
}
