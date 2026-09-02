using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pure TSV codec for the per-world rejected-observation list
/// (RC11 blocker 9). Rows: prefab, name, icon, category, x, y, z,
/// rejected-utc-ticks, marker "1". No file IO; tabs/newlines in names are
/// flattened on write so a row can never split.</summary>
internal static class SurveyRejectedCodec
{
    public const string Header = "# ConcernedCartographer survey-rejected v1";
    private const string RowMarkerV1 = "1";

    public static IEnumerable<string> Serialize(IEnumerable<SurveyEngine.RejectedObservation> entries)
    {
        yield return Header;
        foreach (SurveyEngine.RejectedObservation entry in entries)
        {
            yield return string.Join(
                "\t",
                Flatten(entry.PrefabName),
                Flatten(entry.SuggestedName),
                Flatten(entry.IconId),
                Flatten(entry.Category),
                entry.Position.X.ToString("R", CultureInfo.InvariantCulture),
                entry.Position.Y.ToString("R", CultureInfo.InvariantCulture),
                entry.Position.Z.ToString("R", CultureInfo.InvariantCulture),
                entry.RejectedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                RowMarkerV1);
        }
    }

    public static List<SurveyEngine.RejectedObservation> Parse(IEnumerable<string> lines, out int malformedRows)
    {
        var entries = new List<SurveyEngine.RejectedObservation>();
        malformedRows = 0;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 9 ||
                parts[0].Trim().Length == 0 ||
                parts[8] != RowMarkerV1 ||
                !TryParseFloat(parts[4], out float x) ||
                !TryParseFloat(parts[5], out float y) ||
                !TryParseFloat(parts[6], out float z) ||
                !long.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ||
                ticks < 0 || ticks > DateTime.MaxValue.Ticks)
            {
                malformedRows++;
                continue;
            }

            entries.Add(new SurveyEngine.RejectedObservation(
                parts[0], parts[1], parts[2], parts[3],
                new RoadPoint(x, y, z),
                new DateTime(ticks, DateTimeKind.Utc)));
        }

        return entries;
    }

    private static string Flatten(string value)
    {
        return value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
            !float.IsNaN(result) && !float.IsInfinity(result);
    }
}
