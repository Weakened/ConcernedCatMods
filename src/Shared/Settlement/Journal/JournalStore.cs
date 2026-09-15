using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Storage;

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
/// so.</summary>
internal sealed class JournalStore
{
    private const string Extension = ".settlement.tsv";
    private const string TemporarySuffix = ".tmp";

    /// <summary>Bumped only when a change would confuse an older build. An
    /// older build refuses a newer file rather than reading half of it.</summary>
    public const int SchemaVersion = 1;

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
        {
            Journal = journal;
            Outcome = outcome;
            SkippedLines = skippedLines;
            ReadOnly = readOnly;
            Notice = notice;
        }

        public SettlementJournal Journal { get; }
        public JournalLoadOutcome Outcome { get; }
        public int SkippedLines { get; }

        /// <summary>True when this build must not write over the file. The
        /// settlement then refuses new work rather than losing the record.</summary>
        public bool ReadOnly { get; }

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
        bool sawHeader = false;

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

            if (!sawHeader)
            {
                sawHeader = true;
                if (fields.Length >= 3 && string.Equals(fields[0], "v", StringComparison.Ordinal))
                {
                    if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) ||
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

                    continue;
                }

                // No header at all: an older or hand-made file. Fall through
                // and try to read it as entries.
                sawHeader = true;
            }

            if (TryParseEntry(fields, out JournalEntry entry))
            {
                journal.Restore(entry);
            }
            else
            {
                skipped++;
            }
        }

        journal.MarkClean();

        if (skipped > 0)
        {
            return new LoadReport(
                journal, JournalLoadOutcome.LoadedWithSkippedLines, skipped, readOnly: true,
                skipped.ToString(CultureInfo.InvariantCulture) + " line(s) of this settlement's record " +
                "could not be read. Everything readable was kept and the file will NOT be rewritten, " +
                "so nothing is lost — but no new work will be started until it is repaired, because a " +
                "partial record cannot account for materials.");
        }

        return new LoadReport(journal, JournalLoadOutcome.Loaded, 0, false, null);
    }

    public SaveReport Save(SettlementJournal journal, bool readOnly = false)
    {
        if (journal == null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (readOnly)
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
        yield return "#" + Separator + "settlement journal v" +
            SchemaVersion.ToString(CultureInfo.InvariantCulture);
        yield return string.Join(
            Separator.ToString(),
            new[] { "v", SchemaVersion.ToString(CultureInfo.InvariantCulture), journal.Scope.ToStorageKey() });

        foreach (JournalEntry entry in journal.Entries)
        {
            var fields = new List<string>
            {
                "e",
                entry.Sequence.ToString(CultureInfo.InvariantCulture),
                ((int)entry.Kind).ToString(CultureInfo.InvariantCulture),
                entry.Order.Value,
                entry.Request.IsEmpty ? "" : entry.Request.Value,
                ((int)entry.Transition).ToString(CultureInfo.InvariantCulture),
                AtomicTextFile.Escape(entry.Container),
            };

            foreach (MaterialStack stack in entry.Stacks)
            {
                fields.Add(AtomicTextFile.Escape(stack.Item) + "*" + stack.Count.ToString(CultureInfo.InvariantCulture));
            }

            yield return string.Join(Separator.ToString(), fields.ToArray());
        }
    }

    private static bool TryParseEntry(string[] fields, out JournalEntry entry)
    {
        entry = null!;

        if (fields.Length < 7 || !string.Equals(fields[0], "e", StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence) ||
            sequence < 0)
        {
            return false;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindValue) ||
            !Enum.IsDefined(typeof(JournalEntryKind), kindValue))
        {
            return false;
        }

        if (!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int transitionValue) ||
            !Enum.IsDefined(typeof(OrderTransition), transitionValue))
        {
            return false;
        }

        OrderId order;
        RequestId request = default;
        try
        {
            order = new OrderId(fields[3]);
            if (fields[4].Length > 0)
            {
                request = new RequestId(fields[4]);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        string? container = fields[6].Length == 0 ? null : AtomicTextFile.Unescape(fields[6]);

        var stacks = new List<MaterialStack>();
        for (int index = 7; index < fields.Length; index++)
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

        entry = new JournalEntry(
            sequence, (JournalEntryKind)kindValue, order, request,
            (OrderTransition)transitionValue, container, stacks);
        return true;
    }
}
