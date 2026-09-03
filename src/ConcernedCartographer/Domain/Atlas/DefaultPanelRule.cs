namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC13 polish 4: the Markers panel opens by default on every
/// FRESH large-map open, and only then. Pure state machine so the
/// fight-the-user cases are provable: the auto-open fires at most once
/// per map-open (armed while the map is closed, disarmed the moment it
/// fires), never fires while any CC surface is already visible (a
/// visible surface disarms the whole map-open, so closing or switching
/// panels is always respected for the rest of that map session), and
/// never fires when the enhanced palette is unavailable at map-open
/// (setting off, conflicting pin manager, palette/toolbar failure,
/// NoMap gate) — unavailability disarms rather than waits, so a
/// mid-session availability flip cannot pop a panel minutes after the
/// map opened.</summary>
internal sealed class DefaultPanelRule
{
    private bool _armed = true;

    /// <summary>Whether the next open-map frame would consult the rule.
    /// Callers gate on this BEFORE computing the (possibly expensive)
    /// availability inputs, so a disarmed rule costs nothing per
    /// frame.</summary>
    public bool IsArmed => _armed;

    /// <summary>Call every frame the large map is closed: re-arms the
    /// auto-open for the next fresh map-open.</summary>
    public void NoteMapClosed()
    {
        _armed = true;
    }

    /// <summary>Call every frame the large map is open. True exactly when
    /// the caller should open the Markers panel now.</summary>
    public bool ShouldAutoOpen(bool paletteAvailable, bool anySurfaceVisible)
    {
        if (!_armed)
        {
            return false;
        }

        _armed = false;
        return paletteAvailable && !anySurfaceVisible;
    }
}
