using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection;

/// <summary>The natural source kinds this slice may collect (GATHER-03, D10).
/// </summary>
internal enum NaturalSourceKind
{
    Unspecified = 0,
    LooseStone = 1,
    Branch = 2,
}

/// <summary>One source, for one world load: its prefab, its in-session network
/// id and the epoch, plus its position so a player-facing description and a
/// sanity check survive. A key from another epoch is never trusted.</summary>
internal readonly struct SourceKey : IEquatable<SourceKey>
{
    public SourceKey(string prefabName, string sessionId, Guid worldLoadEpoch, SitePoint position)
    {
        if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("A source key needs a prefab and an in-session id.");
        }

        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A source key belongs to one world load.", nameof(worldLoadEpoch));
        }

        PrefabName = prefabName;
        SessionId = sessionId;
        WorldLoadEpoch = worldLoadEpoch;
        Position = position;
    }

    public string PrefabName { get; }

    public string SessionId { get; }

    public Guid WorldLoadEpoch { get; }

    public SitePoint Position { get; }

    public bool IsEmpty => string.IsNullOrEmpty(SessionId);

    public bool Equals(SourceKey other) =>
        string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
        WorldLoadEpoch == other.WorldLoadEpoch &&
        string.Equals(PrefabName, other.PrefabName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SourceKey other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(SessionId ?? string.Empty) * 397) ^ WorldLoadEpoch.GetHashCode());

    public override string ToString() => IsEmpty ? "<no source>" : PrefabName + "#" + SessionId;
}

/// <summary>What was seen of a source. Exhausted, unloaded, inaccessible and
/// unknown are different answers and are never merged (GATHER-03).</summary>
internal enum SourceAvailability
{
    Unspecified = 0,
    Available = 1,

    /// <summary>Already picked; waiting for the game to respawn it.</summary>
    Exhausted = 2,

    /// <summary>In scope but its ground is not loaded: unknown, not empty.
    /// </summary>
    Unloaded = 3,

    /// <summary>Loaded, but a ward or access rule forbids it.</summary>
    Inaccessible = 4,

    Unknown = 5,
}

internal enum SourceReachability
{
    Unspecified = 0,
    NotChecked = 1,
    Reachable = 2,
    Unreachable = 3,
}

/// <summary>Who surveyed. A busy or absent Hulgi is never credited.</summary>
internal enum SurveyProvenance
{
    Unspecified = 0,
    SoloForeman = 1,
    WithHulgi = 2,
}

internal sealed class SourceObservation
{
    public SourceObservation(
        SourceKey key,
        NaturalSourceKind kind,
        CollectedResource yields,
        int estimatedYield,
        SourceAvailability availability,
        SourceReachability reachability,
        int surveyRevision,
        float observedAt)
    {
        if (key.IsEmpty || kind == NaturalSourceKind.Unspecified || yields == CollectedResource.Unspecified)
        {
            throw new ArgumentException("An observation needs a key, a kind and a yield.");
        }

        Key = key;
        Kind = kind;
        Yields = yields;
        EstimatedYield = Math.Max(0, estimatedYield);
        Availability = availability;
        Reachability = reachability;
        SurveyRevision = surveyRevision;
        ObservedAt = observedAt;
    }

    public SourceKey Key { get; }

    public NaturalSourceKind Kind { get; }

    public CollectedResource Yields { get; }

    /// <summary>What the game's data says one pick gives. An estimate for
    /// planning only; progress counts what was actually received.</summary>
    public int EstimatedYield { get; }

    public SourceAvailability Availability { get; }

    public SourceReachability Reachability { get; }

    public int SurveyRevision { get; }

    public float ObservedAt { get; }
}

/// <summary>One bounded survey of one scope.</summary>
internal sealed class SurveySnapshot
{
    public SurveySnapshot(
        WorkScope scope, IReadOnlyList<SourceObservation> sources, SurveyProvenance provenance, int revision,
        bool truncatedByBudget, int unloadedCells, float takenAt)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        Provenance = provenance;
        Revision = revision;
        TruncatedByBudget = truncatedByBudget;
        UnloadedCells = unloadedCells;
        TakenAt = takenAt;
    }

    public WorkScope Scope { get; }

    public IReadOnlyList<SourceObservation> Sources { get; }

    public SurveyProvenance Provenance { get; }

    public int Revision { get; }

    /// <summary>The scan budget ran out: the snapshot is incomplete, not empty.
    /// </summary>
    public bool TruncatedByBudget { get; }

    /// <summary>Parts of the scope that were not loaded, so "no sources" there
    /// is unknown.</summary>
    public int UnloadedCells { get; }

    public float TakenAt { get; }
}

internal enum ReservationOutcome
{
    Unspecified = 0,
    Reserved = 1,
    AlreadySatisfied = 2,

    /// <summary>Another order holds the source.</summary>
    HeldByAnotherOrder = 3,

    /// <summary>The key is from another world load.</summary>
    StaleEpoch = 4,

    Released = 5,
    NotHeld = 6,
}

/// <summary>At most one order may target a source (DATA-01): two clicks, a
/// retry or two workers never claim the same stone twice. In-memory for one
/// world load; a reload drops every reservation and the survey runs again.
/// </summary>
internal sealed class SourceReservationBook
{
    private readonly Dictionary<SourceKey, OrderId> _held = new Dictionary<SourceKey, OrderId>();

    public SourceReservationBook(Guid worldLoadEpoch)
    {
        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A reservation book belongs to one world load.", nameof(worldLoadEpoch));
        }

        WorldLoadEpoch = worldLoadEpoch;
    }

    public Guid WorldLoadEpoch { get; }

    public int Count => _held.Count;

    public ReservationOutcome Reserve(SourceKey source, OrderId order)
    {
        if (source.IsEmpty || order.IsEmpty)
        {
            throw new ArgumentException("A reservation needs a source and an order.");
        }

        if (source.WorldLoadEpoch != WorldLoadEpoch)
        {
            return ReservationOutcome.StaleEpoch;
        }

        if (_held.TryGetValue(source, out OrderId holder))
        {
            return holder.Equals(order) ? ReservationOutcome.AlreadySatisfied : ReservationOutcome.HeldByAnotherOrder;
        }

        _held[source] = order;
        return ReservationOutcome.Reserved;
    }

    public ReservationOutcome Release(SourceKey source, OrderId order)
    {
        if (!_held.TryGetValue(source, out OrderId holder) || !holder.Equals(order))
        {
            return ReservationOutcome.NotHeld;
        }

        _held.Remove(source);
        return ReservationOutcome.Released;
    }

    public int ReleaseAll(OrderId order)
    {
        var mine = new List<SourceKey>();
        foreach (KeyValuePair<SourceKey, OrderId> pair in _held)
        {
            if (pair.Value.Equals(order))
            {
                mine.Add(pair.Key);
            }
        }

        foreach (SourceKey key in mine)
        {
            _held.Remove(key);
        }

        return mine.Count;
    }

    public bool IsHeldBy(SourceKey source, OrderId order) =>
        _held.TryGetValue(source, out OrderId holder) && holder.Equals(order);
}
