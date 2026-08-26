using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure serialization of the sidecar TSV format. No file IO, no
/// game or BepInEx dependencies, so every rule is unit-testable.
///
/// v1 rows (7 fields, marker "1"): id, kind, index, x, y, z, 1.
/// v2 rows (8 fields, marker "2"): id, kind, index, x, y, z, source, 2.
/// v3 rows (9 fields, marker "3"): id, kind, index, x, y, z, source, flags, 3
/// where flags is an integer bitmask (1 = hidden).
/// Parse accepts all three (v1 rows load as Traversal); Serialize always
/// writes v3. Callers use <see cref="ParseResult.LegacyRows"/> to back up a
/// v1 file before the next save rewrites it in the current format.</summary>
internal static class RoadAtlasCodec
{
    public const string Header = "# ConcernedCartographer roads v3";
    private const string RowMarkerV1 = "1";
    private const string RowMarkerV2 = "2";
    private const string RowMarkerV3 = "3";
    private const int HiddenFlag = 1;

    public sealed class ParseResult
    {
        public ParseResult(List<RoadStroke> strokes, int malformedRows, int legacyRows)
        {
            Strokes = strokes;
            MalformedRows = malformedRows;
            LegacyRows = legacyRows;
        }

        public List<RoadStroke> Strokes { get; }
        public int MalformedRows { get; }

        /// <summary>How many rows used the pre-source v1 format. Non-zero
        /// means the file predates v2 and deserves a one-time backup before
        /// it is rewritten, because a v0.1 mod cannot read v2 rows.</summary>
        public int LegacyRows { get; }
    }

    public static ParseResult Parse(IEnumerable<string> lines)
    {
        var orderedStrokes = new List<RoadStroke>();
        var strokesById = new Dictionary<Guid, RoadStroke>();
        int malformedRows = 0;
        int legacyRows = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (!TryParseRow(parts, out Guid strokeId, out RoadKind kind, out int pointIndex,
                    out RoadPoint point, out RoadObservationSource source, out bool hidden, out bool isLegacyRow))
            {
                malformedRows++;
                continue;
            }

            if (isLegacyRow)
            {
                legacyRows++;
            }

            if (!strokesById.TryGetValue(strokeId, out RoadStroke? stroke))
            {
                stroke = new RoadStroke(strokeId, kind, source) { Hidden = hidden };
                strokesById.Add(strokeId, stroke);
                orderedStrokes.Add(stroke);
            }

            if (stroke.Kind != kind || stroke.Source != source || stroke.Hidden != hidden ||
                pointIndex != stroke.Points.Count)
            {
                malformedRows++;
                continue;
            }

            stroke.Points.Add(point);
        }

        orderedStrokes.RemoveAll(stroke => stroke.Points.Count == 0);
        return new ParseResult(orderedStrokes, malformedRows, legacyRows);
    }

    public static IEnumerable<string> Serialize(IEnumerable<RoadStroke> strokes)
    {
        yield return Header;

        foreach (RoadStroke stroke in strokes)
        {
            string flags = (stroke.Hidden ? HiddenFlag : 0).ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < stroke.Points.Count; index++)
            {
                RoadPoint point = stroke.Points[index];
                yield return string.Join(
                    "\t",
                    stroke.Id.ToString("D", CultureInfo.InvariantCulture),
                    stroke.Kind.ToString(),
                    index.ToString(CultureInfo.InvariantCulture),
                    point.X.ToString("R", CultureInfo.InvariantCulture),
                    point.Y.ToString("R", CultureInfo.InvariantCulture),
                    point.Z.ToString("R", CultureInfo.InvariantCulture),
                    stroke.Source.ToString(),
                    flags,
                    RowMarkerV3);
            }
        }
    }

    private static bool TryParseRow(
        string[] parts,
        out Guid strokeId,
        out RoadKind kind,
        out int pointIndex,
        out RoadPoint point,
        out RoadObservationSource source,
        out bool hidden,
        out bool isLegacyRow)
    {
        strokeId = default;
        kind = default;
        pointIndex = default;
        point = default;
        source = RoadObservationSource.Traversal;
        hidden = false;
        isLegacyRow = parts.Length == 7;

        if (parts.Length < 7 || parts.Length > 9)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out strokeId) ||
            !Enum.TryParse(parts[1], ignoreCase: true, out kind) ||
            !Enum.IsDefined(typeof(RoadKind), kind) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out pointIndex) ||
            !TryParseFloat(parts[3], out float x) ||
            !TryParseFloat(parts[4], out float y) ||
            !TryParseFloat(parts[5], out float z))
        {
            return false;
        }

        if (parts.Length == 7)
        {
            if (parts[6] != RowMarkerV1)
            {
                return false;
            }
        }
        else
        {
            if (!Enum.TryParse(parts[6], ignoreCase: true, out source) ||
                !Enum.IsDefined(typeof(RoadObservationSource), source))
            {
                return false;
            }

            if (parts.Length == 8)
            {
                if (parts[7] != RowMarkerV2)
                {
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags) ||
                    parts[8] != RowMarkerV3)
                {
                    return false;
                }

                hidden = (flags & HiddenFlag) != 0;
            }
        }

        point = new RoadPoint(x, y, z);
        return true;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
