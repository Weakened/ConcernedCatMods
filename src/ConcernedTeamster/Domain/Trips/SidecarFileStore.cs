using System;
using System.IO;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>Durable sidecar file IO (CT-016), the rules Cartographer
/// proved: write to a temp file first and swap it in atomically, so a kill
/// at any moment leaves either the old complete file or the new complete
/// file — never a torn one; back a file up before any migration would
/// rewrite it. Teamster writes only its own sidecar directory — never a
/// Valheim save.</summary>
public static class SidecarFileStore
{
    /// <summary>Reads the file, or null when absent/unreadable (the error
    /// goes to the out param, never an exception).</summary>
    public static string? TryRead(string path, out string? error)
    {
        error = null;
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return null;
        }
    }

    /// <summary>Atomic write: temp file in the same directory, then
    /// File.Replace (same-volume atomic swap) or File.Move for a fresh
    /// file. False (with the error) on any failure — the previous file is
    /// left exactly as it was.</summary>
    public static bool TryWriteAtomic(string path, string content, out string? error)
    {
        error = null;
        string tempPath = path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Leftover temp files are harmless and ignored by loading.
            }

            return false;
        }
    }

    /// <summary>Copies the file to "<c>path.bak-&lt;reason&gt;</c>" before a
    /// migration or refusal-overwrite would touch it. Never throws.</summary>
    public static bool TryBackup(string path, string reason, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            File.Copy(path, path + ".bak-" + reason, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }
}
