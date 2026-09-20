using System;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>The files this product writes that a player is meant to open and
/// edit — and therefore the only ones it is <i>correct</i> for a mod manager's
/// configuration editor to offer.
///
/// <b>What this list is for (#304).</b> Quandru was offered
/// <c>author-id.txt</c> for editing: a generated GUID the atlas keys ownership
/// on. The cause was not the folder. A mod manager's configuration editor walks
/// the whole profile and decides what to show by <b>file extension</b> —
/// <c>.cfg .txt .json .yml .yaml .ini</c> in the Thunderstore Mod Manager /
/// r2modman bundle read for this issue. <c>author-id.txt</c> matched; the
/// atlas's <c>.tsv</c> sidecars never have. So the rule that keeps this from
/// happening again is about extensions, not directories: anything this product
/// writes whose extension that editor opens has to be a file a player edits,
/// and that means it has to be on this list. <c>validate_repo.py</c> enforces
/// exactly that, and it is the rule that would have caught both
/// <c>author-id.txt</c> and <c>support-report.txt</c> before a user did.
///
/// <b>An allowlist, not an inventory.</b> None of the names below currently has
/// an extension that editor opens — they are all <c>.tsv</c> — so the list is
/// empty of anything the rule fires on today, and that is the healthy state.
/// It is written down anyway because it says what "a player edits this" means
/// here: adding a name is a decision that somebody is expected to open the file
/// by hand, and a future <c>.json</c> preset or <c>.ini</c> would need to be on
/// it before it could be written at all.
///
/// <b>Not a statement about where files live.</b> Everything this product
/// writes stays under <c>BepInEx/config/ConcernedCatMods/ConcernedCartographer</c>,
/// because that folder is the one a profile export copies wholesale and
/// unfiltered; anywhere else and a <c>.tsv</c> atlas would be dropped from the
/// player's own backup. <c>CartographerPaths</c> has the measurements.</summary>
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
        // The template is written by this build, but it exists for a person to
        // take, so a person finding it is the intended outcome.
        "cartographer-strings.tsv",
        "cartographer-strings-template.tsv",
    };

    /// <summary>Whether a file this product writes is one a player is meant to
    /// edit.
    ///
    /// A name this cannot account for is <b>not</b> something to edit. That is
    /// the direction that fixes #304; the opposite reading would let any future
    /// file quietly become editable.</summary>
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

    /// <summary>The extensions a mod-manager configuration editor opens, read
    /// from the Thunderstore Mod Manager / r2modman bundle's own
    /// <c>SUPPORTED_CONFIG_FILE_EXTENSIONS</c>.
    ///
    /// Here so a test can pin it and so the reasoning above is checkable rather
    /// than remembered. The validator keeps its own copy, because a Python
    /// check cannot import this.</summary>
    public static readonly string[] ExtensionsAnEditorOpens =
    {
        ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini",
    };

    /// <summary>Whether a configuration editor would offer this file, by
    /// extension alone — which is the only thing it looks at.</summary>
    public static bool AnEditorWouldOfferThis(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        foreach (string extension in ExtensionsAnEditorOpens)
        {
            if (fileName!.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
