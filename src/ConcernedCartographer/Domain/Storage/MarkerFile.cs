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
/// That subfolder was the first half of the answer and not the whole of it: it
/// was still inside the settings folder, so it was still a guess that an editor
/// does not descend. The product's data directory is now a sibling of the
/// settings folder rather than a child of it (<c>CartographerPaths</c>), which
/// is the same reasoning carried to the end — a settings editor has nothing of
/// ours to list because nothing of ours that is not a setting is there.
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
/// this is the shape that cannot reproduce it.</summary>
internal static class MarkerFile
{
    /// <summary>The subfolder. Named for what is in it: state the mod keeps,
    /// not settings a person sets.</summary>
    public const string FolderName = "state";

    /// <summary>Deliberately not <c>.txt</c>, <c>.cfg</c>, <c>.json</c>,
    /// <c>.ini</c> or <c>.yml</c>. The subfolder is what fixes #304; this is
    /// so the file does not read as configuration if somebody does find it.
    ///
    /// <b>Changing this is a migration, not a rename.</b> The prior-location
    /// lists callers pass are historical facts and must be written as literals,
    /// never derived from this constant — deriving them would silently rewrite
    /// history and lose every affected profile's identity the way #343 did.
    /// </summary>
    public const string Extension = ".dat";

    /// <summary>What a search for a marker concluded.</summary>
    internal enum MarkerSearch
    {
        /// <summary>A usable value was found, from the marker or from a prior
        /// location that has now been adopted.</summary>
        Found,

        /// <summary>Nothing is anywhere. The caller may create a value.
        /// </summary>
        NothingAnywhere,

        /// <summary>A prior location <b>exists</b> and could not be read, or did
        /// not hold something usable.
        ///
        /// <b>The caller must not create a value.</b> Creating one writes a
        /// marker that will itself read as usable on the next start, and from
        /// then on nothing looks at the prior file again — which is how one bad
        /// moment permanently orphaned a profile's identity even after the
        /// marker's contents were being validated.</summary>
        PriorFileUnread,
    }

    /// <summary>Finds the value a marker should hold, adopting a prior location
    /// when there is one.
    ///
    /// <b>A prior file that still exists wins.</b> Its continued presence is
    /// proof that adoption never completed, and it predates the marker — so it
    /// is the value the profile's pins and routes were actually authored under,
    /// and the marker beside it is either a partial copy of it or something this
    /// build minted during a failure. Preferring the marker left real identities
    /// orphaned while <c>author-id.txt</c> sat unread in the settings folder for
    /// the life of the profile, which is also the literal #304 report.
    ///
    /// <b>Newest prior location first.</b> Where two exist, the later build's is
    /// the one the atlas was most recently keyed on.
    ///
    /// <b>Nothing is deleted unread.</b> Contents are read, checked, staged to a
    /// temporary file, copied onto the marker, and <i>the marker</i> is then read
    /// back; only when that round trip agrees is any prior file removed.</summary>
    /// <param name="directory">The product's data directory.</param>
    /// <param name="name">The marker file name, including
    /// <see cref="Extension"/>.</param>
    /// <param name="priorNames">Where this marker has lived before, relative to
    /// <paramref name="directory"/>, <b>newest first</b>. Literals, not derived
    /// from <see cref="Extension"/>.</param>
    /// <param name="priorDirectories">Product directories this marker set has
    /// lived in before <paramref name="directory"/>, <b>newest first</b>, and
    /// never <paramref name="directory"/> itself. Each is searched for
    /// <see cref="FolderName"/>/<paramref name="name"/> and then for each
    /// <paramref name="priorNames"/> entry.
    ///
    /// <b>This is what keeps an identity across #304.</b> The settings folder
    /// held every marker until the data folder existed, and
    /// <see cref="DataRelocation"/> moves them on the first start of a build
    /// that has one. If that move cannot finish — a locked file, a read-only
    /// profile — a search of the new directory alone finds nothing, the caller
    /// mints a fresh GUID, and the player is silently somebody else while their
    /// real identity sits unread a folder away. A directory a marker has lived
    /// in is a prior location in exactly the sense a name is.</param>
    /// <param name="isUsable">Whether contents are worth keeping. Required:
    /// defaulting it to "anything will do" made the unsafe behaviour the one you
    /// get by leaving an argument out.</param>
    /// <param name="log">Told why an adoption did not happen. Required, for the
    /// same reason.</param>
    /// <param name="path">Where the marker belongs, whether or not one is there.
    /// Always set.</param>
    /// <param name="contents">The usable value found, or null.</param>
    public static MarkerSearch Resolve(
        string directory,
        string name,
        IReadOnlyList<string> priorNames,
        IReadOnlyList<string> priorDirectories,
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
        bool priorFileSeen = false;

        try
        {
            // 1. Prior locations, newest first. A file still sitting in one of
            //    them means adoption never finished, and it predates the marker.
            foreach (string prior in PriorLocations(directory, name, priorNames, priorDirectories))
            {
                if (PathsMatch(prior, path) || !Exists(prior))
                {
                    continue;
                }

                priorFileSeen = true;
                if (!TryRead(prior, isUsable, log, out string? found) || found is null)
                {
                    continue;
                }

                if (TryWrite(path, found, log))
                {
                    // Every prior copy, not only the one adopted from. Leaving
                    // the others behind keeps a raw GUID visible in the config
                    // editor for the life of the profile.
                    RemoveAll(directory, name, priorNames, priorDirectories, path, log);
                }

                contents = found;
                return MarkerSearch.Found;
            }

            // 2. The marker, read rather than merely counted.
            if (TryRead(path, isUsable, log, out string? held))
            {
                contents = held;
                return MarkerSearch.Found;
            }
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while looking for \"{name}\"; " +
                        "nothing was moved or removed");
            return MarkerSearch.PriorFileUnread;
        }

        return priorFileSeen ? MarkerSearch.PriorFileUnread : MarkerSearch.NothingAnywhere;
    }

    /// <summary>Writes a marker, staged through a temporary file the way every
    /// other writer in this product does, and verified <b>at its destination</b>.
    ///
    /// The marker was the only thing here written straight onto its destination,
    /// and it is the one file whose loss cannot be recovered from anywhere else.
    /// Reading back the <i>temporary</i> file instead would be near-tautological:
    /// it is the destination that a filter driver, a disk quota or a
    /// network-backed config folder can accept and then truncate, and a prior
    /// file is deleted on the strength of this returning true.</summary>
    public static bool TryWrite(string path, string contents, Action<string> log)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, contents);
            File.Copy(temporary, path, overwrite: true);

            if (!string.Equals(File.ReadAllText(path), contents, StringComparison.Ordinal))
            {
                Report(log, $"\"{Path.GetFileName(path)}\" did not read back as written, so " +
                            "nothing older was removed");
                Delete(temporary, null);
                return false;
            }

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

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            // Cannot even ask. Treated as present, because the expensive mistake
            // is deciding nothing is there and creating a second identity.
            return true;
        }
    }

    /// <summary>Reads a candidate, and <b>says so when it cannot</b>.
    ///
    /// Swallowing the exception here was worse than it looked: the caller's own
    /// reporting catch can never see a failure this method has already eaten, so
    /// an identity could be replaced or created with nothing at all in the log —
    /// under a doc comment promising every failure is reported.</summary>
    private static bool TryRead(
        string path, Func<string, bool> isUsable, Action<string> log, out string? contents)
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
                Report(log, $"\"{Path.GetFileName(path)}\" did not contain something this build " +
                            "can use, so it was left alone");
                return false;
            }

            contents = text;
            return true;
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while reading \"{Path.GetFileName(path)}\"; " +
                        "it was left where it is");
            return false;
        }
    }

    /// <summary>Every place this marker could be sitting, newest first.
    ///
    /// <b>A directory that has not been relocated beats one that has.</b> A
    /// marker still in the settings folder proves the move out of it never
    /// finished, so the copy in the data folder is at most a partial one; and
    /// within a directory, the <see cref="FolderName"/> layout is newer than a
    /// bare name beside it. Both markers this product keeps are written once
    /// and never changed afterwards, so no version of this ordering can prefer
    /// a stale value to a live one.</summary>
    private static IEnumerable<string> PriorLocations(
        string directory,
        string name,
        IReadOnlyList<string>? priorNames,
        IReadOnlyList<string>? priorDirectories)
    {
        foreach (string priorDirectory in priorDirectories ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(priorDirectory))
            {
                continue;
            }

            yield return Path.Combine(priorDirectory, FolderName, name);

            foreach (string priorName in priorNames ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(priorName))
                {
                    yield return Path.Combine(priorDirectory, priorName);
                }
            }
        }

        foreach (string priorName in priorNames ?? Array.Empty<string>())
        {
            if (!string.IsNullOrEmpty(priorName))
            {
                yield return Path.Combine(directory, priorName);
            }
        }
    }

    /// <summary>Guards the marker itself against the removal sweep. A caller
    /// that named the current directory among the prior ones would otherwise
    /// delete the file it has just written and verified.</summary>
    private static bool PathsMatch(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RemoveAll(
        string directory,
        string name,
        IReadOnlyList<string> priorNames,
        IReadOnlyList<string> priorDirectories,
        string marker,
        Action<string> log)
    {
        foreach (string prior in PriorLocations(directory, name, priorNames, priorDirectories))
        {
            if (!PathsMatch(prior, marker))
            {
                Delete(prior, log);
            }
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
