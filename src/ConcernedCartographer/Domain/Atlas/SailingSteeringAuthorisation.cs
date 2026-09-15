namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The one-shot permission slip that lets sailing Route Follow write
/// a rudder value, and the invariant that keeps that write inside the control
/// call which authorised it (#243).</summary>
/// <remarks>Valheim dispatches doodad controls from inside
/// <c>Player.SetControls</c>, so the raw-input observer runs first and the
/// steering seam runs later in that SAME call. The observer arms this token;
/// the steering seam consumes it. Consumption ALWAYS clears, and it only
/// succeeds for the exact controller object that was armed, so a synthetic
/// write can never
///
///  * survive into a later control call,
///  * reach a different ship's controls, or
///  * fire on a path where vanilla never called the observer at all.
///
/// The controller identity is kept as <see cref="object"/> so this invariant
/// stays game-free and directly testable; the runtime passes the live
/// <c>ShipControlls</c> instance.</remarks>
internal sealed class SailingSteeringAuthorisation
{
    private object? _controller;
    private float _rudderInput;

    /// <summary>True while a write is authorised and not yet consumed.</summary>
    public bool IsArmed => _controller is not null;

    /// <summary>Drops any unconsumed authorisation. Called unconditionally at
    /// the start of every raw-input observation.</summary>
    public void Clear()
    {
        _controller = null;
        _rudderInput = 0f;
    }

    /// <summary>Authorises exactly one write, for exactly this controller.
    /// A null controller authorises nothing.</summary>
    public void Arm(object? controller, float rudderInput)
    {
        if (controller is null)
        {
            Clear();
            return;
        }

        _controller = controller;
        _rudderInput = rudderInput;
    }

    /// <summary>Consumes the authorisation. Always clears, whether or not it
    /// matched, so a mismatched attempt cannot be retried.</summary>
    public bool TryConsume(object? controller, out float rudderInput)
    {
        bool matched = _controller is not null &&
            controller is not null &&
            ReferenceEquals(_controller, controller);
        rudderInput = matched ? _rudderInput : 0f;
        Clear();
        return matched;
    }
}
