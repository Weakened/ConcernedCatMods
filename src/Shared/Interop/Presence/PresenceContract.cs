using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.Presence;

/// <summary><c>concernedcat.presence</c> major 1: Concerned Cartographer is the
/// provider, and anything that wants to know whether one of its companions is
/// actually here is a consumer. Normative text:
/// <c>docs/settlement/cart-and-collection/SPEC.md</c> GATHER-03.
///
/// <b>This contract is read-only in the strongest sense.</b> There is no op
/// that moves a companion, changes a setting, starts anything or writes
/// anything. A consumer learns where somebody is and whether they are free; it
/// cannot ask them to do a thing. That is the whole point of GATHER-03's
/// <i>"a busy or absent Hulgi is never cloned or credited"</i> — the only way
/// to be sure nobody is credited with work they did not do is for the credit to
/// depend on an answer the companion's own product gives, and for the asking
/// side to have no way to make that answer true.
///
/// Only mscorlib types cross, like every capability (see
/// <see cref="CapabilityMap"/>): each product compiles this file into its own
/// assembly, so a shared class would be two unrelated CLR types.</summary>
internal static class PresenceContract
{
    public const string Id = "concernedcat.presence";
    public const int Major = 1;
    public const int Minor = 0;

    /// <summary>The provider plugin's BepInEx GUID.</summary>
    public const string ProviderGuid = "com.theconcernedcat.valheim.concernedcartographer";

    /// <summary>Companion ids, which are stable keys and never display names.
    /// A display name is editable and localized; this is not.</summary>
    internal static class Companions
    {
        public const string Hulgi = "hulgi";
    }

    internal static class Ops
    {
        /// <summary>Handshake: contract versions and the provider's own
        /// version. Carries no companion.</summary>
        public const string Hello = "hello";

        /// <summary>Where one companion is, and whether they are free. The only
        /// other op there is.</summary>
        public const string DescribeCompanion = "describeCompanion";
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
        public const string Detail = "detail";

        public const string CompanionId = "companionId";

        /// <summary>The player has this companion at all: recruited, and the
        /// product's own feature gate is open.</summary>
        public const string Known = "known";

        /// <summary>A body exists in the world right now.</summary>
        public const string Present = "present";

        /// <summary>Presentation is switched on. A hidden companion is still
        /// known — hiding him never revokes anything (#264) — but he is not
        /// here to help with anything.</summary>
        public const string Visible = "visible";

        /// <summary>Present, visible, and not in the middle of something. This
        /// is the only key a consumer should gate credit on.</summary>
        public const string Available = "available";

        /// <summary>Where the body actually is, as <c>x;y;z</c>. Absent when
        /// there is no body.</summary>
        public const string Position = "position";
    }

    internal static class Statuses
    {
        public const string Ok = "ok";

        /// <summary>The op or the contract major is not one this provider
        /// knows.</summary>
        public const string Unsupported = "unsupported";

        /// <summary>The provider is here but cannot answer right now: no world,
        /// shutting down, companions switched off entirely.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The request was malformed — no op, no companion id.
        /// </summary>
        public const string BadRequest = "badRequest";

        /// <summary>The provider caught its own exception. A consumer treats
        /// this exactly like <see cref="Unavailable"/>; it is separate so a log
        /// can tell a bug from an absence.</summary>
        public const string ProviderError = "providerError";
    }

    public static string Bool(bool value) => value ? "true" : "false";

    public static bool ReadBool(IReadOnlyDictionary<string, string>? reply, string key)
    {
        return reply != null
            && reply.TryGetValue(key, out string? raw)
            && string.Equals(raw, "true", StringComparison.Ordinal);
    }

    public static string? Read(IReadOnlyDictionary<string, string>? reply, string key)
    {
        return reply != null && reply.TryGetValue(key, out string? raw) ? raw : null;
    }

    public static string Point(float x, float y, float z)
    {
        return x.ToString("R", CultureInfo.InvariantCulture) + ";" +
            y.ToString("R", CultureInfo.InvariantCulture) + ";" +
            z.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses <c>x;y;z</c>. False for anything else, including a
    /// partially numeric one — a position that cannot be read is not a
    /// position near the origin.</summary>
    public static bool TryPoint(string? raw, out float x, out float y, out float z)
    {
        x = 0f;
        y = 0f;
        z = 0f;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        string[] parts = raw!.Split(';');
        return parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }

    public static IReadOnlyDictionary<string, string> Request(string op, string? companionId, string consumerVersion)
    {
        var request = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Op] = op ?? string.Empty,
            [Keys.ContractMajor] = Major.ToString(CultureInfo.InvariantCulture),
            [Keys.ContractMinor] = Minor.ToString(CultureInfo.InvariantCulture),
            [Keys.ConsumerVersion] = consumerVersion ?? "unknown",
        };

        if (!string.IsNullOrEmpty(companionId))
        {
            request[Keys.CompanionId] = companionId!;
        }

        return request;
    }

    public static IReadOnlyDictionary<string, string> Reply(string status, string providerVersion, string? reason = null)
    {
        var reply = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Status] = status,
            [Keys.ContractMajor] = Major.ToString(CultureInfo.InvariantCulture),
            [Keys.ContractMinor] = Minor.ToString(CultureInfo.InvariantCulture),
            [Keys.ProviderVersion] = providerVersion ?? "unknown",
        };

        if (!string.IsNullOrEmpty(reason))
        {
            reply[Keys.Reason] = reason!;
        }

        return reply;
    }
}
