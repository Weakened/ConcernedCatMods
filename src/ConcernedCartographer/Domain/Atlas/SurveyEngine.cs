using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The bounded survey pipeline: candidate sightings become
/// temporary observations that the player reviews before anything turns
/// into a permanent pin. Bounds are structural — a hard observation cap, a
/// per-rule duplicate radius against both pins and other observations,
/// base-exclusion zones, and expiry — so a broad rule can never flood the
/// atlas. Pure and fully tested; the scanner adapter only feeds
/// sightings.</summary>
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

    public enum OfferResult
    {
        Added,
        NoRule,
        Blacklisted,
        DuplicatePin,
        DuplicateObservation,
        InsideBaseExclusion,
        CapReached,
    }

    private readonly List<Observation> _observations = new();

    public SurveyRuleSet Rules { get; set; } = new();
    public int MaxObservations { get; set; } = 200;
    public float BaseExclusionRadiusMeters { get; set; } = 30f;

    public IReadOnlyList<Observation> Observations => _observations;

    public OfferResult Offer(string prefabName, RoadPoint position, PinStore pins, DateTime nowUtc)
    {
        if (!Rules.TryMatch(prefabName, out SurveyRule rule))
        {
            return OfferResult.NoRule;
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

        string cleaned = SurveyRuleSet.Clean(prefabName);
        string suggestedName = QuickPinSuggester.CleanName(null, prefabName);
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
        return OfferResult.Added;
    }

    /// <summary>Drops expired observations; returns how many were removed.</summary>
    public int Prune(DateTime nowUtc)
    {
        return _observations.RemoveAll(observation => observation.ExpiresUtc <= nowUtc);
    }

    public bool Accept(Guid id, PinStore pins)
    {
        Observation? observation = Find(id);
        if (observation is null)
        {
            return false;
        }

        pins.Create(pin =>
        {
            pin.Name = observation.SuggestedName;
            pin.IconId = observation.IconId;
            pin.Category = observation.Category;
            pin.Source = AtlasPinSource.Generated;
            pin.Tags.Add("surveyed");
            pin.Position = observation.Position;
        });
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

    public bool Reject(Guid id)
    {
        Observation? observation = Find(id);
        if (observation is null)
        {
            return false;
        }

        _observations.Remove(observation);
        return true;
    }

    public int RejectAll()
    {
        int count = _observations.Count;
        _observations.Clear();
        return count;
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
