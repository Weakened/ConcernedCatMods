using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Renames Jötunn's visible "Mod Overlays" map button to
/// "Map Overlays" (RC10 feedback 7), safely and reversibly: only Text
/// components under the MinimapOverlayPanel whose text EXACTLY matches the
/// known original are changed, originals are remembered, and
/// <see cref="Restore"/> puts every changed label back (teardown/disable).
/// Anything unexpected — panel missing, label already customized by
/// another mod, future Jötunn wording — is left untouched.</summary>
internal sealed class OverlayPanelRelabel
{
    private const string OriginalLabel = "Mod Overlays";
    private const string ReplacementLabel = "Map Overlays";

    private readonly ManualLogSource _log;
    private readonly List<(Text Label, string Original)> _renamed = new();
    private bool _missingLogged;

    public OverlayPanelRelabel(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>Idempotent per-frame maintenance while the large map is
    /// open; re-applies after Jötunn rebuilds the panel.</summary>
    public void EnsureApplied()
    {
        try
        {
            _renamed.RemoveAll(entry => entry.Label == null);

            Minimap minimap = Minimap.instance;
            if (minimap == null || minimap.m_mapLarge == null)
            {
                return;
            }

            Transform? panel = FindOverlayPanel(minimap.m_mapLarge.transform);
            if (panel == null)
            {
                return;
            }

            bool found = false;
            foreach (Text label in panel.GetComponentsInChildren<Text>(includeInactive: true))
            {
                if (label == null)
                {
                    continue;
                }

                string text = label.text?.Trim() ?? string.Empty;
                if (string.Equals(text, ReplacementLabel, StringComparison.Ordinal))
                {
                    found = true;
                }
                else if (string.Equals(text, OriginalLabel, StringComparison.Ordinal))
                {
                    _renamed.Add((label, label.text!));
                    label.text = ReplacementLabel;
                    found = true;
                }
            }

            if (!found && !_missingLogged)
            {
                _missingLogged = true;
                _log.LogInfo(
                    "The Jötunn overlay panel does not carry the expected 'Mod Overlays' label; " +
                    "leaving its wording untouched.");
            }
        }
        catch (Exception exception)
        {
            _missingLogged = true;
            _log.LogWarning($"Could not rename the overlay panel label: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>Puts every renamed label back exactly (uninstall/disable).</summary>
    public void Restore()
    {
        foreach ((Text label, string original) in _renamed)
        {
            try
            {
                if (label != null)
                {
                    label.text = original;
                }
            }
            catch
            {
                // A dead label needs no restore.
            }
        }

        _renamed.Clear();
    }

    private static Transform? FindOverlayPanel(Transform mapLarge)
    {
        for (int index = 0; index < mapLarge.childCount; index++)
        {
            Transform child = mapLarge.GetChild(index);
            if (child != null && child.name.StartsWith("MinimapOverlayPanel", StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }
}
