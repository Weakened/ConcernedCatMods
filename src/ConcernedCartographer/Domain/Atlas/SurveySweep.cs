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
/// Unity. The per-tick budget is shared across sources, and the range test
/// runs on the cheap positional read BEFORE a source is asked for a name,
/// so adding the loaded-<c>Location</c> surface does not raise the
/// per-frame cost.</remarks>
internal sealed class SurveySweep
{
    /// <summary>Hard clamp on how much an object's own declared footprint
    /// may extend the scan range. Keeps discovery bounded even if a
    /// location (or a mod-added one) reports an absurd radius.</summary>
    public const float MaxFootprintBonusMeters = 32f;

    private readonly int _perTickExamineBudget;
    private readonly int[] _examinedPerSource;
    private int _sourceIndex;
    private int _cursor;

    public SurveySweep(int perTickExamineBudget, int sourceCount = 8)
    {
        _perTickExamineBudget = Math.Max(1, perTickExamineBudget);
        _examinedPerSource = new int[Math.Max(1, sourceCount)];
    }

    /// <summary>Entries examined so far in the current sweep.</summary>
    public int Examined { get; private set; }

    /// <summary>Observations the current sweep has added.</summary>
    public int Added { get; private set; }

    /// <summary>True once every source has been walked (or the engine hit
    /// its observation cap). The caller publishes stats and restarts.</summary>
    public bool Completed { get; private set; }

    /// <summary>Entries the current sweep examined from one source, so the
    /// Survey panel can report per-surface figures that add up instead of
    /// double-counting a shared total.</summary>
    public int ExaminedFrom(int sourceIndex)
    {
        return sourceIndex >= 0 && sourceIndex < _examinedPerSource.Length
            ? _examinedPerSource[sourceIndex]
            : 0;
    }

    /// <summary>Begins a fresh sweep over a fresh snapshot.</summary>
    public void Restart()
    {
        _sourceIndex = 0;
        _cursor = 0;
        Examined = 0;
        Added = 0;
        Completed = false;
        Array.Clear(_examinedPerSource, 0, _examinedPerSource.Length);
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
            int sourceIndex = _sourceIndex;
            budget--;
            Examined++;
            if (sourceIndex < _examinedPerSource.Length)
            {
                _examinedPerSource[sourceIndex]++;
            }

            if (!source.TryReadPlacement(index, out RoadPoint position, out float footprint) ||
                origin.DistanceTo(position) > radiusMeters + FootprintBonus(footprint) ||
                !source.TryReadName(index, out string name) ||
                string.IsNullOrEmpty(name))
            {
                continue;
            }

            SurveyEngine.OfferResult result = engine.Offer(name, position, pins, nowUtc);
            if (result == SurveyEngine.OfferResult.Added)
            {
                Added++;
                addedThisTick++;
            }
            else if (result == SurveyEngine.OfferResult.CapReached)
            {
                // Nothing more can be added until the player reviews the
                // pending list; end the sweep instead of burning frames.
                // Source ORDER therefore matters: the scanner puts the
                // small, high-value loaded-location surface first so a full
                // pending list can never starve dungeon discovery.
                Completed = true;
                break;
            }
        }

        return addedThisTick;
    }

    private static float FootprintBonus(float footprint)
    {
        if (float.IsNaN(footprint) || footprint <= 0f)
        {
            return 0f;
        }

        return Math.Min(footprint, MaxFootprintBonusMeters);
    }
}
