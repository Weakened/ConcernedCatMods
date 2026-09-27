using System;
using TheConcernedCat.Ladders;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>Restrained rung contact from Valheim's own FootStep system.
///
/// The player is marked as wall-running by ClimbPose while climbing, so
/// FootStep selects its built-in Climbing motion type. We only ask for a step
/// when the rung index changes and after a short rate limit. No custom audio,
/// no per-frame effect creation and no custom network message is introduced.</summary>
internal sealed class ClimbSounds
{
    private readonly Action<string> _log;
    private Player? _player;
    private FootStep? _footStep;
    private int _lastRung = -1;
    private float _lastContactTime = float.NegativeInfinity;
    private bool _installed;
    private bool _saidFailure;

    internal ClimbSounds(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal void Install()
    {
        if (_installed)
        {
            return;
        }

        ClimbController.TelemetryChanged += OnTelemetry;
        _installed = true;
    }

    internal void Remove()
    {
        if (_installed)
        {
            ClimbController.TelemetryChanged -= OnTelemetry;
        }

        Reset();
        _installed = false;
    }

    private void OnTelemetry(ClimbTelemetry telemetry)
    {
        try
        {
            if (!telemetry.IsClimbing)
            {
                Reset();
                return;
            }

            Player? player = Player.m_localPlayer;
            if (player == null)
            {
                Reset();
                return;
            }

            if (!ReferenceEquals(player, _player))
            {
                _player = player;
                _footStep = player.GetComponent<FootStep>() ?? player.GetComponentInChildren<FootStep>();
                _lastRung = -1;
                _lastContactTime = float.NegativeInfinity;
            }

            float now = Time.time;
            if (ClimbPresentation.ShouldPlayRungContact(
                    _lastRung,
                    telemetry.Rung,
                    telemetry.Velocity,
                    now - _lastContactTime))
            {
                _footStep?.OnFoot();
                _lastContactTime = now;
            }

            _lastRung = telemetry.Rung;
        }
        catch (Exception exception)
        {
            if (!_saidFailure)
            {
                _saidFailure = true;
                _log("Ladder contact sound failed soft: " + SafeFailure.Brief(exception) + ". Traversal continues.");
            }

            Reset();
        }
    }

    private void Reset()
    {
        _player = null;
        _footStep = null;
        _lastRung = -1;
        _lastContactTime = float.NegativeInfinity;
    }
}
