using System;
using BepInEx.Logging;
using HarmonyLib;

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
/// Fail-soft: if the patch cannot install, deletions are simply never
/// captured — a vanilla-deleted managed pin is then restored by the next
/// reconcile instead of tombstoned, which is the safe direction (data is
/// kept, never falsely destroyed). One startup warning documents the
/// degraded mode.</summary>
internal static class PinDeletionWatch
{
    /// <summary>Runtime-supplied sink for explicit deletions. Called from
    /// the RemovePin prefix (before the pin leaves the list), on the main
    /// thread, never for self-removals.</summary>
    public static Action<Minimap.PinData>? ExplicitDelete;

    public static bool Installed { get; private set; }

    private static Harmony? _harmony;
    private static int _selfDepth;

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
                prefix: new HarmonyMethod(typeof(PinDeletionWatch), nameof(BeforeRemovePin)));
            Installed = true;
        }
        catch (Exception exception)
        {
            log.LogWarning(
                $"Vanilla pin-delete detection unavailable ({exception.Message}); " +
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
        ExplicitDelete = null;
        _selfDepth = 0;
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

    private static void BeforeRemovePin(Minimap.PinData pin)
    {
        try
        {
            if (_selfDepth == 0 && pin is not null)
            {
                ExplicitDelete?.Invoke(pin);
            }
        }
        catch
        {
            // Never disturb vanilla pin removal on a sink failure; the
            // adapter's own fail-soft handles downstream errors.
        }
    }
}
