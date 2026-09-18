using System;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>A one-line file the mod writes for itself and a person never edits:
/// the profile's author identity, the "the tip has been shown" marker.
///
/// These live beside the real sidecars in the mod's folder under
/// <c>BepInEx/config</c>, because that is the one directory a mod manager keeps
/// across an update (DATA_FORMATS.md). They used to be named <c>.txt</c>, and a
/// mod manager's config editor listed <c>author-id.txt</c> among the things a
/// player may edit (#304) — which it is not: it is a generated GUID, and
/// editing or deleting it silently changes who the atlas thinks owns the entries
/// this profile wrote.
///
/// So the marker extension is <see cref="Extension"/>, which no config editor
/// claims, and <see cref="Adopt"/> moves an existing <c>.txt</c> over once and
/// removes it. Nothing is lost by the rename: the identity in the old file is
/// the identity in the new one.</summary>
internal static class MarkerFile
{
    /// <summary>Deliberately not <c>.txt</c>, <c>.cfg</c>, <c>.json</c>,
    /// <c>.ini</c> or <c>.yml</c> — the extensions a configuration editor
    /// offers to open.</summary>
    public const string Extension = ".dat";

    /// <summary>The marker's path, adopting a value left by an older build
    /// under <paramref name="legacyName"/> if one is there and the marker is
    /// not. Returns the marker path whether or not anything was adopted; it
    /// does not create the file.
    ///
    /// Adoption is copy-then-delete rather than a move, so a failure at any
    /// point leaves the old file readable: the worst outcome is that the next
    /// start tries again.</summary>
    /// <param name="directory">The product's folder; created if missing.</param>
    /// <param name="name">The marker file name, including
    /// <see cref="Extension"/>.</param>
    /// <param name="legacyName">What the same marker was called before.</param>
    public static string Adopt(string directory, string name, string legacyName)
    {
        if (directory is null)
        {
            throw new ArgumentNullException(nameof(directory));
        }

        string marker = Path.Combine(directory, name);
        string legacy = Path.Combine(directory, legacyName);

        try
        {
            if (File.Exists(marker))
            {
                // Already adopted. An old file still sitting beside it is the
                // tail of an interrupted adoption, and removing it is the rest
                // of that work — the marker is the truth either way.
                if (File.Exists(legacy))
                {
                    File.Delete(legacy);
                }

                return marker;
            }

            if (!File.Exists(legacy))
            {
                return marker;
            }

            Directory.CreateDirectory(directory);
            File.Copy(legacy, marker, overwrite: true);
            File.Delete(legacy);
        }
        catch (Exception)
        {
            // A marker is never worth failing a load over. The caller finds no
            // marker, writes a fresh one, and the only cost is a new identity
            // or a tip shown twice.
        }

        return marker;
    }
}
