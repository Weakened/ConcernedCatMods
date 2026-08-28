namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Managed-from-birth state machine for the Enhanced Pin Palette
/// (#96). Valheim creates a palette pin through its own double-click →
/// name-input flow; this tracker watches the map's "pin being named"
/// handle each frame and reports the newborn exactly once, when its
/// naming flow closes — the moment its final name exists and it is safe
/// to associate an AtlasPin. Only pins whose naming STARTED while the
/// palette selection was armed are ever claimed, so pins the player
/// created before choosing a palette icon stay plain vanilla.</summary>
internal sealed class PaletteBirthTracker<THandle> where THandle : class
{
    private THandle? _lastNaming;
    private THandle? _pending;

    public bool IsArmed { get; private set; }

    /// <summary>Stable registry icon ID the newborn will carry.</summary>
    public string IconId { get; private set; } = "";

    /// <summary>Default category the newborn will carry.</summary>
    public string Category { get; private set; } = "";

    public void Arm(string iconId, string category)
    {
        IsArmed = true;
        IconId = iconId;
        Category = category;
    }

    public void Disarm()
    {
        IsArmed = false;
        _pending = null;
    }

    /// <summary>Feed the map's current naming target every frame (null when
    /// the name input is closed). Returns a newborn handle exactly once,
    /// the moment its naming flow ends — including when vanilla swaps
    /// straight to naming another pin in the same frame. Cancelling the
    /// name input still counts as a birth: vanilla keeps the (unnamed)
    /// pin either way.</summary>
    public THandle? Observe(THandle? namingPin)
    {
        if (ReferenceEquals(namingPin, _lastNaming))
        {
            return null;
        }

        THandle? born = _pending;
        _pending = null;
        _lastNaming = namingPin;
        if (namingPin is not null && IsArmed)
        {
            _pending = namingPin;
        }

        return born;
    }

    /// <summary>Forget in-flight state on map/world teardown. The armed
    /// selection itself survives (it is UI state, reset by its owner).</summary>
    public void Reset()
    {
        _lastNaming = null;
        _pending = null;
    }
}
