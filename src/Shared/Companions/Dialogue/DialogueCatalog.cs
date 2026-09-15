using System;
using System.Collections.Generic;

namespace TheConcernedCat.Companions.Dialogue;

/// <summary>An immutable set of lines a companion can say.
///
/// Ownership matters here: the catalogue is a container, and the lines in it
/// belong to the product that built it. Nothing about a particular companion's
/// voice lives in this shared layer.</summary>
internal sealed class DialogueCatalog
{
    private readonly List<DialogueLine> _lines;

    public DialogueCatalog(IEnumerable<DialogueLine> lines)
    {
        if (lines == null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        _lines = new List<DialogueLine>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DialogueLine line in lines)
        {
            if (line == null)
            {
                continue;
            }

            // A duplicate key would let one line be said twice as often as the
            // rest while the suppression window believed otherwise.
            if (!seenKeys.Add(line.Key))
            {
                throw new ArgumentException(
                    "Duplicate dialogue key in catalogue: " + line.Key, nameof(lines));
            }

            _lines.Add(line);
        }
    }

    public IReadOnlyList<DialogueLine> Lines => _lines;

    public int Count => _lines.Count;

    public int CountIn(DialogueCategory category)
    {
        int count = 0;
        foreach (DialogueLine line in _lines)
        {
            if (line.Category == category)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Lines eligible right now: the right category, and permitted by
    /// what this character knows.</summary>
    /// <summary>This line's position in the catalogue, or -1.
    ///
    /// Exists so a rotation cursor can be expressed in CATALOGUE space rather
    /// than in the space of whichever filtered list it happened to walk last.
    /// A cursor folded into a three-line category and then reused for the whole
    /// catalogue restarts near the front every time, which starves the tail —
    /// and the tail is where most of a 24-line catalogue lives.</summary>
    public int IndexOf(DialogueLine? line)
    {
        if (line == null)
        {
            return -1;
        }

        for (int index = 0; index < _lines.Count; index++)
        {
            if (string.Equals(_lines[index].Key, line.Key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public List<DialogueLine> Eligible(DialogueCategory? category, DialogueContext context)
    {
        DialogueContext effective = context ?? DialogueContext.Empty;
        var eligible = new List<DialogueLine>();
        foreach (DialogueLine line in _lines)
        {
            if (category.HasValue && line.Category != category.Value)
            {
                continue;
            }

            if (effective.Allows(line))
            {
                eligible.Add(line);
            }
        }

        return eligible;
    }

    /// <summary>Categories with at least one line that is not biome gated. Used
    /// to prove a catalogue still has something to say to a character who has
    /// been nowhere.</summary>
    public IReadOnlyList<DialogueCategory> UnconditionalCategories()
    {
        var found = new List<DialogueCategory>();
        foreach (DialogueLine line in _lines)
        {
            if (line.RequiredBiome == null && !found.Contains(line.Category))
            {
                found.Add(line.Category);
            }
        }

        return found;
    }
}
