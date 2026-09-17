using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheConcernedCat.Settlement.Storage;

/// <summary>Writing a settlement file so that an interrupted write leaves
/// either the whole old file or the whole new one — on the path that normally
/// runs.
///
/// <b>Durability, stated exactly.</b> The temporary file's bytes are flushed to
/// the disk (<c>FileStream.Flush(true)</c>) before it replaces the live file, so
/// a power loss right after the replace cannot leave a live file whose blocks
/// were never written. <c>File.Replace</c> is the atomic swap on NTFS. The
/// directory entry itself is not flushed — .NET offers no way to — so a power
/// loss in the instant after the replace can still bring back the previous
/// complete file, never a mixture.
///
/// <b>The fallback is not atomic, and says so.</b> When the replace is refused
/// (a scanner or sync client holding the file, or a runtime without it), the
/// complete temporary copy is copied over the live file and flushed, and only
/// then deleted. A crash in the middle of that copy can leave a partial live
/// file. That is what the record trailer exists for (#293): the next load finds
/// the closing line missing or wrong, goes read-only, and the complete copy is
/// still beside it as <c>.tmp</c>.
///
/// Extracted from <c>JournalStore</c> when CF-SET-004 added a second file with
/// the same requirement. The alternative was a second copy of the
/// temp-file-and-swap, and two copies of "how do we not lose the record" drift
/// apart — usually in the direction of the copy nobody looked at again. Nothing
/// here changes what the journal already did; it is the same code in one place.
///
/// The escaping pair lives here for the same reason. Both files are
/// tab-separated and both carry player-supplied text — a container name, a
/// settlement name — so both need the same answer to "what if that text
/// contains a tab", and the answer has to be identical in the writer and the
/// reader of each.</summary>
internal static class AtomicTextFile
{
    /// <summary>Makes one field safe for a tab-separated line.
    ///
    /// <c>%</c> is escaped <b>first</b> on the way out and unescaped
    /// <b>last</b> on the way back, so a literal <c>%09</c> typed by a player
    /// survives the round trip instead of coming back as a tab.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value!
            .Replace("%", "%25")
            .Replace("\t", "%09")
            .Replace("*", "%2A")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A");
    }

    public static string Unescape(string value)
    {
        return value
            .Replace("%0A", "\n")
            .Replace("%0D", "\r")
            .Replace("%2A", "*")
            .Replace("%09", "\t")
            .Replace("%25", "%");
    }

    /// <summary>Writes every line to a temporary file beside the target.
    ///
    /// Returns the exception rather than throwing, because every caller has a
    /// sentence to show a player and none of them want a stack trace.</summary>
    public static Exception? TryWriteTemporary(
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

                // Down to the disk before the swap, not merely into the OS
                // cache: a replace whose new blocks were never written is a
                // truncated live file with nothing complete beside it.
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

    /// <summary>Swaps a completed temporary file over the live one.
    ///
    /// <c>File.Replace</c> is the atomic path and is what runs on the Windows
    /// filesystem the plugins ship to. The fallbacks exist because the tests
    /// run on a different runtime from the plugins, and because a file being
    /// scanned or synchronised can refuse a replace it would normally allow.</summary>
    public static void Commit(string temporaryPath, string path)
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
            CopyOver(temporaryPath, path);
        }
    }

    /// <summary>The non-atomic fallback. The temporary file is deleted only
    /// after the copy has been flushed to the disk, so an interruption at any
    /// point leaves the complete copy behind; the partial live file it may leave
    /// is caught on the next load by the record trailer.</summary>
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

    public static void TryDelete(string path)
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
}
