using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop.Presence;

/// <summary>What one companion's own product knows about them right now. Four
/// booleans and an optional position, and nothing that is a decision the asking
/// side could influence.</summary>
internal readonly struct PresenceFacts
{
    public PresenceFacts(bool known, bool present, bool visible, bool available)
        : this(known, present, visible, available, false, 0f, 0f, 0f)
    {
    }

    public PresenceFacts(
        bool known, bool present, bool visible, bool available,
        bool hasPosition, float x, float y, float z)
    {
        Known = known;
        Present = present;
        Visible = visible;
        Available = available;
        HasPosition = hasPosition;
        X = x;
        Y = y;
        Z = z;
    }

    public bool Known { get; }

    public bool Present { get; }

    public bool Visible { get; }

    public bool Available { get; }

    public bool HasPosition { get; }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    /// <summary>Nobody by that name lives here. A true answer, not an error:
    /// a consumer asking about somebody else's companion should get a solo
    /// survey rather than a failure to handle.</summary>
    public static PresenceFacts Nobody => new(false, false, false, false);
}

/// <summary>The whole of <c>concernedcat.presence/1</c> on the provider's side,
/// with no game types in it.
///
/// The plugin adapter's entire job is to read four booleans off its own state
/// and hand them here. Op dispatch, the major check, the malformed cases and
/// the exact reply shape all live in this file — which is the file both shipped
/// products compile and the file the two-assembly test exercises. A protocol
/// that lived in the Unity-bound adapter could only be tested by a harness that
/// re-implemented it, and a harness that re-implements the thing under test
/// proves nothing about the thing that ships.</summary>
internal static class PresenceResponder
{
    /// <summary>Answers one request. Never throws for a malformed request;
    /// <paramref name="facts"/> throwing is the caller's to catch, because only
    /// the caller knows whether that is a bug worth logging.</summary>
    /// <param name="request">The consumer's payload.</param>
    /// <param name="providerVersion">This product's version.</param>
    /// <param name="ready">False while there is no runtime to ask — no world,
    /// shutting down. Answers Unavailable without calling
    /// <paramref name="facts"/>.</param>
    /// <param name="facts">Called only for a well-formed describe of a
    /// companion this product might have.</param>
    public static IReadOnlyDictionary<string, string> Answer(
        IReadOnlyDictionary<string, string>? request,
        string providerVersion,
        bool ready,
        Func<string, PresenceFacts> facts)
    {
        if (facts == null)
        {
            throw new ArgumentNullException(nameof(facts));
        }

        string version = string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion;

        if (request == null ||
            !request.TryGetValue(PresenceContract.Keys.Op, out string? op) ||
            string.IsNullOrEmpty(op))
        {
            return PresenceContract.Reply(PresenceContract.Statuses.BadRequest, version, "no op");
        }

        if (request.TryGetValue(PresenceContract.Keys.ContractMajor, out string? major) &&
            !string.Equals(major, MajorText, StringComparison.Ordinal))
        {
            return PresenceContract.Reply(
                PresenceContract.Statuses.Unsupported, version, "this provider speaks major " + MajorText);
        }

        if (string.Equals(op, PresenceContract.Ops.Hello, StringComparison.Ordinal))
        {
            return PresenceContract.Reply(PresenceContract.Statuses.Ok, version);
        }

        if (!string.Equals(op, PresenceContract.Ops.DescribeCompanion, StringComparison.Ordinal))
        {
            return PresenceContract.Reply(PresenceContract.Statuses.Unsupported, version, op);
        }

        if (!request.TryGetValue(PresenceContract.Keys.CompanionId, out string? companion) ||
            string.IsNullOrEmpty(companion))
        {
            return PresenceContract.Reply(PresenceContract.Statuses.BadRequest, version, "no companion id");
        }

        if (!ready)
        {
            return PresenceContract.Reply(PresenceContract.Statuses.Unavailable, version, "no runtime");
        }

        return Describe(companion!, facts(companion!), version);
    }

    public static IReadOnlyDictionary<string, string> Describe(
        string companionId, PresenceFacts facts, string providerVersion)
    {
        var reply = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PresenceContract.Keys.Status] = PresenceContract.Statuses.Ok,
            [PresenceContract.Keys.ContractMajor] = MajorText,
            [PresenceContract.Keys.ContractMinor] =
                PresenceContract.Minor.ToString(CultureInfo.InvariantCulture),
            [PresenceContract.Keys.ProviderVersion] =
                string.IsNullOrEmpty(providerVersion) ? "unknown" : providerVersion,
            [PresenceContract.Keys.CompanionId] = companionId ?? string.Empty,
            [PresenceContract.Keys.Known] = PresenceContract.Bool(facts.Known),
            [PresenceContract.Keys.Present] = PresenceContract.Bool(facts.Present),
            [PresenceContract.Keys.Visible] = PresenceContract.Bool(facts.Visible),
            [PresenceContract.Keys.Available] = PresenceContract.Bool(facts.Available),
        };

        if (facts.HasPosition)
        {
            reply[PresenceContract.Keys.Position] = PresenceContract.Point(facts.X, facts.Y, facts.Z);
        }

        return reply;
    }

    private static string MajorText =>
        PresenceContract.Major.ToString(CultureInfo.InvariantCulture);
}
