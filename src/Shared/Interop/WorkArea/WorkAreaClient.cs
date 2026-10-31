using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.WorkArea;

/// <summary>How a consumer's question about a work area came out.</summary>
internal enum WorkAreaLookupStatus
{
    Unspecified = 0,

    /// <summary>The provider answered for this world and the boundary is a
    /// valid work area.</summary>
    Ok = 1,

    /// <summary>The provider answered for this world: no such area.</summary>
    NotFound = 2,

    /// <summary>No provider, no world on its side, an endpoint that threw or
    /// returned nothing, or a status the consumer does not know.</summary>
    Unavailable = 3,

    /// <summary>The provider answered about a different world than the one
    /// loaded here. Never used as this world's area.</summary>
    WrongWorld = 4,

    /// <summary>The provider said Ok but the reply is not a usable area (a
    /// missing field, a boundary that does not validate).</summary>
    BadReply = 5,
}

/// <summary>One area as the consumer understood it.</summary>
internal sealed class WorkAreaSummary
{
    public WorkAreaSummary(string id, string name, string purpose, string revision, WorkAreaPolygon? boundary)
    {
        Id = id;
        Name = name;
        Purpose = purpose;
        Revision = revision;
        Boundary = boundary;
    }

    public string Id { get; }

    public string Name { get; }

    public string Purpose { get; }

    /// <summary>The provider's revision of the boundary; changes when the
    /// area is redrawn.</summary>
    public string Revision { get; }

    /// <summary>The validated boundary. Present for a described area; a list
    /// entry carries none.</summary>
    public WorkAreaPolygon? Boundary { get; }
}

/// <summary>The answer to one consumer question. Exactly one of a usable
/// result or a reason.</summary>
internal sealed class WorkAreaLookup
{
    private WorkAreaLookup(WorkAreaLookupStatus status, string reason, WorkAreaSummary? area, IReadOnlyList<WorkAreaSummary> areas)
    {
        Status = status;
        Reason = reason;
        Area = area;
        Areas = areas;
    }

    public WorkAreaLookupStatus Status { get; }

    /// <summary>For the player when not Ok.</summary>
    public string Reason { get; }

    /// <summary>The described area (Ok from a describe only).</summary>
    public WorkAreaSummary? Area { get; }

    /// <summary>The listed areas (Ok from a list only; empty otherwise).</summary>
    public IReadOnlyList<WorkAreaSummary> Areas { get; }

    public bool IsOk => Status == WorkAreaLookupStatus.Ok;

    internal static WorkAreaLookup Described(WorkAreaSummary area) =>
        new(WorkAreaLookupStatus.Ok, string.Empty, area, Array.Empty<WorkAreaSummary>());

    internal static WorkAreaLookup Listed(IReadOnlyList<WorkAreaSummary> areas) =>
        new(WorkAreaLookupStatus.Ok, string.Empty, null, areas);

    internal static WorkAreaLookup Refused(WorkAreaLookupStatus status, string reason) =>
        new(status, reason, null, Array.Empty<WorkAreaSummary>());
}

/// <summary>The consumer side of <c>concernedcat.workarea/1</c> (#296 slice 3),
/// game-free so every refusal is tested against the real responder.
///
/// It only reads. It never treats a failure as an empty world: a missing
/// provider, a throwing endpoint, a reply about another world or a boundary
/// that does not validate each come back as their own status, and none of them
/// is ever turned into an area of some other shape.</summary>
internal static class WorkAreaClient
{
    public static WorkAreaLookup List(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint,
        string currentWorldUid,
        string consumerVersion)
    {
        IReadOnlyDictionary<string, string>? reply = CapabilityMap.TryCall(
            endpoint, WorkAreaContract.Request(WorkAreaContract.Ops.ListAreas, null, consumerVersion));
        if (!TryCheck(reply, currentWorldUid, out WorkAreaLookup? refusal))
        {
            return refusal!;
        }

        if (!int.TryParse(WorkAreaContract.Read(reply, WorkAreaContract.Keys.Count), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
            count < 0 || count > 1024)
        {
            return WorkAreaLookup.Refused(WorkAreaLookupStatus.BadReply, "Concerned Cartographer's list of work areas could not be read");
        }

        var areas = new List<WorkAreaSummary>(count);
        for (int index = 0; index < count; index++)
        {
            string? id = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.AreaId, index));
            string? name = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Name, index));
            string? purpose = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Purpose, index));
            string? revision = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Indexed(WorkAreaContract.Keys.Revision, index));
            if (string.IsNullOrEmpty(id) || name == null || revision == null)
            {
                return WorkAreaLookup.Refused(WorkAreaLookupStatus.BadReply, "Concerned Cartographer's list of work areas could not be read");
            }

            areas.Add(new WorkAreaSummary(id!, name, purpose ?? string.Empty, revision, null));
        }

        return WorkAreaLookup.Listed(areas);
    }

    public static WorkAreaLookup Describe(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint,
        string areaId,
        string currentWorldUid,
        string consumerVersion)
    {
        if (string.IsNullOrEmpty(areaId))
        {
            return WorkAreaLookup.Refused(WorkAreaLookupStatus.NotFound, "no work area was chosen");
        }

        IReadOnlyDictionary<string, string>? reply = CapabilityMap.TryCall(
            endpoint, WorkAreaContract.Request(WorkAreaContract.Ops.DescribeArea, areaId, consumerVersion));
        if (!TryCheck(reply, currentWorldUid, out WorkAreaLookup? refusal))
        {
            return refusal!;
        }

        string? id = WorkAreaContract.Read(reply, WorkAreaContract.Keys.AreaId);
        string? name = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Name);
        string? revision = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Revision);
        if (!string.Equals(id, areaId, StringComparison.Ordinal) || name == null || string.IsNullOrEmpty(revision))
        {
            return WorkAreaLookup.Refused(WorkAreaLookupStatus.BadReply, "Concerned Cartographer's answer about that work area could not be read");
        }

        WorkAreaShapeProblem problem = WorkAreaPolygon.TryParseWire(
            WorkAreaContract.Read(reply, WorkAreaContract.Keys.Boundary), out WorkAreaPolygon? boundary);
        if (problem != WorkAreaShapeProblem.None || boundary == null)
        {
            return WorkAreaLookup.Refused(
                WorkAreaLookupStatus.BadReply, "the work area's boundary is not usable (" + WorkAreaPolygon.Describe(problem) + ")");
        }

        return WorkAreaLookup.Described(new WorkAreaSummary(
            id!, name, WorkAreaContract.Read(reply, WorkAreaContract.Keys.Purpose) ?? string.Empty, revision!, boundary));
    }

    /// <summary>The status and world checks every reply shares. False with the
    /// refusal to return.</summary>
    private static bool TryCheck(
        IReadOnlyDictionary<string, string>? reply, string currentWorldUid, out WorkAreaLookup? refusal)
    {
        refusal = null;
        if (reply == null)
        {
            refusal = WorkAreaLookup.Refused(
                WorkAreaLookupStatus.Unavailable, "Concerned Cartographer's work areas are not available (not installed, or it did not answer)");
            return false;
        }

        string? status = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Status);
        string? reason = WorkAreaContract.Read(reply, WorkAreaContract.Keys.Reason);
        if (string.Equals(status, WorkAreaContract.Statuses.Ok, StringComparison.Ordinal) ||
            string.Equals(status, WorkAreaContract.Statuses.NotFound, StringComparison.Ordinal))
        {
            string? world = WorkAreaContract.Read(reply, WorkAreaContract.Keys.WorldUid);
            if (string.IsNullOrEmpty(currentWorldUid) || !string.Equals(world, currentWorldUid, StringComparison.Ordinal))
            {
                refusal = WorkAreaLookup.Refused(
                    WorkAreaLookupStatus.WrongWorld, "Concerned Cartographer answered about a different world than the one loaded here");
                return false;
            }

            if (string.Equals(status, WorkAreaContract.Statuses.NotFound, StringComparison.Ordinal))
            {
                refusal = WorkAreaLookup.Refused(WorkAreaLookupStatus.NotFound, "that work area no longer exists in this world");
                return false;
            }

            return true;
        }

        refusal = WorkAreaLookup.Refused(
            WorkAreaLookupStatus.Unavailable,
            "Concerned Cartographer's work areas are not available (" +
            (string.IsNullOrEmpty(status) ? "no answer" : status) + (string.IsNullOrEmpty(reason) ? string.Empty : ": " + reason) + ")");
        return false;
    }
}
