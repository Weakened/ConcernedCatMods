using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pure serialization of the pins sidecar (v1) and its append-only
/// journal. Snapshot and journal share one row format, so crash recovery is
/// simply parsing the snapshot lines followed by the journal lines: rows
/// resolve per identity with the highest revision winning, making replay
/// idempotent and order-safe. Malformed rows are counted and skipped
/// without discarding valid entities.</summary>
internal static class PinCodec
{
    public const string Header = "# ConcernedCartographer pins v2";
    private const string RowMarkerV1 = "1";
    private const string RowMarker = "2";
    private const int FieldCountV1 = 22;
    private const int FieldCount = 24;

    public sealed class ParseResult
    {
        public ParseResult(List<AtlasPin> pins, int malformedRows, int supersededRows)
        {
            Pins = pins;
            MalformedRows = malformedRows;
            SupersededRows = supersededRows;
        }

        public List<AtlasPin> Pins { get; }
        public int MalformedRows { get; }

        /// <summary>Rows that parsed but lost to a higher revision of the
        /// same identity (normal for journal replay).</summary>
        public int SupersededRows { get; }
    }

    public static ParseResult Parse(IEnumerable<string> lines)
    {
        var byId = new Dictionary<Guid, AtlasPin>();
        var order = new List<Guid>();
        int malformed = 0;
        int superseded = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseRow(line, out AtlasPin pin))
            {
                malformed++;
                continue;
            }

            if (byId.TryGetValue(pin.Id.Value, out AtlasPin? existing))
            {
                if (pin.Revision > existing.Revision)
                {
                    existing.CopyFrom(pin);
                }
                else
                {
                    superseded++;
                }
            }
            else
            {
                byId.Add(pin.Id.Value, pin);
                order.Add(pin.Id.Value);
            }
        }

        var pins = new List<AtlasPin>(order.Count);
        foreach (Guid key in order)
        {
            pins.Add(byId[key]);
        }

        return new ParseResult(pins, malformed, superseded);
    }

    public static IEnumerable<string> Serialize(IEnumerable<AtlasPin> pins)
    {
        yield return Header;
        foreach (AtlasPin pin in pins)
        {
            yield return SerializeRow(pin);
        }
    }

    public static string SerializeRow(AtlasPin pin)
    {
        return string.Join(
            "\t",
            pin.Id.ToString(),
            pin.Revision.ToString(CultureInfo.InvariantCulture),
            pin.CreatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            pin.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            AtlasText.Escape(pin.Name),
            AtlasText.Escape(pin.IconId),
            AtlasText.Escape(pin.Category),
            pin.ColorArgb?.ToString(CultureInfo.InvariantCulture) ?? "",
            pin.SizeScale.ToString("R", CultureInfo.InvariantCulture),
            AtlasText.Escape(pin.Notes),
            AtlasText.JoinTags(pin.Tags),
            ((int)pin.Status).ToString(CultureInfo.InvariantCulture),
            pin.Checked ? "1" : "0",
            ((int)pin.Scope).ToString(CultureInfo.InvariantCulture),
            ((int)pin.Source).ToString(CultureInfo.InvariantCulture),
            pin.Archived ? "1" : "0",
            pin.Deleted ? "1" : "0",
            pin.DeletedUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            AtlasText.Escape(pin.OwnerAuthor),
            AtlasText.Escape(pin.LastAuthor),
            pin.Position.X.ToString("R", CultureInfo.InvariantCulture),
            pin.Position.Y.ToString("R", CultureInfo.InvariantCulture),
            pin.Position.Z.ToString("R", CultureInfo.InvariantCulture),
            RowMarker);
    }

    private static bool TryParseRow(string line, out AtlasPin pin)
    {
        pin = null!;
        string[] parts = line.Split('\t');
        bool isLegacy = parts.Length == FieldCountV1 && parts[FieldCountV1 - 1] == RowMarkerV1;
        bool isCurrent = parts.Length == FieldCount && parts[FieldCount - 1] == RowMarker;
        if (!isLegacy && !isCurrent)
        {
            return false;
        }

        // Position fields sit after the optional author columns.
        int positionIndex = isCurrent ? 20 : 18;
        string ownerAuthor = isCurrent ? AtlasText.Unescape(parts[18]) : "";
        string lastAuthor = isCurrent ? AtlasText.Unescape(parts[19]) : "";

        if (!AtlasId.TryParse(parts[0], out AtlasId id) ||
            !string.Equals(id.Kind, AtlasId.PinKind, StringComparison.Ordinal) ||
            !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long revision) ||
            revision < 1 || revision > AtlasLimits.MaxRevision ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long createdTicks) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long modifiedTicks) ||
            !TryParseNullableInt(parts[7], out int? colorArgb) ||
            !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float sizeScale) ||
            !int.TryParse(parts[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int status) ||
            !Enum.IsDefined(typeof(AtlasPinStatus), status) ||
            !TryParseFlag(parts[12], out bool isChecked) ||
            !int.TryParse(parts[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out int scope) ||
            !Enum.IsDefined(typeof(AtlasScope), scope) ||
            !int.TryParse(parts[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int source) ||
            !Enum.IsDefined(typeof(AtlasPinSource), source) ||
            !TryParseFlag(parts[15], out bool archived) ||
            !TryParseFlag(parts[16], out bool deleted) ||
            !TryParseNullableTicks(parts[17], out DateTime? deletedUtc) ||
            !float.TryParse(parts[positionIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[positionIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[positionIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !AtlasLimits.IsFinite(x) || !AtlasLimits.IsFinite(y) || !AtlasLimits.IsFinite(z) ||
            !AtlasLimits.IsFinite(sizeScale))
        {
            return false;
        }

        pin = new AtlasPin(id)
        {
            Revision = revision,
            CreatedUtc = new DateTime(createdTicks, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(modifiedTicks, DateTimeKind.Utc),
            Name = AtlasLimits.Cap(AtlasText.Unescape(parts[4]), AtlasLimits.MaxNameLength),
            IconId = AtlasLimits.Cap(AtlasText.Unescape(parts[5]), AtlasLimits.MaxIconIdLength),
            Category = AtlasLimits.Cap(AtlasText.Unescape(parts[6]), AtlasLimits.MaxCategoryLength),
            ColorArgb = colorArgb,
            SizeScale = sizeScale,
            Notes = AtlasLimits.Cap(AtlasText.Unescape(parts[9]), AtlasLimits.MaxNotesLength),
            Status = (AtlasPinStatus)status,
            Checked = isChecked,
            Scope = (AtlasScope)scope,
            Source = (AtlasPinSource)source,
            Archived = archived,
            Deleted = deleted,
            DeletedUtc = deletedUtc,
            OwnerAuthor = ownerAuthor,
            LastAuthor = lastAuthor,
            Position = new RoadPoint(x, y, z),
        };
        foreach (string tag in AtlasText.SplitTags(parts[10]))
        {
            if (pin.Tags.Count >= AtlasLimits.MaxTags)
            {
                break;
            }

            pin.Tags.Add(AtlasLimits.Cap(tag, AtlasLimits.MaxTagLength));
        }

        return true;
    }

    private static bool TryParseFlag(string value, out bool flag)
    {
        flag = value == "1";
        return value == "1" || value == "0";
    }

    private static bool TryParseNullableInt(string value, out int? result)
    {
        if (value.Length == 0)
        {
            result = null;
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryParseNullableTicks(string value, out DateTime? result)
    {
        if (value.Length == 0)
        {
            result = null;
            return true;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
        {
            result = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        result = null;
        return false;
    }
}
