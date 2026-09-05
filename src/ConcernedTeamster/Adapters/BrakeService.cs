using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Brake;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Owns the parking brake at runtime (CT-012): the pure lifecycle
/// decides, this service applies the physics and confirms — engage marks
/// only after the freeze succeeded, release always marks (a destroyed
/// cart's constraints died with it). Logs one INFO line per state change,
/// never per tick. Ticked from the pump's due ticks plus immediately on a
/// toggle press; released unconditionally on world exit and plugin
/// shutdown.</summary>
internal sealed class BrakeService
{
    private readonly BrakeLifecycle _lifecycle = new();
    private readonly ManualLogSource _log;
    private int _capturedConstraints;

    public BrakeService(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsEngaged => _lifecycle.IsEngaged;

    public string? EngagedCartId => _lifecycle.EngagedCartId;

    /// <summary>Explicit toggle from the visible control.</summary>
    public void RequestToggle(string? cartId)
    {
        if (cartId is null)
        {
            return;
        }

        BrakeFacts facts = CartBrakeAdapter.ReadFacts(cartId);
        BrakeAction action = _lifecycle.EvaluateToggle(cartId, facts, out string reason);
        Apply(action, cartId, reason);
    }

    /// <summary>Re-check the engaged cart against fresh facts.</summary>
    public void Tick()
    {
        if (!_lifecycle.IsEngaged)
        {
            return;
        }

        BrakeFacts facts = CartBrakeAdapter.ReadFacts(_lifecycle.EngagedCartId);
        BrakeAction action = _lifecycle.EvaluateTick(facts, out string reason);
        if (action == BrakeAction.Release)
        {
            Apply(BrakeAction.Release, _lifecycle.EngagedCartId!, reason);
        }
    }

    /// <summary>Unconditional release path (world exit, plugin shutdown,
    /// master switch off).</summary>
    public void ReleaseNow(string reason)
    {
        if (_lifecycle.IsEngaged)
        {
            Apply(BrakeAction.Release, _lifecycle.EngagedCartId!, reason);
        }
    }

    private void Apply(BrakeAction action, string cartId, string reason)
    {
        switch (action)
        {
            case BrakeAction.Engage:
                if (CartBrakeAdapter.TryEngage(cartId, out int captured))
                {
                    _capturedConstraints = captured;
                    _lifecycle.MarkEngaged(cartId);
                    _log.LogInfo($"Parking brake ENGAGED on cart {cartId} ({reason}).");
                }
                else
                {
                    _log.LogWarning(
                        $"Parking brake could not engage on cart {cartId}; nothing was changed.");
                }

                break;

            case BrakeAction.Release:
                bool restored = CartBrakeAdapter.TryRelease(cartId, _capturedConstraints);
                _lifecycle.MarkReleased();
                _log.LogInfo(
                    $"Parking brake RELEASED on cart {cartId} ({reason})" +
                    (restored ? "." : "; the cart no longer exists, nothing to restore."));
                break;
        }
    }
}
