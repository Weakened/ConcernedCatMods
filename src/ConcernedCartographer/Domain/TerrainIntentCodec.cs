using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Pure serializer/parser for the per-world terrain-intent
/// sidecar (<c>&lt;world-uid&gt;.terrain-intent.tsv</c>, DEF-v1.0-005).
///
/// Format v1: a header row <c>cc-terrain-intent&lt;TAB&gt;v1</c> followed
/// by one <c>cell&lt;TAB&gt;cx&lt;TAB&gt;cz</c> row per excluded 1 m cell.
/// Malformed rows are skipped and counted. A file with an unknown header
/// (newer format or foreign file) loads as EMPTY — the mask is derived
/// safety data, so degrading to "no exclusions" for one session (and
/// rewriting v1 on the next save) is the documented downgrade path; it can
/// re-ink at worst, never corrupt user-authored data.</summary>
internal static class TerrainIntentCodec
{
    public const string HeaderName = "cc-terrain-intent";
    public const string Version = "v1";

    public sealed class ParseResult
    {
        public ParseResult(TerrainIntentMask mask, int malformedRows, bool unsupportedVersion)
        {
            Mask = mask;
            MalformedRows = malformedRows;
            UnsupportedVersion = unsupportedVersion;
        }

        public TerrainIntentMask Mask { get; }
        public int MalformedRows { get; }
        public bool UnsupportedVersion { get; }
    }

    public static IEnumerable<string> Serialize(TerrainIntentMask mask)
    {
        yield return HeaderName + "\t" + Version;
        foreach ((int cx, int cz) in mask.Cells)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "cell\t{0}\t{1}", cx, cz);
        }
    }

    public static ParseResult Parse(IEnumerable<string> lines)
    {
        var cells = new List<(int, int)>();
        int malformed = 0;
        bool headerSeen = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!headerSeen)
            {
                headerSeen = true;
                string[] header = line.Split('\t');
                if (header.Length < 2 || header[0] != HeaderName || header[1] != Version)
                {
                    return new ParseResult(new TerrainIntentMask(), 0, unsupportedVersion: true);
                }

                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length == 3 && fields[0] == "cell" &&
                int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cx) &&
                int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cz) &&
                cells.Count < TerrainIntentMask.DefaultMaxCells)
            {
                cells.Add((cx, cz));
            }
            else
            {
                malformed++;
            }
        }

        return new ParseResult(new TerrainIntentMask(cells), malformed, unsupportedVersion: false);
    }
}
