using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.Settlement.Storage;

/// <summary>What a record's closing line says about the lines above it.</summary>
internal enum TrailerVerdict
{
    /// <summary>Not checked, or checked with nothing to say. A zero that never
    /// reads as "intact".</summary>
    Unspecified = 0,

    /// <summary>The trailer is present and agrees with every line above it.
    /// </summary>
    Intact = 1,

    /// <summary>The file ends without a trailer: it lost its tail. A file cut
    /// on a line boundary parses cleanly line by line, which is exactly why this
    /// line exists (#293).</summary>
    Missing = 2,

    /// <summary>A trailer is present but its row count, last sequence or
    /// checksum disagrees with the lines above it: rows were lost, added or
    /// changed after it was written.</summary>
    Mismatch = 3,

    /// <summary>Lines follow the trailer. Something appended to a finished
    /// record.</summary>
    LinesAfterTrailer = 4,
}

/// <summary>The closing line of a settlement record, and how to check it.
///
/// <b>Why a trailer, and why a count and a checksum both.</b> Both settlement
/// files are parsed line by line, and a line that parses is kept. Without a
/// trailer, a file that lost its tail — an interrupted non-atomic copy, a
/// truncating sync client, a partially restored backup — reads as a shorter
/// file somebody wrote on purpose, and the next save rewrites that shorter file
/// as the truth (#293). The trailer is the last line written, so any cut on a
/// line boundary removes it. The row count catches rows lost from the middle,
/// and the checksum catches rows that were changed or swapped while still
/// parsing.
///
/// <b>What it does not do.</b> It does not detect a complete older copy of the
/// file restored over a newer one: that copy carries its own consistent
/// trailer. Nothing inside a file can detect that; the world-save marker rule
/// and the reconciliation against real inventories are what catch it for
/// custody.
///
/// <b>Hand repair stays possible.</b> The checksum is FNV-1a 64 over the UTF-8
/// bytes of every non-comment, non-blank line above the trailer, each followed
/// by a single <c>\n</c> — so it does not depend on the platform's line ending,
/// and the repair guide gives a four-line script that recomputes it.
///
/// The format is <c>end\t&lt;rows&gt;\t&lt;lastSequence&gt;\t&lt;checksum&gt;</c>.
/// A record without sequences (the register) writes <c>-1</c>.</summary>
internal static class RecordTrailer
{
    public const string Tag = "end";

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>Accumulates the checksum and row count while lines are written
    /// or read, so the whole file never has to be held twice.</summary>
    internal sealed class Accumulator
    {
        private ulong _hash = FnvOffset;

        public int Rows { get; private set; }

        public long LastSequence { get; private set; } = -1L;

        /// <summary>Folds one header or row line into the checksum.</summary>
        public void AddLine(string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line ?? string.Empty);
            foreach (byte value in bytes)
            {
                _hash ^= value;
                _hash *= FnvPrime;
            }

            _hash ^= (byte)'\n';
            _hash *= FnvPrime;
        }

        /// <summary>Counts a data row (not the header) and remembers the highest
        /// sequence it carried, when it carried one.</summary>
        public void CountRow(long sequence)
        {
            Rows++;
            if (sequence > LastSequence)
            {
                LastSequence = sequence;
            }
        }

        public string Checksum => _hash.ToString("x16", CultureInfo.InvariantCulture);

        public string Line =>
            string.Join(
                "\t",
                new[]
                {
                    Tag,
                    Rows.ToString(CultureInfo.InvariantCulture),
                    LastSequence.ToString(CultureInfo.InvariantCulture),
                    Checksum,
                });
    }

    public static bool IsTrailer(string[] fields) =>
        fields.Length >= 1 && string.Equals(fields[0], Tag, StringComparison.Ordinal);

    /// <summary>Compares a trailer read from disk with what the lines above it
    /// actually add up to.</summary>
    public static TrailerVerdict Check(string[] trailerFields, Accumulator read)
    {
        if (trailerFields == null || read == null || trailerFields.Length < 4)
        {
            return TrailerVerdict.Mismatch;
        }

        if (!int.TryParse(trailerFields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rows)
            || !long.TryParse(trailerFields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long last))
        {
            return TrailerVerdict.Mismatch;
        }

        return rows == read.Rows
            && last == read.LastSequence
            && string.Equals(trailerFields[3], read.Checksum, StringComparison.Ordinal)
                ? TrailerVerdict.Intact
                : TrailerVerdict.Mismatch;
    }

    /// <summary>Wraps a record's lines with the trailer. Every line the writer
    /// yields is folded in exactly as the reader will fold it back.</summary>
    public static IEnumerable<string> Seal(IEnumerable<string> lines, Func<string, long?> rowSequence)
    {
        var accumulator = new Accumulator();
        foreach (string line in lines)
        {
            yield return line;

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            accumulator.AddLine(line);
            long? sequence = rowSequence(line);
            if (sequence.HasValue)
            {
                accumulator.CountRow(sequence.Value);
            }
        }

        yield return accumulator.Line;
    }

    /// <summary>One sentence for a player, naming the file's state.</summary>
    public static string Describe(TrailerVerdict verdict)
    {
        switch (verdict)
        {
            case TrailerVerdict.Intact:
                return "the record is complete";
            case TrailerVerdict.Missing:
                return "the record ends early: its closing line is missing, so the end of the file was lost";
            case TrailerVerdict.Mismatch:
                return "the record's closing line does not match the lines above it, so lines were lost or changed";
            case TrailerVerdict.LinesAfterTrailer:
                return "something was written after the record's closing line";
            default:
                return "the record's completeness was not checked";
        }
    }
}
