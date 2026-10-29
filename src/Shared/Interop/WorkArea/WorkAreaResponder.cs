using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.WorkArea;

/// <summary>One area as the provider publishes it: a stable id, the player's
/// name for it, its purpose and a validated boundary. Nothing here can be
/// written back.</summary>
internal sealed class PublishedWorkArea
{
    public PublishedWorkArea(string id, string name, string purpose, WorkAreaPolygon boundary)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? string.Empty;
        Purpose = purpose ?? string.Empty;
        Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
    }

    public string Id { get; }

    public string Name { get; }

    public string Purpose { get; }

    public WorkAreaPolygon Boundary { get; }
}

/// <summary>The current world's published areas, or null when there is no
/// world to answer about.</summary>
internal sealed class PublishedWorkAreas
{
    public PublishedWorkAreas(string worldUid, IReadOnlyList<PublishedWorkArea> areas)
    {
        WorldUid = worldUid ?? string.Empty;
        Areas = areas ?? Array.Empty<PublishedWorkArea>();
    }

    public string WorldUid { get; }

    public IReadOnlyList<PublishedWorkArea> Areas { get; }
}

/// <summary>The whole of <c>concernedcat.workarea/1</c> on the provider's side,
/// with no game types in it: op dispatch, the major check, the malformed cases
/// and the exact reply shapes. The plugin adapter only supplies the snapshot.
/// </summary>
internal static class WorkAreaResponder
{
    /// <summary>Answers one request. Never throws for a malformed request.
    /// <paramref name="snapshot"/> returning null means "no world", which is
    /// Unavailable — never an empty list.</summary>
    public static IReadOnlyDictionary<string, string> Answer(
        IReadOnlyDictionary<string, string>? request,
        string providerVersion,
        Func<PublishedWorkAreas?> snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        string version = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;

        if (request == null ||
            !request.TryGetValue(WorkAreaContract.Keys.Op, out string? op) ||
            string.IsNullOrEmpty(op))
        {
            return WorkAreaContract.Reply(WorkAreaContract.Statuses.BadRequest, version, "no op");
        }

        if (request.TryGetValue(WorkAreaContract.Keys.ContractMajor, out string? major) &&
            !string.Equals(major, MajorText, StringComparison.Ordinal))
        {
            return WorkAreaContract.Reply(
                WorkAreaContract.Statuses.Unsupported, version, "this provider speaks major " + MajorText);
        }

        if (string.Equals(op, WorkAreaContract.Ops.Hello, StringComparison.Ordinal))
        {
            return WorkAreaContract.Reply(WorkAreaContract.Statuses.Ok, version);
        }

        bool list = string.Equals(op, WorkAreaContract.Ops.ListAreas, StringComparison.Ordinal);
        bool describe = string.Equals(op, WorkAreaContract.Ops.DescribeArea, StringComparison.Ordinal);
        if (!list && !describe)
        {
            return WorkAreaContract.Reply(WorkAreaContract.Statuses.Unsupported, version, op);
        }

        string? areaId = null;
        if (describe &&
            (!request.TryGetValue(WorkAreaContract.Keys.AreaId, out areaId) || string.IsNullOrEmpty(areaId)))
        {
            return WorkAreaContract.Reply(WorkAreaContract.Statuses.BadRequest, version, "no area id");
        }

        PublishedWorkAreas? areas = snapshot();
        if (areas == null)
        {
            return WorkAreaContract.Reply(WorkAreaContract.Statuses.Unavailable, version, "no world");
        }

        return list ? List(areas, version) : Describe(areas, areaId!, version);
    }

    private static IReadOnlyDictionary<string, string> List(PublishedWorkAreas areas, string version)
    {
        Dictionary<string, string> reply = WorkAreaContract.Reply(WorkAreaContract.Statuses.Ok, version);
        reply[WorkAreaContract.Keys.WorldUid] = areas.WorldUid;
        reply[WorkAreaContract.Keys.Count] = areas.Areas.Count.ToString(CultureInfo.InvariantCulture);
        for (int index = 0; index < areas.Areas.Count; index++)
        {
            PublishedWorkArea area = areas.Areas[index];
            reply[WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.AreaId, index)] = area.Id;
            reply[WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Name, index)] = area.Name;
            reply[WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Purpose, index)] = area.Purpose;
            reply[WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Revision, index)] = area.Boundary.Revision;
        }

        return reply;
    }

    private static IReadOnlyDictionary<string, string> Describe(
        PublishedWorkAreas areas, string areaId, string version)
    {
        foreach (PublishedWorkArea area in areas.Areas)
        {
            if (!string.Equals(area.Id, areaId, StringComparison.Ordinal))
            {
                continue;
            }

            Dictionary<string, string> reply = WorkAreaContract.Reply(WorkAreaContract.Statuses.Ok, version);
            reply[WorkAreaContract.Keys.WorldUid] = areas.WorldUid;
            reply[WorkAreaContract.Keys.AreaId] = area.Id;
            reply[WorkAreaContract.Keys.Name] = area.Name;
            reply[WorkAreaContract.Keys.Purpose] = area.Purpose;
            reply[WorkAreaContract.Keys.Revision] = area.Boundary.Revision;
            reply[WorkAreaContract.Keys.Boundary] = area.Boundary.ToWire();
            return reply;
        }

        Dictionary<string, string> missing = WorkAreaContract.Reply(
            WorkAreaContract.Statuses.NotFound, version, "no such area in this world");
        missing[WorkAreaContract.Keys.WorldUid] = areas.WorldUid;
        missing[WorkAreaContract.Keys.AreaId] = areaId;
        return missing;
    }

    private static string MajorText => WorkAreaContract.Major.ToString(CultureInfo.InvariantCulture);
}
