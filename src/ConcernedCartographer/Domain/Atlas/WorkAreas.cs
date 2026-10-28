using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.Interop.WorkArea;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>#296: a named work area the player drew. Player intent only: it
/// never starts work, never names a worker in this slice, and a consumer
/// validates every order against it on the host.</summary>
internal sealed class WorkArea
{
    public WorkArea(string id, string name, string purpose, WorkAreaPolygon boundary, DateTime createdUtc, DateTime modifiedUtc)
    {
        Id = id;
        Name = name;
        Purpose = purpose;
        Boundary = boundary;
        CreatedUtc = createdUtc;
        ModifiedUtc = modifiedUtc;
    }

    /// <summary>Stable for the life of the area, never reused, never a
    /// display name: <c>wa-</c> and 32 hex digits.</summary>
    public string Id { get; }

    public string Name { get; private set; }

    public string Purpose { get; }

    public WorkAreaPolygon Boundary { get; }

    public DateTime CreatedUtc { get; }

    public DateTime ModifiedUtc { get; private set; }

    internal void Rename(string name, DateTime nowUtc)
    {
        Name = name;
        ModifiedUtc = nowUtc;
    }
}

/// <summary>Why a request about work areas was refused, for the player.
/// </summary>
internal enum WorkAreaRefusal
{
    None = 0,
    BadName,
    DuplicateName,
    UnknownPurpose,
    BadShape,
    NotClosed,
    TooMany,
    NotFound,
}

/// <summary>The current world's work areas. Purely in memory; the runtime
/// loads and saves it through the per-world sidecar. Edits are by id, and an
/// edit of the boundary is a new area rather than a mutation, so a consumer
/// that captured an area's revision with a job can always tell it changed.
/// </summary>
internal sealed class WorkAreaStore
{
    /// <summary>Areas one world may hold. The first playable version has one
    /// worker and one active order; a few named regions is plenty.</summary>
    public const int MaxAreas = 32;

    public const int MaxNameLength = 48;

    private const string IdPrefix = "wa-";

    private readonly List<WorkArea> _areas = new();

    public IReadOnlyList<WorkArea> Areas => _areas;

    public bool IsDirty { get; private set; }

    public void MarkClean() => IsDirty = false;

    public static bool IsKnownPurpose(string? purpose) =>
        string.Equals(purpose, WorkAreaContract.Purposes.WoodGathering, StringComparison.Ordinal);

    public static bool IsValidId(string? id)
    {
        if (id == null || id.Length != IdPrefix.Length + 32 || !id.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = IdPrefix.Length; index < id.Length; index++)
        {
            char c = id[index];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A name the player can type and read back, and that survives
    /// the tab-separated sidecar unchanged.</summary>
    public static bool TryNormaliseName(string? raw, out string name)
    {
        name = (raw ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    public WorkArea? Find(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName))
        {
            return null;
        }

        foreach (WorkArea area in _areas)
        {
            if (string.Equals(area.Id, idOrName, StringComparison.Ordinal))
            {
                return area;
            }
        }

        foreach (WorkArea area in _areas)
        {
            if (string.Equals(area.Name, idOrName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return area;
            }
        }

        return null;
    }

    public WorkAreaRefusal TryCreate(
        string? name, string purpose, WorkAreaPolygon boundary, Guid idSeed, DateTime nowUtc, out WorkArea? created)
    {
        created = null;
        if (!TryNormaliseName(name, out string clean))
        {
            return WorkAreaRefusal.BadName;
        }

        if (Find(clean) != null)
        {
            return WorkAreaRefusal.DuplicateName;
        }

        if (!IsKnownPurpose(purpose))
        {
            return WorkAreaRefusal.UnknownPurpose;
        }

        if (boundary == null)
        {
            return WorkAreaRefusal.BadShape;
        }

        if (_areas.Count >= MaxAreas)
        {
            return WorkAreaRefusal.TooMany;
        }

        string id = IdPrefix + idSeed.ToString("N", CultureInfo.InvariantCulture);
        if (idSeed == Guid.Empty || Find(id) != null)
        {
            return WorkAreaRefusal.BadName;
        }

        created = new WorkArea(id, clean, purpose, boundary, nowUtc, nowUtc);
        _areas.Add(created);
        IsDirty = true;
        return WorkAreaRefusal.None;
    }

    public WorkAreaRefusal Rename(string idOrName, string? newName, DateTime nowUtc)
    {
        WorkArea? area = Find(idOrName);
        if (area == null)
        {
            return WorkAreaRefusal.NotFound;
        }

        if (!TryNormaliseName(newName, out string clean))
        {
            return WorkAreaRefusal.BadName;
        }

        WorkArea? clash = Find(clean);
        if (clash != null && !ReferenceEquals(clash, area))
        {
            return WorkAreaRefusal.DuplicateName;
        }

        area.Rename(clean, nowUtc);
        IsDirty = true;
        return WorkAreaRefusal.None;
    }

    public WorkAreaRefusal Delete(string idOrName)
    {
        WorkArea? area = Find(idOrName);
        if (area == null)
        {
            return WorkAreaRefusal.NotFound;
        }

        _areas.Remove(area);
        IsDirty = true;
        return WorkAreaRefusal.None;
    }

    /// <summary>Replaces the contents from a load. Not dirty afterwards.</summary>
    public void Load(IEnumerable<WorkArea> areas)
    {
        _areas.Clear();
        foreach (WorkArea area in areas)
        {
            if (_areas.Count >= MaxAreas)
            {
                break;
            }

            if (Find(area.Id) == null && Find(area.Name) == null)
            {
                _areas.Add(area);
            }
        }

        IsDirty = false;
    }

    public void Clear()
    {
        _areas.Clear();
        IsDirty = false;
    }

    public static string Describe(WorkAreaRefusal refusal)
    {
        return refusal switch
        {
            WorkAreaRefusal.None => "done",
            WorkAreaRefusal.BadName => "the name must be 1 to " + MaxNameLength + " characters with no tabs or line breaks",
            WorkAreaRefusal.DuplicateName => "another work area already has that name",
            WorkAreaRefusal.UnknownPurpose => "that purpose is not one this version knows (wood gathering only)",
            WorkAreaRefusal.BadShape => "the boundary is not a valid work area",
            WorkAreaRefusal.NotClosed => "the route does not end where it starts; draw a closed loop",
            WorkAreaRefusal.TooMany => "this world already has " + MaxAreas + " work areas",
            WorkAreaRefusal.NotFound => "no work area by that name or id in this world",
            _ => "refused",
        };
    }
}

/// <summary>Turns a drawn stroke into a work-area boundary, or says why it
/// cannot. A freehand loop is hundreds of points; it is simplified
/// (Ramer–Douglas–Peucker, deterministic) to at most
/// <see cref="WorkAreaPolygon.MaxPoints"/> before validation, and the result is
/// validated exactly as a consumer will validate it. A stroke that does not
/// close is refused, never closed by a straight line across whatever lies
/// between its ends.</summary>
internal static class WorkAreaDrafting
{
    /// <summary>How near the stroke's end must come back to its start, in
    /// metres, for the loop to count as closed.</summary>
    public const float CloseToleranceMetres = 12f;

    public static WorkAreaRefusal FromStroke(
        IReadOnlyList<RoadPoint>? stroke, out WorkAreaPolygon? boundary, out WorkAreaShapeProblem shapeProblem)
    {
        boundary = null;
        shapeProblem = WorkAreaShapeProblem.None;
        if (stroke == null || stroke.Count < WorkAreaPolygon.MinPoints)
        {
            shapeProblem = WorkAreaShapeProblem.TooFewPoints;
            return WorkAreaRefusal.BadShape;
        }

        var points = new List<WorkAreaPoint>(stroke.Count);
        foreach (RoadPoint point in stroke)
        {
            points.Add(new WorkAreaPoint(point.X, point.Z));
        }

        WorkAreaPoint first = points[0];
        WorkAreaPoint last = points[points.Count - 1];
        double gap = Math.Sqrt(((double)last.X - first.X) * (last.X - first.X) + ((double)last.Z - first.Z) * (last.Z - first.Z));
        if (double.IsNaN(gap) || gap > CloseToleranceMetres)
        {
            if (double.IsNaN(gap))
            {
                shapeProblem = WorkAreaShapeProblem.NotFinite;
                return WorkAreaRefusal.BadShape;
            }

            return WorkAreaRefusal.NotClosed;
        }

        List<WorkAreaPoint> simplified = Simplify(points, WorkAreaPolygon.MaxPoints);
        shapeProblem = WorkAreaPolygon.TryCreate(simplified, out boundary);
        return shapeProblem == WorkAreaShapeProblem.None ? WorkAreaRefusal.None : WorkAreaRefusal.BadShape;
    }

    /// <summary>The one living, unarchived route with this name
    /// (case-insensitive). Two routes sharing the name is ambiguous and
    /// refused rather than resolved by order.</summary>
    public static AtlasRoute? ResolveRoute(IEnumerable<AtlasRoute> living, string? name, out bool ambiguous)
    {
        ambiguous = false;
        string wanted = (name ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        AtlasRoute? match = null;
        foreach (AtlasRoute route in living)
        {
            if (route.Deleted || route.Archived ||
                !string.Equals(route.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match != null)
            {
                ambiguous = true;
                return null;
            }

            match = route;
        }

        return match;
    }

    /// <summary>Ramer–Douglas–Peucker with a tolerance that doubles from
    /// 0.5 m until the result fits. Deterministic for a given stroke.</summary>
    internal static List<WorkAreaPoint> Simplify(List<WorkAreaPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return new List<WorkAreaPoint>(points);
        }

        for (double epsilon = 0.5; epsilon < 1e6; epsilon *= 2)
        {
            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            Mark(points, 0, points.Count - 1, epsilon, keep);
            var result = new List<WorkAreaPoint>();
            for (int index = 0; index < points.Count; index++)
            {
                if (keep[index])
                {
                    result.Add(points[index]);
                }
            }

            if (result.Count <= maxPoints)
            {
                return result;
            }
        }

        return new List<WorkAreaPoint> { points[0], points[points.Count - 1] };
    }

    private static void Mark(List<WorkAreaPoint> points, int start, int end, double epsilon, bool[] keep)
    {
        var stack = new Stack<(int Start, int End)>();
        stack.Push((start, end));
        while (stack.Count > 0)
        {
            (int s, int e) = stack.Pop();
            if (e <= s + 1)
            {
                continue;
            }

            double worst = -1;
            int worstIndex = -1;
            for (int index = s + 1; index < e; index++)
            {
                double distance = Distance(points[index], points[s], points[e]);
                if (distance > worst)
                {
                    worst = distance;
                    worstIndex = index;
                }
            }

            if (worst > epsilon && worstIndex > 0)
            {
                keep[worstIndex] = true;
                stack.Push((s, worstIndex));
                stack.Push((worstIndex, e));
            }
        }
    }

    private static double Distance(WorkAreaPoint p, WorkAreaPoint a, WorkAreaPoint b)
    {
        double dx = (double)b.X - a.X;
        double dz = (double)b.Z - a.Z;
        double lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared <= 0)
        {
            double px = (double)p.X - a.X;
            double pz = (double)p.Z - a.Z;
            return Math.Sqrt((px * px) + (pz * pz));
        }

        double t = Math.Max(0, Math.Min(1, (((double)p.X - a.X) * dx + ((double)p.Z - a.Z) * dz) / lengthSquared));
        double cx = a.X + (t * dx) - p.X;
        double cz = a.Z + (t * dz) - p.Z;
        return Math.Sqrt((cx * cx) + (cz * cz));
    }
}

/// <summary>The per-world sidecar format: a versioned header, then one area
/// per tab-separated row. A row that does not parse completely, or whose
/// boundary no longer validates, is skipped and counted, never loaded as a
/// region of any other shape.</summary>
internal static class WorkAreaCodec
{
    public const string Header = "# cc-work-areas v1";

    public static IEnumerable<string> Serialize(IEnumerable<WorkArea> areas)
    {
        yield return Header;
        foreach (WorkArea area in areas)
        {
            yield return string.Join(
                "\t",
                area.Id,
                area.Name,
                area.Purpose,
                area.CreatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                area.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                area.Boundary.ToWire());
        }
    }

    /// <summary>Parses a sidecar. An unknown header version yields nothing and
    /// sets <paramref name="unsupportedVersion"/>: a newer build's file is not
    /// read by guesswork, and the runtime must then not overwrite it.</summary>
    public static List<WorkArea> Parse(IEnumerable<string> lines, out int malformed, out bool unsupportedVersion)
    {
        malformed = 0;
        unsupportedVersion = false;
        var areas = new List<WorkArea>();
        bool sawHeader = false;
        foreach (string raw in lines)
        {
            if (raw == null)
            {
                continue;
            }

            if (!sawHeader)
            {
                if (raw.Length == 0)
                {
                    continue;
                }

                if (!string.Equals(raw.TrimEnd(), Header, StringComparison.Ordinal))
                {
                    unsupportedVersion = true;
                    return new List<WorkArea>();
                }

                sawHeader = true;
                continue;
            }

            if (raw.Length == 0 || raw.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = raw.Split('\t');
            if (fields.Length != 6 ||
                !WorkAreaStore.IsValidId(fields[0]) ||
                !WorkAreaStore.TryNormaliseName(fields[1], out string name) ||
                !WorkAreaStore.IsKnownPurpose(fields[2]) ||
                !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long created) ||
                !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long modified) ||
                created < DateTime.MinValue.Ticks || created > DateTime.MaxValue.Ticks ||
                modified < DateTime.MinValue.Ticks || modified > DateTime.MaxValue.Ticks ||
                WorkAreaPolygon.TryParseWire(fields[5], out WorkAreaPolygon? boundary) != WorkAreaShapeProblem.None)
            {
                malformed++;
                continue;
            }

            areas.Add(new WorkArea(
                fields[0], name, fields[2], boundary!,
                new DateTime(created, DateTimeKind.Utc), new DateTime(modified, DateTimeKind.Utc)));
        }

        return areas;
    }
}

/// <summary>#296: the <c>cc_areas</c> console command, game-free so every
/// reply is tested. A development aid by the issue's own rule; the visible
/// map editor is the next slice. Nothing here starts work, and nothing here
/// names a worker.</summary>
internal static class WorkAreaCommands
{
    public const string Usage =
        "Usage: cc_areas [list | fromroute <route name> as <area name> | rename <area> to <new name> | delete <area>]. " +
        "A work area is a closed loop you drew as a route; drawing or saving one never starts any work.";

    public static string Execute(
        WorkAreaStore store,
        IEnumerable<AtlasRoute> livingRoutes,
        string subcommand,
        string remainder,
        Guid idSeed,
        DateTime nowUtc)
    {
        switch (subcommand)
        {
            case "":
            case "list":
                return List(store);
            case "fromroute":
                return FromRoute(store, livingRoutes, remainder, idSeed, nowUtc);
            case "rename":
            {
                if (!TrySplit(remainder, " to ", out string target, out string newName))
                {
                    return Usage;
                }

                WorkAreaRefusal refusal = store.Rename(target, newName, nowUtc);
                return refusal == WorkAreaRefusal.None
                    ? "Renamed the work area to \"" + store.Find(newName.Trim())!.Name + "\"."
                    : "Not renamed: " + WorkAreaStore.Describe(refusal) + ".";
            }

            case "delete":
            {
                WorkArea? area = store.Find(remainder.Trim());
                if (area == null)
                {
                    return "Not deleted: " + WorkAreaStore.Describe(WorkAreaRefusal.NotFound) + ".";
                }

                string name = area.Name;
                store.Delete(area.Id);
                return "Deleted the work area \"" + name + "\". Other mods asking for it are now told it no longer exists.";
            }

            default:
                return Usage;
        }
    }

    private static string List(WorkAreaStore store)
    {
        if (store.Areas.Count == 0)
        {
            return "No work areas in this world. Draw a closed loop as a route, then 'cc_areas fromroute <route name> as <area name>'.";
        }

        var lines = new List<string> { store.Areas.Count + " work area(s):" };
        foreach (WorkArea area in store.Areas)
        {
            lines.Add(
                "  \"" + area.Name + "\" - wood gathering, about " +
                Math.Round(area.Boundary.AreaSquareMetres).ToString("0", CultureInfo.InvariantCulture) + " m², " +
                area.Boundary.Points.Count.ToString(CultureInfo.InvariantCulture) + " corners [" + area.Id + "]");
        }

        return string.Join("\n", lines);
    }

    private static string FromRoute(
        WorkAreaStore store, IEnumerable<AtlasRoute> livingRoutes, string remainder, Guid idSeed, DateTime nowUtc)
    {
        if (!TrySplit(remainder, " as ", out string routeName, out string areaName))
        {
            return Usage;
        }

        AtlasRoute? route = WorkAreaDrafting.ResolveRoute(livingRoutes, routeName, out bool ambiguous);
        if (route == null)
        {
            return ambiguous
                ? "Not created: more than one route is called \"" + routeName.Trim() + "\"; rename one first."
                : "Not created: no route called \"" + routeName.Trim() + "\".";
        }

        WorkAreaRefusal drafted = WorkAreaDrafting.FromStroke(route.Points, out WorkAreaPolygon? boundary, out WorkAreaShapeProblem problem);
        if (drafted != WorkAreaRefusal.None)
        {
            return drafted == WorkAreaRefusal.BadShape
                ? "Not created: " + WorkAreaPolygon.Describe(problem) + "."
                : "Not created: " + WorkAreaStore.Describe(drafted) + ".";
        }

        WorkAreaRefusal refusal = store.TryCreate(
            areaName, WorkAreaContract.Purposes.WoodGathering, boundary!, idSeed, nowUtc, out WorkArea? created);
        if (refusal != WorkAreaRefusal.None)
        {
            return "Not created: " + WorkAreaStore.Describe(refusal) + ".";
        }

        return "Work area \"" + created!.Name + "\" saved for wood gathering (about " +
            Math.Round(created.Boundary.AreaSquareMetres).ToString("0", CultureInfo.InvariantCulture) +
            " m²). Nothing starts until you give an order.";
    }

    internal static bool TrySplit(string remainder, string separator, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;
        string text = remainder ?? string.Empty;
        int at = text.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
        if (at <= 0)
        {
            return false;
        }

        left = text.Substring(0, at).Trim();
        right = text.Substring(at + separator.Length).Trim();
        return left.Length > 0 && right.Length > 0;
    }
}
