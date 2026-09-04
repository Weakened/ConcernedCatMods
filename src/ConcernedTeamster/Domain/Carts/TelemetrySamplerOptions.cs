using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Carts;

/// <summary>Validated sampler bounds (CT-003). The constants are the hard
/// limits promised by the architecture ("configurable with safe defaults and
/// hard upper bounds"): config values are clamped here regardless of what the
/// config file says, so no editable file can remove the budget.</summary>
public sealed class TelemetrySamplerOptions
{
    public const float DefaultSampleIntervalSeconds = 0.5f;
    public const float MinSampleIntervalSeconds = 0.1f;
    public const float MaxSampleIntervalSeconds = 10f;

    public const float DefaultSearchRadiusMeters = 30f;
    public const float MinSearchRadiusMeters = 5f;
    public const float MaxSearchRadiusMeters = 100f;

    public const int DefaultMaxCartsPerTick = 2;
    public const int MinMaxCartsPerTick = 1;
    public const int MaxMaxCartsPerTick = 8;

    public const int DefaultMaxTrackedCarts = 8;
    public const int MinMaxTrackedCarts = 1;
    public const int MaxMaxTrackedCarts = 32;

    private TelemetrySamplerOptions(
        float sampleIntervalSeconds,
        float searchRadiusMeters,
        int maxCartsPerTick,
        int maxTrackedCarts)
    {
        SampleIntervalSeconds = sampleIntervalSeconds;
        SearchRadiusMeters = searchRadiusMeters;
        MaxCartsPerTick = maxCartsPerTick;
        MaxTrackedCarts = maxTrackedCarts;
    }

    /// <summary>Seconds between due ticks; between them the sampler's fast
    /// path is a single comparison.</summary>
    public float SampleIntervalSeconds { get; }

    /// <summary>Carts farther than this from the local player are not
    /// collected. Used by the adapter's discovery pass.</summary>
    public float SearchRadiusMeters { get; }

    /// <summary>Hard budget of cart sample attempts per due tick (attempts,
    /// not successes, so failing carts cannot widen the budget).</summary>
    public int MaxCartsPerTick { get; }

    /// <summary>Hard cap of telemetry entries kept; new carts beyond it are
    /// ignored until entries evict.</summary>
    public int MaxTrackedCarts { get; }

    /// <summary>Entries older than this are evicted on due ticks: three
    /// missed samples, floored at 2 s so brief hitches never blank a panel.
    /// Derived, not configurable — one less knob to break.</summary>
    public double EvictAfterSeconds => Math.Max(2.0, SampleIntervalSeconds * 3.0);

    /// <summary>Builds options from raw config values, silently clamping to
    /// the hard bounds above. Non-finite floats fall back to defaults.</summary>
    public static TelemetrySamplerOptions CreateClamped(
        float sampleIntervalSeconds,
        float searchRadiusMeters,
        int maxCartsPerTick,
        int maxTrackedCarts)
    {
        return new TelemetrySamplerOptions(
            ClampFloat(sampleIntervalSeconds, MinSampleIntervalSeconds, MaxSampleIntervalSeconds, DefaultSampleIntervalSeconds),
            ClampFloat(searchRadiusMeters, MinSearchRadiusMeters, MaxSearchRadiusMeters, DefaultSearchRadiusMeters),
            ClampInt(maxCartsPerTick, MinMaxCartsPerTick, MaxMaxCartsPerTick),
            ClampInt(maxTrackedCarts, MinMaxTrackedCarts, MaxMaxTrackedCarts));
    }

    public static TelemetrySamplerOptions CreateDefault()
    {
        return CreateClamped(
            DefaultSampleIntervalSeconds,
            DefaultSearchRadiusMeters,
            DefaultMaxCartsPerTick,
            DefaultMaxTrackedCarts);
    }

    private static float ClampFloat(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
