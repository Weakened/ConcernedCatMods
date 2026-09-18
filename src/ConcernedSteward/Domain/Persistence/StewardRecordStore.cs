using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Storage;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Persistence;

/// <summary>How a record read went. <see cref="Unreadable"/> is zero: a load
/// nobody performed is never "fine".</summary>
internal enum RecordLoadOutcome
{
    /// <summary>The file exists and could not be trusted. The Steward goes
    /// read-only: he keeps what the file said, and writes nothing over it.
    /// </summary>
    Unreadable = 0,

    /// <summary>There is no file. A world where nothing has happened yet.
    /// </summary>
    Absent = 1,

    /// <summary>Read, complete, and its closing line agrees with it.</summary>
    Intact = 2,
}

/// <summary>What one load produced.</summary>
internal sealed class RecordLoadReport
{
    internal RecordLoadReport(
        RecordLoadOutcome outcome,
        IntroductionStage stage,
        IntroductionStage furthest,
        IReadOnlyList<Designation> designations,
        IReadOnlyList<UpkeepIntent> openIntents,
        int recordedLoss,
        string fuelItemName,
        int skippedLines,
        string? notice)
    {
        Outcome = outcome;
        Stage = stage;
        Furthest = furthest;
        Designations = designations;
        OpenIntents = openIntents;
        RecordedLoss = recordedLoss;
        FuelItemName = fuelItemName ?? string.Empty;
        SkippedLines = skippedLines;
        Notice = notice;
    }

    public RecordLoadOutcome Outcome { get; }

    public IntroductionStage Stage { get; }

    public IntroductionStage Furthest { get; }

    public IReadOnlyList<Designation> Designations { get; }

    /// <summary>Steps that were started and never finished. Each one is closed
    /// as uncertain and <b>never</b> re-run.</summary>
    public IReadOnlyList<UpkeepIntent> OpenIntents { get; }

    /// <summary>Units a previous session recorded as unaccounted for.</summary>
    public int RecordedLoss { get; }

    /// <summary>What he was carrying, by name, so the reload path can count his
    /// pack with the same predicate the withdrawal used.</summary>
    public string FuelItemName { get; }

    public int SkippedLines { get; }

    public string? Notice { get; }

    /// <summary>True when new rows may be written. A damaged file is kept, not
    /// rewritten: rewriting it would replace whatever survived with whatever
    /// happened to parse.</summary>
    public bool MayWrite => Outcome != RecordLoadOutcome.Unreadable;
}

/// <summary>The Steward's record on disk: who he is to this settlement, what
/// the player marked, what he was in the middle of, and what he lost.
///
/// <b>What is here and what deliberately is not.</b> This is <i>recovery
/// state</i>, not an audit log. It holds the things that cannot be re-derived
/// after a reload:
///
/// <list type="bullet">
/// <item>the introduction's stage and its high-water mark, because nothing in
/// the world records that a player once employed him;</item>
/// <item>the designations, because they are acts the player performed;</item>
/// <item><b>open</b> steps — an intent with no receipt — because that is the
/// only evidence that a mutation might have happened;</item>
/// <item>a recorded loss, because the units are gone and there is nothing left
/// to measure.</item>
/// </list>
///
/// It does <b>not</b> hold how much he is carrying, and that is the point. His
/// pack is the truth about his pack, and reconciling the record against it is
/// what makes recovery a measurement rather than a guess. Finished steps are
/// dropped once the trip closes; their evidence lines are in the log, where a
/// bug report can reach them, and keeping them here would grow a file
/// unboundedly to answer a question nothing asks.
///
/// <b>Durability.</b> Written through <see cref="AtomicTextFile"/> — whole new
/// file, flushed to the disk, then swapped — and closed by a
/// <see cref="RecordTrailer"/>, so a file that lost its tail is recognised
/// instead of read as a shorter file somebody wrote on purpose.</summary>
internal sealed class StewardRecordStore
{
    private const string Extension = ".steward.tsv";
    private const string Header = "# Concerned Steward record. One settlement, one world.";
    private const string FormatTag = "format";
    private const string FormatVersion = "1";
    private const string IntroTag = "intro";
    private const string MarkTag = "mark";
    private const string StepTag = "step";
    private const string LossTag = "loss";
    private const string CarryingTag = "carrying";

    private readonly string _rootDirectory;

    internal StewardRecordStore(string rootDirectory)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("A record root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    internal string ResolvePath(SettlementScope scope) =>
        Path.Combine(_rootDirectory, scope.ToStorageKey() + Extension);

    internal RecordLoadReport Load(SettlementScope scope)
    {
        string path = ResolvePath(scope);
        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return Empty(RecordLoadOutcome.Absent, null);
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
        {
            return Empty(
                RecordLoadOutcome.Unreadable,
                "The Steward's record could not be read (" + exception.GetType().Name +
                "), so nothing will be written over it.");
        }

        var designations = new List<Designation>();
        var open = new List<UpkeepIntent>();
        var accumulator = new RecordTrailer.Accumulator();
        IntroductionStage stage = IntroductionStage.Unmet;
        IntroductionStage furthest = IntroductionStage.Unmet;
        int loss = 0;
        string fuelItemName = string.Empty;
        int skipped = 0;
        TrailerVerdict verdict = TrailerVerdict.Missing;
        bool afterTrailer = false;

        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (afterTrailer)
            {
                verdict = TrailerVerdict.LinesAfterTrailer;
                break;
            }

            string[] fields = line.Split('\t');
            if (RecordTrailer.IsTrailer(fields))
            {
                verdict = RecordTrailer.Check(fields, accumulator);
                afterTrailer = true;
                continue;
            }

            accumulator.AddLine(line);
            accumulator.CountRow(-1L);

            switch (fields[0])
            {
                case FormatTag:
                    // A file from a format this build does not know is not a
                    // file to half-read and then rewrite.
                    if (fields.Length < 2 || !string.Equals(fields[1], FormatVersion, StringComparison.Ordinal))
                    {
                        return Empty(
                            RecordLoadOutcome.Unreadable,
                            "The Steward's record was written by a different version of this mod, " +
                            "so nothing will be written over it.");
                    }

                    break;

                case IntroTag:
                    if (!TryReadIntro(fields, out stage, out furthest))
                    {
                        skipped++;
                    }

                    break;

                case MarkTag:
                    if (TryReadDesignation(fields, out Designation? mark))
                    {
                        designations.Add(mark!);
                    }
                    else
                    {
                        skipped++;
                    }

                    break;

                case StepTag:
                    if (TryReadStep(fields, out UpkeepIntent intent))
                    {
                        open.Add(intent);
                    }
                    else
                    {
                        skipped++;
                    }

                    break;

                case LossTag:
                    if (fields.Length < 2
                        || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out loss)
                        || loss < 0)
                    {
                        loss = 0;
                        skipped++;
                    }

                    break;

                case CarryingTag:
                    fuelItemName = fields.Length < 2 ? string.Empty : AtomicTextFile.Unescape(fields[1]);
                    break;

                default:
                    skipped++;
                    break;
            }
        }

        if (verdict != TrailerVerdict.Intact)
        {
            // Everything parsed is still handed back — it is what the player
            // has — but nothing new is written over a file that cannot account
            // for itself.
            return new RecordLoadReport(
                RecordLoadOutcome.Unreadable, stage, furthest, designations, open, loss, fuelItemName,
                skipped,
                "The Steward's record is damaged: " + RecordTrailer.Describe(verdict) +
                ". He will not write over it. A complete copy may be beside it as a .tmp file.");
        }

        return new RecordLoadReport(
            RecordLoadOutcome.Intact, stage, furthest, designations, open, loss, fuelItemName, skipped,
            skipped == 0
                ? null
                : skipped.ToString(CultureInfo.InvariantCulture) +
                  " line(s) in the Steward's record could not be read and were left out.");
    }

    /// <summary>Writes the whole record. Returns the exception rather than
    /// throwing, because every caller has a sentence to show a player and none
    /// of them want a stack trace.</summary>
    internal Exception? Save(
        SettlementScope scope,
        IntroductionStage stage,
        IntroductionStage furthest,
        IReadOnlyList<Designation> designations,
        IReadOnlyList<UpkeepIntent> openIntents,
        int recordedLoss,
        string fuelItemName)
    {
        string path = ResolvePath(scope);
        string temporary = path + ".tmp";
        Exception? failure = AtomicTextFile.TryWriteTemporary(
            _rootDirectory, temporary,
            RecordTrailer.Seal(
                Rows(stage, furthest, designations, openIntents, recordedLoss, fuelItemName),
                _ => -1L));
        if (failure != null)
        {
            return failure;
        }

        try
        {
            AtomicTextFile.Commit(temporary, path);
            return null;
        }
        catch (Exception exception)
        {
            AtomicTextFile.TryDelete(temporary);
            return exception;
        }
    }

    private static IEnumerable<string> Rows(
        IntroductionStage stage,
        IntroductionStage furthest,
        IReadOnlyList<Designation> designations,
        IReadOnlyList<UpkeepIntent> openIntents,
        int recordedLoss,
        string fuelItemName)
    {
        yield return Header;
        yield return FormatTag + "\t" + FormatVersion;
        yield return IntroTag + "\t" + (int)stage + "\t" + (int)furthest;

        foreach (Designation designation in designations)
        {
            yield return string.Join(
                "\t",
                new[]
                {
                    MarkTag,
                    ((int)designation.Kind).ToString(CultureInfo.InvariantCulture),
                    Number(designation.Centre.X),
                    Number(designation.Centre.Y),
                    Number(designation.Centre.Z),
                    Number(designation.Radius),
                    AtomicTextFile.Escape(designation.ContainerKey),
                    AtomicTextFile.Escape(designation.IdentityEpoch),
                });
        }

        foreach (UpkeepIntent intent in openIntents)
        {
            yield return string.Join(
                "\t",
                new[]
                {
                    StepTag,
                    AtomicTextFile.Escape(intent.Request.Value),
                    ((int)intent.Step).ToString(CultureInfo.InvariantCulture),
                    AtomicTextFile.Escape(intent.Detail),
                    intent.Count.ToString(CultureInfo.InvariantCulture),
                });
        }

        if (recordedLoss > 0)
        {
            yield return LossTag + "\t" + recordedLoss.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(fuelItemName))
        {
            yield return CarryingTag + "\t" + AtomicTextFile.Escape(fuelItemName);
        }
    }

    private static bool TryReadIntro(
        string[] fields, out IntroductionStage stage, out IntroductionStage furthest)
    {
        stage = IntroductionStage.Unmet;
        furthest = IntroductionStage.Unmet;
        if (fields.Length < 3
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int one)
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int two))
        {
            return false;
        }

        stage = (IntroductionStage)one;
        furthest = (IntroductionStage)two;
        return true;
    }

    private static bool TryReadDesignation(string[] fields, out Designation? designation)
    {
        designation = null;
        if (fields.Length < 8
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
            || !TryNumber(fields[2], out float x)
            || !TryNumber(fields[3], out float y)
            || !TryNumber(fields[4], out float z)
            || !TryNumber(fields[5], out float radius))
        {
            return false;
        }

        string containerKey = AtomicTextFile.Unescape(fields[6]);
        string epoch = AtomicTextFile.Unescape(fields[7]);

        try
        {
            designation = (DesignationKind)kind == DesignationKind.SupplyContainer
                ? new Designation(
                    DesignationKind.SupplyContainer, new SitePoint(x, y, z), 0f,
                    containerKey.Length == 0 ? null : containerKey,
                    epoch.Length == 0 ? null : epoch)
                : new Designation((DesignationKind)kind, new SitePoint(x, y, z), radius, null);
            return true;
        }
        catch (ArgumentException)
        {
            // A row this build cannot represent is left out rather than
            // repaired into something the player did not mark.
            return false;
        }
    }

    private static bool TryReadStep(string[] fields, out UpkeepIntent intent)
    {
        intent = default;
        if (fields.Length < 5
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
            || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            return false;
        }

        try
        {
            intent = new UpkeepIntent(
                new RequestId(AtomicTextFile.Unescape(fields[1])),
                (UpkeepStep)step,
                AtomicTextFile.Unescape(fields[3]),
                count);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static RecordLoadReport Empty(RecordLoadOutcome outcome, string? notice) =>
        new RecordLoadReport(
            outcome, IntroductionStage.Unmet, IntroductionStage.Unmet,
            Array.Empty<Designation>(), Array.Empty<UpkeepIntent>(), 0, string.Empty, 0, notice);

    /// <summary>Round-trippable, so a designation read back is the one written.
    /// </summary>
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !float.IsNaN(value) && !float.IsInfinity(value);
}
