using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The bounded survey pipeline: candidate sightings become
/// temporary observations that the player reviews before anything turns
/// into a permanent pin. Bounds are structural — a hard observation cap, a
/// per-rule duplicate radius against both pins and other observations, a
/// stable per-object location identity, base-exclusion zones, and expiry —
/// so a broad rule can never flood the atlas. RC11 blocker 9: rejecting an
/// observation moves it to a persistent Rejected list with its identity
/// suppressed from future sweeps until the player restores (or accepts)
/// it from the Survey UI. Pure and fully tested; the scanner adapter only
/// feeds sightings.</summary>
internal sealed class SurveyEngine
{
    public sealed class Observation
    {
        public Observation(Guid id, string prefabName, string suggestedName, string iconId, string category, RoadPoint position, DateTime seenUtc, DateTime expiresUtc)
        {
            Id = id;
            PrefabName = prefabName;
            SuggestedName = suggestedName;
            IconId = iconId;
            Category = category;
            Position = position;
            SeenUtc = seenUtc;
            ExpiresUtc = expiresUtc;
        }

        public Guid Id { get; }
        public string PrefabName { get; }
        public string SuggestedName { get; }
        public string IconId { get; }
        public string Category { get; }
        public RoadPoint Position { get; }
        public DateTime SeenUtc { get; }
        public DateTime ExpiresUtc { get; }
    }

    /// <summary>A rejected observation, kept (and persisted per world)
    /// so the same physical object never spams the pending list again,
    /// and so the player can change their mind later.</summary>
    public sealed class RejectedObservation
    {
        public RejectedObservation(string prefabName, string suggestedName, string iconId, string category, RoadPoint position, DateTime rejectedUtc)
        {
            PrefabName = prefabName;
            SuggestedName = suggestedName;
            IconId = iconId;
            Category = category;
            Position = position;
            RejectedUtc = rejectedUtc;
        }

        public string PrefabName { get; }
        public string SuggestedName { get; }
        public string IconId { get; }
        public string Category { get; }
        public RoadPoint Position { get; }
        public DateTime RejectedUtc { get; }
    }

    public enum OfferResult
    {
        Added,
        NoRule,
        Blacklisted,
        DuplicatePin,
        DuplicateObservation,
        DuplicateIdentity,
        RejectedEarlier,
        InsideBaseExclusion,
        CapReached,
    }

    /// <summary>Bound on the persistent rejected list (oldest evicted).</summary>
    public const int MaxRejected = 500;

    private readonly List<Observation> _observations = new();
    private readonly List<RejectedObservation> _rejected = new();
    private readonly HashSet<string> _pendingKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedKeys = new(StringComparer.Ordinal);

    public SurveyRuleSet Rules { get; set; } = new();
    public int MaxObservations { get; set; } = 200;
    public float BaseExclusionRadiusMeters { get; set; } = 30f;

    public IReadOnlyList<Observation> Observations => _observations;

    public IReadOnlyList<RejectedObservation> Rejected => _rejected;

    /// <summary>True when the rejected list changed since the last
    /// <see cref="MarkRejectedClean"/> (drives per-world persistence).</summary>
    public bool RejectedDirty { get; private set; }

    public void MarkRejectedClean()
    {
        RejectedDirty = false;
    }

    /// <summary>The stable per-object location identity (RC11 blockers
    /// 9/13): cleaned prefab plus the world cell the object stands in.
    /// World objects do not move; repeated sweeps of the same object
    /// produce the same key.</summary>
    public static string IdentityKey(string cleanedPrefabName, RoadPoint position)
    {
        return FormattableString.Invariant(
            $"{cleanedPrefabName}|{(int)Math.Round(position.X)}|{(int)Math.Round(position.Z)}");
    }

    public OfferResult Offer(string prefabName, RoadPoint position, PinStore pins, DateTime nowUtc)
    {
        if (!Rules.TryMatch(prefabName, out SurveyRule rule))
        {
            return OfferResult.NoRule;
        }

        string cleaned = SurveyRuleSet.Clean(prefabName);
        string key = IdentityKey(cleaned, position);
        if (_rejectedKeys.Contains(key))
        {
            return OfferResult.RejectedEarlier;
        }

        if (_pendingKeys.Contains(key) || _acceptedKeys.Contains(key))
        {
            return OfferResult.DuplicateIdentity;
        }

        if (_observations.Count >= MaxObservations)
        {
            return OfferResult.CapReached;
        }

        if (BaseExclusionRadiusMeters > 0f)
        {
            foreach (AtlasPin pin in pins.Living)
            {
                if (IsBaseMarker(pin) &&
                    pin.Position.HorizontalDistanceTo(position) <= BaseExclusionRadiusMeters)
                {
                    return OfferResult.InsideBaseExclusion;
                }
            }
        }

        // RC11 blocker 11: names come from the shared humanizer, so the
        // pending list and the pins it creates read "Raspberry Bush".
        string suggestedName = HumanizedName(prefabName);
        if (rule.DuplicateRadiusMeters > 0f)
        {
            foreach (AtlasPin pin in pins.Living)
            {
                if (string.Equals(pin.Name, suggestedName, StringComparison.OrdinalIgnoreCase) &&
                    pin.Position.HorizontalDistanceTo(position) <= rule.DuplicateRadiusMeters)
                {
                    return OfferResult.DuplicatePin;
                }
            }

            foreach (Observation existing in _observations)
            {
                if (existing.PrefabName == cleaned &&
                    existing.Position.HorizontalDistanceTo(position) <= rule.DuplicateRadiusMeters)
                {
                    return OfferResult.DuplicateObservation;
                }
            }
        }

        DateTime expires = rule.ExpiryMinutes > 0f
            ? nowUtc.AddMinutes(rule.ExpiryMinutes)
            : DateTime.MaxValue;
        _observations.Add(new Observation(
            Guid.NewGuid(), cleaned, suggestedName,
            rule.IconId, rule.Category, position, nowUtc, expires));
        _pendingKeys.Add(key);
        return OfferResult.Added;
    }

    private static string HumanizedName(string prefabName)
    {
        string humanized = NameHumanizer.Humanize(prefabName);
        return humanized.Length > 0 ? humanized : QuickPinSuggester.FallbackName;
    }

    /// <summary>Drops expired observations; returns how many were removed.</summary>
    public int Prune(DateTime nowUtc)
    {
        return _observations.RemoveAll(observation =>
        {
            if (observation.ExpiresUtc <= nowUtc)
            {
                _pendingKeys.Remove(IdentityKey(observation.PrefabName, observation.Position));
                return true;
            }

            return false;
        });
    }

    public bool Accept(Guid id, PinStore pins)
    {
        Observation? observation = Find(id);
        if (observation is null)
        {
            return false;
        }

        CreatePin(pins, observation.SuggestedName, observation.IconId, observation.Category, observation.Position);
        string key = IdentityKey(observation.PrefabName, observation.Position);
        _pendingKeys.Remove(key);
        _acceptedKeys.Add(key);
        _observations.Remove(observation);
        return true;
    }

    public int AcceptAll(PinStore pins)
    {
        int accepted = 0;
        foreach (Observation observation in _observations.ToArray())
        {
            if (Accept(observation.Id, pins))
            {
                accepted++;
            }
        }

        return accepted;
    }

    /// <summary>RC11 blocker 9: rejection MOVES the observation to the
    /// persistent Rejected list and suppresses its identity from future
    /// sweeps until restored.</summary>
    public bool Reject(Guid id, DateTime nowUtc)
    {
        Observation? observation = Find(id);
        if (observation is null)
        {
            return false;
        }

        _observations.Remove(observation);
        _pendingKeys.Remove(IdentityKey(observation.PrefabName, observation.Position));
        AddRejected(new RejectedObservation(
            observation.PrefabName, observation.SuggestedName, observation.IconId,
            observation.Category, observation.Position, nowUtc));
        return true;
    }

    public int RejectAll(DateTime nowUtc)
    {
        int count = 0;
        foreach (Observation observation in _observations.ToArray())
        {
            if (Reject(observation.Id, nowUtc))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Moves a rejected entry back to pending review (bounded by
    /// the observation cap).</summary>
    public bool RestoreRejected(int index, DateTime nowUtc)
    {
        if (index < 0 || index >= _rejected.Count || _observations.Count >= MaxObservations)
        {
            return false;
        }

        RejectedObservation entry = _rejected[index];
        RemoveRejectedAt(index);
        _observations.Add(new Observation(
            Guid.NewGuid(), entry.PrefabName, entry.SuggestedName, entry.IconId,
            entry.Category, entry.Position, nowUtc, DateTime.MaxValue));
        _pendingKeys.Add(IdentityKey(entry.PrefabName, entry.Position));
        return true;
    }

    public int RestoreAllRejected(DateTime nowUtc)
    {
        int restored = 0;
        while (_rejected.Count > 0 && RestoreRejected(0, nowUtc))
        {
            restored++;
        }

        return restored;
    }

    /// <summary>Accepts straight from the Rejected list (changed my
    /// mind): creates the pin and retires the identity.</summary>
    public bool AcceptRejected(int index, PinStore pins)
    {
        if (index < 0 || index >= _rejected.Count)
        {
            return false;
        }

        RejectedObservation entry = _rejected[index];
        RemoveRejectedAt(index);
        CreatePin(pins, entry.SuggestedName, entry.IconId, entry.Category, entry.Position);
        _acceptedKeys.Add(IdentityKey(entry.PrefabName, entry.Position));
        return true;
    }

    /// <summary>Loads the persisted rejected list (world switch). Replaces
    /// the current list; does not mark dirty.</summary>
    public void LoadRejected(IEnumerable<RejectedObservation> entries)
    {
        _rejected.Clear();
        _rejectedKeys.Clear();
        foreach (RejectedObservation entry in entries)
        {
            if (_rejected.Count >= MaxRejected)
            {
                break;
            }

            _rejected.Add(entry);
            _rejectedKeys.Add(IdentityKey(entry.PrefabName, entry.Position));
        }

        RejectedDirty = false;
    }

    /// <summary>World switch: forget session state (pending, accepted
    /// keys). The rejected list is replaced by LoadRejected.</summary>
    public void ResetSession()
    {
        _observations.Clear();
        _pendingKeys.Clear();
        _acceptedKeys.Clear();
    }

    private void AddRejected(RejectedObservation entry)
    {
        _rejected.Add(entry);
        _rejectedKeys.Add(IdentityKey(entry.PrefabName, entry.Position));
        while (_rejected.Count > MaxRejected)
        {
            RemoveRejectedAt(0);
        }

        RejectedDirty = true;
    }

    private void RemoveRejectedAt(int index)
    {
        RejectedObservation entry = _rejected[index];
        _rejected.RemoveAt(index);
        _rejectedKeys.Remove(IdentityKey(entry.PrefabName, entry.Position));
        RejectedDirty = true;
    }

    private static void CreatePin(PinStore pins, string name, string iconId, string category, RoadPoint position)
    {
        pins.Create(pin =>
        {
            pin.Name = name;
            pin.IconId = iconId;
            pin.Category = category;
            pin.Source = AtlasPinSource.Generated;
            pin.Tags.Add("surveyed");
            pin.Position = position;
        });
    }

    private Observation? Find(Guid id)
    {
        foreach (Observation observation in _observations)
        {
            if (observation.Id == id)
            {
                return observation;
            }
        }

        return null;
    }

    private static bool IsBaseMarker(AtlasPin pin)
    {
        if (string.Equals(pin.Category, "Base", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string tag in pin.Tags)
        {
            if (string.Equals(tag, "base", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
