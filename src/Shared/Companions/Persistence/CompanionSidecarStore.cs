using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.Companions.Identity;

namespace TheConcernedCat.Companions.Persistence;

/// <summary>Atomic, scope-addressed file storage for companion sidecars.
///
/// This type uses <see cref="System.IO"/> only - no game, Unity, or BepInEx
/// types - so the whole recovery surface is exercised against a temporary
/// directory in unit tests rather than reasoned about.
///
/// Three rules keep a bad file from becoming a bad experience:
/// a write goes to a temporary file and is swapped in, so an interrupted save
/// never truncates the live one; an unreadable file is quarantined beside
/// itself rather than deleted; and a file this build must not rewrite - a
/// newer schema, or another character's data - makes the sidecar read-only so
/// no later save can clobber it.</summary>
internal sealed class CompanionSidecarStore
{
    private const string Extension = ".companions.tsv";
    private const string TemporarySuffix = ".tmp";
    private const string QuarantineSuffix = ".corrupt";

    /// <summary>Bound on quarantine attempts, so a permanently failing rename
    /// cannot spin or fill a directory.</summary>
    private const int MaxQuarantineAttempts = 20;

    private readonly string _rootDirectory;

    public CompanionSidecarStore(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("A sidecar root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    public sealed class LoadReport
    {
        public LoadReport(
            CompanionSidecar sidecar, SidecarLoadOutcome outcome, int skippedRows,
            string? notice, string? quarantinePath)
        {
            Sidecar = sidecar;
            Outcome = outcome;
            SkippedRows = skippedRows;
            Notice = notice;
            QuarantinePath = quarantinePath;
        }

        public CompanionSidecar Sidecar { get; }
        public SidecarLoadOutcome Outcome { get; }
        public int SkippedRows { get; }

        /// <summary>An actionable, non-technical sentence for the player, or
        /// null when nothing needs saying.</summary>
        public string? Notice { get; }

        /// <summary>Where an unreadable file was moved, when one was.</summary>
        public string? QuarantinePath { get; }
    }

    public sealed class SaveReport
    {
        public SaveReport(bool saved, string? notice)
        {
            Saved = saved;
            Notice = notice;
        }

        public bool Saved { get; }
        public string? Notice { get; }
    }

    public string ResolvePath(CompanionScope scope)
    {
        return Path.Combine(_rootDirectory, scope.ToStorageKey() + Extension);
    }

    public LoadReport Load(CompanionScope scope)
    {
        string path = ResolvePath(scope);

        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return new LoadReport(
                    new CompanionSidecar(scope), SidecarLoadOutcome.Missing, 0, null, null);
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            // An IO failure is not evidence about the player's progress, so the
            // sidecar is marked read-only: this session works from whatever the
            // unlock policy can establish, and never overwrites a file it could
            // not read.
            var unreadable = new CompanionSidecar(scope);
            unreadable.MarkReadOnly();
            return new LoadReport(
                unreadable,
                SidecarLoadOutcome.Corrupt,
                0,
                "Could not read your companion data (" + Describe(exception) + "). " +
                "Your existing features stay available and nothing was deleted.",
                null);
        }

        CompanionSidecarCodec.ParseResult parsed = CompanionSidecarCodec.Parse(lines, scope);

        switch (parsed.Outcome)
        {
            case SidecarLoadOutcome.Corrupt:
            {
                // Move it aside so a fresh file can be written, and tell the
                // player exactly where the old one went.
                string? quarantine = TryQuarantine(path);
                if (quarantine == null)
                {
                    // The unreadable file is still sitting at the live path.
                    // Leaving the sidecar writable here would let the very next
                    // save overwrite the file this notice just promised was
                    // kept - so it does not stay writable.
                    parsed.Sidecar.MarkReadOnly();
                    return new LoadReport(
                        parsed.Sidecar,
                        parsed.Outcome,
                        parsed.SkippedRows,
                        "Your companion data could not be read and could not be moved aside, so it " +
                        "was left exactly as it is. Your existing features stay available and " +
                        "nothing was deleted.",
                        null);
                }

                return new LoadReport(
                    parsed.Sidecar,
                    parsed.Outcome,
                    parsed.SkippedRows,
                    "Your companion data could not be read. The old file was kept as \"" +
                    Path.GetFileName(quarantine) + "\" and a fresh one will be written. " +
                    "Your existing features stay available.",
                    quarantine);
            }

            case SidecarLoadOutcome.ScopeMismatch:
            {
                // Almost certainly a copied profile. Adopting it would mix two
                // characters together and overwriting it would destroy somebody
                // else's progress, so this build does neither.
                parsed.Sidecar.MarkReadOnly();
                return new LoadReport(
                    parsed.Sidecar,
                    parsed.Outcome,
                    parsed.SkippedRows,
                    "The companion data file \"" + Path.GetFileName(path) + "\" belongs to a different " +
                    "character or world, so it was left untouched and no new progress will be saved " +
                    "over it. Move or rename that file to start fresh here.",
                    null);
            }

            case SidecarLoadOutcome.UnsupportedSchema:
                return new LoadReport(
                    parsed.Sidecar,
                    parsed.Outcome,
                    parsed.SkippedRows,
                    "Your companion data was written by a newer version of this mod. It is being left " +
                    "as it is so nothing gets lost, and your existing features stay available.",
                    null);

            case SidecarLoadOutcome.LoadedWithSkippedRows:
                return new LoadReport(
                    parsed.Sidecar,
                    parsed.Outcome,
                    parsed.SkippedRows,
                    parsed.SkippedRows.ToString(CultureInfo.InvariantCulture) +
                    " damaged companion entr" + (parsed.SkippedRows == 1 ? "y was" : "ies were") +
                    " skipped. Everything readable was kept.",
                    null);

            default:
                return new LoadReport(parsed.Sidecar, parsed.Outcome, parsed.SkippedRows, null, null);
        }
    }

    /// <summary>Writes the sidecar if it changed. Returns whether anything was
    /// written, plus a notice when a save was deliberately refused.</summary>
    public SaveReport Save(CompanionSidecar sidecar, bool force = false)
    {
        if (sidecar == null)
        {
            throw new ArgumentNullException(nameof(sidecar));
        }

        if (sidecar.IsReadOnly)
        {
            return new SaveReport(
                false,
                "Companion progress was not saved: the existing data file is from a newer version or " +
                "another character, and overwriting it would lose data.");
        }

        if (!force && !sidecar.IsDirty)
        {
            return new SaveReport(false, null);
        }

        string path = ResolvePath(sidecar.Scope);
        string temporaryPath = path + TemporarySuffix;

        try
        {
            Directory.CreateDirectory(_rootDirectory);

            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                foreach (string line in CompanionSidecarCodec.Serialize(sidecar))
                {
                    writer.WriteLine(line);
                }
            }
        }
        catch (Exception exception)
        {
            // The write phase failed, so the temporary file is incomplete and
            // the live file was never touched. Discarding the temporary file is
            // safe here and only here.
            TryDelete(temporaryPath);
            return new SaveReport(false, DescribeSaveFailure(exception));
        }

        try
        {
            Commit(temporaryPath, path);
            sidecar.MarkClean();
            return new SaveReport(true, null);
        }
        catch (Exception exception)
        {
            // The commit phase failed. The temporary file holds a COMPLETE copy
            // of the new data and the live file may be mid-replace, so it is
            // deliberately left on disk: deleting it here would throw away the
            // only intact copy. It is named in the notice so it can be
            // recovered by hand, and the next successful save overwrites it.
            return new SaveReport(
                false,
                DescribeSaveFailure(exception) + " A complete copy was left as \"" +
                Path.GetFileName(temporaryPath) + "\".");
        }
    }

    private static string DescribeSaveFailure(Exception exception)
    {
        return "Could not save companion progress (" + Describe(exception) + "). " +
            "Your existing features stay available and will be saved again on the next change.";
    }

    /// <summary>Swaps the finished temporary file into place. The live file is
    /// only ever replaced whole, so a crash mid-save leaves either the old
    /// complete file or the new complete file - never half of either.</summary>
    private static void Commit(string temporaryPath, string path)
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
            // File.Replace refuses across volumes and on some filesystems.
            CopyOver(temporaryPath, path);
        }
    }

    /// <summary>Last-resort commit for filesystems where <see cref="File.Replace"/>
    /// does not work. This one is genuinely not atomic - a crash part way
    /// through leaves a truncated live file - so it runs only when the atomic
    /// path is unavailable, and the temporary file is kept until the copy has
    /// completed so a failure still leaves one intact copy on disk.</summary>
    private static void CopyOver(string temporaryPath, string path)
    {
        File.Copy(temporaryPath, path, overwrite: true);
        TryDelete(temporaryPath);
    }

    /// <summary>Renames an unreadable file to a free <c>.corrupt</c> name.
    /// Returns the new path, or null if it could not be moved.</summary>
    private static string? TryQuarantine(string path)
    {
        for (int attempt = 0; attempt < MaxQuarantineAttempts; attempt++)
        {
            string candidate = attempt == 0
                ? path + QuarantineSuffix
                : path + QuarantineSuffix + "." + attempt.ToString(CultureInfo.InvariantCulture);

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

    private static void TryDelete(string path)
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
            // Best effort: the caller already has a real error to report.
        }
    }

    /// <summary>Exception text for a player-facing notice: the type and message
    /// only, never a path or a stack trace.</summary>
    private static string Describe(Exception exception)
    {
        return exception.GetType().Name;
    }

    /// <summary>True when THIS scope — this product, this world, this
    /// character — already has a sidecar.
    ///
    /// This is the question a migration probe actually wants, and it is a
    /// separate method because the obvious alternative is a trap: a root can
    /// hold files for several products, worlds and characters, so counting
    /// files there answers "has anybody ever played anything" and would hand
    /// one product an unlock earned by another. The grant it produces is
    /// monotonic, so that mistake is permanent and silent.</summary>
    public bool HasSidecarFor(CompanionScope scope)
    {
        try
        {
            return File.Exists(ResolvePath(scope));
        }
        catch
        {
            // Unreadable is not the same as absent; the caller's evidence rule
            // decides what to do with "do not know", and every one of them
            // resolves towards granting.
            return false;
        }
    }

    /// <summary>Sidecar files belonging to one product, across every world and
    /// character. Still not an answer about one character — use
    /// <see cref="HasSidecarFor"/> for that.</summary>
    public IReadOnlyList<string> ListSidecarFiles(ProductId product)
    {
        if (product.IsEmpty)
        {
            return new string[0];
        }

        var owned = new List<string>();
        string prefix = product.Value + ".";
        foreach (string path in ListSidecarFiles())
        {
            if (Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal))
            {
                owned.Add(path);
            }
        }

        return owned;
    }

    /// <summary>Every sidecar file under this root — <b>every product, every
    /// world, every character</b>.
    ///
    /// Deliberately blunt, and deliberately not what a legacy-evidence probe
    /// should call: answering "is this player an existing user" from this list
    /// grants one product's tools on another product's history. Use
    /// <see cref="HasSidecarFor"/> or the product-scoped overload.</summary>
    public IReadOnlyList<string> ListSidecarFiles()
    {
        try
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return new string[0];
            }

            return Directory.GetFiles(_rootDirectory, "*" + Extension);
        }
        catch
        {
            return new string[0];
        }
    }
}
