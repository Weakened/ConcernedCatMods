using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Navigation;

/// <summary>Deterministic focus traversal over a panel's focusable elements
/// (CT-031). Controller navigation walks this ring: Next/Previous wrap in a
/// fixed order so every element is reachable and the order never depends on
/// frame timing or hash iteration. Pure and headless — the panel binds the
/// current item to a visible focus indicator; this class only decides which
/// element is focused.</summary>
public sealed class FocusRing
{
    private readonly IReadOnlyList<FocusItem> _items;

    public FocusRing(IReadOnlyList<FocusItem> items)
    {
        _items = items ?? Array.Empty<FocusItem>();
        CurrentIndex = _items.Count > 0 ? 0 : -1;
    }

    public int Count => _items.Count;

    /// <summary>Index of the focused item, or -1 when the ring is empty.</summary>
    public int CurrentIndex { get; private set; }

    public bool HasFocus => CurrentIndex >= 0 && CurrentIndex < _items.Count;

    public FocusItem Current =>
        HasFocus ? _items[CurrentIndex] : default;

    /// <summary>Advances focus to the next element, wrapping from the last
    /// back to the first. No-op on an empty ring. Returns the now-focused
    /// item.</summary>
    public FocusItem Next()
    {
        if (_items.Count == 0)
        {
            return default;
        }

        CurrentIndex = (CurrentIndex + 1) % _items.Count;
        return _items[CurrentIndex];
    }

    /// <summary>Moves focus to the previous element, wrapping from the first
    /// to the last. No-op on an empty ring.</summary>
    public FocusItem Previous()
    {
        if (_items.Count == 0)
        {
            return default;
        }

        CurrentIndex = (CurrentIndex - 1 + _items.Count) % _items.Count;
        return _items[CurrentIndex];
    }

    /// <summary>Focuses the item with the given id, returning true when
    /// found; leaves focus unchanged when not.</summary>
    public bool FocusById(string id)
    {
        for (int index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index].Id, id, StringComparison.Ordinal))
            {
                CurrentIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns focus to the first element (or -1 when empty), the
    /// state a panel should show when it opens.</summary>
    public void Reset()
    {
        CurrentIndex = _items.Count > 0 ? 0 : -1;
    }
}
