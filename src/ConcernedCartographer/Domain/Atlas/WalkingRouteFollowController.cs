using System;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

internal enum WalkingRouteFollowCancelReason
{
    None,
    Disabled,
    TogglePressed,
    ManualInput,
    RouteChanged,
    Lifecycle,
    IneligibleMovement,
    OffRoute,
    RouteEnd,
    Stuck,
    InvalidState,
}

internal readonly struct WalkingRouteFollowFrame
{
    public WalkingRouteFollowFrame(
        in RoadPoint position,
        float currentYawDegrees,
        float deltaSeconds,
        bool enabled = true,
        bool vanillaAutorunActive = true,
        bool togglePressed = false,
        bool manualInput = false,
        bool routeUnchanged = true,
        bool lifecycleReady = true,
        bool eligibleMovement = true,
        bool blocked = false)
    {
        Position = position;
        CurrentYawDegrees = currentYawDegrees;
        DeltaSeconds = deltaSeconds;
        Enabled = enabled;
        VanillaAutorunActive = vanillaAutorunActive;
        TogglePressed = togglePressed;
        ManualInput = manualInput;
        RouteUnchanged = routeUnchanged;
        LifecycleReady = lifecycleReady;
        EligibleMovement = eligibleMovement;
        Blocked = blocked;
    }
    public RoadPoint Position { get; }
    public float CurrentYawDegrees { get; }
    public float DeltaSeconds { get; }
    public bool Enabled { get; }
    public bool VanillaAutorunActive { get; }
    public bool TogglePressed { get; }
    public bool ManualInput { get; }
    public bool RouteUnchanged { get; }
    public bool LifecycleReady { get; }
    public bool EligibleMovement { get; }
    public bool Blocked { get; }
}

internal readonly struct WalkingRouteFollowStep
{
    public WalkingRouteFollowStep(
        bool steering,
        float desiredYawDegrees,
        bool stopVanillaAutorun,
        WalkingRouteFollowCancelReason cancelReason)
    {
        Steering = steering;
        DesiredYawDegrees = desiredYawDegrees;
        StopVanillaAutorun = stopVanillaAutorun;
        CancelReason = cancelReason;
    }

    public bool Steering { get; }
    public float DesiredYawDegrees { get; }
    public bool StopVanillaAutorun { get; }
    public WalkingRouteFollowCancelReason CancelReason { get; }
}

internal sealed class WalkingRouteFollowController
{
    private const int ProjectionWindow = 12;
    private const float LookAheadMeters = 5f;
    private const float MaximumCrossTrackMeters = 10f;
    private const float RouteEndToleranceMeters = 1.25f;
    private const float ProgressEpsilonMeters = 0.15f;
    private const float StuckTimeoutSeconds = 2.5f;
    private const float MaximumTurnDegreesPerSecond = 120f;

    private RouteFollowPath? _path;
    private RouteFollowDirection _direction;
    private int _cursor;
    private float _lastRemainingMeters;
    private float _stuckSeconds;

    public bool IsFollowing => _path is not null;

    public bool TryStart(
        RouteFollowPath? path,
        RouteFollowDirection direction,
        in RoadPoint position)
    {
        Cancel();
        if (path is null ||
            !RouteFollowMath.TrySample(path, position, direction, 0,
                path.LastSegmentIndex + 1, LookAheadMeters,
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
        return true;
    }
    public WalkingRouteFollowStep Tick(in WalkingRouteFollowFrame frame)
    {
        if (_path is null)
        {
            return default;
        }

        WalkingRouteFollowCancelReason immediate = ImmediateCancelReason(frame);
        if (immediate != WalkingRouteFollowCancelReason.None)
        {
            return Stop(immediate);
        }

        if (!RouteFollowMath.TrySample(
                _path, frame.Position, _direction, _cursor, ProjectionWindow,
                LookAheadMeters, MaximumCrossTrackMeters,
                RouteEndToleranceMeters, out RouteFollowSample sample))
        {
            return Stop(WalkingRouteFollowCancelReason.OffRoute);
        }

        _cursor = sample.SegmentIndex;
        if (sample.AtRouteEnd)
        {
            return Stop(WalkingRouteFollowCancelReason.RouteEnd);
        }

        if (_lastRemainingMeters - sample.RemainingMeters >= ProgressEpsilonMeters)
        {
            _lastRemainingMeters = sample.RemainingMeters;
            _stuckSeconds = 0f;
        }
        else if (frame.Blocked)
        {
            _stuckSeconds += frame.DeltaSeconds;
            if (_stuckSeconds >= StuckTimeoutSeconds)
            {
                return Stop(WalkingRouteFollowCancelReason.Stuck);
            }
        }
        else
        {
            _stuckSeconds = 0f;
        }

        float targetYaw = YawTo(frame.Position, sample.LookAheadPoint);
        float boundedYaw = MoveTowardsAngle(
            frame.CurrentYawDegrees,
            targetYaw,
            MaximumTurnDegreesPerSecond * frame.DeltaSeconds);
        return new WalkingRouteFollowStep(true, boundedYaw, false,
            WalkingRouteFollowCancelReason.None);
    }

    public void Cancel()
    {
        _path = null;
        _cursor = 0;
        _lastRemainingMeters = 0f;
        _stuckSeconds = 0f;
    }
    private WalkingRouteFollowStep Stop(WalkingRouteFollowCancelReason reason)
    {
        Cancel();
        return new WalkingRouteFollowStep(false, 0f, true, reason);
    }

    private static WalkingRouteFollowCancelReason ImmediateCancelReason(
        in WalkingRouteFollowFrame frame)
    {
        if (!frame.Enabled) return WalkingRouteFollowCancelReason.Disabled;
        if (frame.TogglePressed) return WalkingRouteFollowCancelReason.TogglePressed;
        if (frame.ManualInput) return WalkingRouteFollowCancelReason.ManualInput;
        if (!frame.RouteUnchanged) return WalkingRouteFollowCancelReason.RouteChanged;
        if (!frame.LifecycleReady) return WalkingRouteFollowCancelReason.Lifecycle;
        if (!frame.EligibleMovement) return WalkingRouteFollowCancelReason.IneligibleMovement;
        if (!frame.VanillaAutorunActive) return WalkingRouteFollowCancelReason.InvalidState;
        if (!(frame.DeltaSeconds >= 0f) || float.IsNaN(frame.DeltaSeconds) ||
            float.IsInfinity(frame.DeltaSeconds))
        {
            return WalkingRouteFollowCancelReason.InvalidState;
        }

        return WalkingRouteFollowCancelReason.None;
    }

    private static float YawTo(in RoadPoint from, in RoadPoint to)
    {
        float x = to.X - from.X;
        float z = to.Z - from.Z;
        return NormalizeAngle((float)(Math.Atan2(x, z) * (180d / Math.PI)));
    }

    private static float MoveTowardsAngle(float current, float target, float maximumDelta)
    {
        float delta = NormalizeDelta(target - current);
        if (Math.Abs(delta) <= maximumDelta)
        {
            return NormalizeAngle(target);
        }

        return NormalizeAngle(current + (Math.Sign(delta) * maximumDelta));
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
}
