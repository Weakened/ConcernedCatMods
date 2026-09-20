using System;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>Moves everything that is not configuration out of the settings
/// folder, once, on the first start of a build that has somewhere else to put
/// it (#304).
///
/// <b>This runs against a public beta's real installations.</b> Every file it
/// touches is something the player cannot get back: the identity their pins and
/// roads are attributed to, an atlas they have spent a season building, survey
/// rules they edited by hand, the marker that stops them being re-introduced to
/// a mod they have used for a year. So the ordering is the whole design, and it
/// is the same one <see cref="MarkerFile"/> already uses for a single file:
/// <b>copy, verify at the destination, and only then remove the original.</b>
/// Nothing is deleted on the strength of a copy returning without throwing.
///
/// <b>Every failure keeps the data.</b> If a copy cannot be made, cannot be
/// verified, or the original cannot be removed afterwards, the original stays
/// exactly where it is and the log says why. The cost of that is one file a mod
/// manager still lists; the cost of the other direction is somebody's atlas.
///
/// <b>Idempotent, and safe to interrupt.</b> A second start finds nothing left
/// to move and does nothing. A start interrupted mid-copy leaves a
/// <see cref="StagingSuffix"/> file, which the next start overwrites — the
/// original was never touched. A start interrupted between the copy and the
/// removal leaves the same bytes in both places, which the next start
/// recognises and finishes.
///
/// <b>Two copies that disagree are both kept.</b> That can only happen if an
/// earlier removal failed and the file was then written again in its new home,
/// so the new one is the live copy and the old one is history — but "can only"
/// is a piece of reasoning, and this is somebody's atlas. Nothing is
/// overwritten and nothing is deleted; the log names the file.
///
/// It knows nothing about BepInEx and takes both directories as arguments, so
/// the properties above are tested against a real filesystem rather than
/// described in a comment.</summary>
internal static class DataRelocation
{
    /// <summary>What a half-finished copy is called while it is being made.
    ///
    /// It lands in the destination rather than beside the original, so an
    /// interruption never leaves a new file in the settings folder — the one
    /// place this whole exercise is about keeping clean. It is on
    /// <c>CartographerFirstRunFiles</c>'s list for the same reason every other
    /// name this build writes for itself is: an unrecognised name in the probed
    /// directory is #343.</summary>
    public const string StagingSuffix = ".relocating.tmp";

    /// <summary>What happened to one file.</summary>
    private enum Result
    {
        /// <summary>Copied across, verified, and the original removed.</summary>
        Moved,

        /// <summary>The same bytes were already there: an earlier start copied
        /// it and did not get to remove the original. Removed now.</summary>
        Finished,

        /// <summary>Both copies still exist and nothing was touched — either
        /// they differ, or the original could not be removed. No data is
        /// lost either way.</summary>
        Kept,

        /// <summary>Nothing could be copied. The original is untouched.
        /// </summary>
        Failed,
    }

    /// <summary>What happened, in counts, for the log line and for tests.
    /// </summary>
    internal readonly struct Outcome
    {
        public Outcome(int moved, int finished, int kept, int failed)
        {
            Moved = moved;
            Finished = finished;
            Kept = kept;
            Failed = failed;
        }

        /// <summary>Files now only in their new home.</summary>
        public int Moved { get; }

        /// <summary>Files an earlier start had already copied; the leftover
        /// original is gone now.</summary>
        public int Finished { get; }

        /// <summary>Files deliberately left in both places.</summary>
        public int Kept { get; }

        /// <summary>Files that could not be copied and stayed put.</summary>
        public int Failed { get; }

        /// <summary>Nothing was there to do — the ordinary second start.
        /// </summary>
        public bool NothingToDo => Moved == 0 && Finished == 0 && Kept == 0 && Failed == 0;
    }

    /// <summary>Moves every file under <paramref name="configDirectory"/> that
    /// is not configuration into the matching place under
    /// <paramref name="dataDirectory"/>, preserving subfolders.</summary>
    /// <param name="configDirectory">The product's settings directory. Left in
    /// place; only its non-configuration contents leave.</param>
    /// <param name="dataDirectory">Where they go. Created as needed.</param>
    /// <param name="isConfiguration">Whether a file name directly in the
    /// settings directory is one a player edits, and so stays. Required: a
    /// default of "nothing is configuration" would silently move the survey
    /// rules the moment somebody forgot the argument.</param>
    /// <param name="log">Told about anything that did not go as intended.
    /// Required, for the same reason.</param>
    public static Outcome Run(
        string configDirectory,
        string dataDirectory,
        Func<string, bool> isConfiguration,
        Action<string> log)
    {
        if (string.IsNullOrEmpty(configDirectory))
        {
            throw new ArgumentException("A settings directory is required.", nameof(configDirectory));
        }

        if (string.IsNullOrEmpty(dataDirectory))
        {
            throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        }

        if (isConfiguration == null)
        {
            throw new ArgumentNullException(nameof(isConfiguration));
        }

        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        string[] sources;
        try
        {
            if (!Directory.Exists(configDirectory))
            {
                // Nothing was ever written in the old place. A fresh
                // installation, and the common case forever after.
                return default;
            }

            sources = Directory.GetFiles(configDirectory, "*", SearchOption.AllDirectories);
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while looking at the settings folder; " +
                        "nothing was moved");
            return new Outcome(0, 0, 0, 1);
        }

        int moved = 0;
        int finished = 0;
        int kept = 0;
        int failed = 0;

        foreach (string source in sources)
        {
            string? relative = RelativeTo(configDirectory, source);
            if (relative == null)
            {
                // A path that does not resolve under the directory we listed is
                // not something to start deleting from.
                failed++;
                continue;
            }

            // Only names directly in the settings folder can be configuration.
            // A `survey-rules.tsv` inside `backups/` is a copy of an atlas, not
            // a setting, and belongs with the rest of the backups.
            if (relative.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                relative.IndexOf(Path.AltDirectorySeparatorChar) < 0 &&
                isConfiguration(relative))
            {
                continue;
            }

            switch (MoveOne(source, Path.Combine(dataDirectory, relative), log))
            {
                case Result.Moved:
                    moved++;
                    break;
                case Result.Finished:
                    finished++;
                    break;
                case Result.Kept:
                    kept++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        // `state` and `backups` are empty once their contents have gone, and an
        // empty folder is still a folder a mod manager draws. The settings
        // directory itself stays: the survey rules live in it.
        PruneEmptyChildren(configDirectory);

        return new Outcome(moved, finished, kept, failed);
    }

    private static Result MoveOne(string source, string target, Action<string> log)
    {
        string staging = target + StagingSuffix;
        try
        {
            if (File.Exists(target))
            {
                if (!SameContents(source, target))
                {
                    Report(log, $"\"{Path.GetFileName(source)}\" is in both the settings folder " +
                                "and the data folder and the two differ, so both were left alone");
                    return Result.Kept;
                }

                // An earlier start copied this and did not get to remove the
                // original. Same bytes on both sides: finishing is safe.
                return Delete(source, log) ? Result.Finished : Result.Kept;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Delete(staging, null);
            File.Copy(source, staging, overwrite: true);

            // Verified at the destination, not by trusting Copy. A quota, a
            // filter driver or a network-backed folder can accept a write and
            // then truncate it, and the original is deleted on the strength of
            // this being true.
            if (!SameContents(source, staging))
            {
                Delete(staging, null);
                Report(log, $"\"{Path.GetFileName(source)}\" did not read back as written, so it " +
                            "was left in the settings folder");
                return Result.Failed;
            }

            // Throws rather than overwrites if the destination appeared while
            // we were copying, which is the answer we want: the caller keeps
            // both copies instead of one of them silently winning.
            File.Move(staging, target);

            if (!File.Exists(target))
            {
                Report(log, $"\"{Path.GetFileName(source)}\" did not arrive in the data folder, " +
                            "so it was left in the settings folder");
                return Result.Failed;
            }

            return Delete(source, log) ? Result.Moved : Result.Kept;
        }
        catch (Exception exception)
        {
            Delete(staging, null);
            Report(log, $"{exception.GetType().Name} while moving \"{Path.GetFileName(source)}\" " +
                        "out of the settings folder; it was left where it is");
            return Result.Failed;
        }
    }

    /// <summary>Byte-for-byte, because the question being answered is whether
    /// one of these two copies may be deleted.</summary>
    private static bool SameContents(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using FileStream leftStream = File.OpenRead(left);
        using FileStream rightStream = File.OpenRead(right);
        var leftBuffer = new byte[64 * 1024];
        var rightBuffer = new byte[64 * 1024];

        while (true)
        {
            int leftRead = Fill(leftStream, leftBuffer);
            int rightRead = Fill(rightStream, rightBuffer);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            for (int index = 0; index < leftRead; index++)
            {
                if (leftBuffer[index] != rightBuffer[index])
                {
                    return false;
                }
            }
        }
    }

    /// <summary>A short read is not the end of a file, and treating it as one
    /// would call two different files identical.</summary>
    private static int Fill(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>The path of <paramref name="path"/> below
    /// <paramref name="directory"/>, or null if it is not below it.</summary>
    private static string? RelativeTo(string directory, string path)
    {
        try
        {
            string root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);

            // Ordinal-ignore-case: this runs on Windows profiles where a mod
            // manager and the game may spell the same folder differently.
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Delete(string path, Action<string>? log)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception)
        {
            Report(log, $"{exception.GetType().Name} while removing \"{Path.GetFileName(path)}\" " +
                        "from the settings folder; your copy there was left alone and the next " +
                        "start will try again");
            return false;
        }
    }

    /// <summary>Removes folders under <paramref name="directory"/> that are now
    /// empty, deepest first. Never the directory itself, and never anything
    /// that still holds a file.</summary>
    private static void PruneEmptyChildren(string directory)
    {
        try
        {
            foreach (string child in Directory.GetDirectories(directory))
            {
                PruneIfEmpty(child);
            }
        }
        catch (Exception)
        {
            // Tidying is not worth a failure. An empty folder costs nothing but
            // a line in somebody's file browser.
        }
    }

    private static void PruneIfEmpty(string directory)
    {
        try
        {
            foreach (string child in Directory.GetDirectories(directory))
            {
                PruneIfEmpty(child);
            }

            if (Directory.GetFiles(directory).Length == 0 &&
                Directory.GetDirectories(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception)
        {
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
