using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>RC15: captures EXPLICIT vanilla pin deletions at their choke
/// point, so the atlas never has to infer a deletion from a rendering's
/// absence (the false relog tombstones). Decompile-verified facts this
/// patch rests on (game 0.220.x):
///
/// - The user-facing delete paths — large-map right click and gamepad
///   JoyTabRight — both run <c>Minimap.RemovePin(Vector3, float)</c> →
///   <c>Minimap.RemovePin(PinData)</c>.
/// - Map reconstruction (<c>SetMapData → ClearPins</c>) bypasses
///   <c>RemovePin</c> entirely (<c>DestroyPinMarker</c> + in-place
///   <c>m_pins.Clear()</c>), so a rebuild can never masquerade as a
///   deletion here.
/// - Vanilla's direct <c>RemovePin(PinData)</c> calls touch only pin
///   classes the adapter never tracks (death/spawn/event/ping/shout and
///   owner-stamped shared pins), and the shared-pin sync removes only
///   <c>m_ownerID != 0</c> pins.
///
/// Every non-self <c>RemovePin(PinData)</c> call is therefore a genuine
/// explicit deletion through the vanilla pin system (vanilla UI or another
/// mod using the public API). The adapter's own remove calls run inside
/// <see cref="BeginSelfRemoval"/> scopes and are never reported.
///
/// #419 refines two things, both toward keeping the player's data:
/// - The report is made AFTER the call, and only if the pin really left the
///   list. Another mod's prefix may refuse a removal (TheGreatestMap refuses
///   to remove its own markers without Shift); a refused removal is not a
///   deletion.
/// - Each report says whether the removal came from the player pointing at
///   a pin: every vanilla user delete path goes through
///   <c>Minimap.RemovePin(Vector3, float)</c>, so a scope around that call
///   marks it. A neighbour that clears pins it does not own without the
///   player pointing at them (TheGreatestMap's tgm_clearlocal) is then not
///   mistaken for the player deleting a Cartographer marker. If the scope
///   cannot be installed, no removal is attributed to the player, which can
///   only keep a marker, never delete one.
///
/// Fail-soft: if the patch cannot install, deletions are simply never
/// captured — a vanilla-deleted managed pin is then restored by the next
/// reconcile instead of tombstoned, which is the safe direction (data is
/// kept, never falsely destroyed). One startup warning documents the
/// degraded mode.</summary>
internal static class PinDeletionWatch
{
    /// <summary>Runtime-supplied sink for explicit deletions. Called from
    /// the RemovePin postfix once the pin has really left the list, on the
    /// main thread, never for self-removals. The flag is true when the
    /// removal happened inside the player's pointed-at delete call.</summary>
    public static Action<Minimap.PinData, bool>? ExplicitDelete;

    /// <summary>Runtime-supplied: whether a pin is still in the live pin
    /// list. Null (or a throwing check) counts as removed, the pre-#419
    /// behaviour.</summary>
    public static Func<Minimap.PinData, bool>? StillListed;

    public static bool Installed { get; private set; }

    /// <summary>Whether removals can be attributed to the player pointing
    /// at a pin. False means none are.</summary>
    public static bool PointerScopeInstalled { get; private set; }

    private static Harmony? _harmony;
    private static int _selfDepth;
    private static int _pointerDepth;

    public static void Install(ManualLogSource log)
    {
        if (Installed)
        {
            return;
        }

        try
        {
            var removeByPin = AccessTools.Method(
                typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) });
            if (removeByPin is null)
            {
                log.LogWarning(
                    "Vanilla pin-delete detection unavailable (Minimap.RemovePin(PinData) not found); " +
                    "vanilla-deleted managed markers will be restored by the atlas instead of tombstoned.");
                return;
            }

            _harmony = new Harmony(Plugin.PluginGuid + ".pindeletion");
            _harmony.Patch(
                removeByPin,
                prefix: new HarmonyMethod(typeof(PinDeletionWatch), nameof(BeforeRemovePin)),
                postfix: new HarmonyMethod(typeof(PinDeletionWatch), nameof(AfterRemovePin)));
            Installed = true;

            var removeAtPointer = AccessTools.Method(
                typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Vector3), typeof(float) });
            if (removeAtPointer is null)
            {
                log.LogWarning(
                    "Pointed-at pin removal not found; while a mod that clears other mods' pins is installed, " +
                    "removed Cartographer markers will be redrawn rather than deleted.");
            }
            else
            {
                // Its own try: a failure here must not unwind the deletion
                // patch above, which is installed and stays owned.
                try
                {
                    // A finalizer, not a postfix: it runs even if the call
                    // throws, so the scope can never stay open.
                    _harmony.Patch(
                        removeAtPointer,
                        prefix: new HarmonyMethod(typeof(PinDeletionWatch), nameof(EnterPointerRemoval)),
                        finalizer: new HarmonyMethod(typeof(PinDeletionWatch), nameof(LeavePointerRemoval)));
                    PointerScopeInstalled = true;
                }
                catch (Exception pointerException)
                {
                    log.LogWarning(
                        $"Pointed-at pin removal could not be watched ({SafeLogText.Brief(pointerException)}); " +
                        "while a mod that clears other mods' pins is installed, removed Cartographer markers will be redrawn rather than deleted.");
                }
            }
        }
        catch (Exception exception)
        {
            log.LogWarning(
                $"Vanilla pin-delete detection unavailable ({SafeLogText.Brief(exception)}); " +
                "vanilla-deleted managed markers will be restored by the atlas instead of tombstoned.");
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
        Installed = false;
        PointerScopeInstalled = false;
        ExplicitDelete = null;
        StillListed = null;
        _selfDepth = 0;
        _pointerDepth = 0;
    }

    /// <summary>Marks the adapter's own RemovePin calls (sync replaces,
    /// display hides, reconcile cleanup, sprite rebuilds) so they are
    /// never mistaken for player deletions. Dispose to end the scope.</summary>
    public static SelfRemovalScope BeginSelfRemoval()
    {
        _selfDepth++;
        return default;
    }

    public readonly struct SelfRemovalScope : IDisposable
    {
        public void Dispose()
        {
            if (_selfDepth > 0)
            {
                _selfDepth--;
            }
        }
    }

    private static void BeforeRemovePin(Minimap.PinData pin, out bool __state)
    {
        // Decided before the call: a self-removal scope must not leak into
        // the decision of whoever removes next.
        __state = _selfDepth == 0 && pin is not null;
    }

    private static void AfterRemovePin(Minimap.PinData pin, bool __state)
    {
        try
        {
            if (!__state || pin is null)
            {
                return;
            }

            // A removal another mod's prefix refused left the pin listed:
            // nothing was deleted, so nothing is reported.
            if (StillListed?.Invoke(pin) == true)
            {
                return;
            }

            ExplicitDelete?.Invoke(pin, PointerScopeInstalled && _pointerDepth > 0);
        }
        catch
        {
            // Never disturb vanilla pin removal on a sink failure; the
            // adapter's own fail-soft handles downstream errors.
        }
    }

    private static void EnterPointerRemoval()
    {
        _pointerDepth++;
    }

    private static Exception? LeavePointerRemoval(Exception? __exception)
    {
        if (_pointerDepth > 0)
        {
            _pointerDepth--;
        }

        // Rethrow exactly what vanilla threw (null = no exception).
        return __exception;
    }
}
