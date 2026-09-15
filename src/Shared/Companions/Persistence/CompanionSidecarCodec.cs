using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace TheConcernedCat.Companions.Persistence;

/// <summary>Pure serialization of the companion sidecar.
///
/// The format is line oriented and tab separated, matching the rest of this
/// repository's sidecars. Every file carries its schema version and its own
/// scope, which is what makes the two dangerous cases detectable rather than
/// silent: a file written by a newer build, and a file that belongs to a
/// different character or world.
///
/// Rows this build does not understand are never dropped. They are carried
/// verbatim and re-emitted on save, so running an older build once cannot
/// quietly erase progress a newer one recorded.</summary>
internal static class CompanionSidecarCodec
{
    public const string Header = "# ConcernedCatMods companions";

    /// <summary>The newest schema this build can write and fully read.</summary>
    public const int SchemaVersion = 1;

    private const char FieldSeparator = '\t';
    private const string SchemaRow = "s";
    private const string ScopeRow = "k";
    private const string QuestRow = "q";
    private const string UnlockRow = "u";
    private const int QuestFieldCount = 5;

    public sealed class ParseResult
    {
        public ParseResult(CompanionSidecar sidecar, SidecarLoadOutcome outcome, int skippedRows)
        {
            Sidecar = sidecar;
            Outcome = outcome;
            SkippedRows = skippedRows;
        }

        public CompanionSidecar Sidecar { get; }
        public SidecarLoadOutcome Outcome { get; }

        /// <summary>Rows that could not be parsed. They are still carried into
        /// <see cref="CompanionSidecar.ForwardLines"/> so nothing is lost.</summary>
        public int SkippedRows { get; }
    }

    public static IEnumerable<string> Serialize(CompanionSidecar sidecar)
    {
        if (sidecar == null)
        {
            throw new ArgumentNullException(nameof(sidecar));
        }

        yield return Header + " v" + SchemaVersion.ToString(CultureInfo.InvariantCulture);
        yield return SchemaRow + FieldSeparator + SchemaVersion.ToString(CultureInfo.InvariantCulture);
        yield return string.Join(
            FieldSeparator.ToString(),
            new[]
            {
                ScopeRow,
                sidecar.Scope.Product.Value,
                sidecar.Scope.World.ToStorageToken(),
                sidecar.Scope.Character.ToStorageToken(),
            });

        if (sidecar.GrantedReason != UnlockReason.NotUnlocked)
        {
            yield return UnlockRow + FieldSeparator +
                ((int)sidecar.GrantedReason).ToString(CultureInfo.InvariantCulture);
        }

        foreach (CompanionQuestRecord record in sidecar.Quests)
        {
            yield return SerializeQuest(record);
        }

        // Anything a newer build wrote goes back out unchanged, after the rows
        // this build owns, so the newer build finds it again intact.
        foreach (string line in sidecar.ForwardLines)
        {
            yield return line;
        }
    }

    public static string SerializeQuest(CompanionQuestRecord record)
    {
        var fields = new List<string>(QuestFieldCount + record.UnknownFields.Count)
        {
            QuestRow,
            record.QuestId.Value,
            ((int)record.State).ToString(CultureInfo.InvariantCulture),
            record.Revision.ToString(CultureInfo.InvariantCulture),
            record.PresentationRetired ? "1" : "0",
        };

        foreach (string extra in record.UnknownFields)
        {
            fields.Add(extra);
        }

        return string.Join(FieldSeparator.ToString(), fields.ToArray());
    }

    /// <summary>Parses <paramref name="lines"/> for <paramref name="expected"/>.
    ///
    /// The expected scope is required, not inferred: the caller already knows
    /// which character and world it asked for, and a file that disagrees is a
    /// copied profile rather than data to adopt.</summary>
    public static ParseResult Parse(IEnumerable<string>? lines, CompanionScope expected)
    {
        var sidecar = new CompanionSidecar(expected);
        if (lines == null)
        {
            return new ParseResult(sidecar, SidecarLoadOutcome.Missing, 0);
        }

        var body = new List<string>();
        bool sawAnyContent = false;
        int schema = -1;
        bool scopeSeen = false;
        bool scopeMatches = false;
        int skipped = 0;

        foreach (string rawLine in lines)
        {
            if (rawLine == null)
            {
                continue;
            }

            string line = rawLine.TrimEnd('\r', '\n');
            if (line.Trim().Length == 0)
            {
                continue;
            }

            sawAnyContent = true;
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split(FieldSeparator);
            if (fields.Length >= 2 && string.Equals(fields[0], SchemaRow, StringComparison.Ordinal))
            {
                if (int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    schema = parsed;
                }

                continue;
            }

            if (fields.Length >= 4 && string.Equals(fields[0], ScopeRow, StringComparison.Ordinal))
            {
                // EVERY scope row must match, not just the last one. Appending
                // a matching row to a file that belongs to somebody else must
                // not make this build adopt it.
                scopeMatches = scopeSeen
                    ? scopeMatches && ScopeMatches(fields, expected)
                    : ScopeMatches(fields, expected);
                scopeSeen = true;
                continue;
            }

            body.Add(line);
        }

        if (!sawAnyContent)
        {
            return new ParseResult(sidecar, SidecarLoadOutcome.Missing, 0);
        }

        // No schema row, or no scope row, means this is not a companion
        // sidecar at all - a truncated write, an unrelated file, or garbage.
        if (schema < 0 || !scopeSeen)
        {
            return new ParseResult(sidecar, SidecarLoadOutcome.Corrupt, 0);
        }

        if (!scopeMatches)
        {
            return new ParseResult(sidecar, SidecarLoadOutcome.ScopeMismatch, 0);
        }

        if (schema > SchemaVersion)
        {
            // Readable enough to know we must not touch it.
            sidecar.MarkReadOnly();
            return new ParseResult(sidecar, SidecarLoadOutcome.UnsupportedSchema, 0);
        }

        foreach (string line in body)
        {
            string[] fields = line.Split(FieldSeparator);
            if (string.Equals(fields[0], UnlockRow, StringComparison.Ordinal))
            {
                if (fields.Length >= 2 &&
                    int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int reasonValue) &&
                    IsKnownReason(reasonValue))
                {
                    sidecar.RestoreGrant((UnlockReason)reasonValue);
                }
                else
                {
                    // A reason only a newer build defines, or a damaged row.
                    // Either way the row still says this scope was granted
                    // access, so it is carried verbatim and this build stands
                    // back from writing a competing one.
                    sidecar.MarkCarriedUnlockRow();
                    sidecar.AddCarriedLine(line, isForwardData: true);
                }

                continue;
            }

            if (!string.Equals(fields[0], QuestRow, StringComparison.Ordinal))
            {
                // A row kind only a newer build defines. Keep it verbatim.
                sidecar.AddCarriedLine(line, isForwardData: true);
                continue;
            }

            QuestRowOutcome rowOutcome = TryParseQuest(fields, out CompanionQuestRecord? record);
            if (rowOutcome == QuestRowOutcome.ForwardStage)
            {
                // A quest stage only a newer build defines. Carrying the row is
                // not enough on its own: this build would happily create its
                // own record for the same quest id, write it BEFORE the carried
                // row, and the newer build would then read the older row first
                // and adopt it - losing the progress the carried row was meant
                // to protect. The whole file goes read-only instead, which is
                // the same answer an unsupported schema gets and for the same
                // reason.
                sidecar.MarkReadOnly();
                sidecar.AddCarriedLine(line, isForwardData: true);
                continue;
            }

            if (rowOutcome == QuestRowOutcome.Malformed)
            {
                skipped++;
                sidecar.AddCarriedLine(line, isForwardData: false);
                continue;
            }

            if (!sidecar.Restore(record!))
            {
                // A second row for a quest already restored. Never drop it:
                // this is the one place the codec could destroy data rather
                // than carry it.
                skipped++;
                sidecar.AddCarriedLine(line, isForwardData: false);
            }
        }

        SidecarLoadOutcome outcome = skipped > 0
            ? SidecarLoadOutcome.LoadedWithSkippedRows
            : SidecarLoadOutcome.Loaded;
        return new ParseResult(sidecar, outcome, skipped);
    }

    private static bool IsKnownReason(int value)
    {
        switch ((UnlockReason)value)
        {
            case UnlockReason.QuestCompleted:
            case UnlockReason.ExistingUserData:
            case UnlockReason.AmbiguousLegacyEvidence:
            case UnlockReason.DataUnreadable:
            case UnlockReason.ToolsOnlyPreference:
            case UnlockReason.PreviouslyGranted:
                return true;
            default:
                // NotUnlocked is never written, so seeing it means a damaged
                // row rather than a meaningful absence.
                return false;
        }
    }

    private static bool ScopeMatches(string[] fields, CompanionScope expected)
    {
        if (!string.Equals(fields[1], expected.Product.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (!WorldId.TryParseStorageToken(fields[2], out WorldId world) || !world.Equals(expected.World))
        {
            return false;
        }

        return CharacterId.TryParseStorageToken(fields[3], out CharacterId character)
            && character.Equals(expected.Character);
    }

    private enum QuestRowOutcome
    {
        Parsed,

        /// <summary>Well formed, but names a quest stage only a newer schema
        /// defines.</summary>
        ForwardStage,

        Malformed,
    }

    private static QuestRowOutcome TryParseQuest(string[] fields, out CompanionQuestRecord? record)
    {
        record = null;
        if (fields.Length < QuestFieldCount)
        {
            return QuestRowOutcome.Malformed;
        }

        if (!QuestId.TryCreate(fields[1], out QuestId questId))
        {
            return QuestRowOutcome.Malformed;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stateValue))
        {
            return QuestRowOutcome.Malformed;
        }

        if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int revision))
        {
            return QuestRowOutcome.Malformed;
        }

        var state = (QuestState)stateValue;
        if (!QuestStateMachine.IsKnown(state))
        {
            // A stage only a newer build defines. Refusing to guess what it
            // means is the point: the raw row is carried forward instead, and
            // the sidecar reports forward data so the unlock policy can keep
            // this player's access without inventing progress for them.
            return QuestRowOutcome.ForwardStage;
        }

        bool retired = string.Equals(fields[4], "1", StringComparison.Ordinal);

        var extras = new List<string>();
        for (int index = QuestFieldCount; index < fields.Length; index++)
        {
            extras.Add(fields[index]);
        }

        record = CompanionQuestRecord.Restore(questId, state, revision, retired, extras);
        return QuestRowOutcome.Parsed;
    }
}
