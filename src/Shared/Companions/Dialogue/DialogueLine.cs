using System;

namespace TheConcernedCat.Companions.Dialogue;

/// <summary>One thing a companion can say.
///
/// The line holds a localization key, never text: the shared layer must not
/// carry authored English, and a translator must be able to change the words
/// without the rotation noticing.</summary>
internal sealed class DialogueLine
{
    public DialogueLine(string key, DialogueCategory category, string? requiredBiome = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A dialogue line needs a localization key.", nameof(key));
        }

        Key = key;
        Category = category;
        RequiredBiome = string.IsNullOrEmpty(requiredBiome) ? null : requiredBiome;
    }

    /// <summary>Localization key. Stable across translations and releases.</summary>
    public string Key { get; }

    public DialogueCategory Category { get; }

    /// <summary>A biome this character must already know before the line is
    /// eligible, or null when the line is safe to say anywhere.
    ///
    /// This is the whole spoiler guard. A line about a place is only offered to
    /// someone who has been there, and "has been there" is always about the
    /// local character - never another player's discoveries, and never
    /// somewhere nobody has been yet.</summary>
    public string? RequiredBiome { get; }

    public override string ToString()
    {
        return Category + ":" + Key;
    }
}
