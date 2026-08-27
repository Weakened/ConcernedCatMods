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
        ["workbench.vanillaPin"] = "Vanilla pin",
        ["workbench.foreignPin"] = "Foreign pin",
        ["workbench.foreignInfo"] = "owned by the game or another mod; Concerned Cartographer never edits it.",
        ["workbench.adoptInfo"] = "Not managed yet. Adopting preserves position, icon, name, and checked state.",
        ["workbench.adopt"] = "Adopt this pin",
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
        ["drawer.placeholders"] = "Routes: cc_routes console   ·   Sharing: cc_sync console",
        ["hud.quickPinNothing"] = "Nothing targeted to pin.",
        ["hud.quickPinCreature"] = "Creatures are not pinned.",
        ["hud.quickPinned"] = "Pinned \"{0}\".",
        ["hud.quickPinDuplicate"] = "\"{0}\" is already pinned {1} m away.",
        ["hud.surveyObservations"] = "Survey: {0} new observation(s) — review with cc_survey",
        ["hud.syncReceived"] = "Atlas share received from {0} — review with cc_sync preview",
        ["hud.onboarding"] = "Concerned Cartographer: press {0} on the large map for the Atlas, {1} over a pin to edit it.",
        ["hud.noMapNeedTable"] = "The atlas needs a cartography table nearby in a nomap world.",
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
