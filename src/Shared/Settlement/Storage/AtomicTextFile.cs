using System;
using System.Collections.Generic;
using System.IO;

namespace TheConcernedCat.Settlement.Storage;

/// <summary>Writing a settlement file so that an interrupted write leaves
/// either the whole old file or the whole new one.
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
            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                foreach (string line in lines)
                {
                    writer.WriteLine(line);
                }
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

    private static void CopyOver(string temporaryPath, string path)
    {
        File.Copy(temporaryPath, path, overwrite: true);
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
