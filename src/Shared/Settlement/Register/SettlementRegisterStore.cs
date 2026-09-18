using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Storage;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Register;

internal enum RegisterLoadOutcome
{
    /// <summary>No file. A settlement nobody has marked anything in.</summary>
    Missing = 0,

    Loaded = 1,

    /// <summary>Readable, but some lines were not. The readable ones are kept
    /// and the file is <b>not</b> rewritten.</summary>
    LoadedWithSkippedLines = 2,

    /// <summary>Written by a newer build. Read-only.</summary>
    UnsupportedSchema = 3,

    /// <summary>Belongs to another world or settlement. Read-only.</summary>
    ScopeMismatch = 4,

    Unreadable = 5,

    /// <summary>Every line read, but the record ends without its closing line:
    /// its tail was lost (#293). What was read is kept; read-only.</summary>
    Truncated = 6,

    /// <summary>Every line read, but the closing line disagrees with the lines
    /// above it (#293). Read-only.</summary>
    IntegrityMismatch = 7,
}

/// <summary>Reads and writes what a player marked, as a tab-separated file.
///
/// This is <b>current state</b>, not a log, and that is the one real difference
/// from the journal beside it. A journal's history is its safety property —
/// "what happened to that wood" is only answerable from the record of every
/// step. A register answers "what is marked right now", and a marking replaced
/// by an explicit later act is simply gone. So the file holds today's rows and
/// is rewritten whole, through the same temp-file-and-swap, for the same reason:
/// an interrupted write should leave either the whole old file or the whole new
/// one. That holds on <c>File.Replace</c>, which is the path that normally runs;
/// the fallbacks are <b>not</b> atomic and #293 tracks detecting the truncation
/// they can leave.
///
/// The failure discipline is the journal's, deliberately, and <b>not</b> the
/// companion sidecar's. A damaged file is never quarantined and replaced with an
/// empty one. A companion sidecar holds progress through a story; this holds
/// which chest a worker is allowed to take from, and quietly forgetting that is
/// how a settlement ends up drawing from the wrong container. A register that
/// could not be fully read goes read-only and says so.</summary>
internal sealed class SettlementRegisterStore
{
    private const string Extension = ".settlement-register.tsv";
    private const string TemporarySuffix = ".tmp";

    /// <summary>Bumped only when a change would confuse an older build. An
    /// older build refuses a newer file rather than reading half of it.
    ///
    /// <b>2</b> since the record ends with a closing line (#293). A schema-1
    /// file has none and still loads; the next write seals it.</summary>
    public const int SchemaVersion = 2;

    private const char Separator = '\t';
    private const char CarriageReturn = '\r';
    private const char LineFeed = '\n';

    private const string DesignationTag = "d";
    private const string WorkerTag = "w";

    private readonly string _rootDirectory;

    public SettlementRegisterStore(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("A register root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    public sealed class LoadReport
    {
        public LoadReport(
            SettlementRegister register, RegisterLoadOutcome outcome, int skippedLines, string? notice)
        {
            Register = register;
            Outcome = outcome;
            SkippedLines = skippedLines;
            Notice = notice;
        }

        public SettlementRegister Register { get; }
        public RegisterLoadOutcome Outcome { get; }
        public int SkippedLines { get; }
        public string? Notice { get; }

        /// <summary>Read straight off the register, so the two can never
        /// disagree about whether writing is allowed.</summary>
        public bool ReadOnly => Register.IsReadOnly;
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

    public string ResolvePath(SettlementScope scope)
    {
        return Path.Combine(_rootDirectory, scope.ToStorageKey() + Extension);
    }

    public LoadReport Load(SettlementScope scope)
    {
        string path = ResolvePath(scope);
        string[] lines;

        try
        {
            if (!File.Exists(path))
            {
                return new LoadReport(
                    new SettlementRegister(scope), RegisterLoadOutcome.Missing, 0, null);
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            var unreadable = new SettlementRegister(scope);
            unreadable.MarkReadOnly();
            return new LoadReport(
                unreadable, RegisterLoadOutcome.Unreadable, 0,
                "What you marked in this settlement could not be read (" +
                exception.GetType().Name + "). Nothing was deleted, and nothing new will be " +
                "marked until the file can be read, so it can be recovered or repaired by hand.");
        }

        var register = new SettlementRegister(scope);
        int skipped = 0;
        bool sawHeader = false;
        bool sawAnyLine = false;
        int version = 1;
        var accumulator = new RecordTrailer.Accumulator();
        TrailerVerdict trailer = TrailerVerdict.Unspecified;
        bool sawTrailer = false;

        foreach (string raw in lines)
        {
            // Trimmed at the END ONLY, and only of line endings — the same
            // narrow trim the journal uses, for the same reason: a general
            // Trim() would eat the trailing tab of a row whose last field is
            // empty, which is every area designation, and the row would then be
            // one field short and read as damaged.
            string line = raw.TrimEnd(CarriageReturn, LineFeed);
            if (line.Trim().Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split(Separator);
            sawAnyLine = true;

            if (sawTrailer)
            {
                trailer = TrailerVerdict.LinesAfterTrailer;
                skipped++;
                continue;
            }

            if (!sawHeader)
            {
                sawHeader = true;
                if (fields.Length >= 3 && string.Equals(fields[0], "v", StringComparison.Ordinal))
                {
                    if (!int.TryParse(
                            fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out version)
                        || version > SchemaVersion)
                    {
                        register.MarkReadOnly();
                        return new LoadReport(
                            register, RegisterLoadOutcome.UnsupportedSchema, 0,
                            "What you marked in this settlement was written by a newer version of " +
                            "the mod. It is being left exactly as it is so nothing gets lost, and " +
                            "nothing new will be marked against it.");
                    }

                    if (!string.Equals(fields[2], scope.ToStorageKey(), StringComparison.Ordinal))
                    {
                        register.MarkReadOnly();
                        return new LoadReport(
                            register, RegisterLoadOutcome.ScopeMismatch, 0,
                            "The file \"" + Path.GetFileName(path) + "\" belongs to a different " +
                            "world or settlement, so it was left untouched and nothing will be " +
                            "written over it.");
                    }

                    accumulator.AddLine(line);
                    continue;
                }

                // No header at all: an older or hand-made file. Fall through
                // and try to read it as rows.
                version = 1;
            }

            if (version >= 2 && RecordTrailer.IsTrailer(fields))
            {
                sawTrailer = true;
                trailer = RecordTrailer.Check(fields, accumulator);
                continue;
            }

            accumulator.AddLine(line);
            accumulator.CountRow(-1L);

            if (TryParseDesignation(fields, out Designation designation))
            {
                if (!register.Restore(designation))
                {
                    skipped++;
                }
            }
            else if (TryParseWorker(fields, out WorkerRecord worker))
            {
                if (!register.Restore(worker))
                {
                    skipped++;
                }
            }
            else
            {
                skipped++;
            }
        }

        register.MarkClean();

        if ((version >= 2 && !sawTrailer) || !sawAnyLine)
        {
            // A sealed record without its closing line lost its tail; a file
            // with no line at all lost everything. No build writes either.
            trailer = TrailerVerdict.Missing;
        }

        bool damagedTrailer = (version >= 2 || !sawAnyLine) && trailer != TrailerVerdict.Intact;

        if (skipped > 0)
        {
            register.MarkReadOnly();
            return new LoadReport(
                register, RegisterLoadOutcome.LoadedWithSkippedLines, skipped,
                skipped.ToString(CultureInfo.InvariantCulture) + " line(s) of what you marked in " +
                "this settlement could not be read" +
                (damagedTrailer ? ", and " + RecordTrailer.Describe(trailer) : string.Empty) +
                ". Everything readable was kept and the file " +
                "will NOT be rewritten, so nothing is lost — but nothing new will be marked " +
                "until it is repaired, because a partial record cannot say which container a " +
                "worker is allowed to take from.");
        }

        if (damagedTrailer)
        {
            register.MarkReadOnly();
            return new LoadReport(
                register,
                trailer == TrailerVerdict.Missing ? RegisterLoadOutcome.Truncated : RegisterLoadOutcome.IntegrityMismatch,
                0,
                "What you marked in this settlement is not completely on disk: " + RecordTrailer.Describe(trailer) +
                ". Everything readable was kept and the file will NOT be rewritten, and nothing new will be " +
                "marked until it is repaired.");
        }

        return new LoadReport(register, RegisterLoadOutcome.Loaded, 0, null);
    }

    public SaveReport Save(SettlementRegister register)
    {
        if (register == null)
        {
            throw new ArgumentNullException(nameof(register));
        }

        if (register.IsReadOnly)
        {
            return new SaveReport(
                false,
                "What you marked in this settlement is from a newer version, another settlement, " +
                "or could not be fully read, so nothing was written over it.");
        }

        if (!register.IsDirty)
        {
            return new SaveReport(false, null);
        }

        string path = ResolvePath(register.Scope);
        string temporaryPath = path + TemporarySuffix;

        Exception? writeFailure =
            AtomicTextFile.TryWriteTemporary(_rootDirectory, temporaryPath, Serialize(register));
        if (writeFailure != null)
        {
            return new SaveReport(
                false,
                "Could not record what you marked (" + writeFailure.GetType().Name + "). " +
                "Nothing was changed on disk and it will be recorded again on the next change.");
        }

        try
        {
            AtomicTextFile.Commit(temporaryPath, path);
            register.MarkClean();
            return new SaveReport(true, null);
        }
        catch (Exception exception)
        {
            // The temporary file holds a COMPLETE copy and the live file may be
            // mid-replace, so it is deliberately left on disk.
            return new SaveReport(
                false,
                "Could not record what you marked (" + exception.GetType().Name + "). " +
                "A complete copy was left as \"" + Path.GetFileName(temporaryPath) + "\".");
        }
    }

    public static IEnumerable<string> Serialize(SettlementRegister register)
    {
        return RecordTrailer.Seal(Lines(register), RowMarker);
    }

    /// <summary>Every designation and worker row counts toward the closing
    /// line; the register has no sequences.</summary>
    private static long? RowMarker(string line)
    {
        return line.StartsWith(DesignationTag + Separator, StringComparison.Ordinal)
            || line.StartsWith(WorkerTag + Separator, StringComparison.Ordinal)
                ? -1L
                : (long?)null;
    }

    private static IEnumerable<string> Lines(SettlementRegister register)
    {
        yield return "#" + Separator + "settlement register v" +
            SchemaVersion.ToString(CultureInfo.InvariantCulture);
        yield return string.Join(
            Separator.ToString(),
            new[]
            {
                "v",
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                register.Scope.ToStorageKey(),
            });

        foreach (Designation designation in register.Designations)
        {
            yield return string.Join(
                Separator.ToString(),
                new[]
                {
                    DesignationTag,
                    ((int)designation.Kind).ToString(CultureInfo.InvariantCulture),
                    Number(designation.Centre.X),
                    Number(designation.Centre.Y),
                    Number(designation.Centre.Z),
                    Number(designation.Radius),
                    AtomicTextFile.Escape(designation.ContainerKey),
                    AtomicTextFile.Escape(designation.IdentityEpoch),
                });
        }

        foreach (WorkerRecord worker in register.Workers)
        {
            yield return string.Join(
                Separator.ToString(),
                new[] { WorkerTag, worker.Id.Value, AtomicTextFile.Escape(worker.Role) });
        }
    }

    /// <summary>Round-trip formatting. <c>"R"</c> rather than a fixed number of
    /// decimals: a designation that moved by a fraction of a millimetre across
    /// a save would stop matching itself, and re-marking it would stop being
    /// idempotent.</summary>
    private static string Number(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool TryParseNumber(string field, out float value)
    {
        return float.TryParse(
            field, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value)
            && !float.IsInfinity(value);
    }

    private static bool TryParseDesignation(string[] fields, out Designation designation)
    {
        designation = null!;

        if (fields.Length < 7 || !string.Equals(fields[0], DesignationTag, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue)
            || !Enum.IsDefined(typeof(DesignationKind), kindValue)
            || (DesignationKind)kindValue == DesignationKind.None)
        {
            return false;
        }

        if (!TryParseNumber(fields[2], out float x)
            || !TryParseNumber(fields[3], out float y)
            || !TryParseNumber(fields[4], out float z)
            || !TryParseNumber(fields[5], out float radius))
        {
            return false;
        }

        // The size bound is re-applied on load, unlike the ward check, and the
        // difference is the point: a ward answer depends on the world as it is
        // now, so re-asking it could delete a settlement a player still has,
        // but a radius is a property of the row itself and is wrong in exactly
        // the same way today as it was when it was written. A damaged harvest
        // row carrying a radius of 1e30 would otherwise load clean and make
        // felling legal everywhere in the world, which is the precise opposite
        // of "nothing marked is never anywhere".
        if ((DesignationKind)kindValue != DesignationKind.SupplyContainer
            && (!(radius >= DesignationBook.MinRadius) || !(radius <= DesignationBook.MaxRadius)))
        {
            return false;
        }

        string? containerKey = fields[6].Length == 0 ? null : AtomicTextFile.Unescape(fields[6]);

        // Written by a build that had no epoch, or a row that predates one: the
        // designation loads, and the missing epoch makes it permanently stale,
        // so it resolves to nothing until the player marks the chest again.
        // That is the safe direction.
        string? epoch = fields.Length < 8 || fields[7].Length == 0
            ? null
            : AtomicTextFile.Unescape(fields[7]);

        try
        {
            designation = new Designation(
                (DesignationKind)kindValue, new SitePoint(x, y, z), radius, containerKey, epoch);
            return true;
        }
        catch (ArgumentException)
        {
            // A row that does not satisfy the type's own invariants is damaged,
            // not something to repair by guessing which field was wrong.
            return false;
        }
    }

    private static bool TryParseWorker(string[] fields, out WorkerRecord worker)
    {
        worker = null!;

        if (fields.Length < 3 || !string.Equals(fields[0], WorkerTag, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            worker = new WorkerRecord(new WorkerId(fields[1]), AtomicTextFile.Unescape(fields[2]));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
