using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.WorkArea;

/// <summary><c>concernedcat.workarea</c> major 1 (#296): Concerned
/// Cartographer is the provider of player-drawn work areas, and a product that
/// runs work orders is a consumer.
///
/// <b>Read-only, and intent only.</b> The ops list the areas the player drew
/// in this world and describe one. None of them assigns a worker, starts a
/// job, changes an area or touches the world. A drawn region is the player's
/// intent; the consumer validates every order and every target itself, on the
/// host, exactly as it does for its own default circle. Drawing or saving an
/// area never starts anything.
///
/// Only mscorlib types cross, like every capability (see
/// <see cref="CapabilityMap"/>): each product compiles this file into its own
/// assembly.</summary>
internal static class WorkAreaContract
{
    public const string Id = "concernedcat.workarea";
    public const int Major = 1;
    public const int Minor = 0;

    /// <summary>The provider plugin's BepInEx GUID.</summary>
    public const string ProviderGuid = "com.theconcernedcat.valheim.concernedcartographer";

    internal static class Ops
    {
        /// <summary>Handshake: contract versions and the provider's version.
        /// </summary>
        public const string Hello = "hello";

        /// <summary>The current world's areas: count, then per index the id,
        /// name, purpose and revision. No geometry; describe one to get it.
        /// </summary>
        public const string ListAreas = "listAreas";

        /// <summary>One area by id, with its boundary.</summary>
        public const string DescribeArea = "describeArea";
    }

    /// <summary>What an area is for. Wood gathering is the only purpose in the
    /// first playable version (#296); a consumer refuses a purpose it does not
    /// know rather than guessing.</summary>
    internal static class Purposes
    {
        public const string WoodGathering = "woodGathering";
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

        /// <summary>The world the areas belong to, as the invariant world uid.
        /// A consumer compares it with its own world before trusting an answer.
        /// </summary>
        public const string WorldUid = "worldUid";

        public const string Count = "count";
        public const string AreaId = "areaId";
        public const string Name = "name";
        public const string Purpose = "purpose";

        /// <summary>The geometry fingerprint (<see cref="WorkAreaPolygon.Revision"/>).
        /// A consumer captures it with an accepted job; a different revision
        /// later means the area was edited.</summary>
        public const string Revision = "revision";

        /// <summary>The boundary as <see cref="WorkAreaPolygon.ToWire"/> text.
        /// </summary>
        public const string Boundary = "boundary";

        /// <summary>Indexed form for list replies: <c>areaId.0</c>,
        /// <c>name.0</c> and so on.</summary>
        public static string Indexed(string key, int index) =>
            key + "." + index.ToString(CultureInfo.InvariantCulture);
    }

    internal static class Statuses
    {
        public const string Ok = "ok";

        /// <summary>The op or the contract major is not one this provider
        /// knows.</summary>
        public const string Unsupported = "unsupported";

        /// <summary>No world is loaded, or the provider is shutting down. Not
        /// "no areas": a consumer must not read this as an empty list.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The request was malformed: no op, no area id.</summary>
        public const string BadRequest = "badRequest";

        /// <summary>No area with that id in this world: deleted, never drawn,
        /// or drawn in another world. A consumer pauses an order that names
        /// it; it never substitutes another area or a circle.</summary>
        public const string NotFound = "notFound";

        /// <summary>The provider caught its own exception. Treated like
        /// <see cref="Unavailable"/>.</summary>
        public const string ProviderError = "providerError";
    }

    public static IReadOnlyDictionary<string, string> Request(string op, string? areaId, string consumerVersion)
    {
        var request = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Op] = op ?? string.Empty,
            [Keys.ContractMajor] = Major.ToString(CultureInfo.InvariantCulture),
            [Keys.ContractMinor] = Minor.ToString(CultureInfo.InvariantCulture),
            [Keys.ConsumerVersion] = consumerVersion ?? "unknown",
        };

        if (!string.IsNullOrEmpty(areaId))
        {
            request[Keys.AreaId] = areaId!;
        }

        return request;
    }

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

    public static string? Read(IReadOnlyDictionary<string, string>? reply, string key)
    {
        return reply != null && reply.TryGetValue(key, out string? raw) ? raw : null;
    }
}
