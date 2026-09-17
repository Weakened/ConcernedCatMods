using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Storage;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.Settlement.Journal;

internal enum JournalLoadOutcome
{
    Missing = 0,
    Loaded = 1,

    /// <summary>Readable, but some lines were not. The readable ones are kept
    /// and the file is <b>not</b> rewritten, because a journal is the record of
    /// what happened and silently dropping part of it is how material goes
    /// missing without anybody noticing.</summary>
    LoadedWithSkippedLines = 2,

    /// <summary>Written by a newer build. Read-only.</summary>
    UnsupportedSchema = 3,

    /// <summary>Belongs to another world or settlement. Read-only.</summary>
    ScopeMismatch = 4,

    /// <summary>Unreadable.</summary>
    Unreadable = 5,

    /// <summary>Every line read, but the record ends without its closing line:
    /// its tail was lost (#293). Everything readable is kept; read-only.
    /// </summary>
    Truncated = 6,

    /// <summary>Every line read, but the closing line disagrees with the lines
    /// above it: rows were lost, added or changed (#293). Read-only.</summary>
    IntegrityMismatch = 7,
}

/// <summary>Reads and writes a settlement journal as a tab-separated file.
///
/// A journal is append-only, so the format is one line per entry and the file
/// is rewritten whole on save. That is the same temp-file-and-swap the
/// companion sidecar store uses, and for the same reason: an interrupted write
/// must leave either the old complete file or the new complete file, never half
/// of either — and for a material ledger, half a file is worse than no file.
///
/// One rule differs from the companion store, deliberately. An unreadable
/// journal is <b>never</b> quarantined and replaced with a fresh one. A
/// companion sidecar holds progress through a story and starting over costs a
/// player four pages of reading; a settlement journal holds the only record of
/// whose wood is where, and replacing it with an empty file would silently
/// destroy that. An unreadable journal makes the settlement read-only and says
/// so.
///
/// <b>Schema 3</b> adds, all in one bump:
/// <list type="bullet">
/// <item>the custody kinds of CONTRACTS.md §5.4, as <c>c</c> rows of named
/// fields, each with the world time and the load that wrote it;</item>
/// <item>world time, load epoch and flags on tool rows (an interrupted return,
/// an attempt the handover itself abandoned — #300);</item>
/// <item>the container's identity epoch on reservation rows (#294);</item>
/// <item>a closing line — row count, last sequence, checksum — so a record that
/// lost its tail on a line boundary no longer loads clean (#293).</item>
/// </list>
/// A schema-1 or schema-2 file still loads exactly as it did: it has no closing
/// line to check, so its completeness is unverified, and the next write stores
/// it as schema 3.</summary>
internal sealed class JournalStore
{
    private const string Extension = ".settlement.tsv";
    private const string TemporarySuffix = ".tmp";

    /// <summary>Bumped only when a change would confuse an older build. An
    /// older build refuses a newer file rather than reading half of it.
    ///
    /// <b>2</b> since tool handovers joined the record. The bump is the point: a
    /// build that does not know about tools cannot account for one a player
    /// handed over, and half-reading such a file would let it rewrite the file
    /// with the handovers deleted. Refusing outright is the safe direction, and
    /// it is why the tool rows also carry their own tag rather than extending
    /// the entry row — a version check is a clearer refusal than a pile of
    /// unreadable lines.
    ///
    /// <b>3</b> since gathered-material custody and world-save markers joined
    /// it, for the same reason: a schema-2 build cannot account for carried
    /// stone, and would write the file back without it.</summary>
    public const int SchemaVersion = 3;

    /// <summary>Row tag for a material or order entry.</summary>
    private const string EntryTag = "e";

    /// <summary>Row tag for a tool handover. Deliberately distinct: a handover
    /// is keyed by a worker and carries a tool, and squeezing it into the entry
    /// row would have meant either a synthetic order id or trailing columns
    /// after the variable-length stack list.</summary>
    private const string ToolTag = "t";

    /// <summary>Row tag for a schema-3 custody entry: named fields.</summary>
    private const string CustodyTag = "c";

    private const char Separator = '\t';
    private const char CarriageReturn = '\r';
    private const char LineFeed = '\n';

    private readonly string _rootDirectory;

    public JournalStore(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("A journal root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    public sealed class LoadReport
    {
        public LoadReport(
            SettlementJournal journal, JournalLoadOutcome outcome, int skippedLines,
            bool readOnly, string? notice)
            : this(journal, outcome, skippedLines, readOnly, notice, 0, TrailerVerdict.Unspecified)
        {
        }

        public LoadReport(
            SettlementJournal journal, JournalLoadOutcome outcome, int skippedLines,
            bool readOnly, string? notice, int schemaVersion, TrailerVerdict trailer)
        {
            Journal = journal;
            Outcome = outcome;
            SkippedLines = skippedLines;
            Notice = notice;
            SchemaVersion = schemaVersion;
            Trailer = trailer;

            if (readOnly)
            {
                journal.MarkReadOnly();
            }
        }

        public SettlementJournal Journal { get; }
        public JournalLoadOutcome Outcome { get; }
        public int SkippedLines { get; }

        /// <summary>The schema the file declared; 0 for a missing or headerless
        /// file.</summary>
        public int SchemaVersion { get; }

        /// <summary>What the closing line said. <see cref="TrailerVerdict.Unspecified"/>
        /// for a file that predates closing lines: its completeness is not
        /// verifiable, and the status says so.</summary>
        public TrailerVerdict Trailer { get; }

        /// <summary>True when this build must not write over the file. The
        /// settlement then refuses new work rather than losing the record.
        /// Read straight off the journal, so the two can never disagree about
        /// whether writing is allowed.</summary>
        public bool ReadOnly => Journal.IsReadOnly;

        public string? Notice { get; }
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
                    new SettlementJournal(scope), JournalLoadOutcome.Missing, 0, false, null);
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            return new LoadReport(
                new SettlementJournal(scope), JournalLoadOutcome.Unreadable, 0, readOnly: true,
                "The settlement's record could not be read (" + exception.GetType().Name + "). " +
                "No new work will be started and nothing was deleted, so the file can be " +
                "recovered or repaired by hand.");
        }

        var journal = new SettlementJournal(scope);
        int skipped = 0;
        long highestSequence = -1L;
        bool sawHeader = false;
        int version = 1;
        var accumulator = new RecordTrailer.Accumulator();
        TrailerVerdict trailer = TrailerVerdict.Unspecified;
        bool sawTrailer = false;

        foreach (string raw in lines)
        {
            // Trimmed at the END ONLY, and only of line endings. A general
            // Trim() would eat the trailing tab of an entry whose last field is
            // empty -- which is every entry that reserved nothing -- and the
            // line would then be one field short and read as damaged. That is a
            // silent data-loss bug in a material ledger, so the narrow trim is
            // deliberate.
            string line = raw.TrimEnd(CarriageReturn, LineFeed);
            if (line.Trim().Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split(Separator);

            if (sawTrailer)
            {
                // Nothing may follow a finished record.
                trailer = TrailerVerdict.LinesAfterTrailer;
                skipped++;
                continue;
            }

            if (!sawHeader)
            {
                sawHeader = true;
                if (fields.Length >= 3 && string.Equals(fields[0], "v", StringComparison.Ordinal))
                {
                    if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) ||
                        version > SchemaVersion)
                    {
                        return new LoadReport(
                            journal, JournalLoadOutcome.UnsupportedSchema, 0, readOnly: true,
                            "This settlement's record was written by a newer version of the mod. It is " +
                            "being left exactly as it is so nothing gets lost, and no new work will be " +
                            "started against it.");
                    }

                    if (!string.Equals(fields[2], scope.ToStorageKey(), StringComparison.Ordinal))
                    {
                        return new LoadReport(
                            journal, JournalLoadOutcome.ScopeMismatch, 0, readOnly: true,
                            "The file \"" + Path.GetFileName(path) + "\" belongs to a different world or " +
                            "settlement, so it was left untouched and nothing will be written over it.");
                    }

                    accumulator.AddLine(line);
                    continue;
                }

                // No header at all: an older or hand-made file. Fall through
                // and try to read it as entries, under the oldest layout.
                version = 1;
            }

            if (version >= 3 && RecordTrailer.IsTrailer(fields))
            {
                sawTrailer = true;
                trailer = RecordTrailer.Check(fields, accumulator);
                continue;
            }

            accumulator.AddLine(line);
            accumulator.CountRow(PeekSequence(fields));

            if (TryParseRow(fields, version, out JournalEntry entry) && entry.Sequence > highestSequence)
            {
                highestSequence = entry.Sequence;
                journal.Restore(entry);
            }
            else
            {
                // A row out of sequence is damage, not data. Accepting it would
                // let the file's order differ from the record's order, which is
                // the one thing the sequence exists to prevent.
                skipped++;
            }
        }

        if (version >= 3 && !sawTrailer)
        {
            trailer = TrailerVerdict.Missing;
        }

        journal.MarkClean();

        bool damagedTrailer = version >= 3 && trailer != TrailerVerdict.Intact;

        if (skipped > 0)
        {
            return new LoadReport(
                journal, JournalLoadOutcome.LoadedWithSkippedLines, skipped, readOnly: true,
                skipped.ToString(CultureInfo.InvariantCulture) + " line(s) of this settlement's record " +
                "could not be read" + (damagedTrailer ? ", and " + RecordTrailer.Describe(trailer) : string.Empty) +
                ". Everything readable was kept and the file will NOT be rewritten, " +
                "so nothing is lost — but no new work will be started until it is repaired, because a " +
                "partial record cannot account for materials.",
                version, trailer);
        }

        if (damagedTrailer)
        {
            return new LoadReport(
                journal,
                trailer == TrailerVerdict.Missing ? JournalLoadOutcome.Truncated : JournalLoadOutcome.IntegrityMismatch,
                0, readOnly: true,
                "This settlement's record is not complete: " + RecordTrailer.Describe(trailer) + ". Every " +
                "line that could be read was kept and the file will NOT be rewritten — but no new work will be " +
                "started, because a record with lines missing cannot account for materials. A complete copy " +
                "may be beside it as \"" + Path.GetFileName(path) + TemporarySuffix + "\".",
                version, trailer);
        }

        return new LoadReport(journal, JournalLoadOutcome.Loaded, 0, false, null, version, trailer);
    }

    public SaveReport Save(SettlementJournal journal, bool readOnly = false)
    {
        if (journal == null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (readOnly || journal.IsReadOnly)
        {
            return new SaveReport(
                false,
                "This settlement's record is from a newer version, another settlement, or could not " +
                "be fully read, so nothing was written over it.");
        }

        if (!journal.IsDirty)
        {
            return new SaveReport(false, null);
        }

        string path = ResolvePath(journal.Scope);
        string temporaryPath = path + TemporarySuffix;

        Exception? writeFailure =
            AtomicTextFile.TryWriteTemporary(_rootDirectory, temporaryPath, Serialize(journal));
        if (writeFailure != null)
        {
            return new SaveReport(
                false,
                "Could not record the settlement's progress (" + writeFailure.GetType().Name + "). " +
                "Nothing was changed on disk and the work will be recorded again on the next change.");
        }

        try
        {
            AtomicTextFile.Commit(temporaryPath, path);
            journal.MarkClean();
            return new SaveReport(true, null);
        }
        catch (Exception exception)
        {
            // The temporary file holds a COMPLETE copy and the live file may be
            // mid-replace, so it is deliberately left on disk: deleting it here
            // would throw away the only intact record.
            return new SaveReport(
                false,
                "Could not record the settlement's progress (" + exception.GetType().Name + "). " +
                "A complete copy was left as \"" + Path.GetFileName(temporaryPath) + "\".");
        }
    }

    public static IEnumerable<string> Serialize(SettlementJournal journal)
    {
        return RecordTrailer.Seal(Lines(journal), LineSequence);
    }

    private static IEnumerable<string> Lines(SettlementJournal journal)
    {
        yield return "#" + Separator + "settlement journal v" +
            SchemaVersion.ToString(CultureInfo.InvariantCulture);
        yield return string.Join(
            Separator.ToString(),
            new[] { "v", SchemaVersion.ToString(CultureInfo.InvariantCulture), journal.Scope.ToStorageKey() });

        foreach (JournalEntry entry in journal.Entries)
        {
            yield return FormatRow(entry);
        }
    }

    /// <summary>The sequence a row line carries, for the closing line; null for
    /// anything that is not a row.</summary>
    private static long? LineSequence(string line)
    {
        string[] fields = line.Split(Separator);
        if (fields.Length < 2 || !IsRowTag(fields[0]))
        {
            return null;
        }

        return PeekSequence(fields);
    }

    private static bool IsRowTag(string tag) =>
        string.Equals(tag, EntryTag, StringComparison.Ordinal)
        || string.Equals(tag, ToolTag, StringComparison.Ordinal)
        || string.Equals(tag, CustodyTag, StringComparison.Ordinal);

    /// <summary>The sequence column of any row, or -1 when it has none a
    /// reader could use. Damaged rows still count toward the closing line.
    /// </summary>
    private static long PeekSequence(string[] fields)
    {
        return fields.Length >= 2
            && long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
            && sequence >= 0
                ? sequence
                : -1L;
    }

    private static string FormatRow(JournalEntry entry)
    {
        if (entry.Custody != null)
        {
            var columns = new List<string>
            {
                CustodyTag,
                entry.Sequence.ToString(CultureInfo.InvariantCulture),
                ((int)entry.Kind).ToString(CultureInfo.InvariantCulture),
                FormatTime(entry.WorldTime),
                FormatEpoch(entry.LoadEpoch),
            };
            columns.AddRange(CustodyRowCodec.Encode(entry.Custody).Encode());
            return string.Join(Separator.ToString(), columns.ToArray());
        }

        if (JournalEntryKinds.IsTool(entry.Kind))
        {
            var columns = new List<string>
            {
                ToolTag,
                entry.Sequence.ToString(CultureInfo.InvariantCulture),
                ((int)entry.Kind).ToString(CultureInfo.InvariantCulture),
                entry.Request.Value,
                entry.Worker.Value,
                ((int)entry.Tool.Kind).ToString(CultureInfo.InvariantCulture),
                AtomicTextFile.Escape(entry.Tool.ItemKey),
                entry.Tool.Quality.ToString(CultureInfo.InvariantCulture),
                entry.Tool.DurabilityAtIssue.ToString("R", CultureInfo.InvariantCulture),
                entry.Tool.ToolTier.ToString(CultureInfo.InvariantCulture),
                FormatTime(entry.WorldTime),
                FormatEpoch(entry.LoadEpoch),
            };

            var flags = new JournalFields();
            if (entry.ReturnIntent)
            {
                flags.Add("stage", "started");
            }

            if (entry.WrittenByHandover)
            {
                flags.Add("by", "handover");
            }

            if (entry.Note.Length > 0)
            {
                flags.Add("note", entry.Note);
            }

            columns.AddRange(flags.Encode());
            return string.Join(Separator.ToString(), columns.ToArray());
        }

        var fields = new List<string>
        {
            EntryTag,
            entry.Sequence.ToString(CultureInfo.InvariantCulture),
            ((int)entry.Kind).ToString(CultureInfo.InvariantCulture),
            entry.Order.Value,
            entry.Request.IsEmpty ? "" : entry.Request.Value,
            ((int)entry.Transition).ToString(CultureInfo.InvariantCulture),
            AtomicTextFile.Escape(entry.Container),
            AtomicTextFile.Escape(entry.ContainerEpoch),
            FormatTime(entry.WorldTime),
            FormatEpoch(entry.LoadEpoch),
        };

        foreach (MaterialStack stack in entry.Stacks)
        {
            fields.Add(AtomicTextFile.Escape(stack.Item) + "*" + stack.Count.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(Separator.ToString(), fields.ToArray());
    }

    private static string FormatTime(double? time) =>
        time.HasValue ? time.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatEpoch(Guid epoch) =>
        epoch == Guid.Empty ? string.Empty : epoch.ToString("N", CultureInfo.InvariantCulture);

    private static bool TryParseRow(string[] fields, int version, out JournalEntry entry)
    {
        entry = null!;
        if (fields.Length == 0)
        {
            return false;
        }

        switch (fields[0])
        {
            case EntryTag:
                return TryParseEntry(fields, version, out entry);
            case ToolTag:
                return TryParseToolEntry(fields, version, out entry);
            case CustodyTag:
                return version >= 3 && TryParseCustody(fields, out entry);
            default:
                return false;
        }
    }

    /// <summary>World time and load epoch, recorded together or not at all.
    /// </summary>
    private static bool TryParseTime(string timeField, string epochField, out double? time, out Guid epoch)
    {
        time = null;
        epoch = Guid.Empty;

        if (timeField.Length == 0 && epochField.Length == 0)
        {
            return true;
        }

        if (!double.TryParse(timeField, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || !Guid.TryParseExact(epochField, "N", out epoch)
            || epoch == Guid.Empty)
        {
            return false;
        }

        time = parsed;
        return true;
    }

    private static bool TryParseCustody(string[] fields, out JournalEntry entry)
    {
        entry = null!;

        if (fields.Length < 5)
        {
            return false;
        }

        if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
            || sequence < 0)
        {
            return false;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue)
            || !Enum.IsDefined(typeof(JournalEntryKind), kindValue)
            || !JournalEntryKinds.IsCustody((JournalEntryKind)kindValue))
        {
            return false;
        }

        if (!TryParseTime(fields[3], fields[4], out double? time, out Guid epoch) || !time.HasValue)
        {
            // Every custody row carries its world time; one without it could
            // never be placed against a save.
            return false;
        }

        if (!JournalFields.TryDecode(fields, 5, out JournalFields named)
            || !CustodyRowCodec.TryDecode((JournalEntryKind)kindValue, named, out CustodyRow payload))
        {
            return false;
        }

        try
        {
            entry = new JournalEntry(sequence, payload, time.Value, epoch);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads a tool handover row.
    ///
    /// Every field is validated before the entry is built, and a row that fails
    /// any of them is damage rather than something to repair by guessing which
    /// column was wrong -- the same rule the entry rows follow.</summary>
    private static bool TryParseToolEntry(string[] fields, int version, out JournalEntry entry)
    {
        entry = null!;

        int minimum = version >= 3 ? 12 : 10;
        if (fields.Length < minimum || !string.Equals(fields[0], ToolTag, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
            || sequence < 0)
        {
            return false;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue)
            || !Enum.IsDefined(typeof(JournalEntryKind), kindValue)
            || !JournalEntryKinds.IsTool((JournalEntryKind)kindValue))
        {
            return false;
        }

        if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int toolKind)
            || !Enum.IsDefined(typeof(ToolKind), toolKind)
            || (ToolKind)toolKind == ToolKind.None)
        {
            return false;
        }

        if (!int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality)
            || !float.TryParse(
                fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float durability)
            || float.IsNaN(durability)
            || float.IsInfinity(durability)
            || !int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier))
        {
            return false;
        }

        if (fields[3].Length == 0)
        {
            // Every tool kind keys off a request id -- the replay classifies
            // them that way -- so a row without one is damage rather than a row
            // that parses cleanly and is then silently ignored.
            return false;
        }

        double? time = null;
        Guid epoch = Guid.Empty;
        bool returnIntent = false;
        bool byHandover = false;
        string? note = null;

        if (version >= 3)
        {
            if (!TryParseTime(fields[10], fields[11], out time, out epoch)
                || !JournalFields.TryDecode(fields, 12, out JournalFields flags))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> flag in flags.Pairs)
            {
                switch (flag.Key)
                {
                    case "stage" when string.Equals(flag.Value, "started", StringComparison.Ordinal):
                        returnIntent = true;
                        break;
                    case "by" when string.Equals(flag.Value, "handover", StringComparison.Ordinal):
                        byHandover = true;
                        break;
                    case "note":
                        note = flag.Value;
                        break;
                    default:
                        // A flag this build does not understand changes what the
                        // row means. Reading the row without it would be reading
                        // a different row.
                        return false;
                }
            }
        }

        try
        {
            var specimen = new ToolSpecimen(
                (ToolKind)toolKind, AtomicTextFile.Unescape(fields[6]), quality, durability, tier);

            entry = new JournalEntry(
                sequence,
                (JournalEntryKind)kindValue,
                default,
                new RequestId(fields[3]),
                OrderTransition.Approve,
                null,
                null,
                new WorkerId(fields[4]),
                specimen,
                worldTime: time,
                loadEpoch: epoch,
                returnIntent: returnIntent,
                writtenByHandover: byHandover,
                note: note);
            return true;
        }
        catch (ArgumentException)
        {
            // A row that does not satisfy the types' own invariants is damaged.
            // ArgumentOutOfRangeException derives from ArgumentException, so
            // this one clause covers both the identity and the range guards.
            return false;
        }
    }

    private static bool TryParseEntry(string[] fields, int version, out JournalEntry entry)
    {
        entry = null!;

        int stacksStart = version >= 3 ? 10 : 7;
        if (fields.Length < stacksStart || !string.Equals(fields[0], EntryTag, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence) ||
            sequence < 0)
        {
            return false;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue) ||
            !Enum.IsDefined(typeof(JournalEntryKind), kindValue) ||
            !JournalEntryKinds.IsLegacyMaterial((JournalEntryKind)kindValue))
        {
            // A tool or custody kind on an entry row is damage. Without this the
            // JournalEntry constructor below throws straight out of Load, and one
            // corrupted tag byte makes every command for that world fail forever
            // with no read-only mode and no notice, because no LoadReport is ever
            // built.
            return false;
        }

        if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int transitionValue) ||
            !Enum.IsDefined(typeof(OrderTransition), transitionValue))
        {
            return false;
        }

        var kind = (JournalEntryKind)kindValue;
        if (SettlementJournal.CarriesRequest(kind) && fields[4].Length == 0)
        {
            // Every one of these keys off its request, and the replay used to
            // skip such a row without a word. A row the record cannot use is
            // damage, so the settlement goes read-only and says so (#283's
            // audit: silent replay drops).
            return false;
        }

        string? container = fields[6].Length == 0 ? null : AtomicTextFile.Unescape(fields[6]);
        string? containerEpoch = null;
        double? time = null;
        Guid epoch = Guid.Empty;

        if (version >= 3)
        {
            containerEpoch = fields[7].Length == 0 ? null : AtomicTextFile.Unescape(fields[7]);
            if (!TryParseTime(fields[8], fields[9], out time, out epoch))
            {
                return false;
            }
        }

        var stacks = new List<MaterialStack>();
        for (int index = stacksStart; index < fields.Length; index++)
        {
            int star = fields[index].LastIndexOf('*');
            if (star <= 0 ||
                !int.TryParse(
                    fields[index].Substring(star + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int count) ||
                count <= 0)
            {
                return false;
            }

            stacks.Add(new MaterialStack(AtomicTextFile.Unescape(fields[index].Substring(0, star)), count));
        }

        if (kind == JournalEntryKind.Reserved && (container == null || stacks.Count == 0))
        {
            // A reservation that names no container or holds nothing cannot be
            // replayed. It used to be skipped silently; it is damage.
            return false;
        }

        try
        {
            entry = new JournalEntry(
                sequence, kind, new OrderId(fields[3]),
                fields[4].Length > 0 ? new RequestId(fields[4]) : default,
                (OrderTransition)transitionValue, container, stacks,
                containerEpoch: containerEpoch, worldTime: time, loadEpoch: epoch);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
