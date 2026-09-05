using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>Hard-clamped trip recording bounds (CT-016). The caps are the
/// promise: no config edit can make the sidecar grow without limit.</summary>
public sealed class TripRecorderOptions
{
    public const float DefaultRecordSpacingSeconds = 1f;
    public const float MinRecordSpacingSeconds = 0.25f;
    public const float MaxRecordSpacingSeconds = 10f;

    public const int DefaultMaxSamplesPerTrip = 600;
    public const int MinMaxSamplesPerTrip = 50;
    public const int MaxMaxSamplesPerTrip = 5000;

    public const int DefaultMaxTripsRetained = 50;
    public const int MinMaxTripsRetained = 5;
    public const int MaxMaxTripsRetained = 500;

    /// <summary>A finished trip shorter than this is measurement noise and
    /// is discarded (attach fumbles, doorway shuffles).</summary>
    public const int MinSamplesToKeep = 5;

    /// <summary>Detach shorter than this (re-grab fumble) continues the
    /// same trip instead of splitting it.</summary>
    public const double EndDebounceSeconds = 3.0;

    private TripRecorderOptions(float recordSpacingSeconds, int maxSamplesPerTrip, int maxTripsRetained)
    {
        RecordSpacingSeconds = recordSpacingSeconds;
        MaxSamplesPerTrip = maxSamplesPerTrip;
        MaxTripsRetained = maxTripsRetained;
    }

    public float RecordSpacingSeconds { get; }

    public int MaxSamplesPerTrip { get; }

    public int MaxTripsRetained { get; }

    public static TripRecorderOptions CreateClamped(
        float recordSpacingSeconds, int maxSamplesPerTrip, int maxTripsRetained)
    {
        float spacing = float.IsNaN(recordSpacingSeconds) || float.IsInfinity(recordSpacingSeconds)
            ? DefaultRecordSpacingSeconds
            : Math.Min(MaxRecordSpacingSeconds, Math.Max(MinRecordSpacingSeconds, recordSpacingSeconds));
        return new TripRecorderOptions(
            spacing,
            Math.Min(MaxMaxSamplesPerTrip, Math.Max(MinMaxSamplesPerTrip, maxSamplesPerTrip)),
            Math.Min(MaxMaxTripsRetained, Math.Max(MinMaxTripsRetained, maxTripsRetained)));
    }

    public static TripRecorderOptions CreateDefault()
    {
        return CreateClamped(DefaultRecordSpacingSeconds, DefaultMaxSamplesPerTrip, DefaultMaxTripsRetained);
    }
}
