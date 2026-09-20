using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TheConcernedCat.ConcernedNPC.Persistence;

/// <summary>Writing a line-oriented data file so that an interrupted write
/// leaves either the whole old file or the whole new one - and the escaping
/// pair that makes a tab-separated line able to carry a player's own text.
///
/// <b>Five copies became one, and the fold is not arbitrary.</b> Five places in
/// this repository write a file this way, and they disagreed on two things that
/// matter. Only one of them flushed the temporary file's bytes to the disk
/// before the swap; the others left a window in which a power loss produced a
/// live file whose blocks were never written, which is a truncated file with
/// nothing complete beside it. And only one kept the complete temporary copy
/// when the commit failed; the rest deleted it in a <c>finally</c>, throwing
/// away the only intact copy at exactly the moment it was the only intact copy.
/// This is the strict version of both.
///
/// <b>Durability, stated exactly.</b> The temporary file's bytes are flushed to
/// the disk before it replaces the live file, so a power loss right after the
/// replace cannot leave a live file whose blocks were never written. The
/// replace itself is the atomic swap on the filesystem the plugins ship to. The
/// directory entry is not flushed - the runtime offers no way to - so a power
/// loss in the instant after the replace can still bring back the previous
/// complete file, never a mixture.
///
/// <b>The fallback is not atomic, and says so.</b> When the replace is refused
/// - a scanner or a sync client holding the file, a different filesystem, a
/// runtime without it - the complete temporary copy is copied over the live
/// file and flushed, and only then deleted. A crash in the middle of that copy
/// can leave a partial live file. The complete copy is still beside it, and a
/// caller whose format carries a closing marker will notice on the next
/// read.</summary>
internal static class NpcAtomicText
{
    /// <summary>Makes one field safe for a tab-separated line.
    ///
    /// The per-cent sign is escaped <b>first</b> on the way out and unescaped
    /// <b>last</b> on the way back, so a literal escape sequence typed by a
    /// player survives the round trip instead of coming back as a tab. The five
    /// sequences and their order are exactly what the shipped writers and
    /// readers use; changing either would make every existing file read back
    /// differently, which is a data migration wearing a refactor's
    /// clothes.</summary>
    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value!
            .Replace("%", "%25")
            .Replace("\t", "%09")
            .Replace("*", "%2A")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A");
    }

    internal static string Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value!
            .Replace("%0A", "\n")
            .Replace("%0D", "\r")
            .Replace("%2A", "*")
            .Replace("%09", "\t")
            .Replace("%25", "%");
    }

    /// <summary>Writes every line to a temporary file beside the target.
    ///
    /// Returns the exception rather than throwing, because every caller has a
    /// sentence to show a player and none of them want a stack trace. The
    /// temporary file is removed on failure, because a partial temporary is
    /// worse than none: the commit path would otherwise swap it in.</summary>
    internal static Exception? TryWriteTemporary(
        string directory, string temporaryPath, IEnumerable<string> lines)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (string line in lines)
                {
                    writer.WriteLine(line);
                }

                // Down to the disk before the swap, not merely into the
                // operating system's cache: a replace whose new blocks were
                // never written is a truncated live file with nothing complete
                // beside it.
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            return null;
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            return exception;
        }
    }

    /// <summary>Swaps a completed temporary file over the live one.</summary>
    internal static void Commit(string temporaryPath, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(temporaryPath, path);
            return;
        }

        try
        {
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            CopyOver(temporaryPath, path);
        }
        catch (IOException)
        {
            // The replace is refused across volumes and on some filesystems.
            CopyOver(temporaryPath, path);
        }
    }

    /// <summary>Renames an unreadable file aside, to the first free name.
    /// Returns the new path, or null when it could not be moved - which is not
    /// a failure to report and forget: a caller that could not move a file
    /// aside must also not overwrite it.
    ///
    /// Bounded, so a permanently failing rename cannot spin or fill a
    /// directory with attempts.</summary>
    internal static string? TryQuarantine(string path, string suffix, int maximumAttempts = 20)
    {
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            string candidate = attempt == 0
                ? path + suffix
                : path + suffix + "." + attempt.ToString(CultureInfo.InvariantCulture);

            if (File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Move(path, candidate);
                return candidate;
            }
            catch (IOException)
            {
                // Raced with something else, or locked. Try the next name.
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort; the caller already has a real error to report.
        }
    }

    /// <summary>The non-atomic fallback. The temporary file is deleted only
    /// after the copy has been flushed to the disk, so an interruption at any
    /// point leaves one complete copy behind.</summary>
    private static void CopyOver(string temporaryPath, string path)
    {
        using (var source = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
            destination.Flush(flushToDisk: true);
        }

        TryDelete(temporaryPath);
    }
}
