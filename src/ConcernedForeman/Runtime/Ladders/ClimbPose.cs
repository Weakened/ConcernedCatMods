using System;
using HarmonyLib;
using TheConcernedCat.Ladders;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>Presentation for the local climber (CF-LAD-003).
///
/// Valheim has no ladder animation clip. It does have a player wall-running
/// pose, and that pose already writes the visual tilt to the player's existing
/// replicated tilt state. While the climb controller owns the local body this
/// class supplies the wall-running inputs and the normal locomotion parameters.
/// When the climb ends it clears them immediately; it never owns movement.</summary>
internal sealed class ClimbPose
{
    private static readonly int ForwardSpeed = ZSyncAnimation.GetHash("forward_speed");
    private static readonly int SidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");
    private static readonly int TurnSpeed = ZSyncAnimation.GetHash("turn_speed");

    private readonly Action<string> _log;
    private AccessTools.FieldRef<Character, bool>? _wallRunning;
    private AccessTools.FieldRef<Character, Vector3>? _lastGroundNormal;
    private AccessTools.FieldRef<Character, bool>? _walking;
    private AccessTools.FieldRef<Character, bool>? _running;

    private Player? _player;
    private ZSyncAnimation? _animation;
    private bool _installed;
    private bool _saidFailure;

    internal ClimbPose(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal bool Install()
    {
        if (_installed)
        {
            return true;
        }

        _wallRunning = TryBind<bool>("m_wallRunning");
        _lastGroundNormal = TryBind<Vector3>("m_lastGroundNormal");
        _walking = TryBind<bool>("m_walking");
        _running = TryBind<bool>("m_running");

        if (_wallRunning == null || _lastGroundNormal == null)
        {
            _log("Ladder pose unavailable on this Valheim build; climbing remains functional without the lean.");
            return false;
        }

        ClimbController.TelemetryChanged += OnTelemetry;
        _installed = true;
        OnTelemetry(ClimbController.Telemetry);
        return true;
    }

    internal void Remove()
    {
        if (_installed)
        {
            ClimbController.TelemetryChanged -= OnTelemetry;
        }

        Clear();
        _installed = false;
    }

    private void OnTelemetry(ClimbTelemetry telemetry)
    {
        try
        {
            if (!telemetry.IsClimbing)
            {
                Clear();
                return;
            }

            Player? player = Player.m_localPlayer;
            if (player == null)
            {
                Clear();
                return;
            }

            if (!ReferenceEquals(player, _player))
            {
                Clear();
                _player = player;
                _animation = player.GetComponent<ZSyncAnimation>();
            }

            _wallRunning!(player) = true;
            if (_walking != null)
            {
                _walking(player) = false;
            }

            if (_running != null)
            {
                _running(player) = false;
            }

            if (telemetry.Facing.IsKnown)
            {
                var facing = new Vector3(telemetry.Facing.X, 0f, telemetry.Facing.Z);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    Vector3 outward = -facing.normalized;
                    float blend = ClimbPresentation.PoseBlend(telemetry);
                    Vector3 normal = Vector3.Slerp(Vector3.up, outward, blend);
                    _lastGroundNormal!(player) = normal.normalized;
                }
            }

            if (_animation != null)
            {
                _animation.SetFloat(ForwardSpeed, ClimbPresentation.AnimatorForwardSpeed(telemetry));
                _animation.SetFloat(SidewaySpeed, 0f);
                _animation.SetFloat(TurnSpeed, 0f);
            }
        }
        catch (Exception exception)
        {
            if (!_saidFailure)
            {
                _saidFailure = true;
                _log("Ladder pose failed soft: " + SafeFailure.Brief(exception) + ". Traversal continues.");
            }

            Clear();
        }
    }

    private void Clear()
    {
        Player? player = _player;
        try
        {
            if (player != null)
            {
                if (_wallRunning != null)
                {
                    _wallRunning(player) = false;
                }

                if (_lastGroundNormal != null)
                {
                    _lastGroundNormal(player) = Vector3.up;
                }
            }

            if (_animation != null)
            {
                _animation.SetFloat(ForwardSpeed, 0f);
                _animation.SetFloat(SidewaySpeed, 0f);
                _animation.SetFloat(TurnSpeed, 0f);
            }
        }
        catch
        {
            // Presentation teardown must never strand or stop a climber.
        }

        _player = null;
        _animation = null;
    }

    private static AccessTools.FieldRef<Character, TField>? TryBind<TField>(string field)
    {
        try
        {
            return AccessTools.FieldRefAccess<Character, TField>(field);
        }
        catch
        {
            return null;
        }
    }
}
