namespace TheConcernedCat.Companions.Dialogue;

/// <summary>What a companion line is about.
///
/// Categories exist so a rotation can stay varied on purpose rather than by
/// luck: without them, a catalogue drifts towards whichever subject was easiest
/// to write and the companion starts sounding like a single joke.</summary>
internal enum DialogueCategory
{
    /// <summary>Home, the cat, the life that was left behind.</summary>
    CatAndHome = 0,

    /// <summary>Caution, preparation, and knowing when to turn back.</summary>
    Travel = 1,

    /// <summary>Weather and the sea.</summary>
    Weather = 2,

    /// <summary>Practical advice about a place this character already knows.
    /// Gated on <see cref="DialogueLine.RequiredBiome"/> so nothing is ever said
    /// about somewhere they have not been.</summary>
    BiomeTip = 3,

    /// <summary>Acknowledging the player's arrival or return.</summary>
    Greeting = 4,
}
