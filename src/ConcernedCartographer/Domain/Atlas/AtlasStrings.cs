using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The localization catalog for player-facing UI strings: English
/// defaults, override loading from a translator file, and a template
/// generator. Console tool output deliberately stays English (power-tool
/// surface; documented). Missing keys always fall back to English, so a
/// partial translation can never blank the UI.</summary>
internal static class AtlasStrings
{
    public const string TemplateHeader = "# ConcernedCartographer strings v1 — key<TAB>translation (missing keys fall back to English)";

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["workbench.title"] = "Pin Workbench",
        ["workbench.vanillaPin"] = "Vanilla marker",
        ["workbench.foreignPin"] = "Foreign pin",
        ["workbench.foreignInfo"] = "owned by the game or another mod; Concerned Cartographer never edits it.",
        ["workbench.adoptInfo"] = "Keep this existing marker and enable Concerned Cartographer editing, notes, categories and atlas features. Its map position is preserved.",
        ["workbench.adopt"] = "Upgrade & Edit",
        ["workbench.apply"] = "Apply",
        ["workbench.delete"] = "Delete",
        ["workbench.close"] = "Close",
        ["workbench.saved"] = "Saved.",
        ["workbench.name"] = "Name",
        ["workbench.icon"] = "Icon",
        ["workbench.category"] = "Category",
        ["workbench.color"] = "Color hex",
        ["workbench.size"] = "Size 0.5-2",
        ["workbench.tags"] = "Tags (a, b)",
        ["workbench.notes"] = "Notes",
        ["workbench.status"] = "Status",
        ["workbench.scope"] = "Scope",
        ["workbench.checked"] = "Crossed off",
        ["workbench.customIcon"] = "Custom: {0}",
        ["workbench.keepCustomIcon"] = "Keep custom ({0})",
        ["workbench.sizeMeta"] = "Size (metadata)",
        ["workbench.colorMeta"] = "Color hex (metadata)",
        ["workbench.reset"] = "Reset",
        ["drawer.title"] = "Atlas",
        ["drawer.layers"] = "Layers",
        ["drawer.dirtRoads"] = "Dirt roads",
        ["drawer.pavedRoads"] = "Paved roads",
        ["drawer.pins"] = "Pins",
        ["drawer.clustering"] = "Clustering",
        ["drawer.search"] = "Search",
        ["drawer.go"] = "Go",
        ["drawer.clearFilter"] = "Clear filter",
        ["drawer.views"] = "Views",
        ["drawer.save"] = "Save",
        ["drawer.placeholders"] = "Routes and Sharing live on the map toolbar: [Routes] · [Share]",
        ["drawer.privacy"] = "Privacy",
        ["toolbar.atlas"] = "Atlas",
        ["toolbar.markers"] = "Markers",
        ["toolbar.routes"] = "Routes",
        ["toolbar.survey"] = "Survey",
        ["toolbar.share"] = "Share",
        ["toolbar.quickpin"] = "Quick Pin",
        ["toolbar.settings"] = "Settings",
        ["system.title"] = "System Markers",
        ["system.note"] = "Filters change map visibility only — nothing is ever deleted.",
        ["system.visibleToOthers"] = "Visible to other players",
        ["system.death"] = "Death markers",
        ["system.boss"] = "Boss markers",
        ["share.instructions"] = "Broadcast your shared scope; preview an inbox entry, then apply with an explicit conflict choice.",
        ["routes.title"] = "Routes",
        ["routes.explainer"] = "Routes are plans. Optional Route Follow can steer vanilla Q autorun along the selected route; it is OFF by default and manual input cancels immediately.",
        ["routes.nameHint"] = "new route name",
        ["routes.freeDraw"] = "Free Draw",
        ["routes.waypoints"] = "Waypoints",
        ["routes.erase"] = "Erase",
        ["routes.finish"] = "Finish",
        ["routes.snap"] = "Snap to roads",
        ["routes.modeDraw"] = "DRAWING \"{0}\" — hold LMB on the map to draw; release ends that stroke. Finish or Esc exits.",
        ["routes.modeWaypoint"] = "WAYPOINTS \"{0}\" — click the map to add points. Finish or Esc exits.",
        ["routes.modeErase"] = "ERASING — hold LMB over route ink on the map. Finish or Esc exits.",
        ["routes.mergePick"] = "Merge: click the route to fold INTO \"{0}\".",
        ["survey.title"] = "Survey",
        ["survey.enable"] = "Survey rules enabled",
        ["survey.note"] = "Matches are observations only — nothing becomes a marker until you accept it.",
        ["survey.confirm"] = "Click again to confirm",
        ["share.title"] = "Share",
        ["share.now"] = "Share now",
        ["settings.title"] = "Settings",
        ["settings.emailLine"] = "Bugs: GitHub issues · Private/security: support@theconcernedcat.com",
        ["quickpin.armed"] = "Quick Pin armed — look at an object and click to capture. Esc to cancel.",
        ["quickpin.cancelled"] = "Quick Pin cancelled.",
        ["privacy.consentTitle"] = "Help improve Concerned Cartographer",
        ["privacy.consentQuestion"] = "Would you like to send anonymous crash reports when Concerned Cartographer encounters an error?",
        ["privacy.consentSent"] = "Sent: Concerned Cartographer version/release, Valheim/Unity/BepInEx/Jötunn versions, the affected subsystem, the exception type, and a sanitized stack trace.",
        ["privacy.consentNever"] = "Never sent: Steam or player identity, character or world names, world seeds, server addresses or passwords, IP addresses (also scrubbed by the provider), map coordinates, pin names/notes/tags, route names, saves, screenshots, or your log file.",
        ["privacy.consentProvider"] = "Provider: Sentry   ·   No gameplay analytics",
        ["privacy.consentYes"] = "Send anonymous crash reports",
        ["privacy.consentNo"] = "No thanks",
        ["privacy.consentLearnMore"] = "Learn more",
        ["privacy.settingsState"] = "Send anonymous crash reports: {0}",
        ["privacy.turnOn"] = "Turn on",
        ["privacy.turnOff"] = "Turn off",
        ["privacy.noticeSent"] = "Concerned Cartographer disabled {0} after an unexpected error. An anonymous crash report was sent.",
        ["privacy.noticeOff"] = "Concerned Cartographer disabled {0} after an unexpected error. Crash reporting is off. Enable it under CC Atlas → Privacy, or use 'cc_atlas support'.",
        ["hud.quickPinNothing"] = "Nothing targeted to pin.",
        ["hud.quickPinCreature"] = "Creatures are not pinned.",
        ["hud.quickPinned"] = "Pinned \"{0}\".",
        ["hud.quickPinDuplicate"] = "\"{0}\" is already pinned {1} m away.",
        ["hud.surveyObservations"] = "Survey: {0} new observation(s) — review in [Survey]",
        ["hud.syncReceived"] = "Atlas share received from {0} — review with cc_sync preview",
        ["hud.onboarding"] = "Concerned Cartographer: use the Atlas and marker palette buttons on the large map. Select any marker to edit it.",
        ["hud.atlasButton"] = "Atlas",
        ["hud.atlasTooltip"] = "Concerned Cartographer Atlas — search, filter, routes and map layers (hotkey {0})",
        ["hud.editHint"] = "{0} — Edit with Concerned Cartographer",
        ["hud.upgradeEdit"] = "Upgrade & Edit",
        ["hud.editPin"] = "Edit Pin",
        ["palette.toggle"] = "Markers",
        ["palette.title"] = "New Marker",
        ["palette.search"] = "Search icons...",
        ["palette.recent"] = "— Recent —",
        ["palette.all"] = "— Markers —",
        ["palette.place"] = "Double-click the map to place: {0}",
        ["palette.pick"] = "Pick a marker, then double-click the map.",
        ["hud.noMapNeedTable"] = "The atlas needs a cartography table nearby in a nomap world.",

        // CC-NPC-003 — Concerned Companions. Hulgi's introduction is authored
        // content and lives here with every other player-facing string, so a
        // translator replaces the story in the same file as the buttons.
        ["companion.hulgi.name"] = "Hulgi",
        ["companion.compass.name"] = "Broken compass",
        ["companion.compass.hoverVerb"] = "Examine",
        ["companion.compass.examine"] = "Salt has clouded the glass. The needle will not move.",
        ["companion.compass.prompt"] = "A broken compass lies here. Press [E] to examine it.",
        ["companion.locked"] = "Concerned Cartographer's map tools are waiting on an introduction. Look for a broken compass near where you wake up — or turn on Companions → ToolsOnly in the config to use them straight away.",
        ["companion.welcomed"] = "Hulgi has joined your camp. Concerned Cartographer is ready.",
        ["companion.toolsOnlyChosen"] = "Just the tools, then. Concerned Cartographer is ready.",
        ["companion.saveFailed"] = "That choice could not be saved, so nothing was changed. The compass is still there — try again.",
        ["companion.storyUnavailable"] = "The introduction cannot be shown right now. The compass stays where it is; try again in a moment.",
        ["companion.replayUnavailable"] = "There is no introduction to replay yet.",
        ["story.page"] = "Page {0} of {1}",
        ["story.back"] = "Back",
        ["story.next"] = "Next",
        ["story.close"] = "Close",
        ["story.welcome"] = "Welcome aboard, Hulgi.",
        ["story.toolsOnly"] = "Just the tools, thanks.",
        ["story.hulgi.page1"] = "Salt has clouded the glass. The needle will not move.\n\nScratched into the back, in a steadier hand than the rest:\n\n\"Be careful, Hulgi!\"",
        ["story.hulgi.page2"] = "\"I was not.\n\nTrusted the weather, then my luck. The sea had other ideas. Never made it home.\"",
        ["story.hulgi.page3"] = "\"Someone there was waiting for me. Small fellow. Four paws. Very poor at forgiving a late supper.\"",
        ["companion.presentationUnavailable"] = "Hulgi cannot be shown on this game build, so he stays out of sight. Your tools, your progress and his company are unaffected.",
        ["companion.hiddenNotice"] = "Hulgi is hidden. Your tools, progress and data are unaffected.",
        ["story.hulgi.page4"] = "\"I cannot change my last voyage. Perhaps I can help with yours.\n\nHulgi the cartographer, at your service.\"",
    };

    private static Dictionary<string, string> _overrides = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Defaults => English;

    public static void LoadOverrides(Dictionary<string, string> overrides)
    {
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static string Get(string key)
    {
        if (_overrides.TryGetValue(key, out string? translated) && !string.IsNullOrEmpty(translated))
        {
            return translated;
        }

        return English.TryGetValue(key, out string? english) ? english : key;
    }

    public static string Format(string key, params object[] arguments)
    {
        try
        {
            return string.Format(Get(key), arguments);
        }
        catch (FormatException)
        {
            // A broken translation must never crash the UI.
            return English.TryGetValue(key, out string? english) ? string.Format(english, arguments) : key;
        }
    }

    /// <summary>The translator template: every key with its English text.</summary>
    public static IEnumerable<string> TranslatorTemplate()
    {
        yield return TemplateHeader;
        foreach (KeyValuePair<string, string> entry in English)
        {
            yield return entry.Key + "\t" + AtlasText.Escape(entry.Value);
        }
    }

    /// <summary>Parses a translation file; malformed or unknown-key rows are
    /// counted and skipped.</summary>
    public static Dictionary<string, string> ParseOverrides(IEnumerable<string> lines, out int skippedRows)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        skippedRows = 0;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                skippedRows++;
                continue;
            }

            string key = line.Substring(0, tab);
            if (!English.ContainsKey(key))
            {
                skippedRows++;
                continue;
            }

            overrides[key] = AtlasText.Unescape(line.Substring(tab + 1));
        }

        return overrides;
    }
}
