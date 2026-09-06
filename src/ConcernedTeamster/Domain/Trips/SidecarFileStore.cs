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

    /// <summary>How many generations of a backup a single reason keeps
    /// (CT-039 / DEF-teamster-v0.4-001): bounded so repeated refusals or
    /// migrations cannot accumulate disk usage forever, but more than one
    /// so a second event does not silently destroy the only prior
    /// evidence of the first.</summary>
    public const int MaxBackupGenerationsPerReason = 3;

    /// <summary>Copies the file to "<c>path.bak-&lt;reason&gt;-1</c>" before a
    /// migration or a refusal/malformed-overwrite would touch it, rotating
    /// any existing generations for that same reason up
    /// (<c>-1</c>→<c>-2</c>→<c>-3</c>, oldest evicted) so a bounded set of
    /// distinct backups survives repeated events instead of one fixed name
    /// silently overwriting itself every time. Never throws.</summary>
    public static bool TryBackup(string path, string reason, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            string oldest = BackupPath(path, reason, MaxBackupGenerationsPerReason);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int generation = MaxBackupGenerationsPerReason - 1; generation >= 1; generation--)
            {
                string from = BackupPath(path, reason, generation);
                if (File.Exists(from))
                {
                    File.Move(from, BackupPath(path, reason, generation + 1));
                }
            }

            File.Copy(path, BackupPath(path, reason, 1), overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static string BackupPath(string path, string reason, int generation) =>
        path + ".bak-" + reason + "-" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
