using System;
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
/// A bare rename would have been: <c>author-id.dat</c> is not in
/// <c>CartographerFirstRunFiles.Names</c>, so every brand-new player would have
/// been classified as a returning one and the #264 introduction would never
/// have run again for anybody.
///
/// <c>backups/</c> already sits beside it and survives updates, so this is the
/// directory layout the product already has rather than a new idea.</summary>
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

    /// <summary>The outcome of looking for a marker, for the caller's log.
    /// </summary>
    internal enum Adoption
    {
        /// <summary>Nothing to do: no marker and nothing to adopt.</summary>
        Nothing,

        /// <summary>The marker was already there.</summary>
        AlreadyThere,

        /// <summary>An older build's file became the marker.</summary>
        Adopted,

        /// <summary>An older build's file is there and could not be adopted.
        /// It was <b>not</b> deleted.</summary>
        Failed,
    }

    /// <summary>The marker's path, adopting a value an older build left in the
    /// product directory if one is there and the marker is not.
    ///
    /// <b>Nothing is deleted unread.</b> The old file's contents are read,
    /// checked by <paramref name="isUsable"/>, written to the marker, and read
    /// back; only when the round trip agrees is the old file removed. A failure
    /// at any point leaves the old file exactly where it was, so the next start
    /// tries again — which is what the first version of this claimed and did
    /// not do: it deleted the legacy file whenever a marker existed, so one
    /// interrupted copy destroyed a player's identity on the following start.
    ///
    /// Every failure is reported through <paramref name="log"/>. A migration
    /// that can silently lose the identity the atlas is keyed on must not be
    /// the one thing in this product that says nothing when it fails.</summary>
    /// <param name="directory">The product's data directory.</param>
    /// <param name="name">The marker file name, including
    /// <see cref="Extension"/>.</param>
    /// <param name="legacyName">What the same marker was called before, in
    /// <paramref name="directory"/> itself.</param>
    /// <param name="isUsable">Whether adopted contents are worth keeping. A
    /// marker whose content this build cannot use is not adopted, so the
    /// caller writes a fresh one rather than inheriting a damaged value.</param>
    /// <param name="log">Told why an adoption did not happen.</param>
    public static string Adopt(
        string directory,
        string name,
        string legacyName,
        Func<string, bool>? isUsable = null,
        Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("A directory is required.", nameof(directory));
        }

        string marker;
        string legacy;
        try
        {
            marker = Path.Combine(directory, FolderName, name);
            legacy = Path.Combine(directory, legacyName);
        }
        catch (ArgumentException)
        {
            // .NET Framework's Path.Combine rejects characters .NET Core
            // accepts. A bad config path must not take the plugin down with
            // it — the caller gets a path it will fail to write, and says so.
            Report(log, "the marker path could not be composed");
            return Path.Combine(directory, name);
        }

        Adopt(marker, legacy, isUsable, log);
        return marker;
    }

    private static void Adopt(string marker, string legacy, Func<string, bool>? isUsable, Action<string>? log)
    {
        try
        {
            if (File.Exists(marker))
            {
                // Already adopted, or written fresh. The old file is left
                // alone: this build does not read it, and deleting somebody's
                // data because we happen to have our own copy is not ours to
                // do. It costs one listed file in a folder nothing scans.
                return;
            }

            if (!File.Exists(legacy))
            {
                return;
            }

            string contents = File.ReadAllText(legacy);
            if (isUsable != null && !isUsable(contents))
            {
                Report(log, $"\"{Path.GetFileName(legacy)}\" did not contain something this build can use, " +
                            "so it was left alone and a new one will be written");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, contents);

            // Read it back before removing the only other copy. A write that
            // reported success and produced something else is exactly the
            // case that costs a player their identity.
            if (!string.Equals(File.ReadAllText(marker), contents, StringComparison.Ordinal))
            {
                Report(log, $"\"{Path.GetFileName(marker)}\" did not read back as written, so " +
                            $"\"{Path.GetFileName(legacy)}\" was kept");
                return;
            }

            File.Delete(legacy);
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while moving \"{Path.GetFileName(legacy)}\" " +
                        $"into \"{FolderName}\"; it was left where it is");
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
