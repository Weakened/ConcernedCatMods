using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Route sidecar codec (v1): each route serializes as one meta row
/// plus its point rows, all stamped with the route's revision. Snapshot and
/// journal share the format; parsing keeps, per identity, only the rows of
/// the highest revision seen, so replay is idempotent and a truncated
/// trailing line costs at most itself.</summary>
internal static class RouteCodec
{
    public const string Header = "# ConcernedCartographer routes v2";
    private const string RowMarker = "1";
    private const string MetaMarkerV2 = "2";
    private const string MetaTag = "M";
    private const string PointTag = "P";
    private const int MetaFieldCountV1 = 17;
    private const int MetaFieldCountV2 = 19;

    public sealed class ParseResult
    {
        public ParseResult(List<AtlasRoute> routes, int malformedRows, int supersededRows)
        {
            Routes = routes;
            MalformedRows = malformedRows;
            SupersededRows = supersededRows;
        }

        public List<AtlasRoute> Routes { get; }
        public int MalformedRows { get; }
        public int SupersededRows { get; }
    }

    public static IEnumerable<string> Serialize(IEnumerable<AtlasRoute> routes)
    {
        yield return Header;
        foreach (AtlasRoute route in routes)
        {
            foreach (string line in SerializeRoute(route))
            {
                yield return line;
            }
        }
    }

    /// <summary>All rows of one route at its current revision (used for
    /// journal appends).</summary>
    public static IEnumerable<string> SerializeRoute(AtlasRoute route)
    {
        string revision = route.Revision.ToString(CultureInfo.InvariantCulture);
        yield return string.Join(
            "\t",
            route.Id.ToString(),
            revision,
            route.CreatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            route.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            MetaTag,
            AtlasText.Escape(route.Name),
            ((int)route.Kind).ToString(CultureInfo.InvariantCulture),
            ((int)route.Style).ToString(CultureInfo.InvariantCulture),
            ((int)route.Status).ToString(CultureInfo.InvariantCulture),
            route.ColorArgb?.ToString(CultureInfo.InvariantCulture) ?? "",
            AtlasText.Escape(route.Notes),
            ((int)route.Scope).ToString(CultureInfo.InvariantCulture),
            route.Locked ? "1" : "0",
            route.Archived ? "1" : "0",
            route.Deleted ? "1" : "0",
            route.DeletedUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            AtlasText.Escape(route.OwnerAuthor),
            AtlasText.Escape(route.LastAuthor),
            MetaMarkerV2);

        for (int index = 0; index < route.Points.Count; index++)
        {
            RoadPoint point = route.Points[index];
            yield return string.Join(
                "\t",
                route.Id.ToString(),
                revision,
                index.ToString(CultureInfo.InvariantCulture),
                point.X.ToString("R", CultureInfo.InvariantCulture),
                point.Y.ToString("R", CultureInfo.InvariantCulture),
                point.Z.ToString("R", CultureInfo.InvariantCulture),
                PointTag,
                RowMarker);
        }
    }

    public static ParseResult Parse(IEnumerable<string> lines)
    {
        var buckets = new Dictionary<Guid, Bucket>();
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

            string[] parts = line.Split('\t');
            if (parts.Length < 8 ||
                (parts[parts.Length - 1] != RowMarker && parts[parts.Length - 1] != MetaMarkerV2) ||
                !AtlasId.TryParse(parts[0], out AtlasId id) ||
                !string.Equals(id.Kind, AtlasId.RouteKind, StringComparison.Ordinal) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long revision) ||
                revision < 1)
            {
                malformed++;
                continue;
            }

            if (!buckets.TryGetValue(id.Value, out Bucket? bucket))
            {
                bucket = new Bucket();
                buckets.Add(id.Value, bucket);
                order.Add(id.Value);
            }

            if (revision < bucket.Revision)
            {
                superseded++;
                continue;
            }

            if (revision > bucket.Revision)
            {
                if (bucket.Revision > 0)
                {
                    superseded++;
                }

                bucket.Reset(revision);
            }

            if ((parts.Length == MetaFieldCountV1 || parts.Length == MetaFieldCountV2) && parts[4] == MetaTag)
            {
                if (!TryParseMeta(parts, id, revision, out AtlasRoute meta))
                {
                    malformed++;
                    continue;
                }

                bucket.Meta = meta;
            }
            else if (parts.Length == 8 && parts[6] == PointTag && parts[7] == RowMarker)
            {
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 ||
                    !TryParseFloat(parts[3], out float x) ||
                    !TryParseFloat(parts[4], out float y) ||
                    !TryParseFloat(parts[5], out float z))
                {
                    malformed++;
                    continue;
                }

                bucket.Points.Add((index, new RoadPoint(x, y, z)));
            }
            else
            {
                malformed++;
            }
        }

        var routes = new List<AtlasRoute>();
        foreach (Guid key in order)
        {
            Bucket bucket = buckets[key];
            if (bucket.Meta is null)
            {
                malformed++;
                continue;
            }

            bucket.Points.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach ((int _, RoadPoint point) in bucket.Points)
            {
                bucket.Meta.Points.Add(point);
            }

            routes.Add(bucket.Meta);
        }

        return new ParseResult(routes, malformed, superseded);
    }

    private static bool TryParseMeta(string[] parts, AtlasId id, long revision, out AtlasRoute route)
    {
        route = null!;
        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long created) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long modified) ||
            !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind) ||
            !Enum.IsDefined(typeof(RouteKind), kind) ||
            !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int style) ||
            !Enum.IsDefined(typeof(RouteStyle), style) ||
            !int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int status) ||
            !Enum.IsDefined(typeof(RouteStatus), status) ||
            !TryParseNullableInt(parts[9], out int? colorArgb) ||
            !int.TryParse(parts[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int scope) ||
            !Enum.IsDefined(typeof(AtlasScope), scope) ||
            !TryParseFlag(parts[12], out bool locked) ||
            !TryParseFlag(parts[13], out bool archived) ||
            !TryParseFlag(parts[14], out bool deleted) ||
            !TryParseNullableTicks(parts[15], out DateTime? deletedUtc))
        {
            return false;
        }

        bool hasAuthors = parts.Length == MetaFieldCountV2;
        route = new AtlasRoute(id)
        {
            Revision = revision,
            OwnerAuthor = hasAuthors ? AtlasText.Unescape(parts[16]) : "",
            LastAuthor = hasAuthors ? AtlasText.Unescape(parts[17]) : "",
            CreatedUtc = new DateTime(created, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(modified, DateTimeKind.Utc),
            Name = AtlasText.Unescape(parts[5]),
            Kind = (RouteKind)kind,
            Style = (RouteStyle)style,
            Status = (RouteStatus)status,
            ColorArgb = colorArgb,
            Notes = AtlasText.Unescape(parts[10]),
            Scope = (AtlasScope)scope,
            Locked = locked,
            Archived = archived,
            Deleted = deleted,
            DeletedUtc = deletedUtc,
        };
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

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private sealed class Bucket
    {
        public long Revision { get; private set; }
        public AtlasRoute? Meta { get; set; }
        public List<(int Index, RoadPoint Point)> Points { get; } = new();

        public void Reset(long revision)
        {
            Revision = revision;
            Meta = null;
            Points.Clear();
        }
    }
}
