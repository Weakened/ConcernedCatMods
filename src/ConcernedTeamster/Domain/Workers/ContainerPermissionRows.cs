using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;

namespace TheConcernedCat.ConcernedTeamster.Domain.Workers;

/// <summary>How Teamster spells a player's container permissions on disk
/// (#374).
///
/// <b>Why the product owns the spelling.</b> The shared runtime hands out
/// <see cref="NpcContainerDecision"/> - four numbers and a flag - and names no
/// file, no header, no row tag and no schema, deliberately: a shared runtime that
/// can name a file in a role's data directory is the first-run detection bug this
/// repository has shipped twice, where a product decides whether a player is new
/// by looking for its own known files and one stranger among them grants a
/// permanent unlock to every fresh install. So the schema is here, beside the
/// product's other sidecar rows, and the library never sees it.
///
/// <b>It fails in one direction.</b> A row that cannot be read is dropped and
/// counted, never guessed at. Every drop is a forgotten permission, and a
/// forgotten permission only ever keeps an NPC out of a chest; the alternative -
/// half-reading a row - is an NPC in a chest the player never opened to it. The
/// same rule the door permissions use, for the same reason.
///
/// <b>Positions are round-tripped exactly.</b> `R` format, not a fixed number of
/// decimals: a permission is found again by matching a position within a
/// tolerance, and a value that loses its last digit every save is a value that
/// can walk out of its own tolerance one save at a time. That is the defect the
/// book's own `SetAllowance` guards against on the in-memory side by keeping the
/// original place, and this is the disk side of it.</summary>
public static class ContainerPermissionRows
{
    /// <summary>The first line of the file. A version, so a later format can be
    /// recognised rather than misread, and a comment a player who opens the file
    /// can understand.</summary>
    public const string Header = "# tcc.teamster.containers v1\tx\ty\tz\tprefab\tuse";

    private const char Separator = '\t';

    /// <summary>Every decision as rows, header first. An empty desk still writes
    /// a header: a file with nothing but a header means "nothing is enabled",
    /// which is a different fact from no file at all and worth being able to
    /// tell apart when a player asks why their chest went quiet.</summary>
    public static string Serialize(IReadOnlyList<NpcContainerDecision> decisions)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        if (decisions != null)
        {
            foreach (NpcContainerDecision decision in decisions)
            {
                text.Append(Format(decision.X)).Append(Separator)
                    .Append(Format(decision.Y)).Append(Separator)
                    .Append(Format(decision.Z)).Append(Separator)
                    .Append(decision.Prefab.ToString(CultureInfo.InvariantCulture)).Append(Separator)
                    .Append(Name(decision.Allowed)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Reads rows back, reporting how many were dropped. Null or empty
    /// content is no decisions and no drops - a fresh world, which is the
    /// ordinary case and never an error.</summary>
    public static List<NpcContainerDecision> Deserialize(string? content, out int dropped)
    {
        var decisions = new List<NpcContainerDecision>();
        dropped = 0;
        if (string.IsNullOrEmpty(content))
        {
            return decisions;
        }

        foreach (string raw in content!.Split('\n'))
        {
            string line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (TryParse(line, out NpcContainerDecision decision))
            {
                decisions.Add(decision);
            }
            else
            {
                dropped++;
            }
        }

        return decisions;
    }

    /// <summary>One row, or false. Anything unrecognised is a drop: a missing
    /// field, a number that will not parse, a non-finite coordinate, a use this
    /// version does not know, or <c>off</c> - which is the absence of a record
    /// and never a record of its own.</summary>
    public static bool TryParse(string? line, out NpcContainerDecision decision)
    {
        decision = default;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        string[] fields = line!.Split(Separator);
        if (fields.Length != 5)
        {
            return false;
        }

        if (!TryNumber(fields[0], out float x)
            || !TryNumber(fields[1], out float y)
            || !TryNumber(fields[2], out float z))
        {
            return false;
        }

        if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prefab))
        {
            return false;
        }

        if (!TryUse(fields[4], out NpcContainerUse use))
        {
            return false;
        }

        decision = new NpcContainerDecision(x, y, z, prefab, use);
        return true;
    }

    /// <summary>The four states as a player would read them. Lower case and
    /// spelled out rather than a bit pattern, because this file is one a player
    /// can open, and because an unrecognised WORD is obviously a drop where an
    /// unrecognised NUMBER looks like a permission.</summary>
    public static string Name(NpcContainerUse use)
    {
        switch (use)
        {
            case NpcContainerUse.Take:
                return "take";
            case NpcContainerUse.Deposit:
                return "deposit";
            case NpcContainerUse.Both:
                return "both";
            default:
                return "off";
        }
    }

    private static bool TryUse(string field, out NpcContainerUse use)
    {
        switch (field.Trim().ToLowerInvariant())
        {
            case "take":
                use = NpcContainerUse.Take;
                return true;
            case "deposit":
                use = NpcContainerUse.Deposit;
                return true;
            case "both":
                use = NpcContainerUse.Both;
                return true;
            default:
                // `off` included: off is the absence of a record, so a row
                // claiming it is a row that should not exist.
                use = NpcContainerUse.Off;
                return false;
        }
    }

    private static bool TryNumber(string field, out float value)
    {
        if (!float.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // A position nobody could compute can never be matched again, so a row
        // carrying one is a permission nothing could ever find.
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string Format(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}
