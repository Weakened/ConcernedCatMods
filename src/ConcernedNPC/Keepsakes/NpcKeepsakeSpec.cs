using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Keepsakes;

/// <summary>What a role's keepsake looks like and says: the small local object
/// that starts a companion's introduction (#295 S2, owner decision 2026-10-30:
/// one shared keepsake for Thorstein's square, Gunnar's buckle and Sunniva's
/// flint). All of it is the role's data; this package names no object, no
/// prefab and no line.</summary>
public sealed class NpcKeepsakeSpec
{
    public NpcKeepsakeSpec(string hoverName, string hoverVerb, IReadOnlyList<string> prefabCandidates, float scale)
    {
        if (string.IsNullOrWhiteSpace(hoverName))
        {
            throw new ArgumentException("A keepsake has a name the player sees.", nameof(hoverName));
        }

        if (string.IsNullOrWhiteSpace(hoverVerb))
        {
            throw new ArgumentException("A keepsake says what using it does.", nameof(hoverVerb));
        }

        if (prefabCandidates == null || prefabCandidates.Count == 0)
        {
            throw new ArgumentException("A keepsake borrows its look from at least one game prefab.",
                nameof(prefabCandidates));
        }

        if (float.IsNaN(scale) || scale <= 0f || scale > 10f)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "A keepsake's scale is above 0 and at most 10.");
        }

        HoverName = hoverName.Trim();
        HoverVerb = hoverVerb.Trim();
        PrefabCandidates = prefabCandidates;
        Scale = scale;
    }

    /// <summary>The name in the hover prompt, e.g. "Worn carpenter's square".</summary>
    public string HoverName { get; }

    /// <summary>What the use key does, e.g. "Examine".</summary>
    public string HoverVerb { get; }

    /// <summary>Game prefabs whose meshes the keepsake borrows, best first. Read
    /// from the installed game; one that is missing is skipped, and if none is
    /// found no keepsake is placed.</summary>
    public IReadOnlyList<string> PrefabCandidates { get; }

    /// <summary>Uniform scale applied to the borrowed meshes.</summary>
    public float Scale { get; }
}
