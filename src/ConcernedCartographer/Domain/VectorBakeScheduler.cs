using System;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The rebake decision state for the large-map vector layer
/// (RC11, feedback 3): pure and exhaustively testable, because a wrong
/// decision here is invisible ink — a zoom band or an invalidation window
/// where roads simply do not draw.
///
/// Rules:
///  - the first bake fires immediately (no committed width yet);
///  - data changes rebake after a short debounce (bursts coalesce);
///  - zoom rebakes whenever the uv window width drifts a step ratio away
///    from the COMMITTED width, in either direction, so screen-space
///    widths and cadences stay calibrated at every zoom;
///  - a slow periodic rebake picks up fog/exploration changes;
///  - an INCOMPLETE bake (projection unavailable for some points) commits
///    nothing and stays dirty, so the next frame retries instead of
///    leaving a hole until the next zoom step or periodic tick.</summary>
internal sealed class VectorBakeScheduler
{
    public const float ZoomStepRatio = 1.25f;
    public const float DebounceSeconds = 0.5f;
    public const float PeriodicSeconds = 30f;

    /// <summary>How soon an incomplete bake retries. Short enough to be
    /// invisible, long enough that a permanently broken projection cannot
    /// turn into a per-frame rebake loop.</summary>
    public const float IncompleteRetrySeconds = 0.25f;

    private float _bakedUvWidth = -1f;
    private bool _dataDirty = true;
    private float _debounceElapsed;
    private float _periodicElapsed;

    /// <summary>The uv width of the last COMMITTED bake, or a negative
    /// value before the first commit.</summary>
    public float BakedUvWidth => _bakedUvWidth;

    public bool DataDirty => _dataDirty;

    public void MarkDataDirty()
    {
        _dataDirty = true;
    }

    /// <summary>Container rebuilt (map/world lifecycle): behave like the
    /// first bake again.</summary>
    public void Invalidate()
    {
        _bakedUvWidth = -1f;
        _dataDirty = true;
    }

    public void Advance(float deltaTime)
    {
        if (deltaTime > 0f)
        {
            _debounceElapsed += deltaTime;
            _periodicElapsed += deltaTime;
        }
    }

    public bool ShouldRebake(float uvWidth, bool stylingChanged)
    {
        if (_bakedUvWidth <= 0f || stylingChanged)
        {
            return true;
        }

        if (uvWidth > _bakedUvWidth * ZoomStepRatio || uvWidth < _bakedUvWidth / ZoomStepRatio)
        {
            return true;
        }

        if (_dataDirty && _debounceElapsed >= DebounceSeconds)
        {
            return true;
        }

        return _periodicElapsed >= PeriodicSeconds;
    }

    /// <summary>A bake ran and every point projected: commit the width and
    /// clear the pending reasons.</summary>
    public void OnBakeCommitted(float uvWidth)
    {
        _bakedUvWidth = uvWidth;
        _dataDirty = false;
        _debounceElapsed = 0f;
        _periodicElapsed = 0f;
    }

    /// <summary>A bake ran but the projection was unavailable for at least
    /// one point: whatever was drawn is better than nothing, but nothing
    /// is committed — the layer stays dirty with the debounce nearly
    /// satisfied, so it retries within <see cref="IncompleteRetrySeconds"/>
    /// (no invisible-road window until the next zoom step or periodic
    /// tick, and no per-frame loop if the projection stays broken).</summary>
    public void OnBakeIncomplete()
    {
        _dataDirty = true;
        _debounceElapsed = Math.Max(0f, DebounceSeconds - IncompleteRetrySeconds);
        _periodicElapsed = 0f;
    }
}
