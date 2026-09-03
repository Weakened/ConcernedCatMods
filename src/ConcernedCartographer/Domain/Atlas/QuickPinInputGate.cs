namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC14 final-smoke fix 3: input ownership for the armed Quick
/// Pin interaction as a pure, frame-based state machine. The RC13 armed
/// mode only OBSERVED raw input — vanilla read the same click as an
/// attack and the same Escape as "open the pause menu" in the same frame.
/// This gate owns the interaction: while armed, the capture click must
/// not start an attack and Escape must only cancel Quick Pin — and
/// because the mod's tick order against vanilla's is undefined, the
/// owned press stays swallowed for the WHOLE frame it happened in (a
/// cancel handled before vanilla's update would otherwise let that same
/// Escape still open the menu). Suppression is narrowly scoped by
/// construction: it covers exactly the armed lifetime plus the single
/// arming/cancel/capture frame, and an external <see cref="Disarm"/>
/// (world switch, mod disable, dispose) releases everything
/// immediately.</summary>
internal sealed class QuickPinInputGate
{
    public enum FrameAction
    {
        None,
        Cancel,
        Capture,
    }

    private bool _armed;
    private int _lastOwnedFrame = int.MinValue;

    /// <summary>True while the one-shot armed capture is pending.</summary>
    public bool Armed => _armed;

    /// <summary>Arms the one-shot capture. The arming frame itself is
    /// owned — the toolbar click that armed must not become an attack
    /// when the map closes in the same frame.</summary>
    public void Arm(int frame)
    {
        _armed = true;
        _lastOwnedFrame = frame;
    }

    /// <summary>Feeds one frame of observed input while armed. Cancel wins
    /// over capture when both arrive in the same frame (Escape is the
    /// player changing their mind).</summary>
    public FrameAction HandleFrame(int frame, bool cancelPressed, bool capturePressed)
    {
        if (!_armed)
        {
            return FrameAction.None;
        }

        _lastOwnedFrame = frame;
        if (cancelPressed)
        {
            _armed = false;
            return FrameAction.Cancel;
        }

        if (capturePressed)
        {
            _armed = false;
            return FrameAction.Capture;
        }

        return FrameAction.None;
    }

    /// <summary>External teardown (world switch, disable, dispose):
    /// releases all suppression immediately, with no same-frame
    /// tail.</summary>
    public void Disarm()
    {
        _armed = false;
        _lastOwnedFrame = int.MinValue;
    }

    /// <summary>True while vanilla must not start an attack: the armed
    /// lifetime plus the owned arming/cancel/capture frame.</summary>
    public bool SuppressAttack(int frame)
    {
        return _armed || frame == _lastOwnedFrame;
    }

    /// <summary>True while vanilla must not open the pause menu: the armed
    /// lifetime plus the owned arming/cancel/capture frame.</summary>
    public bool SuppressMenu(int frame)
    {
        return _armed || frame == _lastOwnedFrame;
    }
}
