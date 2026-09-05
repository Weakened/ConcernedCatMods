using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>Turns pulled-cart telemetry into bounded trips (CT-016). Pure
/// state machine: a trip starts when pulled telemetry arrives, records at
/// the configured spacing, splits when the per-trip cap fills, survives a
/// detach shorter than the debounce (re-grab fumbles), and finalizes on a
/// real detach, a cart switch, or a reset (world exit). Trips shorter than
/// the keep threshold are discarded as noise. Finished trips queue in
/// <see cref="DrainFinishedTrips"/> for the persistence layer.</summary>
public sealed class TripRecorder
{
    private readonly TripRecorderOptions _options;
    private readonly List<TripSample> _current = new();
    private readonly List<Trip> _finished = new();
    private string? _currentCartId;
    private double _lastRecordTime = double.NegativeInfinity;
    private double _pendingEndSince = double.NaN;

    public TripRecorder(TripRecorderOptions options)
    {
        _options = options;
    }

    public bool IsRecording => _currentCartId is not null;

    public int CurrentSampleCount => _current.Count;

    /// <summary>Feeds one fresh snapshot of the pulled cart.</summary>
    public void FeedPulled(CartTelemetry telemetry)
    {
        if (_currentCartId is not null && _currentCartId != telemetry.CartId)
        {
            // Cart switch is a real end of the previous trip.
            Finalize();
        }

        if (_currentCartId is null)
        {
            _currentCartId = telemetry.CartId;
            _current.Clear();
            _lastRecordTime = double.NegativeInfinity;
        }

        _pendingEndSince = double.NaN;

        if (telemetry.SampleTimeSeconds - _lastRecordTime < _options.RecordSpacingSeconds)
        {
            return;
        }

        _lastRecordTime = telemetry.SampleTimeSeconds;
        _current.Add(new TripSample(
            telemetry.SampleTimeSeconds,
            telemetry.PositionX,
            telemetry.PositionZ,
            telemetry.GradeAvailable ? telemetry.SmoothedGradePercent : float.NaN,
            telemetry.VelocityAvailable ? telemetry.SpeedMetersPerSecond : float.NaN,
            telemetry.TotalMass));

        if (_current.Count >= _options.MaxSamplesPerTrip)
        {
            // Cap reached: split — finalize this bounded segment and let the
            // ongoing haul continue as a fresh trip.
            string cartId = _currentCartId!;
            Finalize();
            _currentCartId = cartId;
            _lastRecordTime = double.NegativeInfinity;
        }
    }

    /// <summary>Ticks the end debounce while nothing is pulled. A detach
    /// shorter than the debounce keeps the trip open; a longer one
    /// finalizes it.</summary>
    public void NotifyNotPulled(double nowSeconds)
    {
        if (_currentCartId is null)
        {
            return;
        }

        if (double.IsNaN(_pendingEndSince))
        {
            _pendingEndSince = nowSeconds;
            return;
        }

        if (nowSeconds - _pendingEndSince >= TripRecorderOptions.EndDebounceSeconds)
        {
            Finalize();
        }
    }

    /// <summary>Finalizes any open trip (world exit, shutdown) and returns
    /// everything finished so far.</summary>
    public IReadOnlyList<Trip> DrainOnReset()
    {
        Finalize();
        return DrainFinishedTrips();
    }

    /// <summary>Removes and returns the finished-trip queue.</summary>
    public IReadOnlyList<Trip> DrainFinishedTrips()
    {
        if (_finished.Count == 0)
        {
            return Array.Empty<Trip>();
        }

        Trip[] drained = _finished.ToArray();
        _finished.Clear();
        return drained;
    }

    private void Finalize()
    {
        if (_currentCartId is not null && _current.Count >= TripRecorderOptions.MinSamplesToKeep)
        {
            _finished.Add(new Trip(0, _currentCartId, _current.ToArray()));
        }

        _currentCartId = null;
        _current.Clear();
        _pendingEndSince = double.NaN;
        _lastRecordTime = double.NegativeInfinity;
    }
}
