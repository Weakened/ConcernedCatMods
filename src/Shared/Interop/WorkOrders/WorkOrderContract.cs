using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.WorkOrders;

/// <summary><c>concernedcat.workorders/1</c> (#296): what a worker product says
/// about the orders running in a map work area, so the map can show the
/// assigned worker, progress and the attention reason.
///
/// Read only, both ways. The ops are <c>hello</c> and <c>listOrders</c>; there
/// is no op that starts, pauses, edits or cancels anything. The provider is
/// Concerned Foreman; the consumer is Concerned Cartographer. Neither
/// references the other at compile time: only BCL dictionaries cross, through
/// <see cref="CapabilityMap"/>.</summary>
internal static class WorkOrderContract
{
    public const string Id = "concernedcat.workorders";
    public const int Major = 1;
    public const int Minor = 0;

    public const string ProviderGuid = "com.theconcernedcat.valheim.concernedforeman";

    /// <summary>A provider never lists more than this; a reply claiming more
    /// is not read.</summary>
    public const int MaxOrders = 16;

    /// <summary>Progress lines per order the consumer reads.</summary>
    public const int MaxProgressLines = 8;

    internal static class Ops
    {
        public const string Hello = "hello";

        public const string ListOrders = "listOrders";
    }

    internal static class Keys
    {
        public const string Op = "op";
        public const string ContractMajor = "contractMajor";
        public const string ContractMinor = "contractMinor";
        public const string ConsumerVersion = "consumerVersion";
        public const string ProviderVersion = "providerVersion";
        public const string Status = "status";
        public const string Reason = "reason";
        public const string WorldUid = "worldUid";
        public const string Count = "count";

        public const string AreaId = "areaId";
        public const string Worker = "worker";
        public const string StateLine = "stateLine";
        public const string ReasonLine = "reasonLine";
        public const string NeedsAttention = "needsAttention";
        public const string ProgressCount = "progressCount";
        public const string Progress = "progress";

        public static string Indexed(string key, int index) =>
            key + "." + index.ToString(CultureInfo.InvariantCulture);

        public static string Indexed(string key, int index, int line) =>
            key + "." + index.ToString(CultureInfo.InvariantCulture) + "." + line.ToString(CultureInfo.InvariantCulture);
    }

    internal static class Statuses
    {
        public const string Ok = "ok";
        public const string Unsupported = "unsupported";
        public const string Unavailable = "unavailable";
        public const string BadRequest = "badRequest";
        public const string ProviderError = "providerError";
    }

    public static IReadOnlyDictionary<string, string> Request(string op, string consumerVersion) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Op] = op ?? string.Empty,
            [Keys.ContractMajor] = Major.ToString(CultureInfo.InvariantCulture),
            [Keys.ContractMinor] = Minor.ToString(CultureInfo.InvariantCulture),
            [Keys.ConsumerVersion] = consumerVersion ?? "unknown",
        };

    public static Dictionary<string, string> Reply(string status, string providerVersion, string? reason = null)
    {
        var reply = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Status] = status,
            [Keys.ContractMajor] = Major.ToString(CultureInfo.InvariantCulture),
            [Keys.ContractMinor] = Minor.ToString(CultureInfo.InvariantCulture),
            [Keys.ProviderVersion] = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion,
        };

        if (!string.IsNullOrEmpty(reason))
        {
            reply[Keys.Reason] = reason!;
        }

        return reply;
    }

    public static string? Read(IReadOnlyDictionary<string, string>? reply, string key) =>
        reply != null && reply.TryGetValue(key, out string? raw) ? raw : null;
}

/// <summary>One order as published: the work area it runs in and the lines
/// the provider's own order panel shows.</summary>
internal sealed class PublishedWorkOrder
{
    public PublishedWorkOrder(
        string areaId, string worker, string stateLine, string reasonLine, bool needsAttention, IReadOnlyList<string> progressLines)
    {
        AreaId = areaId ?? throw new ArgumentNullException(nameof(areaId));
        Worker = worker ?? string.Empty;
        StateLine = stateLine ?? string.Empty;
        ReasonLine = reasonLine ?? string.Empty;
        NeedsAttention = needsAttention;
        ProgressLines = progressLines ?? Array.Empty<string>();
    }

    public string AreaId { get; }

    public string Worker { get; }

    public string StateLine { get; }

    public string ReasonLine { get; }

    public bool NeedsAttention { get; }

    public IReadOnlyList<string> ProgressLines { get; }
}

/// <summary>The current world's published orders.</summary>
internal sealed class PublishedWorkOrders
{
    public PublishedWorkOrders(string worldUid, IReadOnlyList<PublishedWorkOrder> orders)
    {
        WorldUid = worldUid ?? string.Empty;
        Orders = orders ?? Array.Empty<PublishedWorkOrder>();
    }

    public string WorldUid { get; }

    public IReadOnlyList<PublishedWorkOrder> Orders { get; }
}

/// <summary>The provider's side, without game types.</summary>
internal static class WorkOrderResponder
{
    /// <summary>Never throws for a malformed request. A null snapshot means
    /// "no world", which is Unavailable, never an empty list.</summary>
    public static IReadOnlyDictionary<string, string> Answer(
        IReadOnlyDictionary<string, string>? request, string providerVersion, Func<PublishedWorkOrders?> snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        string version = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;
        if (request == null || !request.TryGetValue(WorkOrderContract.Keys.Op, out string? op) || string.IsNullOrEmpty(op))
        {
            return WorkOrderContract.Reply(WorkOrderContract.Statuses.BadRequest, version, "no op");
        }

        string major = WorkOrderContract.Major.ToString(CultureInfo.InvariantCulture);
        if (request.TryGetValue(WorkOrderContract.Keys.ContractMajor, out string? asked) && !string.Equals(asked, major, StringComparison.Ordinal))
        {
            return WorkOrderContract.Reply(WorkOrderContract.Statuses.Unsupported, version, "this provider speaks major " + major);
        }

        if (string.Equals(op, WorkOrderContract.Ops.Hello, StringComparison.Ordinal))
        {
            return WorkOrderContract.Reply(WorkOrderContract.Statuses.Ok, version);
        }

        if (!string.Equals(op, WorkOrderContract.Ops.ListOrders, StringComparison.Ordinal))
        {
            return WorkOrderContract.Reply(WorkOrderContract.Statuses.Unsupported, version, op);
        }

        PublishedWorkOrders? orders = snapshot();
        if (orders == null)
        {
            return WorkOrderContract.Reply(WorkOrderContract.Statuses.Unavailable, version, "no world");
        }

        Dictionary<string, string> reply = WorkOrderContract.Reply(WorkOrderContract.Statuses.Ok, version);
        reply[WorkOrderContract.Keys.WorldUid] = orders.WorldUid;
        int count = Math.Min(orders.Orders.Count, WorkOrderContract.MaxOrders);
        reply[WorkOrderContract.Keys.Count] = count.ToString(CultureInfo.InvariantCulture);
        for (int index = 0; index < count; index++)
        {
            PublishedWorkOrder order = orders.Orders[index];
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.AreaId, index)] = order.AreaId;
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.Worker, index)] = order.Worker;
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.StateLine, index)] = order.StateLine;
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.ReasonLine, index)] = order.ReasonLine;
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.NeedsAttention, index)] = order.NeedsAttention ? "true" : "false";
            int lines = Math.Min(order.ProgressLines.Count, WorkOrderContract.MaxProgressLines);
            reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.ProgressCount, index)] = lines.ToString(CultureInfo.InvariantCulture);
            for (int line = 0; line < lines; line++)
            {
                reply[WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.Progress, index, line)] = order.ProgressLines[line] ?? string.Empty;
            }
        }

        return reply;
    }
}

internal enum WorkOrderLookupStatus
{
    Unspecified = 0,
    Ok = 1,
    Unavailable = 2,
    WrongWorld = 3,
    BadReply = 4,
}

/// <summary>The consumer's reading of one <c>listOrders</c>.</summary>
internal sealed class WorkOrderLookup
{
    private WorkOrderLookup(WorkOrderLookupStatus status, IReadOnlyList<PublishedWorkOrder> orders)
    {
        Status = status;
        Orders = orders;
    }

    public WorkOrderLookupStatus Status { get; }

    /// <summary>Empty unless Ok.</summary>
    public IReadOnlyList<PublishedWorkOrder> Orders { get; }

    public bool IsOk => Status == WorkOrderLookupStatus.Ok;

    /// <summary>The order running in this area, if the provider listed one.
    /// </summary>
    public PublishedWorkOrder? ForArea(string areaId)
    {
        foreach (PublishedWorkOrder order in Orders)
        {
            if (string.Equals(order.AreaId, areaId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        return null;
    }

    internal static WorkOrderLookup Of(WorkOrderLookupStatus status) => new(status, Array.Empty<PublishedWorkOrder>());

    internal static WorkOrderLookup Listed(IReadOnlyList<PublishedWorkOrder> orders) => new(WorkOrderLookupStatus.Ok, orders);
}

/// <summary>The consumer's side. Never throws; a failure is never an empty
/// world, and an answer about another world is never this world's.</summary>
internal static class WorkOrderClient
{
    public static WorkOrderLookup List(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint,
        string currentWorldUid,
        string consumerVersion)
    {
        IReadOnlyDictionary<string, string>? reply = CapabilityMap.TryCall(
            endpoint, WorkOrderContract.Request(WorkOrderContract.Ops.ListOrders, consumerVersion));
        if (reply == null ||
            !string.Equals(WorkOrderContract.Read(reply, WorkOrderContract.Keys.Status), WorkOrderContract.Statuses.Ok, StringComparison.Ordinal))
        {
            return WorkOrderLookup.Of(WorkOrderLookupStatus.Unavailable);
        }

        if (string.IsNullOrEmpty(currentWorldUid) ||
            !string.Equals(WorkOrderContract.Read(reply, WorkOrderContract.Keys.WorldUid), currentWorldUid, StringComparison.Ordinal))
        {
            return WorkOrderLookup.Of(WorkOrderLookupStatus.WrongWorld);
        }

        if (!TryCount(reply, WorkOrderContract.Keys.Count, WorkOrderContract.MaxOrders, out int count))
        {
            return WorkOrderLookup.Of(WorkOrderLookupStatus.BadReply);
        }

        var orders = new List<PublishedWorkOrder>(count);
        for (int index = 0; index < count; index++)
        {
            string? areaId = WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.AreaId, index));
            string? attention = WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.NeedsAttention, index));
            if (string.IsNullOrEmpty(areaId) || (attention != "true" && attention != "false") ||
                !TryCount(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.ProgressCount, index), WorkOrderContract.MaxProgressLines, out int lines))
            {
                return WorkOrderLookup.Of(WorkOrderLookupStatus.BadReply);
            }

            var progress = new List<string>(lines);
            for (int line = 0; line < lines; line++)
            {
                string? text = WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.Progress, index, line));
                if (text == null)
                {
                    return WorkOrderLookup.Of(WorkOrderLookupStatus.BadReply);
                }

                progress.Add(text);
            }

            orders.Add(new PublishedWorkOrder(
                areaId!,
                WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.Worker, index)) ?? string.Empty,
                WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.StateLine, index)) ?? string.Empty,
                WorkOrderContract.Read(reply, WorkOrderContract.Keys.Indexed(WorkOrderContract.Keys.ReasonLine, index)) ?? string.Empty,
                attention == "true",
                progress));
        }

        return WorkOrderLookup.Listed(orders);
    }

    private static bool TryCount(IReadOnlyDictionary<string, string> reply, string key, int max, out int count) =>
        int.TryParse(WorkOrderContract.Read(reply, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) &&
        count >= 0 && count <= max;
}
