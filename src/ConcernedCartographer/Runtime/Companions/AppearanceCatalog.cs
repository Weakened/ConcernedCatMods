using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Companions;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>The hair and beard presets this game build actually has, with the
/// labels its own customization screen shows for them.
///
/// The preset names are serialized values inside compressed asset bundles, not
/// API, and unpacking those bundles would be asset extraction. So the list is
/// read from the live item table at runtime through <c>ObjectDB.GetAllItems</c>
/// — the same call <c>PlayerCustomizaton</c> makes — and localized through
/// <c>Localization</c>, which is how the owner's label "Long Braid" becomes a
/// lookup rather than a guess.
///
/// Running <c>cc_companion appearance</c> in game prints what was really found
/// and which key matched, which is what turns "the reference says Long Braid"
/// into an observation a tuning pass can use.</summary>
internal sealed class AppearanceCatalog
{
    /// <summary>Valheim names these prefabs by family prefix, and its own
    /// customization screen filters on exactly these two strings. Used only to
    /// group what was found — never to assume a particular member exists.</summary>
    public const string HairPrefix = "Hair";
    public const string BeardPrefix = "Beard";

    private AppearanceCatalog(
        IReadOnlyList<AppearanceOption> hair,
        IReadOnlyList<AppearanceOption> beards,
        int totalPrefabs,
        bool available,
        bool localized)
    {
        Hair = hair;
        Beards = beards;
        TotalPrefabs = totalPrefabs;
        Available = available;
        Localized = localized;
    }

    public IReadOnlyList<AppearanceOption> Hair { get; }

    public IReadOnlyList<AppearanceOption> Beards { get; }

    /// <summary>How many names were visible at all. Distinguishes "this build
    /// has no hair presets" from "the item table was not ready".</summary>
    public int TotalPrefabs { get; }

    /// <summary>False when no table could be read. The caller then skips
    /// appearance entirely rather than concluding the presets are missing.</summary>
    public bool Available { get; }

    /// <summary>True when display labels were resolved. False means matching
    /// falls back to prefab ids, which the report says out loud.</summary>
    public bool Localized { get; }

    public static AppearanceCatalog Empty =>
        new AppearanceCatalog(
            new AppearanceOption[0], new AppearanceOption[0], 0, available: false, localized: false);

    public static AppearanceCatalog Read()
    {
        bool localized = false;
        List<AppearanceOption>? hair = ReadCustomizationItems(HairPrefix, ref localized);
        List<AppearanceOption>? beards = ReadCustomizationItems(BeardPrefix, ref localized);

        if (hair != null && beards != null && (hair.Count > 0 || beards.Count > 0))
        {
            return new AppearanceCatalog(
                hair, beards, hair.Count + beards.Count, available: true, localized: localized);
        }

        // The customization query was unavailable on this build. Fall back to
        // the raw prefab-name scan, which loses the labels but keeps the prefab
        // ids working as a secondary key.
        List<string>? names = ReadPrefabNames();
        if (names == null || names.Count == 0)
        {
            return Empty;
        }

        IReadOnlyList<AppearanceOption> all = AppearancePlan.FromPrefabNames(names);
        return new AppearanceCatalog(
            AppearancePlan.FilterFamily(all, HairPrefix),
            AppearancePlan.FilterFamily(all, BeardPrefix),
            names.Count,
            available: true,
            localized: false);
    }

    /// <summary>Asks the item table for one customization family, exactly as
    /// the game's own character screen does, and localizes each label.
    ///
    /// The <c>_</c> filter is the game's: variant prefabs like
    /// <c>Hair11_Sami</c> are not offered on the customization screen, so a
    /// companion should not wear one either. Read-only in the strictest sense —
    /// names and labels are copied out and nothing is instantiated, modified or
    /// equipped.</summary>
    private static List<AppearanceOption>? ReadCustomizationItems(string prefix, ref bool localized)
    {
        try
        {
            if (ObjectDB.instance == null)
            {
                return null;
            }

            List<ItemDrop> items = ObjectDB.instance.GetAllItems(
                ItemDrop.ItemData.ItemType.Customization, prefix);
            if (items == null)
            {
                return null;
            }

            var options = new List<AppearanceOption>(items.Count);
            foreach (ItemDrop item in items)
            {
                if (item == null || item.gameObject == null)
                {
                    continue;
                }

                string prefabName = item.gameObject.name;
                if (string.IsNullOrEmpty(prefabName) || prefabName.IndexOf('_') >= 0)
                {
                    continue;
                }

                string? label = Localize(item);
                if (label != null)
                {
                    localized = true;
                }

                options.Add(new AppearanceOption(prefabName, label));
            }

            options.Sort((left, right) =>
                string.Compare(left.PrefabName, right.PrefabName, StringComparison.OrdinalIgnoreCase));
            return options;
        }
        catch
        {
            return null;
        }
    }

    private static string? Localize(ItemDrop item)
    {
        try
        {
            string token = item.m_itemData?.m_shared?.m_name ?? "";
            if (string.IsNullOrEmpty(token) || Localization.instance == null)
            {
                return null;
            }

            string label = Localization.instance.Localize(token);

            // An unresolved token comes back looking like "$item_hair11". That
            // is not a label, and matching the owner's "Long Braid" against it
            // would silently fail rather than fall back.
            if (string.IsNullOrEmpty(label) || label.IndexOf('$') >= 0)
            {
                return null;
            }

            return label;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads every prefab name, preferring <c>ObjectDB</c> (where
    /// wearable items live) and falling back to the scene table.</summary>
    private static List<string>? ReadPrefabNames()
    {
        var names = new List<string>();

        try
        {
            if (ObjectDB.instance != null && ObjectDB.instance.m_items != null)
            {
                foreach (GameObject item in ObjectDB.instance.m_items)
                {
                    if (item != null && !string.IsNullOrEmpty(item.name))
                    {
                        names.Add(item.name);
                    }
                }
            }
        }
        catch
        {
            // Fall through to the scene table.
        }

        try
        {
            if (ZNetScene.instance != null)
            {
                List<string> scene = ZNetScene.instance.GetPrefabNames();
                if (scene != null)
                {
                    names.AddRange(scene);
                }
            }
        }
        catch
        {
            // Whatever was gathered above still stands.
        }

        return names.Count == 0 ? null : names;
    }

    /// <summary>A short, human-readable dump for the console tool. It leads
    /// with what the owner reference asked for and whether this build has it,
    /// because that is the acceptance row; the raw lists follow, capped,
    /// because a customization family can hold dozens of entries and a console
    /// window cannot.</summary>
    public string Describe(
        int maxPerFamily = 20,
        string? hairOverride = null,
        string? beardOverride = null)
    {
        if (!Available)
        {
            return "Appearance presets: the item table is not readable yet (load a world first).";
        }

        AppearanceChoice hair = AppearancePlan.Choose(Hair, AppearancePlan.HulgiHair, hairOverride);
        AppearanceChoice beard = AppearancePlan.Choose(Beards, AppearancePlan.HulgiBeard, beardOverride);
        CustomizationPalette palette = CustomizationPaletteReader.Read();
        ColourTriple colour = AppearanceColour.HulgiHair(palette);

        return "Appearance presets from this build (" + TotalPrefabs + " customization entries" +
            (Localized ? ", labels resolved" : ", LABELS UNAVAILABLE - matching by prefab id only") + ")\n" +
            "  asked for: hair \"" + AppearancePlan.HulgiHair.DisplayName + "\", beard \"" +
            AppearancePlan.HulgiBeard.DisplayName + "\" (owner reference CC-NPC-006)\n" +
            "  hair  -> " + Describe(hair) + "\n" +
            "  beard -> " + Describe(beard) + "\n" +
            "  hair colour -> " + colour + " from " +
            (palette.Observed
                ? "the live customization palette (tone " + AppearanceColour.HulgiHairTone +
                  ", level " + AppearanceColour.HulgiHairLevel + ")"
                : "the built-in fallback; the live palette was not readable this session") + "\n" +
            "  hair  (" + Hair.Count + "): " + Join(Hair, maxPerFamily) + "\n" +
            "  beard (" + Beards.Count + "): " + Join(Beards, maxPerFamily);
    }

    private static string Describe(AppearanceChoice choice)
    {
        if (choice.PrefabName == null)
        {
            return "<none found; the model keeps its own>";
        }

        return choice + (choice.MatchesReference ? "  MATCHES REFERENCE" : "  fallback");
    }

    private static string Join(IReadOnlyList<AppearanceOption> values, int max)
    {
        if (values.Count == 0)
        {
            return "<none found>";
        }

        int shown = Math.Min(values.Count, max);
        var builder = new System.Text.StringBuilder();
        for (int index = 0; index < shown; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[index].ToString());
        }

        if (values.Count > shown)
        {
            builder.Append(", … (+").Append(values.Count - shown).Append(" more)");
        }

        return builder.ToString();
    }
}
