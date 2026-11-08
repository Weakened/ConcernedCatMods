using System;
namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;
/// <summary>Only the executor's timers stop. World facts and authority stay live.</summary>
internal sealed class UpkeepActiveClock
{
    private bool _started, _wasPaused;
    private float _wall, _active;
    internal bool Read(float wall, bool paused, out float active)
    {
        active = _active;
        if (wall < 0 || float.IsNaN(wall) || float.IsInfinity(wall) || (_started && wall < _wall)) return false;
        if (!_started) { _active = wall; _started = true; }
        else if (!paused && !_wasPaused) _active += wall - _wall;
        _wall = wall; _wasPaused = paused; active = _active;
        return true;
    }
    internal void Reset() { _started = _wasPaused = false; _wall = _active = 0; }
}
