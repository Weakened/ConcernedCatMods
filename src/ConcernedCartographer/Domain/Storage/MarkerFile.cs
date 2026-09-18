using System;
using System.Collections.Generic;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>A one-line file the mod writes for itself and a person never edits:
/// the profile's author identity, the "the tip has been shown" marker.
///
/// <b>Where they live, and why it is a subfolder.</b> A mod manager's
/// configuration editor listed <c>author-id.txt</c> among the things a player
/// may edit (#304), and that file is a generated GUID: editing or deleting it
/// silently changes who the atlas believes wrote this profile's entries, which
/// is what the non-owner-delete policy is keyed on. The reporter's own
/// diagnosis was the <i>location</i>, so these markers move into a
/// <see cref="FolderName"/> subfolder of the product's data directory rather
/// than merely changing extension — which would have been a guess about which
/// files that editor lists, and would have been wrong if it lists them all.
///
/// The subfolder is also what makes this safe for something else entirely.
/// <c>CartographerLegacyProbe</c> decides whether a player is new or returning
/// by listing this directory with <c>Directory.GetFiles</c> and asking whether
/// everything in it is a name this build writes for itself. <c>GetFiles</c>
/// does not return subdirectories, so a marker under <see cref="FolderName"/>
/// is invisible to that listing and cannot be mistaken for a player's own data.
/// A bare rename would have been: <c>author-id.dat</c> in the product directory
/// is a name that listing does not recognise, so every brand-new player would
/// have been classified as a returning one and the #264 introduction would
/// never have run again for anybody. That regression shipped once (#343) and
/// this is the shape that cannot reproduce it.
///
/// <c>backups/</c> already sits beside it, so this is the directory layout the
/// product already has rather than a new idea.</summary>
internal static class MarkerFile
{
    /// <summary>The subfolder. Named for what is in it: state the mod keeps,
    /// not settings a person sets.</summary>
    public const string FolderName = "state";

    /// <summary>Deliberately not <c>.txt</c>, <c>.cfg</c>, <c>.json</c>,
    /// <c>.ini</c> or <c>.yml</c>. The subfolder is what fixes #304; this is
    /// so the file does not read as configuration if somebody does find it.
    /// </summary>
    public const string Extension = ".dat";

    /// <summary>Resolves a marker, adopting a value an older build left behind
    /// if the marker itself does not hold one this build can use.
    ///
    /// <b>Existence is not adoption.</b> The first version of this returned as
    /// soon as the marker file was present, without ever reading it — so a
    /// marker that was empty, truncated by a torn write, or freshly minted
    /// during a transient failure permanently ended the migration, and the
    /// older build's file sat unread beside it forever while the caller carried
    /// on with a different identity. Every early return here is guarded by
    /// <paramref name="isUsable"/>, so the only thing that stops the search is
    /// an answer, never a file.
    ///
    /// <b>Prior locations are a list, in order.</b> Two builds have written
    /// this marker in two different places, and a profile that ran the middle
    /// one has neither the original file nor the current path. One name was
    /// enough to lose those profiles' identities silently.
    ///
    /// <b>Nothing is deleted unread.</b> Contents are read, checked, staged to
    /// a temporary file, copied onto the marker, and read back; only when the
    /// round trip agrees is the old file removed. A failure at any point leaves
    /// the old file exactly where it was and the next start tries again — which
    /// is only true because a marker is now re-examined rather than trusted.
    ///
    /// Every failure is reported through <paramref name="log"/>. A migration
    /// that can silently lose the identity the atlas is keyed on must not be
    /// the one thing in this product that says nothing when it fails.</summary>
    /// <param name="directory">The product's data directory.</param>
    /// <param name="name">The marker file name, including
    /// <see cref="Extension"/>.</param>
    /// <param name="legacyNames">Where this marker has lived before, relative
    /// to <paramref name="directory"/>, oldest first. Each is tried in turn.
    /// </param>
    /// <param name="isUsable">Whether contents are worth keeping. Required:
    /// defaulting it to "anything will do" made the unsafe behaviour the one
    /// you get by leaving an argument out.</param>
    /// <param name="log">Told why an adoption did not happen. Required, for
    /// the same reason.</param>
    /// <param name="path">Where the marker belongs, whether or not one is
    /// there. Always set.</param>
    /// <param name="contents">The usable value found, or null when there is
    /// none and the caller must create one.</param>
    /// <returns>True when <paramref name="contents"/> holds a usable value.
    /// </returns>
    public static bool TryResolve(
        string directory,
        string name,
        IReadOnlyList<string> legacyNames,
        Func<string, bool> isUsable,
        Action<string> log,
        out string path,
        out string? contents)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("A directory is required.", nameof(directory));
        }

        if (isUsable == null)
        {
            throw new ArgumentNullException(nameof(isUsable));
        }

        path = Path.Combine(directory, FolderName, name);
        contents = null;

        try
        {
            // 1. The marker, read rather than merely counted.
            if (TryRead(path, isUsable, out string? held))
            {
                contents = held;
                return true;
            }

            // 2. Every place this marker has lived before, oldest first.
            foreach (string legacyName in legacyNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(legacyName))
                {
                    continue;
                }

                string legacy = Path.Combine(directory, legacyName);
                if (!TryRead(legacy, isUsable, out string? found) || found is null)
                {
                    continue;
                }

                if (TryWrite(path, found, log))
                {
                    Delete(legacy, log);
                }

                // Returned whether or not the write succeeded. The value is
                // good and the caller must not mint over it; a failed write
                // only means the migration is tried again next start, with the
                // legacy file still in place because it was not deleted.
                contents = found;
                return true;
            }
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while looking for \"{name}\"; " +
                        "nothing was moved or removed");
        }

        return false;
    }

    /// <summary>Writes a marker, staged through a temporary file the way every
    /// other writer in this product does.
    ///
    /// The marker was the only thing here written straight onto its
    /// destination, and it is the one file whose loss cannot be recovered from
    /// anywhere else — a torn write left a truncated marker that the old
    /// existence check then trusted forever.</summary>
    public static bool TryWrite(string path, string contents, Action<string> log)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, contents);

            // Read the staged copy back before it replaces anything. A write
            // that reported success and produced something else is exactly the
            // case that costs a player their identity.
            if (!string.Equals(File.ReadAllText(temporary), contents, StringComparison.Ordinal))
            {
                Report(log, $"\"{Path.GetFileName(path)}\" did not read back as written, so " +
                            "nothing was replaced");
                Delete(temporary, null);
                return false;
            }

            File.Copy(temporary, path, overwrite: true);
            Delete(temporary, null);
            return true;
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while writing \"{Path.GetFileName(path)}\"");
            Delete(temporary, null);
            return false;
        }
    }

    private static bool TryRead(string path, Func<string, bool> isUsable, out string? contents)
    {
        contents = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string text = File.ReadAllText(path);
            if (!isUsable(text))
            {
                return false;
            }

            contents = text;
            return true;
        }
        catch (Exception)
        {
            // Unreadable is not unusable: say nothing here and let the caller's
            // own report cover it, so a locked file does not produce one
            // warning per candidate location.
            return false;
        }
    }

    private static void Delete(string path, Action<string>? log)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while removing \"{Path.GetFileName(path)}\"; " +
                        "it was left where it is");
        }
    }

    private static void Report(Action<string>? log, string what)
    {
        try
        {
            log?.Invoke(what);
        }
        catch (Exception)
        {
            // A broken log sink must not turn a handled failure into an
            // unhandled one.
        }
    }
}
