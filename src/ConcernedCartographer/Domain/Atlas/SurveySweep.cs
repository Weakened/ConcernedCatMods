using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The bounded per-tick walk over the survey's loaded-world
/// surfaces. One sweep visits every <see cref="ISurveySightingSource"/> in
/// order, a slice at a time, offering each in-range sighting to the
/// engine; the engine still enforces rules, duplicates, rejections, base
/// exclusion, and the observation cap on top.</summary>
/// <remarks>Extracted from <c>SurveyScanner</c> for issue #258 so the
/// scanner's surfaces are pluggable and the scan path is provable without
/// Unity. The per-tick budget is shared across sources, so adding the
/// loaded-<c>Location</c> surface cannot raise the per-frame cost.</remarks>
internal sealed class SurveySweep
{
    /// <summary>Hard clamp on how much an object's own declared footprint
    /// may extend the scan range. Keeps discovery bounded even if a
    /// location (or a mod-added one) reports an absurd radius.</summary>
    public const float MaxFootprintBonusMeters = 32f;

    private readonly int _perTickExamineBudget;
    private int _sourceIndex;
    private int _cursor;

    public SurveySweep(int perTickExamineBudget)
    {
        _perTickExamineBudget = Math.Max(1, perTickExamineBudget);
    }

    /// <summary>Entries examined so far in the current sweep.</summary>
    public int Examined { get; private set; }

    /// <summary>Observations the current sweep has added.</summary>
    public int Added { get; private set; }

    /// <summary>True once every source has been walked (or the engine hit
    /// its observation cap). The caller publishes stats and restarts.</summary>
    public bool Completed { get; private set; }

    /// <summary>Begins a fresh sweep over a fresh snapshot.</summary>
    public void Restart()
    {
        _sourceIndex = 0;
        _cursor = 0;
        Examined = 0;
        Added = 0;
        Completed = false;
    }

    /// <summary>Examines up to the per-tick budget of entries across the
    /// ordered sources and returns how many observations were added this
    /// tick. Safe to call after <see cref="Completed"/>; it does nothing.</summary>
    public int Tick(
        IReadOnlyList<ISurveySightingSource> sources,
        RoadPoint origin,
        float radiusMeters,
        SurveyEngine engine,
        PinStore pins,
        DateTime nowUtc)
    {
        if (Completed || sources.Count == 0)
        {
            Completed = true;
            return 0;
        }

        int addedThisTick = 0;
        int budget = _perTickExamineBudget;
        while (budget > 0)
        {
            if (_sourceIndex >= sources.Count)
            {
                Completed = true;
                break;
            }

            ISurveySightingSource source = sources[_sourceIndex];
            if (_cursor >= source.Count)
            {
                _sourceIndex++;
                _cursor = 0;
                continue;
            }

            int index = _cursor++;
            budget--;
            Examined++;
            if (!source.TryRead(index, out SurveySighting sighting) ||
                string.IsNullOrEmpty(sighting.Name))
            {
                continue;
            }

            if (origin.DistanceTo(sighting.Position) > radiusMeters + FootprintBonus(sighting))
            {
                continue;
            }

            SurveyEngine.OfferResult result = engine.Offer(
                sighting.Name, sighting.Position, pins, nowUtc);
            if (result == SurveyEngine.OfferResult.Added)
            {
                Added++;
                addedThisTick++;
            }
            else if (result == SurveyEngine.OfferResult.CapReached)
            {
                // Nothing more can be added until the player reviews the
                // pending list; end the sweep instead of burning frames.
                Completed = true;
                break;
            }
        }

        return addedThisTick;
    }

    private static float FootprintBonus(in SurveySighting sighting)
    {
        float footprint = sighting.FootprintRadiusMeters;
        if (float.IsNaN(footprint) || footprint <= 0f)
        {
            return 0f;
        }

        return Math.Min(footprint, MaxFootprintBonusMeters);
    }
}
