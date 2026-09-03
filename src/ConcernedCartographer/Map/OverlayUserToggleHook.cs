using System;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Makes Jötunn's per-overlay checkbox a REAL layer switch for a
/// CC overlay (RC10 feedback 7). Two problems with the raw checkbox:
/// user clicks only set <c>MapOverlay.Enabled</c>, which the CC vector
/// presentations never saw; and the RC8 "one road presentation at a time"
/// rule suppresses the texture overlay through that same Enabled flag,
/// which dragged the checkbox to OFF while the roads stayed visible as
/// vector ink. This hook (a) attaches a listener to the checkbox so every
/// user click reaches CC as explicit layer intent, and (b) re-syncs the
/// checkbox VISUAL to the user's layer state after a programmatic
/// suppression write, so the checkbox always tells the truth. The Toggle
/// reference is internal to Jötunn; reflection fails soft to exactly the
/// RC8 behavior.</summary>
internal sealed class OverlayUserToggleHook
{
    private static readonly FieldInfo? ToggleField =
        AccessTools.Field(typeof(MinimapManager.MapOverlayBase), "Toggle");

    private Toggle? _hooked;

    /// <summary>Call on the per-frame cadence: hooks (or re-hooks after map
    /// teardown) the overlay's checkbox and aligns its visual with the
    /// user's layer state.</summary>
    public void Maintain(MinimapManager.MapOverlayBase overlay, bool userState, Action<bool> onUserToggled)
    {
        if (ToggleField is null)
        {
            return;
        }

        Toggle? toggle;
        try
        {
            toggle = ToggleField.GetValue(overlay) as Toggle;
        }
        catch
        {
            return;
        }

        if (toggle == null || toggle == _hooked)
        {
            return;
        }

        _hooked = toggle;
        toggle.onValueChanged.AddListener(value => onUserToggled(value));
        toggle.SetIsOnWithoutNotify(userState);
    }

    /// <summary>Restores the checkbox visual to the user's layer state
    /// after a programmatic Enabled write (suppression) moved it.</summary>
    public void SyncCheckbox(bool userState)
    {
        if (_hooked != null && _hooked.isOn != userState)
        {
            _hooked.SetIsOnWithoutNotify(userState);
        }
    }
}
