using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Atlas → System Markers (#99): the CC replacement for the
/// hidden vanilla right-rail filter buttons. Every toggle drives the
/// game's own canonical state — pin-type visibility through
/// Minimap.ToggleIconFilter (display filtering only; pins are never
/// deleted) and position sharing through ZNet.SetPublicReferencePosition.
/// Toggles resync from vanilla state every time the panel opens.</summary>
internal sealed class SystemMarkersPanel : CcSidePanel
{
    private static readonly (string LabelKey, string? RegistryIcon, int VanillaType)[] FilterRows =
    {
        ("", "vanilla:fire", 0),
        ("", "vanilla:house", 1),
        ("", "vanilla:hammer", 2),
        ("", "vanilla:dot", 3),
        ("", "vanilla:portal", 6),
        ("system.death", null, 4),
        ("system.boss", null, 9),
    };

    private readonly List<(int VanillaType, Toggle Toggle)> _filterToggles = new();
    private Toggle? _publicPosition;

    public SystemMarkersPanel(ManualLogSource log)
        : base(log, "system.title", 320f, 480f)
    {
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        AddBody(gui, font, AtlasStrings.Get("system.note"), 12, Color.white, ref y, 34f);

        // RC12 blocker 4 clearance: center-pivot toggles reach half their
        // height above their y.
        y -= 8f;

        foreach ((string labelKey, string? registryIcon, int vanillaType) in FilterRows)
        {
            string label = labelKey.Length > 0
                ? AtlasStrings.Get(labelKey)
                : (IconRegistry.TryResolve(registryIcon, out IconRegistry.IconDefinition definition)
                    ? definition.DisplayName
                    : registryIcon ?? "");
            int captured = vanillaType;
            Toggle toggle = AddToggle(gui, font, Color.white, label, -120f, y, wanted =>
            {
                if (MinimapReflection.TryGetIconFilterVisible(captured, out bool current) && current != wanted)
                {
                    MinimapReflection.TryToggleIconFilter(captured);
                }
            });
            _filterToggles.Add((vanillaType, toggle));
            y -= 34f;
        }

        y -= 8f;
        _publicPosition = AddToggle(gui, font, headerColor, AtlasStrings.Get("system.visibleToOthers"), -120f, y, wanted =>
        {
            if (ZNet.instance != null)
            {
                ZNet.instance.SetPublicReferencePosition(wanted);
            }
        });
        y -= 34f;
    }

    protected override void OnShown()
    {
        foreach ((int vanillaType, Toggle toggle) in _filterToggles)
        {
            if (MinimapReflection.TryGetIconFilterVisible(vanillaType, out bool visible))
            {
                SetToggleSilently(toggle, visible);
            }
        }

        if (_publicPosition != null)
        {
            bool shared = false;
            try
            {
                shared = ZNet.instance != null && ZNet.instance.IsReferencePositionPublic();
            }
            catch
            {
                // Offline/menu edge; leave unchecked.
            }

            SetToggleSilently(_publicPosition, shared);
        }
    }
}
