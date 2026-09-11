using System;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

internal enum SailingRouteFollowCancelReason
{
    None,
    Disabled,
    TogglePressed,
    ManualInput,
    ExitAction,
    RouteChanged,
    Lifecycle,
    HelmInvalid,
    OffRoute,
    RouteEnd,
    NoProgressTimeout,
    InvalidState,
}

internal readonly struct SailingRouteFollowFrame
{
    public SailingRouteFollowFrame(
        in RoadPoint position,
        float currentYawDegrees,
        float deltaSeconds,
        bool enabled = true,
        bool manualInput = false,
        bool exitAction = false,
        bool togglePressed = false,
        bool routeUnchanged = true,
        bool lifecycleReady = true,
        bool helmValid = true,
        float currentRudderValue = 0f)
    {
        Position = position;
        CurrentYawDegrees = currentYawDegrees;
        DeltaSeconds = deltaSeconds;
        Enabled = enabled;
        ManualInput = manualInput;
        ExitAction = exitAction;
        TogglePressed = togglePressed;
        RouteUnchanged = routeUnchanged;
        LifecycleReady = lifecycleReady;
        HelmValid = helmValid;
        CurrentRudderValue = currentRudderValue;
    }

    public RoadPoint Position { get; }
    public float CurrentYawDegrees { get; }
    public float DeltaSeconds { get; }
    public bool Enabled { get; }
    public bool ManualInput { get; }
    public bool ExitAction { get; }
    public bool TogglePressed { get; }
    public bool RouteUnchanged { get; }
    public bool LifecycleReady { get; }
    public bool HelmValid { get; }
    public float CurrentRudderValue { get; }
}

internal readonly struct SailingRouteFollowStep
{
    public SailingRouteFollowStep(
        bool injectRudder,
        float rudderInput,
        SailingRouteFollowCancelReason cancelReason)
    {
        InjectRudder = injectRudder;
        RudderInput = rudderInput;
        CancelReason = cancelReason;
    }

    public bool InjectRudder { get; }
    public float RudderInput { get; }
    public SailingRouteFollowCancelReason CancelReason { get; }
}

/// <summary>Pure input policy for the narrow ShipControlls adapter.
/// An inactive/cancelled step leaves the caller's vanilla input untouched.</summary>
internal static class SailingRouteFollowControlPolicy
{
    public static bool TryApply(
        in SailingRouteFollowStep step,
        ref float rudderInput)
    {
        if (!step.InjectRudder ||
            float.IsNaN(step.RudderInput) ||
            float.IsInfinity(step.RudderInput))
        {
            return false;
        }

        rudderInput = Math.Max(-1f, Math.Min(1f, step.RudderInput));
        return true;
    }
}

/// <summary>Pure representation of the read-only Player.SetControls
/// observer. A terminal reason is evaluated before vanilla dispatch reaches
/// the helm injection seam.</summary>
internal static class SailingRouteFollowInputPolicy
{
    public static SailingRouteFollowCancelReason Evaluate(
        bool togglePressed,
        bool manualRudderOrSail,
        bool exitAction)
    {
        if (togglePressed)
        {
            return SailingRouteFollowCancelReason.TogglePressed;
        }

        if (manualRudderOrSail)
        {
            return SailingRouteFollowCancelReason.ManualInput;
        }

        return exitAction
            ? SailingRouteFollowCancelReason.ExitAction
            : SailingRouteFollowCancelReason.None;
    }
}

/// <summary>Game-free sailing course controller. It projects the ship onto
/// the immutable shared route snapshot and emits only a bounded vanilla
/// rudder direction/rate input. Sail, wind, forces and authority stay outside
/// this type and are never changed by Route Follow.</summary>
internal sealed class SailingRouteFollowController
{
    private const int ProjectionWindow = 20;
    private const float LookAheadMeters = 18f;
    private const float MaximumCrossTrackMeters = 30f;
    private const float RouteEndToleranceMeters = 4f;
    private const float ProgressEpsilonMeters = 0.5f;
    private const float NoProgressTimeoutSeconds = 15f;
    private const float FullRudderErrorDegrees = 45f;
    private const float RudderRateSpan = 0.25f;
    private const float RudderCenterDeadband = 0.04f;

    private RouteFollowPath? _path;
    private RouteFollowDirection _direction;
    private int _cursor;
    private float _lastRemainingMeters;
    private float _noProgressSeconds;

    public bool IsFollowing => _path is not null;

    public bool TryStart(
        RouteFollowPath? path,
        RouteFollowDirection direction,
        in RoadPoint position)
    {
        Cancel();
        int initialCursor = direction == RouteFollowDirection.Reverse &&
            path is not null ? path.LastSegmentIndex : 0;
        if (path is null ||
            !RouteFollowMath.TrySample(
                path, position, direction, initialCursor,
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

        float targetYaw = YawTo(frame.Position, sample.LookAheadPoint);
        float headingError = NormalizeDelta(
            targetYaw - frame.CurrentYawDegrees);
        float desiredRudder = Math.Max(-1f, Math.Min(
            1f, headingError / FullRudderErrorDegrees));
        float currentRudder = Math.Max(
            -1f, Math.Min(1f, frame.CurrentRudderValue));
        float rudderError = desiredRudder - currentRudder;
        float rudderRate = Math.Abs(rudderError) <= RudderCenterDeadband
            ? 0f
            : Math.Max(-1f, Math.Min(1f, rudderError / RudderRateSpan));
        if (float.IsNaN(rudderRate) || float.IsInfinity(rudderRate))
        {
            return Stop(SailingRouteFollowCancelReason.InvalidState);
        }

        return new SailingRouteFollowStep(
            true, rudderRate, SailingRouteFollowCancelReason.None);
    }

    public void Cancel()
    {
        _path = null;
        _cursor = 0;
        _lastRemainingMeters = 0f;
        _noProgressSeconds = 0f;
    }

    private SailingRouteFollowStep Stop(SailingRouteFollowCancelReason reason)
    {
        Cancel();
        return new SailingRouteFollowStep(false, 0f, reason);
    }

    private static SailingRouteFollowCancelReason ImmediateCancelReason(
        in SailingRouteFollowFrame frame)
    {
        if (!frame.Enabled) return SailingRouteFollowCancelReason.Disabled;
        if (frame.TogglePressed) return SailingRouteFollowCancelReason.TogglePressed;
        if (frame.ManualInput) return SailingRouteFollowCancelReason.ManualInput;
        if (frame.ExitAction) return SailingRouteFollowCancelReason.ExitAction;
        if (!frame.RouteUnchanged) return SailingRouteFollowCancelReason.RouteChanged;
        if (!frame.LifecycleReady) return SailingRouteFollowCancelReason.Lifecycle;
        if (!frame.HelmValid) return SailingRouteFollowCancelReason.HelmInvalid;
        if (!(frame.DeltaSeconds >= 0f) ||
            float.IsNaN(frame.DeltaSeconds) ||
            float.IsInfinity(frame.DeltaSeconds) ||
            float.IsNaN(frame.CurrentYawDegrees) ||
            float.IsInfinity(frame.CurrentYawDegrees) ||
            float.IsNaN(frame.CurrentRudderValue) ||
            float.IsInfinity(frame.CurrentRudderValue) ||
            frame.CurrentRudderValue < -1.25f ||
            frame.CurrentRudderValue > 1.25f)
        {
            return SailingRouteFollowCancelReason.InvalidState;
        }

        return SailingRouteFollowCancelReason.None;
    }

    private static float YawTo(in RoadPoint from, in RoadPoint to)
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
}
