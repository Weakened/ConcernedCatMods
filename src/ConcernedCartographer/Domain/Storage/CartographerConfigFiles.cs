using System;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>The only files of this product's that belong in the settings
/// folder: the ones a person is meant to open and change.
///
/// <b>Why the list exists rather than the rule being kept in someone's head.</b>
/// A mod manager presents everything under <c>BepInEx/config</c> as this mod's
/// settings. #304 is what happens when something that is not a setting lands
/// there: Quandru was offered <c>author-id.txt</c> — a generated GUID the atlas
/// keys its ownership on — as though it were a thing to edit. The fix is not a
/// note explaining why a data file is in the settings folder; it is that data
/// files are not in the settings folder. This list is the whole of what is.
///
/// <see cref="DataRelocation"/> reads it to decide what stays put, and the
/// validator holds it and <c>CartographerPaths.InConfig</c> to each other in
/// both directions, so adding a file to one without the other fails the gate.
/// Adding a name here is a decision that a player edits that file by hand.
/// </summary>
internal static class CartographerConfigFiles
{
    /// <summary>The names, as literals, because the validator reads them from
    /// this source.</summary>
    public static readonly string[] Names =
    {
        // The survey rule set: the shareable import/export format, and the one
        // file the product actively expects people to edit (#385 exists
        // entirely to keep an edited copy of it from ever being trampled).
        "survey-rules.tsv",

        // A translator's overrides, and the template they are copied from.
        // The template is written by this build, but it is here for a person
        // to take, so the settings folder is where it belongs.
        "cartographer-strings.tsv",
        "cartographer-strings-template.tsv",
    };

    /// <summary>Whether a file directly in the settings folder is one of ours
    /// that belongs there.
    ///
    /// A name this cannot account for is <b>not</b> configuration, so it is
    /// moved out. That is the direction that fixes #304; the opposite reading
    /// would let any future file quietly reappear in the settings editor.
    /// Files another mod owns are not in this product's folder at all.</summary>
    public static bool IsConfiguration(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        foreach (string name in Names)
        {
            if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
