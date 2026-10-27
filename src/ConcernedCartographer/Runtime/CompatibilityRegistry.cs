using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Runtime detection of known neighboring mods with documented
/// coexistence behavior. Detection is by BepInEx plugin GUID, evaluated
/// once; adapters read the flags instead of probing foreign types. The
/// mod's baseline is already interop-safe (foreign pins untouchable, own
/// named overlays, no pin-UI patches) — flags only soften optional
/// behaviors further.</summary>
internal sealed class CompatibilityRegistry
{
    public sealed class KnownMod
    {
        public KnownMod(string guidFragment, string displayName, string behavior)
        {
            GuidFragment = guidFragment;
            DisplayName = displayName;
            Behavior = behavior;
        }

        public string GuidFragment { get; }
        public string DisplayName { get; }
        public string Behavior { get; }
        public bool Detected { get; set; }
        public string DetectedGuid { get; set; } = "";
    }

    private readonly List<KnownMod> _knownMods = new()
    {
        new KnownMod("pinnacle", "Pinnacle",
            "Pin manager detected: adoption stays fully manual (no adopt prompts from the hotkey on unadopted vanilla pins) so both managers never fight over a pin."),
        new KnownMod("pinassistant", "PinAssistant",
            "Pin manager detected: adoption stays fully manual; its auto-pins look like vanilla pins and are only touched if you explicitly adopt them."),
        new KnownMod("automappins", "AutoMapPins",
            "Auto-pinner detected: its pins are unsaved/foreign and are never adopted or edited."),
        new KnownMod("maproutes", "MapRoutes",
            "Route drawer detected: both route layers coexist (separate overlays); imports are not performed automatically."),
        new KnownMod("bettercartographytable", "Better Cartography Table",
            "Table mod detected: Concerned Cartographer sharing stays on its own cc_sync channel and never touches table data."),
        new KnownMod("onemap", "OneMap",
            "Shared-map mod detected: vanilla shared pins carry owner IDs and remain foreign/untouchable to Concerned Cartographer."),
        // #419: exact GUIDs, verified against each mod (see
        // GREATESTMAP_COMPATIBILITY.md); NeighborMapMods matches them exactly.
        new KnownMod(NeighborMapMods.TheGreatestMapGuid.ToLowerInvariant(), "The Greatest Map",
            "Map mod detected: it owns hand-placed markers (it shares them), so Concerned Cartographer's palette and adopt prompts step aside and its own markers are made only from the CC toolbar, Quick Pin and survey. A Cartographer marker removed by The Greatest Map's commands (tgm_clearlocal, tgm_import) is kept and redrawn rather than deleted; delete it with a right-click or from the Pin Workbench."),
        new KnownMod(NeighborMapMods.HudCompassGuid, "HUDCompass",
            "Compass mod detected: Cartographer markers are ordinary map pins, so they appear on its compass bar; hide them there with its own Ignore settings if you prefer. Its dynamic cart, ship and portal markers are its own and are never touched."),
        new KnownMod(NeighborMapMods.RoundMinimapGuid, "Round Minimap",
            "Minimap mod detected: Cartographer markers turn with the round minimap. Road and route ink cannot (the overlay layer it is drawn in keeps its square, north-up shape), so it is left off the minimap while the round shape is on; Map/ReshapedMinimapRoads = Show draws it anyway. The large map is unaffected."),
    };

    private bool _evaluated;

    public IReadOnlyList<KnownMod> KnownMods => _knownMods;

    /// <summary>True when a pin-managing mod is present, which turns the
    /// workbench hotkey's adopt-prompt for unadopted vanilla pins into a
    /// read-only info panel (explicit cc_pins adopt still works).</summary>
    public bool PinManagerPresent { get; private set; }

    /// <summary>#419: the map mods detected by exact GUID. Empty until
    /// <see cref="Evaluate"/> has run.</summary>
    public NeighborMapModPresence Neighbors { get; private set; } =
        NeighborMapMods.Detect(null);

    public void Evaluate(ManualLogSource log)
    {
        if (_evaluated)
        {
            return;
        }

        _evaluated = true;
        try
        {
            foreach (KeyValuePair<string, BepInEx.PluginInfo> plugin in Chainloader.PluginInfos)
            {
                string guid = plugin.Key.ToLowerInvariant();
                foreach (KnownMod known in _knownMods)
                {
                    if (!known.Detected && guid.Contains(known.GuidFragment))
                    {
                        known.Detected = true;
                        known.DetectedGuid = plugin.Key;
                        log.LogInfo($"Compatibility: {known.DisplayName} detected ({plugin.Key}). {known.Behavior}");
                    }
                }
            }

            foreach (KnownMod known in _knownMods)
            {
                if (known.Detected && (known.GuidFragment == "pinnacle" || known.GuidFragment == "pinassistant"))
                {
                    PinManagerPresent = true;
                }
            }

            Neighbors = NeighborMapMods.Detect(Chainloader.PluginInfos.Keys);
            if (Neighbors.OwnsVanillaPinFlow)
            {
                // A mod that adopts hand-placed markers is a pin manager for
                // this purpose: one pin, one owner.
                PinManagerPresent = true;
            }
        }
        catch (Exception exception)
        {
            log.LogWarning($"Compatibility detection failed harmlessly: {SafeLogText.Brief(exception)}");
        }
    }

    public string Report()
    {
        var builder = new System.Text.StringBuilder("Compatibility:");
        bool any = false;
        foreach (KnownMod known in _knownMods)
        {
            if (known.Detected)
            {
                any = true;
                builder.Append($"\n  {known.DisplayName} ({known.DetectedGuid}) — {known.Behavior}");
            }
        }

        if (!any)
        {
            builder.Append(" no known neighboring mods detected. Baseline interop safety applies regardless.");
        }

        return builder.ToString();
    }
}
