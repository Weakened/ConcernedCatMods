using System;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>Honest walking fallback. No custom clip, IK, tilt, controller replacement, or root motion.</summary>
internal sealed class LadderWalkPose
{
    private static readonly int Forward = ZSyncAnimation.GetHash("forward_speed");
    private static readonly int Sideways = ZSyncAnimation.GetHash("sideway_speed");
    private static readonly int Turning = ZSyncAnimation.GetHash("turn_speed");
    private ZSyncAnimation? _animation;
    private bool _failed;
    private readonly Action<string> _log;

    internal LadderWalkPose(Action<string> log) { _log = log; }

    internal void Begin(Player player)
    {
        _animation = player.GetComponent<ZSyncAnimation>();
        _failed = false;
    }

    internal void Apply(float verticalSpeed)
    {
        if (_animation == null || _failed) { return; }
        try
        {
            _animation.SetFloat(Forward, Mathf.Clamp(verticalSpeed, -1.8f, 1.8f));
            _animation.SetFloat(Sideways, 0f);
            _animation.SetFloat(Turning, 0f);
        }
        catch (Exception exception)
        {
            _failed = true;
            _log("Ladder walking pose unavailable (" + SafeFailure.Brief(exception) + "); movement remains available.");
        }
    }

    internal void Clear(bool resetOwnedAnimation = true)
    {
        if (resetOwnedAnimation) { Apply(0f); }
        _animation = null;
    }
}
