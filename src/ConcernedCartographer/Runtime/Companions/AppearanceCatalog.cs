using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Companions;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>The hair and beard presets this game build actually has.
///
/// The compatibility audit refused to name them, and was right to: they are
/// serialized values inside compressed asset bundles, not API, and unpacking
/// those bundles would be asset extraction. So the list is read from the live
/// prefab tables at runtime, exactly as the audit prescribed, and the choice is
/// made from what is really there.
///
/// This is also how the audit's two open appearance rows get closed. Running
/// <c>cc_companion appearance</c> in game prints the real names, which turns
/// "we think the presets are called something like this" into an observation
/// that a later tuning pass can use.</summary>
internal sealed class AppearanceCatalog
{
    /// <summary>Valheim names these prefabs by family prefix. Used only to
    /// group what was found — never to assume a particular member exists.</summary>
    public const string HairPrefix = "Hair";
    public const string BeardPrefix = "Beard";

    private AppearanceCatalog(
        IReadOnlyList<string> hair, IReadOnlyList<string> beards, int totalPrefabs, bool available)
    {
        Hair = hair;
        Beards = beards;
        TotalPrefabs = totalPrefabs;
        Available = available;
    }

    public IReadOnlyList<string> Hair { get; }

    public IReadOnlyList<string> Beards { get; }

    /// <summary>How many prefab names were visible at all. Distinguishes "this
    /// build has no hair presets" from "the prefab table was not ready".</summary>
    public int TotalPrefabs { get; }

    /// <summary>False when no prefab table could be read. The caller then skips
    /// appearance entirely rather than concluding the presets are missing.</summary>
    public bool Available { get; }

    public static AppearanceCatalog Empty =>
        new AppearanceCatalog(new string[0], new string[0], 0, available: false);

    public static AppearanceCatalog Read()
    {
        List<string>? names = ReadPrefabNames();
        if (names == null || names.Count == 0)
        {
            return Empty;
        }

        return new AppearanceCatalog(
            AppearancePlan.FilterFamily(names, HairPrefix),
            AppearancePlan.FilterFamily(names, BeardPrefix),
            names.Count,
            available: true);
    }

    /// <summary>Reads every prefab name, preferring <c>ObjectDB</c> (where
    /// wearable items live) and falling back to the scene table. Read-only in
    /// the strictest sense: names are copied out and nothing is instantiated,
    /// modified or resolved to an object.</summary>
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

    /// <summary>A short, human-readable dump for the console tool. Capped,
    /// because a prefab table can hold hundreds of names and a console window
    /// cannot.</summary>
    public string Describe(int maxPerFamily = 20)
    {
        if (!Available)
        {
            return "Appearance presets: the prefab table is not readable yet (load a world first).";
        }

        return "Appearance presets from this build (" + TotalPrefabs + " prefabs scanned)\n" +
            "  hair  (" + Hair.Count + "): " + Join(Hair, maxPerFamily) + "\n" +
            "  beard (" + Beards.Count + "): " + Join(Beards, maxPerFamily);
    }

    private static string Join(IReadOnlyList<string> values, int max)
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

            builder.Append(values[index]);
        }

        if (values.Count > shown)
        {
            builder.Append(", … (+").Append(values.Count - shown).Append(" more)");
        }

        return builder.ToString();
    }
}
