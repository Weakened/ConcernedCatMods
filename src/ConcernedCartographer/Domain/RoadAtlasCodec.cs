using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure serialization of the v1 sidecar TSV format. No file IO, no
/// game or BepInEx dependencies, so every rule is unit-testable.</summary>
internal static class RoadAtlasCodec
{
    public const string Header = "# ConcernedCartographer roads v1";
    private const string RowMarker = "1";

    public sealed class ParseResult
    {
        public ParseResult(List<RoadStroke> strokes, int malformedRows)
        {
            Strokes = strokes;
            MalformedRows = malformedRows;
        }

        public List<RoadStroke> Strokes { get; }
        public int MalformedRows { get; }
    }

    public static ParseResult Parse(IEnumerable<string> lines)
    {
        var orderedStrokes = new List<RoadStroke>();
        var strokesById = new Dictionary<Guid, RoadStroke>();
        int malformedRows = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 7 ||
                !Guid.TryParse(parts[0], out Guid strokeId) ||
                !Enum.TryParse(parts[1], ignoreCase: true, out RoadKind kind) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pointIndex) ||
                !TryParseFloat(parts[3], out float x) ||
                !TryParseFloat(parts[4], out float y) ||
                !TryParseFloat(parts[5], out float z) ||
                parts[6] != RowMarker)
            {
                malformedRows++;
                continue;
            }

            if (!strokesById.TryGetValue(strokeId, out RoadStroke? stroke))
            {
                stroke = new RoadStroke(strokeId, kind);
                strokesById.Add(strokeId, stroke);
                orderedStrokes.Add(stroke);
            }

            if (stroke.Kind != kind || pointIndex != stroke.Points.Count)
            {
                malformedRows++;
                continue;
            }

            stroke.Points.Add(new RoadPoint(x, y, z));
        }

        orderedStrokes.RemoveAll(stroke => stroke.Points.Count == 0);
        return new ParseResult(orderedStrokes, malformedRows);
    }

    public static IEnumerable<string> Serialize(IEnumerable<RoadStroke> strokes)
    {
        yield return Header;

        foreach (RoadStroke stroke in strokes)
        {
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
                    RowMarker);
            }
        }
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
