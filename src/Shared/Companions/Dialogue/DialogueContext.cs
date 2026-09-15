using System;
using System.Collections.Generic;

namespace TheConcernedCat.Companions.Dialogue;

/// <summary>What the rotation is allowed to know about the local character.
///
/// Deliberately tiny. The only thing dialogue may condition on is where this
/// character has already been, which is what keeps the companion from hinting
/// at content the player has not reached - or worse, at content only somebody
/// else on the server has reached.</summary>
internal sealed class DialogueContext
{
    private readonly HashSet<string> _knownBiomes;

    public DialogueContext(IEnumerable<string>? knownBiomes = null)
    {
        _knownBiomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (knownBiomes == null)
        {
            return;
        }

        foreach (string biome in knownBiomes)
        {
            if (!string.IsNullOrEmpty(biome))
            {
                _knownBiomes.Add(biome);
            }
        }
    }

    /// <summary>An empty context: nothing is known, so no biome-gated line is
    /// eligible. This is the correct state whenever progression cannot be read,
    /// because the failure mode of guessing is a spoiler.</summary>
    public static DialogueContext Empty { get; } = new DialogueContext();

    public bool KnowsBiome(string? biome)
    {
        return !string.IsNullOrEmpty(biome) && _knownBiomes.Contains(biome!);
    }

    public bool Allows(DialogueLine line)
    {
        return line.RequiredBiome == null || KnowsBiome(line.RequiredBiome);
    }

    public int KnownBiomeCount => _knownBiomes.Count;
}
