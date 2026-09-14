using System;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Why sailing Route Follow stopped. Every reason restores plain
/// vanilla helm behaviour by writing nothing further.</summary>
internal enum SailingRouteFollowCancelReason
{
    None,
    Disabled,
    TogglePressed,
    ManualRudderInput,
    ManualSailInput,
    ExitInput,
    HelmLost,
    RouteChanged,
    Lifecycle,
    OffRoute,
    RouteEnd,
    NoProgressTimeout,
    InvalidState,
}

/// <summary>One control frame as the runtime observed it. Everything the
/// controller needs is passed in, so the whole decision is game-free and
/// deterministic.</summary>
internal readonly struct SailingRouteFollowFrame
{
    public SailingRouteFollowFrame(
        in RoadPoint position,
        float headingDegrees,
        float rudderValue,
        float deltaSeconds,
        bool enabled = true,
        bool helmGranted = true,
        bool togglePressed = false,
        bool manualRudderInput = false,
        bool manualSailInput = false,
        bool exitInput = false,
        bool routeUnchanged = true,
        bool lifecycleReady = true)
    {
        Position = position;
        HeadingDegrees = headingDegrees;
        RudderValue = rudderValue;
        DeltaSeconds = deltaSeconds;
        Enabled = enabled;
        HelmGranted = helmGranted;
        TogglePressed = togglePressed;
        ManualRudderInput = manualRudderInput;
        ManualSailInput = manualSailInput;
        ExitInput = exitInput;
        RouteUnchanged = routeUnchanged;
        LifecycleReady = lifecycleReady;
    }

    /// <summary>Ship world position.</summary>
    public RoadPoint Position { get; }

    /// <summary>Ship forward heading in degrees, 0 = +Z, clockwise.</summary>
    public float HeadingDegrees { get; }

    /// <summary>Vanilla <c>Ship.GetRudderValue()</c>, the persistent rudder
    /// deflection in [-1, 1]. Read only; never written.</summary>
    public float RudderValue { get; }

    public float DeltaSeconds { get; }
    public bool Enabled { get; }

    /// <summary>The local player is the granted helmsman of this exact
    /// controls instance.</summary>
    public bool HelmGranted { get; }

    public bool TogglePressed { get; }

    /// <summary>Raw left/right helm input this call.</summary>
    public bool ManualRudderInput { get; }

    /// <summary>Raw forward/back sail-step input this call.</summary>
    public bool ManualSailInput { get; }

    /// <summary>Jump, attack, secondary attack or dodge — vanilla leaves the
    /// helm on these, but only AFTER it has already dispatched doodad
    /// controls, so they must cancel in the raw-input observer.</summary>
    public bool ExitInput { get; }

    public bool RouteUnchanged { get; }
    public bool LifecycleReady { get; }
}

/// <summary>What the runtime may do with this control call.</summary>
internal readonly struct SailingRouteFollowStep
{
    public SailingRouteFollowStep(
        bool steering,
        float rudderInput,
        bool cancelled,
        SailingRouteFollowCancelReason cancelReason)
    {
        Steering = steering;
        RudderInput = rudderInput;
        Cancelled = cancelled;
        CancelReason = cancelReason;
    }

    /// <summary>True only when a synthetic rudder-rate write is authorised.
    /// False while following but inside the rudder deadband, so vanilla is
    /// left holding exactly the rudder it already had.</summary>
    public bool Steering { get; }

    /// <summary>The bounded rudder-RATE input, always within [-1, 1]. It is
    /// exactly what a player holding the helm left/right key supplies; it is
    /// never an absolute rudder angle.</summary>
    public float RudderInput { get; }

    public bool Cancelled { get; }
    public SailingRouteFollowCancelReason CancelReason { get; }
}

/// <summary>The only place a synthetic sailing input is written. It replaces
/// the rudder axis and nothing else, so sail power, look direction, run and
/// block stay exactly as the player supplied them.</summary>
internal static class SailingRouteFollowControlPolicy
{
    /// <summary>Returns true when <paramref name="moveDirX"/> was replaced.
    /// A non-steering or non-finite step writes nothing at all.</summary>
    public static bool TryApply(in SailingRouteFollowStep step, ref float moveDirX)
    {
        if (!step.Steering ||
            float.IsNaN(step.RudderInput) ||
            float.IsInfinity(step.RudderInput) ||
            step.RudderInput < -1f || step.RudderInput > 1f)
        {
            return false;
        }

        moveDirX = step.RudderInput;
        return true;
    }
}

/// <summary>Sailing Route Follow state for one helmsman and one selected
/// sailing route (#243).</summary>
/// <remarks>It steers by feeding vanilla's own rudder-RATE axis. Valheim's
/// <c>Ship.ApplyControlls</c> computes
/// <c>m_rudderValue += dir.x * lerp(0.5,1,|m_rudderValue|) * m_rudderSpeed * dt</c>,
/// so <c>dir.x</c> is a rate, not an angle: the controller asks for a target
/// deflection and then feeds the bounded rate that walks the vanilla rudder
/// toward it, exactly as a player holding the helm key would. It never writes
/// <c>m_rudderValue</c>, never touches <c>dir.z</c> (which is what vanilla
/// turns into Forward/Backward sail steps), and never reads or writes
/// physics, wind, or ownership.
///
/// A ship is a SECOND-ORDER plant: vanilla applies a torque impulse to a
/// rigidbody with angular damping, and the turn authority scales with forward
/// speed, so heading lags the rudder by seconds. Proportional-only steering
/// on a lagging plant hunts and can saturate the cross-track bound. The
/// controller therefore adds two leads, both derived from frames it already
/// receives and neither requiring a new game API:
///
///  * a yaw-RATE damping term (<see cref="YawLeadSeconds"/>), which is the
///    derivative half of a PD controller and supplies the phase margin the
///    plant lag eats;
///  * a look-ahead measured in TIME rather than metres
///    (<see cref="LookAheadSeconds"/>, clamped to a metre band), so a fast
///    longship aims further ahead than a raft.
///
/// Adverse wind, an obstacle and a becalmed ship are all bounded by the same
/// deterministic rule: if the ship fails to gain
/// <see cref="ProgressEpsilonMeters"/> along the route within
/// <see cref="NoProgressTimeoutSeconds"/>, following cancels. No wind
/// heuristic is inferred and no tacking is attempted.</remarks>
internal sealed class SailingRouteFollowController
{
    /// <summary>Corner/off-route tolerance. A hand-drawn sea route around an
    /// island is followed within this band or following cancels; it is the
    /// bounded corner tolerance, not a licence to cut one.</summary>
    public const float MaximumCrossTrackMeters = 40f;

    /// <summary>Look-ahead horizon in seconds of travel.</summary>
    public const float LookAheadSeconds = 3.5f;

    /// <summary>Metre band the time-based look-ahead is clamped into, so a
    /// stopped ship still has a target and a fast one cannot aim past a
    /// corner.</summary>
    public const float MinimumLookAheadMeters = 18f;
    public const float MaximumLookAheadMeters = 45f;

    /// <summary>Heading error that asks for full rudder.</summary>
    public const float FullRudderHeadingErrorDegrees = 25f;

    /// <summary>How far ahead the yaw-rate damping term looks. This is the
    /// derivative gain expressed as a lead time, so it is comparable with the
    /// hull's own turn lag.</summary>
    public const float YawLeadSeconds = 1.6f;

    /// <summary>Ships stop slowly; the route ends before they do.</summary>
    public const float RouteEndToleranceMeters = 12f;

    public const float ProgressEpsilonMeters = 0.5f;
    public const float NoProgressTimeoutSeconds = 12f;

    private const int ProjectionWindow = 24;

    /// <summary>Converts a rudder-deflection error into a rate request.
    /// Bounded to [-1, 1] afterwards, so it can never exceed the vanilla
    /// axis a player supplies.</summary>
    private const float RudderSlewGain = 4f;

    /// <summary>Below this deflection error the rudder is left exactly where
    /// vanilla put it — no synthetic write at all.</summary>
    private const float RudderDeadband = 0.02f;

    /// <summary>Low-pass factor for the derived speed and yaw rate. Frame
    /// deltas are small and noisy; the plant is not.</summary>
    private const float DerivedSmoothing = 0.15f;

    /// <summary>Sanity bound on derived speed, well above any vanilla hull.</summary>
    private const float MaximumPlausibleSpeed = 60f;

    /// <summary>Sanity bound on derived yaw rate.</summary>
    private const float MaximumPlausibleYawRate = 360f;

    private RouteFollowPath? _path;
    private RouteFollowDirection _direction;
    private int _cursor;
    private float _lastRemainingMeters;
    private float _noProgressSeconds;
    private bool _hasPreviousFrame;
    private RoadPoint _previousPosition;
    private float _previousHeadingDegrees;
    private float _speedMetersPerSecond;
    private float _yawRateDegreesPerSecond;

    public bool IsFollowing => _path is not null;

    /// <summary>Seconds since the ship last made real progress along the
    /// route. Surfaced for diagnostics and tests.</summary>
    public float NoProgressSeconds => _noProgressSeconds;

    /// <summary>Smoothed speed derived from successive frames, in m/s.</summary>
    public float DerivedSpeedMetersPerSecond => _speedMetersPerSecond;

    /// <summary>Smoothed yaw rate derived from successive frames, in deg/s.</summary>
    public float DerivedYawRateDegreesPerSecond => _yawRateDegreesPerSecond;

    /// <summary>The look-ahead distance the current speed estimate asks for.</summary>
    public float LookAheadMeters =>
        Clamp(_speedMetersPerSecond * LookAheadSeconds,
            MinimumLookAheadMeters, MaximumLookAheadMeters);

    public bool TryStart(
        RouteFollowPath? path,
        RouteFollowDirection direction,
        in RoadPoint position)
    {
        Cancel();
        int initialCursor = direction == RouteFollowDirection.Reverse && path is not null
            ? path.LastSegmentIndex
            : 0;
        if (path is null ||
            !RouteFollowMath.TrySample(path, position, direction, initialCursor,
                path.LastSegmentIndex + 1, MinimumLookAheadMeters,
                MaximumCrossTrackMeters, RouteEndToleranceMeters,
                out RouteFollowSample sample) ||
            sample.AtRouteEnd)
        {
            return false;
        }

        _path = path;
        _direction = direction;
        _cursor = sample.SegmentIndex;
        _lastRemainingMeters = sample.RemainingMeters;
        _noProgressSeconds = 0f;
        _hasPreviousFrame = false;
        _speedMetersPerSecond = 0f;
        _yawRateDegreesPerSecond = 0f;
        return true;
    }

    public SailingRouteFollowStep Tick(in SailingRouteFollowFrame frame)
    {
        if (_path is null)
        {
            return default;
        }

        SailingRouteFollowCancelReason immediate = ImmediateCancelReason(frame);
        if (immediate != SailingRouteFollowCancelReason.None)
        {
            return Stop(immediate);
        }

        UpdateDerivedMotion(frame);

        if (!RouteFollowMath.TrySample(
                _path, frame.Position, _direction, _cursor, ProjectionWindow,
                LookAheadMeters, MaximumCrossTrackMeters,
                RouteEndToleranceMeters, out RouteFollowSample sample))
        {
            return Stop(SailingRouteFollowCancelReason.OffRoute);
        }

        _cursor = sample.SegmentIndex;
        if (sample.AtRouteEnd)
        {
            return Stop(SailingRouteFollowCancelReason.RouteEnd);
        }

        if (_lastRemainingMeters - sample.RemainingMeters >= ProgressEpsilonMeters)
        {
            _lastRemainingMeters = sample.RemainingMeters;
            _noProgressSeconds = 0f;
        }
        else
        {
            _noProgressSeconds += frame.DeltaSeconds;
            if (_noProgressSeconds >= NoProgressTimeoutSeconds)
            {
                return Stop(SailingRouteFollowCancelReason.NoProgressTimeout);
            }
        }

        float bearing = BearingTo(frame.Position, sample.LookAheadPoint);
        float headingError = NormalizeDelta(bearing - frame.HeadingDegrees);

        // PD: the yaw-rate term is subtracted as a lead, so the controller
        // starts easing off while the hull is still swinging toward the
        // course instead of waiting for the error to close.
        float dampedError = headingError - (YawLeadSeconds * _yawRateDegreesPerSecond);
        float targetRudder = Clamp(
            dampedError / FullRudderHeadingErrorDegrees, -1f, 1f);
        float rudderError = targetRudder - frame.RudderValue;
        if (Math.Abs(rudderError) <= RudderDeadband)
        {
            // Authorise no write at all: vanilla keeps exactly the rudder it
            // already has.
            return new SailingRouteFollowStep(
                false, 0f, false, SailingRouteFollowCancelReason.None);
        }

        float rudderInput = Clamp(rudderError * RudderSlewGain, -1f, 1f);
        return new SailingRouteFollowStep(
            true, rudderInput, false, SailingRouteFollowCancelReason.None);
    }

    public void Cancel()
    {
        _path = null;
        _cursor = 0;
        _lastRemainingMeters = 0f;
        _noProgressSeconds = 0f;
        _hasPreviousFrame = false;
        _previousPosition = default;
        _previousHeadingDegrees = 0f;
        _speedMetersPerSecond = 0f;
        _yawRateDegreesPerSecond = 0f;
    }

    /// <summary>Cancels and reports the reason, for the raw-input observer
    /// that must stop following BEFORE vanilla dispatches doodad controls in
    /// the same <c>Player.SetControls</c> call.</summary>
    public SailingRouteFollowStep CancelWith(SailingRouteFollowCancelReason reason)
    {
        if (_path is null)
        {
            return default;
        }

        return Stop(reason);
    }

    /// <summary>Derives speed and yaw rate from successive frames. Both are
    /// smoothed and sanity-bounded, so a teleport or a frame hitch cannot
    /// inject a wild steering command.</summary>
    private void UpdateDerivedMotion(in SailingRouteFollowFrame frame)
    {
        if (!_hasPreviousFrame || frame.DeltaSeconds <= 0f)
        {
            _hasPreviousFrame = true;
            _previousPosition = frame.Position;
            _previousHeadingDegrees = frame.HeadingDegrees;
            return;
        }

        float travelled = frame.Position.HorizontalDistanceTo(_previousPosition);
        float speed = travelled / frame.DeltaSeconds;
        if (speed >= 0f && speed <= MaximumPlausibleSpeed)
        {
            _speedMetersPerSecond +=
                (speed - _speedMetersPerSecond) * DerivedSmoothing;
        }

        float yawRate =
            NormalizeDelta(frame.HeadingDegrees - _previousHeadingDegrees) /
            frame.DeltaSeconds;
        if (Math.Abs(yawRate) <= MaximumPlausibleYawRate)
        {
            _yawRateDegreesPerSecond +=
                (yawRate - _yawRateDegreesPerSecond) * DerivedSmoothing;
        }

        _previousPosition = frame.Position;
        _previousHeadingDegrees = frame.HeadingDegrees;
    }

    private SailingRouteFollowStep Stop(SailingRouteFollowCancelReason reason)
    {
        Cancel();
        return new SailingRouteFollowStep(false, 0f, true, reason);
    }

    private static SailingRouteFollowCancelReason ImmediateCancelReason(
        in SailingRouteFollowFrame frame)
    {
        if (!frame.Enabled) return SailingRouteFollowCancelReason.Disabled;
        if (frame.TogglePressed) return SailingRouteFollowCancelReason.TogglePressed;
        if (frame.ManualRudderInput) return SailingRouteFollowCancelReason.ManualRudderInput;
        if (frame.ManualSailInput) return SailingRouteFollowCancelReason.ManualSailInput;
        if (frame.ExitInput) return SailingRouteFollowCancelReason.ExitInput;
        if (!frame.HelmGranted) return SailingRouteFollowCancelReason.HelmLost;
        if (!frame.RouteUnchanged) return SailingRouteFollowCancelReason.RouteChanged;
        if (!frame.LifecycleReady) return SailingRouteFollowCancelReason.Lifecycle;
        if (!IsFinite(frame.DeltaSeconds) || frame.DeltaSeconds < 0f ||
            !IsFinite(frame.HeadingDegrees) ||
            !IsFinite(frame.RudderValue) ||
            frame.RudderValue < -1f || frame.RudderValue > 1f ||
            !IsFinite(frame.Position.X) || !IsFinite(frame.Position.Y) ||
            !IsFinite(frame.Position.Z))
        {
            return SailingRouteFollowCancelReason.InvalidState;
        }

        return SailingRouteFollowCancelReason.None;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float BearingTo(in RoadPoint from, in RoadPoint to)
    {
        float x = to.X - from.X;
        float z = to.Z - from.Z;
        return NormalizeAngle((float)(Math.Atan2(x, z) * (180d / Math.PI)));
    }

    private static float NormalizeDelta(float angle)
    {
        float normalized = NormalizeAngle(angle);
        return normalized > 180f ? normalized - 360f : normalized;
    }

    private static float NormalizeAngle(float angle)
    {
        float result = angle % 360f;
        return result < 0f ? result + 360f : result;
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
